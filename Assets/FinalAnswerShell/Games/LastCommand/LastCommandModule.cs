using FinalAnswer.Application;
using FinalAnswer.Contracts;
using FinalAnswer.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using Machine = FinalAnswer.GameManager;
using Scenes = UnityEngine.SceneManagement.SceneManager;

namespace FinalAnswer.LastCommandHost
{
    /// <summary>
    /// Hosts the original 3D machine (Assets/FinalAnswer/Scenes/TheLastCommand) inside the shell. The scene is
    /// loaded additively and its GameManager runs in Hosted mode: no socket of its own — its commands
    /// (CLIENT_READY, CLOCK_SYNC, TOUCH_PANEL …) go out through the shell's RoomManager and every server
    /// event is forwarded back, so phones and the server see one host connection.
    /// </summary>
    public sealed class LastCommandModule : IGameModule
    {
        public const string SceneName = "TheLastCommand";

        readonly Transform parent;
        ModuleContext context;
        RectTransform root;
        Image curtain;
        Text loading;
        GameObject shellBackground;
        Camera shellCamera;
        UnityEngine.SceneManagement.Scene previousActive;
        AsyncOperation load;
        RoomSnapshot latest;
        Machine machine;
        string sessionId = "";
        float reveal;

        public LastCommandModule(Transform parent) { this.parent = parent; }

        public ModuleKind Kind => ModuleKind.LastCommand;

        public void Enter(ModuleContext moduleContext, RoomSnapshot snapshot)
        {
            context = moduleContext;
            latest = snapshot;
            Build();
            ApplySnapshot(snapshot);
            previousActive = Scenes.GetActiveScene();
            Machine.Hosted = true;
            load = Scenes.LoadSceneAsync(SceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            if (load == null) loading.text = "Сцена " + SceneName + " не добавлена в сборку";
            else load.completed += _ => Attach();
        }

        void Build()
        {
            root = UiKit.Stretch(UiKit.Rect("LastCommand", parent));
            curtain = root.gameObject.AddComponent<Image>();
            curtain.color = Color.black;
            curtain.raycastTarget = false;
            loading = UiKit.Label(root, "ЗАПУСК МАШИНЫ…", 44, new Color(.86f, .79f, .68f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Stretch(loading.rectTransform);
            loading.raycastTarget = false;

            var bar = UiKit.Rect("Controls", root);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(1, 0);
            bar.anchoredPosition = new Vector2(-28, 24);
            bar.sizeDelta = new Vector2(900, 110);
            var row = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.spacing = 14;
            row.childAlignment = TextAnchor.LowerRight;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            Control(bar, "ОБЗОР", () => machine?.Machine?.CameraRig?.Overview());
            Control(bar, "ДАЛЕЕ", () => machine?.Machine?.FocusNext());
            Control(bar, "ЗАВЕРШИТЬ", End);
        }

        static void Control(Transform bar, string text, System.Action click)
        {
            var button = ArtButton.Plate(bar, text, click, 30);
            UiKit.Size(button.gameObject, 250, 250 * button.AspectRatio);
        }

        void Attach()
        {
            machine = Machine.Instance;
            if (root == null) { Teardown(); return; } // left the module while loading
            if (machine == null) { loading.text = "Машина не запустилась (нет GameManager в сцене)"; return; }
            var scene = Scenes.GetSceneByName(SceneName);
            if (scene.IsValid()) Scenes.SetActiveScene(scene); // the machine's lighting, fog and skybox
            shellBackground = parent.parent != null ? parent.parent.Find("Background")?.gameObject : null;
            if (shellBackground != null) shellBackground.SetActive(false);
            var shell = GameObject.Find("ShellCamera");
            shellCamera = shell != null ? shell.GetComponent<Camera>() : null;
            if (shellCamera != null) shellCamera.enabled = false;

            var room = context.Room;
            machine.Network.AttachExternal((type, payload, requestId) =>
            {
                payload["roomCode"] = room.RoomCode; // the hosted RoomManager never joined, so it has no code
                return room.Send(type, payload);
            });
            room.EventReceived += Forward;
            if (latest != null) Forward(new Envelope("ROOM_STATE", "", latest.Raw));
            reveal = 1;
        }

        void Forward(Envelope envelope)
        {
            if (machine == null) return;
            machine.HostedReceive(new JObject
            {
                ["type"] = envelope.Type,
                ["requestId"] = envelope.RequestId,
                ["payload"] = envelope.Payload
            });
        }

        public void ApplySnapshot(RoomSnapshot snapshot)
        {
            latest = snapshot;
            sessionId = snapshot.LastCommand?.Value<string>("sessionId") ?? sessionId;
            if (!Debug.isDebugBuild || !(snapshot.LastCommand?["testRound"] is JObject round)) return;
            var line = "status=" + round.Value<string>("status") + " task=" + round.Value<string>("taskType") + " step=" + round.Value<string>("step")
                + " expanded=" + round.Value<string>("expanded") + " awaitingReady=" + round.Value<string>("awaitingReady")
                + " waitingForTerminals=" + round.Value<string>("waitingForTerminals") + " paused=" + round.Value<string>("paused")
                + " feedback=" + round.Value<string>("feedback");
            if (line != debugLine) Debug.Log("[LastCommand] " + (debugLine = line));
        }

        string debugLine = "";

        public void Tick(float localDelta, long serverNow)
        {
            if (reveal <= 0 || curtain == null) return;
            reveal = Mathf.Max(0, reveal - localDelta * 1.6f); // fade the black curtain off the 3D scene
            curtain.color = new Color(0, 0, 0, reveal);
            loading.color = new Color(loading.color.r, loading.color.g, loading.color.b, reveal);
            if (reveal <= 0) loading.gameObject.SetActive(false);
        }

        void End()
        {
            var payload = context.Room.RoomPayload();
            payload["sessionId"] = sessionId;
            context.Room.Send(MessageTypes.EndLastCommand, payload);
        }

        public void Exit()
        {
            if (root != null) UiKit.Dispose(root.gameObject);
            root = null;
            if (load == null || load.isDone) Teardown();
        }

        void Teardown()
        {
            if (context != null) context.Room.EventReceived -= Forward;
            if (Machine.Instance != null) Object.Destroy(Machine.Instance.gameObject); // DontDestroyOnLoad: not unloaded with the scene
            machine = null;
            Machine.Hosted = false;
            var scene = Scenes.GetSceneByName(SceneName);
            if (scene.IsValid() && scene.isLoaded) Scenes.UnloadSceneAsync(scene);
            if (previousActive.IsValid()) Scenes.SetActiveScene(previousActive);
            if (shellBackground != null) shellBackground.SetActive(true);
            if (shellCamera != null) shellCamera.enabled = true;
        }
    }
}
