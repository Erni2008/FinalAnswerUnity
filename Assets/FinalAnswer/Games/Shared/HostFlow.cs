using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace FinalAnswer
{
    public enum GameKind { Quiz, CursedBoxes, Impostor, LastCommand }

    // Only host presentation acknowledgements belong here. Answers and scores remain on Java.
    public sealed class HostFlow
    {
        readonly HashSet<string> acknowledged = new HashSet<string>();
        string room = "";
        public static GameKind Resolve(JObject state, GameKind fallback)
        {
            if (state.Obj("lastCommand") != null) return GameKind.LastCommand;
            if (state.Obj("cursedBoxes") != null) return GameKind.CursedBoxes;
            if (state.Obj("impostor") != null) return GameKind.Impostor;
            return state.S("gameId") != "" ? GameKind.Quiz : fallback;
        }
        public static string Scene(GameKind game) => game == GameKind.LastCommand ? "TheLastCommand" : "GameHall";
        public void Tick(JObject state, long now, bool ready, Action<string, JObject> send)
        {
            if (state == null || !state.B("host") || !ready) return;
            if (room != state.S("roomId")) { room = state.S("roomId"); acknowledged.Clear(); }
            var cursed = state.Obj("cursedBoxes");
            if (cursed != null)
            {
                string session = cursed.S("sessionId");
                var body = new JObject { { "roomCode", state.S("code") }, { "sessionId", session } };
                if (!cursed.B("hostReady")) Once("CURSED_SCENE_READY", session + ":" + state.L("revision"), body, send);
                var shot = cursed.Obj("pendingShot");
                if (shot != null && cursed.B("hostReady"))
                {
                    body["shotId"] = shot.S("id");
                    if (shot.L("startedAt") == 0) Once("CURSED_SHOT_STARTED", shot.S("id"), body, send);
                    else if (shot.L("impactedAt") == 0 && now >= shot.L("startedAt") + 30)
                        Once("CURSED_SHOT_IMPACT", shot.S("id"), body, send);
                    else if (shot.L("impactedAt") > 0 && now >= shot.L("startedAt") + 1100)
                        Once("CURSED_SHOT_COMPLETE", shot.S("id"), body, send);
                }
                return;
            }
            if (state.Obj("impostor") != null || state.S("gameId") == "") return;
            string question = state.Obj("question").S("id"), phase = state.S("phase");
            var payload = new JObject { { "roomCode", state.S("code") }, { "gameId", state.S("gameId") }, { "questionId", question } };
            string key = state.S("gameId") + ":" + question;
            // 35 glyphs/second; host reveals all four answer cards before opening phone input.
            int glyphs = state.Obj("question").S("text").Length;
            foreach (var answer in state.Obj("question").Arr("answers")) glyphs += answer.ToString().Length;
            if (phase == "QUESTION_PRESENTATION" && now >= state.L("phaseStartedAt") + PresentationMs(glyphs))
                Once("PRESENTATION_READY", key, payload, send);
            if (phase == "REVEALING" && now >= state.L("answeringEndedAt") + 720)
                Once("REVEAL_READY", key, payload, send);
            if (phase == "RESULTS" && now >= state.L("phaseStartedAt") + 3100)
                Once("NEXT_READY", key, payload, send);
        }
        public static long PresentationMs(int glyphs) => 700 + (long)Math.Ceiling(glyphs * 1000.0 / 35);
        void Once(string type, string key, JObject payload, Action<string, JObject> send)
        {
            if (acknowledged.Add(type + ":" + key)) send(type, (JObject)payload.DeepClone());
        }
        // A reconnect/error can have interrupted delivery; server milestones are idempotent.
        public void Retry() => acknowledged.Clear();
    }
}
