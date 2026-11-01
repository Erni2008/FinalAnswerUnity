using System.Collections.Generic;
using System.Linq;
using FinalAnswer.Application;
using FinalAnswer.Contracts;
using FinalAnswer.UI;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.Quiz
{
    /// <summary>
    /// Host presentation of the quiz (port of screens/PlayScreen + NetworkRoundAdapter, 1600×900 design space).
    /// Local time only animates; the server owns the answer window, the key and every phase change. The host
    /// confirms the existing barriers after the matching frame was shown:
    /// PRESENTATION_READY (answers on screen) → REVEAL_READY (clock gone) → NEXT_READY (result hold elapsed).
    /// </summary>
    public sealed class QuizModule : IGameModule
    {
        static readonly Vector2 Size = new Vector2(1600, 900);
        const long ResendMs = 750;
        const long ResultHoldMs = 600 + 2500; // Protocol.RESULT_ANIMATION_MILLIS + RESULT_HOLD_MILLIS

        readonly Transform parent;
        readonly QuizTimeline timeline = new QuizTimeline();
        ModuleContext context;
        RoomSnapshot snapshot;
        RectTransform root;
        QuizBackgroundView background;
        QuestionView question;
        readonly AnswerView[] answers = new AnswerView[4];
        ClockView clock;
        PlayersRow players;
        QuizAudio audio;
        Text heading, status;
        FinalScoreboard scoreboard;
        Localization texts;
        long lastReadyAt;
        bool reduced;

        public QuizModule(Transform parent) { this.parent = parent; }

        public ModuleKind Kind => ModuleKind.Quiz;

        public void Enter(ModuleContext moduleContext, RoomSnapshot first)
        {
            context = moduleContext;
            texts = Localization.Current;
            var art = ArtCatalog.Active;
            reduced = PlayerPrefs.GetInt("fa.v1.reducedMotion", 0) == 1;

            root = UiKit.Stretch(UiKit.Rect("Quiz", parent));
            root.gameObject.AddComponent<Image>().color = new Color(9 / 255f, 9 / 255f, 9 / 255f);
            var design = UiKit.DesignFrame(root, Size.x, Size.y);
            background = new QuizBackgroundView(design, art, Size);
            var body = art.Font("Fonts/NotoSans-Regular");
            var display = art.Font("Fonts/RubikWetPaint-Regular");
            heading = Label(design, body, 19, "AD7777", Size.x * .25f, Size.y * .775f, Size.x * .5f, Size.y * .03f);
            for (var i = 0; i < 4; i++) answers[i] = new AnswerView(design, i, art.Font("Fonts/Creepster-Regular"), display, Size);
            question = new QuestionView(design, display, Size);
            clock = new ClockView(design, art, Size);
            players = new PlayersRow(design, new CageCharacterLibrary(art), display, Size);
            status = Label(design, body, 19, "AD7777", Size.x * .15f, Size.y * .057f, Size.x * .7f, Size.y * .028f);
            scoreboard = new FinalScoreboard(design, art, Size);
            audio = new QuizAudio(root, art);
            ApplySnapshot(first);
        }

        public void ApplySnapshot(RoomSnapshot next)
        {
            snapshot = next;
            var quiz = next.Quiz;
            if (quiz == null) return;
            var key = quiz.GameId + ":" + quiz.QuestionId;
            var newQuestion = key != timeline.Key && quiz.QuestionId.Length > 0;
            if (newQuestion) BeginQuestion(next, quiz, key);
            players.Synchronize(next, newQuestion);
            if (next.Phase == QuizPhases.GameFinished) scoreboard.Show(next, texts);
        }

        void BeginQuestion(RoomSnapshot s, QuizView quiz, string key)
        {
            var presenting = s.Phase == QuizPhases.QuestionPresentation;
            timeline.Start(key, quiz.QuestionText, presenting ? audio.VoiceDuration(quiz.QuestionId) : 0);
            if (!presenting) timeline.RestorePresented();
            lastReadyAt = 0;
            audio.Question(key, quiz.QuestionId, quiz.QuestionNumber, presenting);
            background.ShowForQuestion(quiz.QuestionNumber, reduced);
            heading.text = texts.Get("play.question") + " " + quiz.QuestionNumber.ToString("00");
            question.Prepare(quiz.QuestionId, quiz.QuestionText, reduced);
            for (var i = 0; i < 4; i++) answers[i].Prepare(i < quiz.Answers.Count ? quiz.Answers[i] : "");
        }

        public void Tick(float delta, long serverNow)
        {
            if (snapshot?.Quiz == null) return;
            var quiz = snapshot.Quiz;
            timeline.Advance(delta);
            SynchronizeAnswerWindow(serverNow);

            background.Update(delta);
            question.Present(timeline, players.Count > 0);
            for (var i = 0; i < 4; i++)
                answers[i].Present(timeline.AnswerProgress(i), i == timeline.CorrectIndex, timeline.ResultElapsed, reduced);
            clock.Present(timeline, reduced);

            var phase = snapshot.Phase;
            var activeRound = phase == QuizPhases.Answering && serverNow < quiz.Deadline || phase == QuizPhases.QuestionPresentation;
            players.Advance(delta, timeline.AnswersVisible, activeRound, reduced);

            status.text = phase == QuizPhases.Answering
                ? texts.Format("play.remaining", Mathf.CeilToInt((float)timeline.ClockRemaining))
                : timeline.RevealShown ? texts.Format("play.correct", timeline.CorrectIndex + 1) : "";

            var answering = phase == QuizPhases.Answering && context.Room.State == ConnectionState.Online;
            var locked = phase == QuizPhases.Revealing || phase == QuizPhases.Results || phase == QuizPhases.GameFinished;
            audio.Phase(answering, locked, System.Math.Max(0, timeline.ClockElapsed), timeline.ClockRemaining);
            audio.Update(delta);

            SendReadyBarrier(serverNow);
        }

        /// <summary>NetworkRoundAdapter.synchronize: map the server's answer window onto the local timeline.</summary>
        void SynchronizeAnswerWindow(long now)
        {
            var raw = snapshot.Raw;
            var startedAt = raw.Value<long?>("answeringStartedAt") ?? 0;
            if (startedAt <= 0) return;
            var phase = snapshot.Phase;
            var ended = phase == QuizPhases.Revealing || phase == QuizPhases.Results || phase == QuizPhases.GameFinished;
            var elapsed = ended
                ? QuizTimeline.AnswerSeconds + (now - snapshot.Quiz.AnsweringEndedAt) / 1000.0
                : (now - startedAt) / 1000.0;
            timeline.Synchronize(System.Math.Max(0, elapsed), ended, snapshot.Quiz.CorrectAnswerIndex);
        }

        /// <summary>NetworkRoundAdapter.afterDraw: a phone can never move on before the host showed the matching frame.</summary>
        void SendReadyBarrier(long serverNow)
        {
            var room = context.Room;
            if (room.State != ConnectionState.Online) return;
            var now = context.Clock.NowMs;
            if (now - lastReadyAt < ResendMs) return;
            string type = null;
            var phase = snapshot.Phase;
            if (phase == QuizPhases.QuestionPresentation && timeline.PresentationComplete) type = MessageTypes.PresentationReady;
            if (phase == QuizPhases.Revealing && timeline.RevealShown) type = MessageTypes.RevealReady;
            if (phase == QuizPhases.Results && timeline.RevealComplete && serverNow >= snapshot.Quiz.PhaseStartedAt + ResultHoldMs) type = MessageTypes.NextReady;
            if (type == null) return;
            lastReadyAt = now;
            var payload = room.RoomPayload();
            payload["gameId"] = snapshot.Quiz.GameId;
            payload["questionId"] = snapshot.Quiz.QuestionId;
            room.Send(type, payload);
        }

        public void Exit()
        {
            audio?.Leave();
            audio?.Dispose();
            if (root != null) UiKit.Dispose(root.gameObject);
            root = null;
        }

        static Text Label(Transform parent, Font font, int size, string color, float x, float y, float w, float h)
        {
            var label = UiKit.Label(parent, "", size, GenerationCodeView.Hex(color), TextAnchor.MiddleCenter);
            label.font = font;
            UiKit.Place(label.rectTransform, x, y, w, h);
            return label;
        }
    }

    /// <summary>Improvement over the Java host: an explicit final ranking when the server finishes the game.</summary>
    public sealed class FinalScoreboard
    {
        readonly RectTransform root;
        readonly Text title, rows;
        readonly CanvasGroup group;
        float shown;

        public FinalScoreboard(RectTransform design, ArtCatalog art, Vector2 size)
        {
            root = UiKit.Stretch(UiKit.Rect("FinalScoreboard", design));
            root.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .82f);
            group = root.gameObject.AddComponent<CanvasGroup>();
            title = UiKit.Label(root, "ИГРА ОКОНЧЕНА", 72, GenerationCodeView.Hex("DCC9AE"), TextAnchor.MiddleCenter);
            title.font = art.Font("Fonts/RubikWetPaint-Regular");
            UiKit.Place(title.rectTransform, 0, size.y * .72f, size.x, 120);
            rows = UiKit.Label(root, "", 40, GenerationCodeView.Hex("F2EFE9"), TextAnchor.UpperCenter);
            rows.font = art.Font("Fonts/NotoSans-SemiCondensedSemiBold");
            rows.lineSpacing = 1.2f;
            UiKit.Place(rows.rectTransform, size.x * .2f, size.y * .12f, size.x * .6f, size.y * .56f);
            root.gameObject.SetActive(false);
            root.gameObject.AddComponent<Ticker>().Tick = d => { shown += d; group.alpha = ArtButton.Smoother(Mathf.Clamp01(shown / .8f)); };
        }

        public void Show(RoomSnapshot s, Localization texts)
        {
            var ranked = s.Players.OrderByDescending(p => p.Score).ThenBy(p => p.Name).ToList();
            var text = new System.Text.StringBuilder();
            var place = 0;
            var lastScore = int.MinValue;
            for (var i = 0; i < ranked.Count; i++)
            {
                if (ranked[i].Score != lastScore) { place = i + 1; lastScore = ranked[i].Score; }
                var medal = place == 1 ? "★ " : place + ".  ";
                text.Append(medal).Append(ranked[i].Name).Append(ranked[i].IsGhost ? "  (призрак)" : "").Append("   —   ").Append(ranked[i].Score).Append('\n');
            }
            rows.text = text.ToString();
            if (!root.gameObject.activeSelf) { shown = 0; root.gameObject.SetActive(true); root.SetAsLastSibling(); }
        }
    }
}
