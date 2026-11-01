using FinalAnswer.Application;
using FinalAnswer.Contracts;
using FinalAnswer.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.PendingModules
{
    /// <summary>
    /// Honest placeholder for games whose Unity presentation is a later migration stage
    /// (THE IMPOSTOR: stage 6, THE LAST COMMAND: stages 3–5). It shows the authoritative server phase
    /// so the full room lifecycle can be verified, and exposes only the existing host exit command.
    /// </summary>
    public sealed class PendingModule : IGameModule
    {
        readonly Transform parent;
        readonly ModuleKind kind;
        ModuleContext context;
        RectTransform root;
        Text phase;
        Button exit;
        string sessionId = "";
        bool canExit;

        public PendingModule(Transform parent, ModuleKind kind)
        {
            this.parent = parent;
            this.kind = kind;
        }

        public ModuleKind Kind => kind;

        public void Enter(ModuleContext moduleContext, RoomSnapshot snapshot)
        {
            context = moduleContext;
            root = UiKit.Panel(parent, kind.ToString(), new Vector2(1300, 640));
            var title = kind == ModuleKind.Impostor ? "THE IMPOSTOR" : "THE LAST COMMAND";
            UiKit.Size(UiKit.Label(root, title, 72, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold).gameObject, height: 110);
            var stage = kind == ModuleKind.Impostor ? "этап 6" : "этапы 3–5";
            UiKit.Size(UiKit.Label(root, "Сервер ведёт игру, телефоны работают как раньше.\nUnity-представление этого модуля переносится на " + stage
                + " (docs/migration/MIGRATION_PLAN.md).", 28, Theme.Muted, TextAnchor.MiddleCenter).gameObject, height: 120);
            phase = UiKit.Label(root, "", 40, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(phase.gameObject, height: 140);
            exit = UiKit.Button(root, kind == ModuleKind.Impostor ? "ВЕРНУТЬСЯ В ЛОББИ" : "ЗАВЕРШИТЬ LAST COMMAND", Exit_Click, primary: true, height: 84);
            ApplySnapshot(snapshot);
        }

        public void ApplySnapshot(RoomSnapshot snapshot)
        {
            var mini = kind == ModuleKind.Impostor ? snapshot.Impostor : snapshot.LastCommand;
            if (mini == null) return;
            sessionId = mini.Value<string>("sessionId") ?? "";
            var serverPhase = mini.Value<string>("phase") ?? mini.Value<string>("status") ?? Describe(mini);
            phase.text = "Фаза сервера: " + serverPhase;
            // Impostor may only return from RESULT (RoomService); Last Command can be ended by the host at any time.
            canExit = kind == ModuleKind.LastCommand || serverPhase == "RESULT";
            exit.interactable = canExit;
        }

        public void Tick(float localDelta, long serverNow) { }

        public void Exit()
        {
            if (root != null) UiKit.Dispose(root.gameObject);
            root = null;
        }

        void Exit_Click()
        {
            if (!canExit) return;
            var payload = context.Room.RoomPayload();
            payload["sessionId"] = sessionId;
            context.Room.Send(kind == ModuleKind.Impostor ? MessageTypes.ImpostorReturnToLobby : MessageTypes.EndLastCommand, payload);
        }

        static string Describe(JObject mini)
        {
            var race = mini["race"] as JObject;
            return race?.Value<string>("status") ?? "IN_PROGRESS";
        }
    }
}
