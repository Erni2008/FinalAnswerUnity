using System.Collections.Generic;
using System.Linq;
using FinalAnswer.Application;
using FinalAnswer.Contracts;
using FinalAnswer.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.Impostor
{
    /// <summary>
    /// Host view of THE IMPOSTOR: the round table, a paper card per participant (their living character, name and
    /// the clues revealed so far), the phase timer, the final-vote stamps and the impostor reveal. Sounds and the
    /// recorded host lines follow the Java ImpostorScreen/VotingNarration cues. Secret word/role never appear here:
    /// the host snapshot does not carry them.
    /// </summary>
    public sealed class ImpostorModule : IGameModule
    {
        static readonly Vector2 Size = new Vector2(1672, 941);

        sealed class Card
        {
            public string Id;
            public RectTransform Root;
            public Image Paper, Stamp, Frame;
            public Text Name, Clues, Votes, Verdict;
            public Flipbook Character;
        }

        readonly Transform parent;
        readonly Dictionary<string, Card> cards = new Dictionary<string, Card>();
        readonly HashSet<string> played = new HashSet<string>();
        readonly Queue<string> narration = new Queue<string>();
        ModuleContext context;
        RoomSnapshot snapshot;
        JObject state;
        ArtCatalog art;
        SfxBank sfx;
        CageCharacterLibrary characters;
        RectTransform root, design, cardRow;
        Text title, phaseLabel, timer;
        ArtButton returnButton, replayButton;
        string sessionId = "", phaseKey = "";
        float time, stampClock;
        int stampsShown;

        public ImpostorModule(Transform parent) { this.parent = parent; }

        public ModuleKind Kind => ModuleKind.Impostor;

        public void Enter(ModuleContext moduleContext, RoomSnapshot first)
        {
            context = moduleContext;
            art = ArtCatalog.Active;
            characters = new CageCharacterLibrary(art);
            Build();
            ApplySnapshot(first);
        }

        void Build()
        {
            root = UiKit.Stretch(UiKit.Rect("Impostor", parent));
            root.gameObject.AddComponent<Image>().color = Color.black;
            design = UiKit.DesignFrame(root, Size.x, Size.y);
            sfx = new SfxBank(root, art, "ImpostorSfx");
            UiKit.Picture(design, "Table", art.Sprite("Art/Impostor/table"), 0, 0, Size.x, Size.y);
            var shade = UiKit.Picture(design, "Shade", null, 0, 0, Size.x, Size.y);
            shade.color = new Color(0, 0, 0, .35f);
            var display = art.Font("Fonts/RubikWetPaint-Regular");
            var body = art.Font("Fonts/NotoSans-SemiCondensedSemiBold");
            title = Label(display, 60, "DCC9AE", 0, Size.y - 96, Size.x, 80);
            title.text = "THE IMPOSTOR";
            phaseLabel = Label(body, 30, "F2EFE9", 0, Size.y - 140, Size.x, 40);
            var clock = UiKit.Picture(design, "Clock", art.Sprite("Art/Impostor/clockmini"), Size.x - 190, Size.y - 190, 150, 157);
            clock.preserveAspect = true;
            timer = Label(art.Font("Fonts/Creepster-Regular"), 54, "F2EFE9", Size.x - 190, Size.y - 190, 150, 157);
            cardRow = UiKit.Place(UiKit.Rect("Cards", design), 0, 110, Size.x, 640);
            returnButton = Button("В ЛОББИ", Size.x / 2 + 20, () => Host(MessageTypes.ImpostorReturnToLobby));
            replayButton = Button("ЕЩЁ РАЗ", Size.x / 2 - 340, () => Host((state?.Value<string>("phase") ?? "") == "REPLAY_PREPARATION" ? MessageTypes.StartImpostor : "REPLAY_IMPOSTOR"));
        }

        Text Label(Font font, int size, string color, float x, float y, float w, float h)
        {
            var label = UiKit.Label(design, "", size, GenerationCodeView.Hex(color), TextAnchor.MiddleCenter);
            label.font = font;
            label.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, .85f);
            UiKit.Place(label.rectTransform, x, y, w, h);
            return label;
        }

        ArtButton Button(string text, float x, System.Action action)
        {
            var button = ArtButton.Plate(design, text, action, 34);
            UiKit.Place(button.Rect, x, 14, 320, 320 * button.AspectRatio);
            button.gameObject.SetActive(false);
            return button;
        }

        void Host(string type)
        {
            var payload = context.Room.RoomPayload();
            payload["sessionId"] = sessionId;
            context.Room.Send(type, payload);
        }

        public void ApplySnapshot(RoomSnapshot next)
        {
            snapshot = next;
            state = next.Impostor;
            if (state == null) return;
            var id = state.Value<string>("sessionId") ?? "";
            if (id != sessionId)
            {
                sessionId = id;
                played.Clear();
                narration.Clear();
                phaseKey = "";
            }
            SyncCards();
            var phase = state.Value<string>("phase") ?? "";
            var round = state.Value<int?>("round") ?? 0;
            var key = phase + ":" + round;
            if (key != phaseKey)
            {
                phaseKey = key;
                OnPhase(phase, round);
            }
            phaseLabel.text = PhaseTitle(phase, round);
            RenderVotes(phase);
            var result = phase == "RESULT";
            if (phase == "REPLAY_PREPARATION") { returnButton.gameObject.SetActive(false); replayButton.gameObject.SetActive(true); SetText(replayButton, "НАЧАТЬ"); }
            else { returnButton.gameObject.SetActive(result); replayButton.gameObject.SetActive(result); SetText(replayButton, "ЕЩЁ РАЗ"); }
        }

        static void SetText(ArtButton button, string text) => button.Label.text = text;

        /// <summary>Sound cues and narration per phase (ImpostorScreen + VotingNarration), once per session/phase/round.</summary>
        void OnPhase(string phase, int round)
        {
            switch (phase)
            {
                case "ROLE_REVEAL":
                    sfx.Play("Audio/Impostor/impostor_role_reveal", .8f);
                    Say("impostor_intro");
                    break;
                case "ROUND_CLUE_INPUT":
                    Say("impostor_round_" + Mathf.Clamp(round, 1, 3));
                    break;
                case "ROUND_CLUE_REVEAL":
                    sfx.Play("Audio/Impostor/page_turn", .8f);
                    break;
                case "PRELIMINARY_SUSPICION":
                    Say("impostor_suspicion");
                    break;
                case "EVIDENCE_REVIEW":
                    Say("impostor_discussion");
                    break;
                case "FINAL_VOTING":
                    sfx.Play("Audio/Impostor/vote_started", .8f);
                    Say("impostor_final_voting");
                    break;
                case "VOTING_LOCKED":
                    sfx.Play("Audio/Impostor/voting_closed", .8f);
                    Say("impostor_vote_intro_1");
                    Say("impostor_vote_intro_2");
                    break;
                case "FINAL_VOTE_REVEAL":
                    stampsShown = 0;
                    stampClock = 0;
                    break;
                case "SUSPECT_ANNOUNCEMENT":
                    var leaders = state["leadingSuspects"] as JArray;
                    var submitted = state.Value<int?>("finalSubmittedCount") ?? 0;
                    Say(leaders != null && leaders.Count == 1 ? "impostor_vote_reveal_intro" : submitted > 0 ? "impostor_vote_tie" : "impostor_vote_no_votes");
                    break;
                case "IMPOSTOR_REVEAL":
                    sfx.Play("Audio/Impostor/card_role_impostor", .8f);
                    break;
                case "RESULT":
                    var outcome = state.Value<string>("outcome") ?? "";
                    if (outcome == "IMPOSTOR_CAUGHT") Say("impostor_outcome_caught");
                    else if (outcome == "IMPOSTOR_ESCAPED") Say("impostor_outcome_escaped");
                    else if (outcome == "NO_CONSENSUS") Say("impostor_vote_tie");
                    break;
            }
        }

        void Say(string line)
        {
            if (played.Add(sessionId + ":" + phaseKey + ":" + line)) narration.Enqueue(line);
        }

        void SyncCards()
        {
            if (!(state["participants"] is JArray participants)) return;
            var list = participants.OfType<JObject>().ToList();
            var cell = Mathf.Min(360, (Size.x - 80) / Mathf.Max(1, list.Count));
            var left = (Size.x - cell * list.Count) / 2;
            for (var i = 0; i < list.Count; i++)
            {
                var id = list[i].Value<string>("id");
                if (!cards.TryGetValue(id, out var card)) cards[id] = card = MakeCard(id, list[i].Value<string>("characterId") ?? "skeleton");
                card.Name.text = list[i].Value<string>("name") ?? "";
                UiKit.Place(card.Root, left + i * cell + 12, 0, cell - 24, 620);
            }
            // Clues revealed so far, newest round last.
            var clues = new Dictionary<string, List<string>>();
            if (state["revealedClues"] is JArray rounds)
                foreach (var round in rounds.OfType<JObject>())
                    foreach (var pair in round)
                    {
                        if (!clues.TryGetValue(pair.Key, out var words)) clues[pair.Key] = words = new List<string>();
                        words.Add(pair.Value.ToString());
                    }
            var ready = new HashSet<string>();
            if (state["clueReadyIds"] is JArray readyIds) foreach (var r in readyIds) ready.Add(r.ToString());
            var inputting = (state.Value<string>("phase") ?? "") == "ROUND_CLUE_INPUT";
            foreach (var card in cards.Values)
            {
                var words = clues.TryGetValue(card.Id, out var w) ? w : new List<string>();
                var text = string.Join("\n", words.Select((word, k) => (k + 1) + ".  " + word));
                if (inputting) text += (text.Length > 0 ? "\n" : "") + (ready.Contains(card.Id) ? "✔ подсказка готова" : "пишет…");
                card.Clues.text = text;
            }
        }

        Card MakeCard(string id, string character)
        {
            var card = new Card { Id = id, Root = UiKit.Rect("Card", cardRow) };
            card.Root.anchorMin = card.Root.anchorMax = Vector2.zero;
            card.Paper = UiKit.Stretch(UiKit.Rect("Paper", card.Root)).gameObject.AddComponent<Image>();
            card.Paper.sprite = art.Sprite("Art/Impostor/papper_player_front");
            card.Paper.raycastTarget = false;
            var portrait = UiKit.Rect("Portrait", card.Root);
            portrait.anchorMin = new Vector2(.18f, .52f);
            portrait.anchorMax = new Vector2(.82f, .9f);
            portrait.offsetMin = portrait.offsetMax = Vector2.zero;
            card.Character = characters.Create(portrait, character);
            card.Character.Image.preserveAspect = true;
            card.Frame = UiKit.Stretch(UiKit.Rect("Frame", portrait)).gameObject.AddComponent<Image>();
            card.Frame.sprite = art.Sprite("Art/Impostor/impostor_avatar_frame");
            card.Frame.preserveAspect = true;
            card.Frame.raycastTarget = false;
            card.Frame.gameObject.SetActive(false);
            var ink = new Color(.18f, .08f, .05f);
            card.Name = Ink(card.Root, art.Font("Fonts/RubikWetPaint-Regular"), 30, ink, .44f, .52f);
            card.Clues = Ink(card.Root, art.Font("Fonts/NotoSans-SemiCondensedSemiBold"), 24, ink, .12f, .44f);
            card.Clues.alignment = TextAnchor.UpperCenter;
            card.Votes = Ink(card.Root, art.Font("Fonts/Creepster-Regular"), 44, new Color(.55f, .05f, .05f), .05f, .16f);
            card.Votes.alignment = TextAnchor.MiddleLeft;
            card.Verdict = Ink(card.Root, art.Font("Fonts/RubikWetPaint-Regular"), 22, new Color(.62f, .04f, .04f), .575f, .625f);
            card.Verdict.text = "САМОЗВАНЕЦ";
            card.Verdict.gameObject.SetActive(false);
            card.Stamp = UiKit.Rect("Stamp", card.Root).gameObject.AddComponent<Image>();
            card.Stamp.sprite = art.Sprite("Art/Impostor/stamp");
            card.Stamp.preserveAspect = true;
            card.Stamp.raycastTarget = false;
            var s = card.Stamp.rectTransform;
            s.anchorMin = new Vector2(.55f, .05f);
            s.anchorMax = new Vector2(.98f, .3f);
            s.offsetMin = s.offsetMax = Vector2.zero;
            card.Stamp.gameObject.SetActive(false);
            return card;
        }

        static Text Ink(Transform parent, Font font, int size, Color color, float bottom, float top)
        {
            var label = UiKit.Label(parent, "", size, color, TextAnchor.MiddleCenter);
            label.font = font;
            var r = label.rectTransform;
            r.anchorMin = new Vector2(.12f, bottom);
            r.anchorMax = new Vector2(.88f, top);
            r.offsetMin = r.offsetMax = Vector2.zero;
            return label;
        }

        void RenderVotes(string phase)
        {
            var counts = state["finalVoteSnapshot"]?["counts"] as JObject;
            var impostor = state.Value<string>("revealedImpostorId") ?? "";
            foreach (var card in cards.Values)
            {
                var votes = counts?.Value<int?>(card.Id) ?? 0;
                card.Votes.text = counts != null && votes > 0 ? votes + " ✕" : "";
                var unmasked = impostor.Length > 0 && card.Id == impostor;
                card.Frame.gameObject.SetActive(unmasked);
                card.Paper.color = unmasked ? new Color(1, .75f, .7f) : Color.white;
                card.Verdict.gameObject.SetActive(unmasked);
                card.Verdict.transform.SetAsLastSibling();
            }
        }

        public void Tick(float delta, long serverNow)
        {
            if (state == null) return;
            time += delta;
            foreach (var card in cards.Values) card.Character.Time = 2.6f + time; // idle loop, entrance already played in the lobby

            var deadline = state.Value<long?>("deadline") ?? 0;
            var remaining = deadline > 0 ? Mathf.Max(0, (deadline - serverNow) / 1000f) : 0;
            timer.text = remaining > 0 ? Mathf.CeilToInt(remaining).ToString() : "";
            var phase = state.Value<string>("phase") ?? "";
            if (phase == "ROUND_CLUE_INPUT" && remaining > 0 && remaining <= 5) Say("impostor_round_deadline");

            // Pens scratching while someone is typing (host-only writingUntil).
            var writing = (state.Value<long?>("writingUntil") ?? 0) > serverNow && phase == "ROUND_CLUE_INPUT";
            const string pen = "Audio/Impostor/writing_words_on_paper_theimpostor";
            if (writing && !sfx.IsPlaying(pen)) sfx.Play(pen, .5f, 0, true);
            else if (!writing && sfx.IsPlaying(pen)) sfx.Stop(pen);

            // Final vote reveal: one stamp hit per voted suspect, half a second apart (vote impacts are SFX only).
            if (phase == "FINAL_VOTE_REVEAL" && state["finalVoteSnapshot"]?["stampTargets"] is JArray targets)
            {
                stampClock += delta;
                while (stampsShown < targets.Count && stampClock >= stampsShown * .5f)
                {
                    var target = targets[stampsShown] is JObject o ? o.Value<string>("suspectId") ?? o.Value<string>("playerId") : targets[stampsShown].ToString();
                    if (target != null && cards.TryGetValue(target, out var card)) card.Stamp.gameObject.SetActive(true);
                    sfx.Play("Audio/Impostor/wooden_stumb_hit", .8f);
                    stampsShown++;
                }
            }

            var voice = HostVoice.Current;
            if (voice != null && !voice.Busy && narration.Count > 0) voice.Play("events", narration.Dequeue());
        }

        public void Exit()
        {
            sfx?.Dispose();
            if (root != null) UiKit.Dispose(root.gameObject);
            root = null;
        }

        static string PhaseTitle(string phase, int round)
        {
            switch (phase)
            {
                case "ROLE_REVEAL": return "РОЛИ РАЗДАНЫ · ПРОВЕРЬТЕ ТЕЛЕФОНЫ";
                case "ROUND_CLUE_INPUT": return "РАУНД " + round + " · НАПИШИТЕ ПОДСКАЗКУ";
                case "ROUND_CLUE_REVEAL": return "РАУНД " + round + " · ПОДСКАЗКИ";
                case "PRELIMINARY_SUSPICION": return "РАУНД " + round + " · КОГО ВЫ ПОДОЗРЕВАЕТЕ?";
                case "EVIDENCE_REVIEW": return "ОБСУЖДЕНИЕ";
                case "FINAL_VOTING": return "ФИНАЛЬНОЕ ГОЛОСОВАНИЕ";
                case "VOTING_LOCKED": return "ГОЛОСА ПРИНЯТЫ";
                case "FINAL_VOTE_REVEAL": return "ПОДСЧЁТ ГОЛОСОВ";
                case "SUSPECT_ANNOUNCEMENT": return "ПОДОЗРЕВАЕМЫЙ";
                case "IMPOSTOR_REVEAL": return "КТО ЖЕ САМОЗВАНЕЦ?";
                case "RESULT": return "ИТОГ";
                case "REPLAY_PREPARATION": return "НОВАЯ ПАРТИЯ";
                default: return phase;
            }
        }
    }
}
