using FinalAnswer.Quiz;
using NUnit.Framework;

namespace FinalAnswer.Tests.EditMode
{
    public sealed class QuizTimelineTests
    {
        [Test]
        public void PresentationWaitsForTypingThenVoicePlusHold()
        {
            var t = new QuizTimeline();
            t.Start("g:q", "Какая планета самая большая?", 0);   // 28 code points
            var typing = 27 * .035 + .14 + .15;
            Assert.AreEqual(typing, t.TypingEnd, 1e-9);
            Assert.AreEqual(typing + 2, t.MoveStart, 1e-9, "no recorded line: hold 2 s after typing");
            t.Start("g:q2", "Короткий?", 6.0);
            Assert.AreEqual(8.0, t.MoveStart, 1e-9, "recorded line: voice end + 2 s");
            Assert.AreEqual(8.0 + .75 + 3 * .18 + .35, t.TimerStart, 1e-9);
            Assert.IsFalse(t.PresentationComplete);
            t.Advance(t.TimerStart);
            Assert.IsTrue(t.PresentationComplete);
            Assert.AreEqual(1f, t.AnswerProgress(3), 1e-6);
        }

        [Test]
        public void ClockFollowsServerWindowAndExitsBeforeReveal()
        {
            var t = new QuizTimeline();
            t.Start("g:q", "Вопрос?", 0);
            t.RestorePresented();
            Assert.AreEqual(QuizTimeline.ClockState.Waiting, t.Clock);
            t.Synchronize(5, false, -1);
            Assert.AreEqual(QuizTimeline.ClockState.Running, t.Clock);
            Assert.AreEqual(15, t.ClockRemaining, 1e-9);
            Assert.AreEqual(90f, t.ClockAngle, 1e-4f);
            Assert.AreEqual(-1, t.ResultElapsed, "no key yet");

            t.Synchronize(20, true, 2);                       // server ended answers just now and disclosed the key
            Assert.AreEqual(QuizTimeline.ClockState.Closing, t.Clock);
            Assert.IsFalse(t.RevealShown, "reveal waits for the clock to leave the screen");
            t.Advance(QuizTimeline.ClockExitSeconds + .01);
            t.Synchronize(20 + QuizTimeline.ClockExitSeconds + .01, true, 2);
            Assert.AreEqual(QuizTimeline.ClockState.Finished, t.Clock);
            Assert.IsTrue(t.RevealShown);
            Assert.AreEqual(2, t.CorrectIndex);
            Assert.IsFalse(t.RevealComplete);
            t.Advance(3.2);
            t.Synchronize(20 + QuizTimeline.ClockExitSeconds + 3.21, true, 2);
            Assert.IsTrue(t.RevealComplete);
        }
    }
}
