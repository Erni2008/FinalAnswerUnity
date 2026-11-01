using System;
using FinalAnswer.Application;
using FinalAnswer.Contracts;
using FinalAnswer.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Machine = FinalAnswer.GameManager;
using Scenes = UnityEngine.SceneManagement.SceneManager;

namespace FinalAnswer.LastCommandHost
{
    /// <summary>
    /// Hosts the original 3D machine (Assets/FinalAnswer/Scenes/TheLastCommand) inside the shell. The scene is
    /// loaded additively and its GameManager runs in Hosted mode: no socket of its own — its commands
    /// (CLIENT_READY, TOUCH_PANEL …) go out through the shell's RoomManager and every server event is forwarded
    /// back, so phones and the server see one host connection. With a single phone the server mirrors that
    /// player's terminal to the host, so the machine shows and accepts input for the same task as the phone.
    /// </summary>
    public sealed class LastCommandModule : IGameModule
    {
        public const string SceneName = "TheLastCommand";

        readonly Transform parent;
        readonly Action requestRestart;
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
        DebugPanel debug;
        RoundLog log;

        /// <param name="requestRestart">Called before ending the session when the developer asks for a full restart.</param>
        public LastCommandModule(Transform parent, Action requestRestart = null)
        {
            this.parent = parent;
            this.requestRestart = requestRestart;
        }

        public ModuleKind Kind => ModuleKind.LastCommand;

