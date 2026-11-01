using System;

namespace FinalAnswer.Quiz
{
    /// <summary>
    /// Host presentation timeline for one networked question, ported from QuestionPresentationController +
    /// RoundController (network mode) + ClockTimeline. Local seconds drive only animation; the answer window,
    /// its end and the correct index always come from the server snapshot.
    /// </summary>
    public sealed class QuizTimeline
    {
        public const double CharacterDelay = .035, CharacterAppear = .14, Stabilize = .15;
        public const double VoiceHold = 2, MoveSeconds = .75, AnswerStagger = .18, AnswerAppear = .35;
        public const double AnswerSeconds = 20, RevealHoldSeconds = 2.5;
        public const double WrongFadeSeconds = .30, CorrectGrowSeconds = .60;
        public const double ClockClosing = .2, ClockClosedHold = .1, ClockFall = .42;
        public const double ClockExitSeconds = ClockClosing + ClockClosedHold + ClockFall;
        public const float FinalQuestionScale = .60f;

        double typingEnd, voiceEnd;
        double answerStart = double.PositiveInfinity, forcedReveal = double.NaN;
        bool ended;

        public string Key { get; private set; } = "";
        public double Elapsed { get; private set; }
        public int CorrectIndex { get; private set; } = -1;

        /// <param name="voiceSeconds">Duration of the recorded host line starting at 0, or ≤ 0 when there is none.</param>
        public void Start(string key, string text, double voiceSeconds)
        {
            Key = key;
            Elapsed = 0;
            CorrectIndex = -1;
            answerStart = double.PositiveInfinity;
            forcedReveal = double.NaN;
            ended = false;
            var count = CodePoints(text ?? "");
            typingEnd = Math.Max(0, count - 1) * CharacterDelay + CharacterAppear + Stabilize;
            // Without an external voice the Java controller "finishes" the voice when typing ends.
            voiceEnd = voiceSeconds > 0 ? voiceSeconds : typingEnd;
        }

        /// <summary>Reconnect / late join: show the completed presentation instead of replaying it over a running timer.</summary>
        public void RestorePresented() => Elapsed = Math.Max(Elapsed, TimerStart);

        public void Advance(double delta)
        {
            if (delta > 0 && !double.IsNaN(delta)) Elapsed += delta;
        }

        /// <summary>Applies the authoritative answer window (seconds since answers opened, or since they closed + 20).</summary>
        public void Synchronize(double answerElapsed, bool answerEnded, int correctIndex)
        {
            answerStart = Elapsed - Math.Max(0, answerElapsed);
            ended = answerEnded;
            forcedReveal = answerEnded ? answerStart + AnswerSeconds : double.NaN;
            if (correctIndex >= 0 && correctIndex < 4) CorrectIndex = correctIndex;
        }

        public double TypingEnd => typingEnd;
        public double MoveStart => Math.Max(typingEnd, voiceEnd + VoiceHold);
        public double AnswersStart => MoveStart + MoveSeconds;
        public double TimerStart => AnswersStart + 3 * AnswerStagger + AnswerAppear;
        public bool PresentationComplete => Elapsed >= TimerStart;
        public bool AnswersVisible => AnswerProgress(0) > 0;

        public float MoveProgress => Clamp01((Elapsed - MoveStart) / MoveSeconds);
        public float AnswerProgress(int index) => Clamp01((Elapsed - AnswersStart - index * AnswerStagger) / AnswerAppear);
        public float ClockEntrance => Clamp01((Elapsed - AnswersStart) / .3);

        /// <summary>Clock animation seconds (−1 while waiting); continues past 20 s for closing and falling once the server ended answers.</summary>
        public double ClockElapsed
        {
            get
            {
                if (!ended) return double.IsInfinity(answerStart) ? -1 : Math.Min(AnswerSeconds, Elapsed - answerStart);
                return AnswerSeconds + Elapsed - RevealStart;
            }
        }

        double RevealStart => double.IsNaN(forcedReveal) ? answerStart + AnswerSeconds : forcedReveal;

        /// <summary>Negative until the server ended answers, disclosed the key and the clock has left the screen.</summary>
        public double ResultElapsed => !ended || CorrectIndex < 0 ? -1 : Elapsed - RevealStart - ClockExitSeconds;
        public bool RevealShown => ResultElapsed >= 0;
        public bool RevealComplete => ResultElapsed >= CorrectGrowSeconds + RevealHoldSeconds;

        public enum ClockState { Waiting, Running, Closing, Falling, Finished }

        public ClockState Clock
        {
            get
            {
                var t = ClockElapsed;
                if (t < 0) return ClockState.Waiting;
                if (t < AnswerSeconds) return ClockState.Running;
                if (t < AnswerSeconds + ClockClosing + ClockClosedHold) return ClockState.Closing;
                if (t < AnswerSeconds + ClockExitSeconds) return ClockState.Falling;
                return ClockState.Finished;
            }
        }

        public double ClockRemaining => Math.Max(0, AnswerSeconds - Math.Max(0, ClockElapsed));
        /// <summary>Dial angle: 120° at the start, 0 when time is up (clockwise from XII).</summary>
        public float ClockAngle => (float)(ClockRemaining / AnswerSeconds * 120);
        public float ClosingProgress => Clamp01((ClockElapsed - AnswerSeconds) / ClockClosing);
        public float FallingProgress => Clamp01((ClockElapsed - AnswerSeconds - ClockClosing - ClockClosedHold) / ClockFall);

        static float Clamp01(double v) => (float)(v < 0 ? 0 : v > 1 ? 1 : v);

        static int CodePoints(string text)
        {
            var count = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) i++;
                count++;
            }
            return count;
        }
    }
}
