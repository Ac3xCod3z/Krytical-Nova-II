using KryticalNova.Crossfire;
using KryticalNova.Factions;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Crossfire.Tests
{
    /// <summary>
    /// The owner's war flow (16 Nov 2026) as pure rules: late join until the last 5 min, a late joiner's share of the pay, the Rally Gates
    /// shut through the join window and the settle and opening together when the War starts, and the port taking late joiners.
    /// </summary>
    public sealed class CrossfireFlowRulesTests
    {
        // the owner's times: a war at 3600 s; the settle (Rally) 60 s, so the gates open at 3660; a 15-min War to 4560
        const double WarStart = 3600, GatesOpen = 3660, WarEnd = 4560;
        static readonly CrossfireTimes T = new CrossfireTimes { CallSeconds = 900, JoinWindowSeconds = 300, MusterSeconds = 60, WarSeconds = 900, EndSeconds = 30 };

        static CrossfireNow At(double now) => CrossfireRules.PhaseOf(WarStart, T, now);

        [TestCase(3660, true)]
        [TestCase(4259, true)]
        [TestCase(4260, false)]   // the last 5 min are closed
        [TestCase(3650, false)]   // the settle: the yards take players by the join rule, not the late one
        [TestCase(4570, false)]   // the End
        public void LateJoin_IsOpenInTheWar_UntilItsLastFiveMinutes(double now, bool open)
        {
            Assert.AreEqual(open, CrossfireRules.LateJoinOpen(At(now), 300, now));
        }

        [Test]
        public void TheWarTakesPlayers_FromTheJoinWindow_ThroughTheSettle_AndLateUntilTheCutoff()
        {
            CrossfireState State(double now) => new CrossfireState { Now = At(now), JoinOpensAt = WarStart - T.JoinWindowSeconds, Known = true };
            Assert.IsFalse(State(3200).TakesPlayers(3200, 300), "the countdown: not yet");
            Assert.IsTrue(State(3300).TakesPlayers(3300, 300), "the join window opens 5 min before the settle");
            Assert.IsTrue(State(3630).TakesPlayers(3630, 300), "the settle");
            Assert.IsTrue(State(4000).TakesPlayers(4000, 300), "the War, late");
            Assert.IsFalse(State(4300).TakesPlayers(4300, 300), "the War's last 5 min");
            Assert.IsFalse(default(CrossfireState).TakesPlayers(4000, 300), "nothing known: never");
        }

        [TestCase(double.NaN, 1f)]
        [TestCase(3000, 1f)]           // in during the join window
        [TestCase(GatesOpen, 1f)]
        [TestCase(3960, 2f / 3f)]      // minute 5 of 15
        [TestCase(4260, 1f / 3f)]
        [TestCase(WarEnd, 0f)]
        public void APlayer_EarnsTheShareOfTheWarTheyWereIn(double joinedAt, float share)
        {
            Assert.AreEqual(share, CrossfireRules.PayShare(joinedAt, GatesOpen, WarEnd), 1e-4f);
        }

        [Test]
        public void ALateJoinersPay_IsEveryPartTimesTheirShare()
        {
            var pay = new CrossfirePay { CharacterExp = 3000, PetPercent = 0.5f, MountPercent = 0.25f, Creds = 50000 };
            CrossfirePay late = CrossfireRewards.Scaled(pay, 2f / 3f);
            Assert.AreEqual(2000, late.CharacterExp);
            Assert.AreEqual(1f / 3f, late.PetPercent, 1e-5f);
            Assert.AreEqual(1f / 6f, late.MountPercent, 1e-5f);
            Assert.AreEqual(33333, late.Creds);
            Assert.AreEqual(pay.Creds, CrossfireRewards.Scaled(pay, 1f).Creds, "in from the start: all of it");
            Assert.AreEqual(0, CrossfireRewards.Scaled(pay, -1f).Creds, "never below nothing");
        }

        [Test]
        public void ALateJoinersKillDrop_HasTheirShareOfTheChance()
        {
            var s = ScriptableObject.CreateInstance<CrossfireSettings>();
            try
            {
                s.killDropChance = 0.3f;
                Assert.IsTrue(CrossfireRewards.KillDrops(s, false, false, 0.29));
                Assert.IsFalse(CrossfireRewards.KillDrops(s, false, false, 0.2, 0.5f), "half the war: half the chance (0.15)");
                Assert.IsTrue(CrossfireRewards.KillDrops(s, false, false, 0.14, 0.5f));
                Assert.IsFalse(CrossfireRewards.KillDrops(s, true, false, 0.0, 1f), "War Bots never drop");
            }
            finally { Object.DestroyImmediate(s); }
        }

        [TestCase(3000, CrossfireGatePhase.Shut)]     // the countdown
        [TestCase(3400, CrossfireGatePhase.Shut)]     // the 5-min join window
        [TestCase(3630, CrossfireGatePhase.Shut)]     // the 1-min settle
        [TestCase(3660, CrossfireGatePhase.Opening)]  // the War starts: all three open together
        [TestCase(3662.9, CrossfireGatePhase.Opening)]
        [TestCase(3663, CrossfireGatePhase.Open)]
        [TestCase(4559, CrossfireGatePhase.Open)]
        [TestCase(4560, CrossfireGatePhase.Shut)]     // the End: shut again
        public void TheRallyGates_StayShutThroughTheJoinAndTheSettle_AndOpenWhenTheWarStarts(double now, CrossfireGatePhase phase)
        {
            Assert.AreEqual(phase, CrossfireRules.GatePhaseAt(At(now), 3, now));
        }

        [Test]
        public void TheGatesOpenAmount_RunsFromShutToOpen_AsTheLeavesSwing()
        {
            Assert.AreEqual(0f, CrossfireRules.GateOpenAmount(At(3650), 3, 3650), "the settle: shut");
            Assert.AreEqual(0f, CrossfireRules.GateOpenAmount(At(3660), 3, 3660), 1e-5f);
            Assert.AreEqual(0.5f, CrossfireRules.GateOpenAmount(At(3661.5), 3, 3661.5), 1e-5f, "halfway (eased)");
            Assert.AreEqual(1f, CrossfireRules.GateOpenAmount(At(3670), 3, 3670));
            Assert.AreEqual(1f, CrossfireRules.GateOpenAmount(At(3660), 0, 3660), "no opening time: open at once");
            float a = CrossfireRules.GateOpenAmount(At(3660.5), 3, 3660.5), b = CrossfireRules.GateOpenAmount(At(3661), 3, 3661);
            Assert.Less(a, b, "it only ever opens further");
        }

        [Test]
        public void ThePort_TakesALateJoinerDuringTheWar_ButNotInItsLastFiveMinutes()
        {
            var s = ScriptableObject.CreateInstance<CrossfireSettings>();
            try
            {
                s.lateJoinCutoffMinutes = 5f;
                CrossfireState War(double now) => new CrossfireState { Now = At(now), Known = true };
                Assert.IsFalse(CrossfirePort.Calling(War(4000)), "the old rule: the Call and the Rally only");
                Assert.IsTrue(CrossfirePort.Calling(War(4000), s, 4000), "the War, late");
                Assert.IsFalse(CrossfirePort.Calling(War(4300), s, 4300), "the last 5 min");
                Assert.IsFalse(CrossfirePort.Calling(War(4000), s, double.NaN), "no time given: the old rule");
                Assert.AreEqual(CrossfirePortAnswer.NotCalled,
                    CrossfirePort.Decide(s, War(4300), s.portPortalId, 105, FactionId.Hegemony, 4300, out _));
            }
            finally { Object.DestroyImmediate(s); }
        }
    }
}