        public void Enter(ModuleContext moduleContext, RoomSnapshot snapshot)
        {
            context = moduleContext;
            latest = snapshot;
            log = new RoundLog();
            Build();
            ApplySnapshot(snapshot);
            previousActive = Scenes.GetActiveScene();
            Machine.Hosted = true;
            Debug.Log("[LastCommand] loading scene " + SceneName);
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

        static void Control(Transform bar, string text, Action click)
        {
            var button = ArtButton.Plate(bar, text, click, 30);
            UiKit.Size(button.gameObject, 250, 250 * button.AspectRatio);
        }

        void Attach()
        {
            machine = Machine.Instance;
            if (root == null) { Teardown(); return; } // left the module while loading
            if (machine == null) { loading.text = "Машина не запустилась (нет GameManager в сцене)"; Debug.LogError("[LastCommand] scene has no GameManager"); return; }
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
            Debug.Log("[LastCommand] machine attached (scene loaded, hosted network bound)");
            if (Debug.isDebugBuild) debug = new DebugPanel(root, Send, Restart, End);
            debug?.Apply(latest);
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
            log?.Observe(snapshot.LastCommand);
            debug?.Apply(snapshot);
        }

        public void Tick(float localDelta, long serverNow)
        {
            if (machine != null)
            {
                var mix = ShellAudio.Current; // machine cues follow the shell mixer like every other game
                machine.Audio.SetHostedVolume(mix == null ? .8f : mix.Muted ? 0 : mix.Master * mix.Sfx);
            }
            if (debug != null && Keyboard.current != null && Keyboard.current.f4Key.wasPressedThisFrame) debug.Toggle();
            if (reveal <= 0 || curtain == null) return;
            reveal = Mathf.Max(0, reveal - localDelta * 1.6f); // fade the black curtain off the 3D scene
            curtain.color = new Color(0, 0, 0, reveal);
            loading.color = new Color(loading.color.r, loading.color.g, loading.color.b, reveal);
            if (reveal <= 0) loading.gameObject.SetActive(false);
        }

        /// <summary>LAST_COMMAND_ACTION addressed to the current task (DEBUG commands are host-only on the server).</summary>
        void Send(string element, string action, string value)
        {
            var round = latest?.LastCommand?["testRound"] as JObject;
            if (round == null) return;
            var payload = context.Room.RoomPayload();
            payload["sessionId"] = sessionId;
            payload["taskId"] = round.Value<string>("taskId") ?? "";
            payload["element"] = element;
            payload["action"] = action;
            payload["value"] = value ?? "";
            Debug.Log("[LastCommand] debug → " + action + (string.IsNullOrEmpty(value) ? "" : " " + value));
            context.Room.Send(MessageTypes.LastCommandAction, payload);
        }

        void Restart()
        {
            requestRestart?.Invoke();
            End();
        }

        void End()
        {
            var payload = context.Room.RoomPayload();
            payload["sessionId"] = sessionId;
            Debug.Log("[LastCommand] → END_LAST_COMMAND");
            context.Room.Send(MessageTypes.EndLastCommand, payload);
        }

        public void Exit()
        {
            debug = null;
            if (root != null) UiKit.Dispose(root.gameObject);
            root = null;
            if (load == null || load.isDone) Teardown();
        }

        void Teardown()
        {
            if (context != null) context.Room.EventReceived -= Forward;
            if (Machine.Instance != null) UnityEngine.Object.Destroy(Machine.Instance.gameObject); // DontDestroyOnLoad: not unloaded with the scene
            machine = null;
            Machine.Hosted = false;
            var scene = Scenes.GetSceneByName(SceneName);
            if (scene.IsValid() && scene.isLoaded) Scenes.UnloadSceneAsync(scene);
            if (previousActive.IsValid()) Scenes.SetActiveScene(previousActive);
            if (shellBackground != null) shellBackground.SetActive(true);
            if (shellCamera != null) shellCamera.enabled = true;
            Debug.Log("[LastCommand] scene unloaded, lobby restored");
        }

        /// <summary>One log line per meaningful change: readiness, new task, verdict, progress, completion.</summary>
        sealed class RoundLog
        {
            string ready = "", task = "", verdict = "", status = "";

            public void Observe(JObject panel)
            {
                if (!(panel?["testRound"] is JObject r)) return;
                var mirror = panel.Value<string>("mirroring");
                var nextReady = "machineReady=" + !(r.Value<bool?>("awaitingReady") ?? false) + " terminals=" + !(r.Value<bool?>("waitingForTerminals") ?? false);
                if (nextReady != ready) Debug.Log("[LastCommand] readiness " + (ready = nextReady) + (string.IsNullOrEmpty(mirror) ? "" : " (solo mirror)"));
                var nextTask = r.Value<string>("taskId") + ":" + r.Value<string>("taskType");
                if (nextTask != task && !string.IsNullOrEmpty(r.Value<string>("taskType")))
                    Debug.Log("[LastCommand] task " + r.Value<string>("taskType") + " (" + r.Value<string>("category") + ") step " + r.Value<int?>("step") + "/20 id " + (task = nextTask));
                var feedback = r.Value<string>("feedback") ?? "";
                var nextVerdict = feedback.Length > 0 ? r.Value<string>("taskId") + ":" + feedback : "";
                if (nextVerdict != verdict && feedback.Length > 0)
                    Debug.Log("[LastCommand] verdict " + feedback + " → progress " + r.Value<int?>("step") + "/20, mistakes " + r.Value<int?>("mistakes"));
                verdict = nextVerdict;
                var nextStatus = r.Value<string>("status") ?? "";
                if (nextStatus != status) Debug.Log("[LastCommand] status " + (status = nextStatus) + (nextStatus == "WON" ? " — completion sequence" : ""));
            }
        }

        /// <summary>
        /// Developer panel (development builds, server started with lastcommand.debug). Shows the server's host-only debug
        /// view of the current task and sends DEBUG commands; F4 hides it completely.
        /// </summary>
        sealed class DebugPanel
        {
            readonly RectTransform panel;
            readonly Text info, familyLabel, pauseLabel;
            readonly Action<string, string, string> send;
            JArray families = new JArray();
            int familyIndex;
            int score;
            bool paused, visible = true, available;

            public DebugPanel(RectTransform parent, Action<string, string, string> send, Action restart, Action lobby)
            {
                this.send = send;
                panel = UiKit.Rect("LastCommandDebug", parent);
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0, 1);
                panel.anchoredPosition = new Vector2(20, -20);
                panel.sizeDelta = new Vector2(520, 0);
                panel.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .78f);
                var column = panel.gameObject.AddComponent<VerticalLayoutGroup>();
                column.padding = new RectOffset(16, 16, 12, 14);
                column.spacing = 6;
                column.childControlWidth = column.childControlHeight = true;
                column.childForceExpandHeight = false;
                panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                UiKit.Label(panel, "LAST COMMAND · DEBUG  (F4 — скрыть)", 20, Theme.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
                info = UiKit.Label(panel, "", 17, Theme.Text);
                info.horizontalOverflow = HorizontalWrapMode.Wrap;

                var family = Row();
                Small(family, "◀", () => Cycle(-1), 50);
                familyLabel = UiKit.Label(family, "—", 18, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
                UiKit.Size(familyLabel.gameObject, 200, 40);
                Small(family, "▶", () => Cycle(1), 50);
                Small(family, "МОДУЛЬ", () => { if (families.Count > 0) send("DEBUG", "FAMILY", (string)families[familyIndex]); }, 110);
                Small(family, "АВТО", () => send("DEBUG", "FAMILY", ""), 70);

                var task = Row();
                Small(task, "НОВОЕ ЗАДАНИЕ", () => send("DEBUG", "NEW_TASK", ""), 238);
                Small(task, "ПОВТОР (ТЕ ЖЕ)", () => send("DEBUG", "RESTART_TASK", ""), 238);

                var progress = Row();
                Small(progress, "СЧЁТ −1", () => send("DEBUG", "SET_SCORE", Mathf.Max(0, score - 1).ToString()), 115);
                Small(progress, "+1", () => send("DEBUG", "SET_SCORE", Mathf.Min(19, score + 1).ToString()), 60);
                Small(progress, "→19", () => send("DEBUG", "SET_SCORE", "19"), 70);
                Small(progress, "СБРОС 0", () => send("DEBUG", "SET_SCORE", "0"), 110);
                Small(progress, "ФИНАЛ", () => send("DEBUG", "COMPLETE", ""), 110);

                var session = Row();
                pauseLabel = Small(session, "ПАУЗА", () => send("DEBUG", paused ? "RESUME" : "PAUSE", ""), 120).GetComponentInChildren<Text>();
                Small(session, "ПЕРЕЗАПУСК", restart, 170);
                Small(session, "В ЛОББИ", lobby, 180);
                panel.gameObject.SetActive(false);
            }

            RectTransform Row()
            {
                var row = UiKit.Rect("Row", panel);
                var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                layout.spacing = 6;
                layout.childControlWidth = layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                UiKit.Size(row.gameObject, height: 44);
                return row;
            }

            static Button Small(Transform row, string text, Action click, float width)
            {
                var button = UiKit.Button(row, text, click, false, 42, 17);
                UiKit.Size(button.gameObject, width, 42);
                return button;
            }

            void Cycle(int step)
            {
                if (families.Count == 0) return;
                familyIndex = (familyIndex + step + families.Count) % families.Count;
                familyLabel.text = (string)families[familyIndex];
            }

            public void Toggle()
            {
                visible = !visible;
                panel.gameObject.SetActive(visible && available);
            }

            public void Apply(RoomSnapshot snapshot)
            {
                var lc = snapshot?.LastCommand;
                var view = lc?["debug"] as JObject;
                available = view != null;
                panel.gameObject.SetActive(visible && available);
                if (view == null) return;
                var r = lc["testRound"] as JObject;
                if (families.Count == 0 && view["families"] is JArray list && list.Count > 0) { families = list; familyLabel.text = (string)families[0]; }
                score = view.Value<int?>("score") ?? 0;
                paused = view.Value<bool?>("paused") ?? false;
                pauseLabel.text = paused ? "ПРОДОЛЖИТЬ" : "ПАУЗА";
                var steps = view["steps"] as JArray;
                info.text = "Модуль: " + view.Value<string>("family") + " (" + view.Value<string>("category") + ")"
                    + (string.IsNullOrEmpty(view.Value<string>("previewFamily")) ? "" : "  · закреплён " + view.Value<string>("previewFamily"))
                    + "\nЗадание: " + view.Value<string>("taskId")
                    + "\nОтвет: " + (string.IsNullOrEmpty(view.Value<string>("answer")) ? "по состоянию механизмов (цепь, давление, частота…)" : view.Value<string>("answer")) + (steps != null && steps.Count > 0 ? "\nШаги: " + string.Join(" → ", steps) : "")
                    + "\nСид задания: " + view.Value<string>("taskSeed") + "\nСид гонки: " + view.Value<string>("raceSeed")
                    + "\nПрогресс: " + score + "/20 · ошибок " + view.Value<int?>("failures") + " · #" + view.Value<int?>("serial")
                    + "\nСостояние: " + r?.Value<string>("status") + (r?.Value<bool?>("waitingForTerminals") == true ? " · ждём терминалы" : "")
                    + (paused ? " · ПАУЗА" : "") + (string.IsNullOrEmpty(r?.Value<string>("feedback")) ? "" : " · " + r.Value<string>("feedback"))
                    + (string.IsNullOrEmpty(lc.Value<string>("mirroring")) ? "\nРежим: мультиплеер" : "\nРежим: соло (машина = терминал игрока)");
            }
        }
    }
}
