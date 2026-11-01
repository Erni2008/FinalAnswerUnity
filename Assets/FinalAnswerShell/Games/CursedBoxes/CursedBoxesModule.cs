using System.Collections.Generic;
using System.Linq;
using System.Text;
using FinalAnswer.Application;
using FinalAnswer.Contracts;
using FinalAnswer.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.CursedBoxes
{
    /// <summary>
    /// Text-first host view of Cursed Boxes. Phones choose; the host confirms the existing animation milestones
    /// (CURSED_SCENE_READY, CURSED_SHOT_STARTED/IMPACT/COMPLETE per shotId). The 3D crate scene replaces the
    /// timings below in a later stage without changing the commands.
    /// </summary>
    public sealed class CursedBoxesModule : IGameModule
    {
        const float ShotFlightSeconds = 0.6f;
        const float ShotRevealSeconds = 1.2f;
        const int BoxCount = 16;

        readonly Transform parent;
        readonly HashSet<string> sentMilestones = new HashSet<string>();
        ModuleContext context;
        JObject state;
        RectTransform root;
        Text title, status, details;
        readonly Image[] boxes = new Image[BoxCount];
        readonly Text[] boxLabels = new Text[BoxCount];
        Button finish;
        string sessionId = "";
        string shotKey = "";
        float shotTimer;

        public CursedBoxesModule(Transform parent) { this.parent = parent; }

        public ModuleKind Kind => ModuleKind.CursedBoxes;

        public void Enter(ModuleContext moduleContext, RoomSnapshot snapshot)
        {
            context = moduleContext;
            context.Room.CommandRejected += OnRejected;
            Build();
            ApplySnapshot(snapshot);
        }

        public void ApplySnapshot(RoomSnapshot snapshot)
        {
            state = snapshot.CursedBoxes;
            if (state == null) return;
            var id = state.Value<string>("sessionId") ?? "";
            if (id != sessionId)
            {
                // New session: semantic caches never carry over.
                sessionId = id;
                sentMilestones.Clear();
                shotKey = "";
            }
            Render(snapshot);
        }

        public void Tick(float localDelta, long serverNow)
        {
            if (state == null) return;
            if (!(state.Value<bool?>("hostReady") ?? false)) SendOnce(MessageTypes.CursedSceneReady, "ready", null);

            if (state["pendingShot"] is JObject shot)
            {
                var id = shot.Value<string>("id") ?? "";
                var started = shot.Value<long?>("startedAt") ?? 0;
                var impacted = shot.Value<long?>("impactedAt") ?? 0;
                var stage = started == 0 ? MessageTypes.CursedShotStarted : impacted == 0 ? MessageTypes.CursedShotImpact : MessageTypes.CursedShotComplete;
                var key = id + ":" + stage;
                if (key != shotKey) { shotKey = key; shotTimer = 0; }
                shotTimer += localDelta;
                var wait = stage == MessageTypes.CursedShotStarted ? 0 : stage == MessageTypes.CursedShotImpact ? ShotFlightSeconds : ShotRevealSeconds;
                if (shotTimer >= wait) SendOnce(stage, key, id);
            }
        }

        public void Exit()
        {
            if (context != null) context.Room.CommandRejected -= OnRejected;
            if (root != null) UiKit.Dispose(root.gameObject);
            root = null;
        }

        void SendOnce(string type, string key, string shotId)
        {
            if (!sentMilestones.Add(type + ":" + key)) return;
            var payload = context.Room.RoomPayload();
            payload["sessionId"] = sessionId;
            if (shotId != null) payload["shotId"] = shotId;
            if (context.Room.Send(type, payload) == null) sentMilestones.Remove(type + ":" + key);
        }

        void OnRejected(CommandRejection rejection)
        {
            if (!rejection.CommandType.StartsWith("CURSED_", System.StringComparison.Ordinal)) return;
            // Allow the milestone to be confirmed again once a fresh snapshot arrives.
            sentMilestones.RemoveWhere(k => k.StartsWith(rejection.CommandType, System.StringComparison.Ordinal));
            status.text = "Сервер отклонил " + rejection.CommandType + ": " + rejection.Code;
        }

        public bool IsFinished => state != null && state.Value<string>("phase") == "FINISHED";

        /// <summary>The host's "continue" action (Java: exitNetworkCursed). Inside a quiz it resumes the next question.</summary>
        public void ReturnToLobby()
        {
            if (!IsFinished) return;
            SendOnce(MessageTypes.CursedReturnToLobby, "return", null);
            finish.interactable = false;
        }

        void Build()
        {
            root = UiKit.Stretch(UiKit.Rect("CursedBoxes", parent), 70);
            var column = root.gameObject.AddComponent<VerticalLayoutGroup>();
            column.spacing = 18;
            column.childControlWidth = column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            title = UiKit.Label(root, "CURSED BOXES", 60, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(title.gameObject, height: 80);
            status = UiKit.Label(root, "", 32, Theme.Text, TextAnchor.MiddleCenter);
            UiKit.Size(status.gameObject, height: 56);

            for (var row = 0; row < 4; row++)
            {
                var line = UiKit.Row(root, "Boxes" + row, 110, 16);
                for (var col = 0; col < 4; col++)
                {
                    var i = row * 4 + col;
                    var cell = UiKit.Rect("Box" + i, line);
                    boxes[i] = cell.gameObject.AddComponent<Image>();
                    boxLabels[i] = UiKit.Label(cell, "", 26, Theme.Text, TextAnchor.MiddleCenter);
                    UiKit.Stretch(boxLabels[i].rectTransform, 6);
                }
            }
            details = UiKit.Label(root, "", 26, Theme.Muted, TextAnchor.UpperCenter);
            UiKit.Size(details.gameObject, height: 120, flexibleHeight: 1);
            finish = UiKit.Button(root, "ПРОДОЛЖИТЬ", ReturnToLobby, primary: true, height: 80);
        }

        void Render(RoomSnapshot snapshot)
        {
            var phase = state.Value<string>("phase") ?? "";
            var active = state.Value<string>("activePlayerName") ?? "";
            status.text = PhaseTitle(phase) + (active.Length > 0 ? "   ·   ход: " + active : "");

            var contents = new Dictionary<int, string>();
            if (state["hostContents"] is JArray hidden)
                foreach (var item in hidden.OfType<JObject>())
                    contents[item.Value<int>("boxId")] = item.Value<string>("nickname");

            var states = new Dictionary<int, JObject>();
            if (state["boxes"] is JArray list)
                foreach (var item in list.OfType<JObject>()) states[item.Value<int>("id")] = item;

            for (var i = 0; i < BoxCount; i++)
            {
                states.TryGetValue(i, out var box);
                var boxState = box?.Value<string>("state") ?? "AVAILABLE";
                var destroyed = boxState == "DESTROYED";
                boxes[i].color = destroyed ? Theme.Disabled : boxState == "LOCKED" ? Theme.Warning : Theme.Button;
                var label = (i + 1).ToString();
                if (destroyed) label += box.Value<bool?>("empty") == false ? "\n" + box.Value<string>("nickname") : "\nпусто";
                else if (contents.TryGetValue(i, out var who)) label += "\n<" + who + ">"; // host-only, never sent to phones
                boxLabels[i].text = label;
            }

            var text = new StringBuilder();
            text.Append("Осталось выстрелов: ").Append(state.Value<int?>("remainingShotCount") ?? state.Value<int?>("shotsRemaining") ?? 0);
            var notice = state.Value<string>("notice");
            if (!string.IsNullOrEmpty(notice)) text.Append("   ·   ").Append(notice);
            if (state["outcomes"] is JArray outcomes)
            {
                text.Append('\n');
                foreach (var outcome in outcomes.OfType<JObject>())
                {
                    var id = outcome.Value<string>("playerId");
                    var name = id;
                    foreach (var p in snapshot.Players) if (p.Id == id) name = p.Name;
                    text.Append(name).Append(outcome.Value<bool>("survived") ? " — выжил   " : " — призрак   ");
                }
            }
            details.text = text.ToString();
            var finished = phase == "FINISHED";
            finish.gameObject.SetActive(finished);
            if (finished) finish.interactable = !sentMilestones.Contains(MessageTypes.CursedReturnToLobby + ":return");
        }

        static string PhaseTitle(string phase)
        {
            switch (phase)
            {
                case "PREPARING": return "ПОДГОТОВКА";
                case "HIDING_SELECTION": return "ИГРОКИ ПРЯЧУТСЯ";
                case "TARGET_SELECTION": return "ВЫБОР ЦЕЛЕЙ";
                case "SELECTING": return "ВЫБОР КОРОБКИ";
                case "FIRING": return "ВЫСТРЕЛ";
                case "ELIMINATION": return "ВЫБЫВАНИЕ";
                case "REVEALING": return "РАСКРЫТИЕ";
                case "FINISHED": return "ИТОГ";
                default: return phase;
            }
        }
    }
}
