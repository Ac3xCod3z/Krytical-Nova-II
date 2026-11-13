using NUnit.Framework;

namespace KryticalNova.Crossfire.Tests
{
    /// <summary>Crossfire step 5b: what the HUD says, when the banner is up, and that only a player the war calls sees it.</summary>
    public sealed class CrossfireHudTextTests
    {
        static CrossfireState State(CrossfirePhase phase, double start, double end, double warStart = 3600) => new CrossfireState
        {
            Known = true, Now = new CrossfireNow { Phase = phase, PhaseStart = start, PhaseEnd = end, WarStart = warStart },
            JoinOpensAt = warStart - 180, CallRepeatSeconds = 60, MinLevel = 10, BracketSize = 10, BracketCount = 10,
        };

        [Test]
        public void TheCall_NamesTheBracket_AndRepeatsTheBanner()
        {
            var s = State(CrossfirePhase.Call, 3000, 3600);
            var first = CrossfireHudText.At(s, 3002, 34, 6f);
            Assert.IsTrue(first.Show);
            Assert.IsTrue(first.Banner);
            Assert.AreEqual("Crossfire at nightfall - Bracket III", first.Heading);
            Assert.AreEqual("War in 9:58", first.Timer);
            Assert.IsFalse(CrossfireHudText.At(s, 3030, 34, 6f).Banner, "between two calls only the timer");
            Assert.IsTrue(CrossfireHudText.At(s, 3061, 34, 6f).Banner, "called again a minute later");
            StringAssert.StartsWith("Muster Yards open", CrossfireHudText.At(s, 3500, 34, 6f).Timer);
        }

        [Test]
        public void APlayerTheWarDoesNotCall_SeesNothing()
        {
            Assert.IsFalse(CrossfireHudText.At(State(CrossfirePhase.Call, 3000, 3600), 3002, 5, 6f).Show);
            Assert.IsFalse(CrossfireHudText.At(default, 3002, 50, 6f).Show, "nothing heard from the server");
            Assert.IsFalse(CrossfireHudText.At(State(CrossfirePhase.Gap, 0, 3000), 100, 50, 6f).Show);
        }

        [Test]
        public void MusterWarAndEnd_CountDown()
        {
            Assert.AreEqual("Gates open in 0:42", CrossfireHudText.At(State(CrossfirePhase.Muster, 3600, 3660), 3618, 50, 6f).Timer);
            var war = CrossfireHudText.At(State(CrossfirePhase.War, 3660, 4560), 3720, 50, 6f);
            Assert.AreEqual("14:00 left", war.Timer);
            Assert.That(war.Left, Is.EqualTo(840f / 900f).Within(1e-4));
            var end = CrossfireHudText.At(State(CrossfirePhase.End, 4560, 4580), 4570, 50, 6f);
            Assert.AreEqual("Crossfire over", end.Heading);
            Assert.IsTrue(end.Banner);
        }
    }
}
