using FinalAnswer.App;
using FinalAnswer.Domain;
using FinalAnswer.UI;
using NUnit.Framework;

namespace FinalAnswer.Tests.EditMode
{
    public sealed class ShellTests
    {
        [Test]
        public void CageTimelineMatchesJavaBeats()
        {
            var cage = new CageTimeline();
            cage.Start();
            var impacts = 0;
            var locks = 0;
            for (var i = 0; i < 400; i++)
            {
                cage.Update(1 / 100f);
                if (cage.ImpactNow) impacts++;
                if (cage.LockNow) locks++;
            }
            Assert.AreEqual(1, impacts, "gate impact fires once");
            Assert.AreEqual(1, locks, "key lock fires once");
            Assert.AreEqual(1f, cage.Gate(), 1e-4f);
            Assert.AreEqual(0f, cage.ImpactRotation());
            Assert.AreEqual(2.1f, CageTimeline.ImpactAt, 1e-4f);
            Assert.AreEqual(0f, CageTimeline.Darkness(.4f));
            Assert.AreEqual(1f, CageTimeline.Darkness(2.5f), 1e-4f);
        }

        [Test]
        public void CageSlotsSurviveReorderAndFreeOnRemoval()
        {
            var slots = new CageAssignments();
            slots.Synchronize(new[] { "a", "b", "c" });
            slots.Synchronize(new[] { "c", "a", "b" });
            Assert.AreEqual("a", slots.PlayerAt(0));
            Assert.AreEqual("c", slots.PlayerAt(2));
            slots.Synchronize(new[] { "a", "c", "d" });
            Assert.AreEqual("d", slots.PlayerAt(1), "freed slot is reused");
            Assert.AreEqual("c", slots.PlayerAt(2));
        }

        [Test]
        public void RoomCodesUseServerAlphabet()
        {
            for (var i = 0; i < 200; i++) Assert.IsTrue(RoomCodes.IsValid(RoomCodes.Generate()));
            Assert.IsFalse(RoomCodes.IsValid("ABCDE0"), "0 is ambiguous and excluded");
            Assert.IsFalse(RoomCodes.IsValid("abc234"));
        }

        [Test]
        public void LocalizationReadsJavaProperties()
        {
            var texts = new Localization("lobby.player_count=PLAYERS: {0} / {1}\nui.ok=OK", "# comment\nlobby.player_count=ИГРОКИ: {0} / {1}\nescaped=a\\nb\\u0416");
            Assert.AreEqual("ИГРОКИ: 2 / 4", texts.Format("lobby.player_count", 2, 4));
            Assert.AreEqual("OK", texts.Get("ui.ok"), "falls back to English");
            Assert.AreEqual("a\nbЖ", texts.Get("escaped"));
            texts.Language = "en";
            Assert.AreEqual("PLAYERS: 2 / 4", texts.Format("lobby.player_count", 2, 4));
            Assert.AreEqual("missing.key", texts.Get("missing.key"));
        }

        [Test]
        public void QrHasFinderPatternsAndPicksSmallestVersion()
        {
            var code = QrCode.Encode("http://192.168.1.23:8080/?room=ABC234");
            Assert.AreEqual(3, code.Version);
            Assert.AreEqual(29, code.Size);
            foreach (var (x, y) in new[] { (0, 0), (code.Size - 7, 0), (0, code.Size - 7) })
            {
                for (var i = 0; i < 7; i++)
                {
                    Assert.IsTrue(code[x + i, y], "finder top edge");
                    Assert.IsTrue(code[x + i, y + 6], "finder bottom edge");
                }
                Assert.IsFalse(code[x + 1, y + 1], "finder light ring");
                Assert.IsTrue(code[x + 3, y + 3], "finder core");
            }
            Assert.IsTrue(code[8, code.Size - 8], "dark module");
        }

        [Test]
        public void ServerHealthRejectsOutdatedLastCommandServers()
        {
            Assert.AreEqual(ServerStatus.Outdated, ServerHealth.Parse("{\"status\":\"ok\"}").Status, "pre-race server");
            Assert.AreEqual(ServerStatus.Unreachable, ServerHealth.Parse("<html>").Status);
            var current = ServerHealth.Parse("{\"status\":\"ok\",\"protocol\":1,\"lastCommand\":{\"engine\":\"race\",\"solo\":true,\"debug\":true}}");
            Assert.IsTrue(current.Compatible);
            Assert.IsTrue(current.LastCommandDebug);
        }
    }
}
