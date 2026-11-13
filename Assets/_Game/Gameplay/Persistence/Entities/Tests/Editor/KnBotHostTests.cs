using NUnit.Framework;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>Wave 4 soak: the bot host's command line, ramp and verdict (pure; the host itself runs only in the test-lane build).</summary>
    public class KnBotHostTests
    {
        [Test]
        public void WithoutKnBots_ThereIsNoHost()
        {
            Assert.IsNull(KnBotHost.Options.Parse(new[] { "-batchmode", "-port", "7799" }));
        }

        [Test]
        public void TheFullLine_IsRead()
        {
            var o = KnBotHost.Options.Parse(new[] { "-knbots", "50", "-knbotfirst", "101", "-port", "7799", "-connect", "127.0.0.1",
                                                    "-seconds", "1800", "-out", "C:/runs/s1", "-knbotstep", "5", "-knbotevery", "120" });
            Assert.IsNull(o.Error);
            Assert.AreEqual(50, o.Count);
            Assert.AreEqual(101, o.First);
            Assert.AreEqual(150, o.Last);
            Assert.AreEqual(7799, o.Port);
            Assert.AreEqual(1800f, o.Seconds);
            Assert.AreEqual("C:/runs/s1", o.OutFolder);
            Assert.AreEqual("Knbot101", KnBotHost.Options.CharacterName(101));
        }

        [TestCase(7777)]
        [TestCase(7778)]
        [TestCase(7801)]
        [TestCase(8090)]
        public void TheRealServersPorts_AreRefused(int port)
        {
            var o = KnBotHost.Options.Parse(new[] { "-knbots", "5", "-port", port.ToString() });
            StringAssert.Contains("real server", o.Error);
        }

        [Test]
        public void BadNumbers_AreRefused()
        {
            Assert.IsNotNull(KnBotHost.Options.Parse(new[] { "-knbots", "0" }).Error);
            Assert.IsNotNull(KnBotHost.Options.Parse(new[] { "-knbots", "x" }).Error);
            Assert.IsNotNull(KnBotHost.Options.Parse(new[] { "-knbots", "5", "-knbotstep", "0" }).Error);
            Assert.IsNotNull(KnBotHost.Options.Parse(new[] { "-knbots", "5", "-knbotevery", "-1" }).Error);
        }

        [Test]
        public void TheRamp_AddsAStepEveryInterval_UpToTheCount()
        {
            var o = KnBotHost.Options.Parse(new[] { "-knbots", "12", "-knbotstep", "5", "-knbotevery", "120" });
            Assert.AreEqual(5, o.WantedAt(0f));
            Assert.AreEqual(5, o.WantedAt(119f));
            Assert.AreEqual(10, o.WantedAt(120f));
            Assert.AreEqual(12, o.WantedAt(240f));
            Assert.AreEqual(12, KnBotHost.Options.Parse(new[] { "-knbots", "12" }).WantedAt(0f), "no step: all at once");
        }

        [Test]
        public void TheVerdict_FollowsTheCounts()
        {
            Assert.AreEqual(KnBotHost.ExitOk, KnBotHost.ExitFor(50, 50, 5, false), "10 % dropped is still ok");
            Assert.AreEqual(KnBotHost.ExitDropped, KnBotHost.ExitFor(50, 50, 6, false));
            Assert.AreEqual(KnBotHost.ExitNotAdmitted, KnBotHost.ExitFor(50, 49, 0, false));
            Assert.AreEqual(KnBotHost.ExitNotAdmitted, KnBotHost.ExitFor(50, 50, 0, true));
        }
    }
}
