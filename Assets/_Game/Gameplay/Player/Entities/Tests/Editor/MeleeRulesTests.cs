using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>
    /// The melee rules (M4.4, NGO PlayerMeleeCombat with the prefab's numbers): strike lengths, the hit at the window's end, the combo order
    /// and cadence, the server's validation range, the arc, and who a swing lands on where the attacker saw them.
    /// </summary>
    public class MeleeRulesTests
    {
        static MeleeTiming T => MeleeTiming.Prefab(new int3(1200000, 1000000, 1500000));

        [Test]
        public void Strikes_LastTheirClipAtThePrefabsSpeed_AndLandAtTheWindowsEnd()
        {
            MeleeTiming t = T;
            Assert.AreEqual(850460, MeleeRules.StrikeMicros(t, 1), "1.2 s at 1.411");
            Assert.AreEqual(769230, MeleeRules.StrikeMicros(t, 2), "1.0 s at 1.30");
            Assert.AreEqual(1332149, MeleeRules.StrikeMicros(t, 3), "1.5 s at 1.126");
            Assert.AreEqual(552799, MeleeRules.HitAfterMicros(t, 1), "65 % of the strike");
            Assert.AreEqual(230769, MeleeRules.HitAfterMicros(t, 2), "30 %");
            Assert.AreEqual(932504, MeleeRules.HitAfterMicros(t, 3), "70 % (the prefab's 0.33-0.70)");
            Assert.AreEqual(1.15f, MeleeRules.StepMultiplier(t, 2), 1e-6);
            Assert.AreEqual(1.35f, MeleeRules.StepMultiplier(t, 3), 1e-6);
        }

        [Test]
        public void TheCombo_Goes123_NoSooner70PercentIn_AndRestartsAfterThePause()
        {
            MeleeTiming t = T;
            Assert.AreEqual(1, MeleeRules.NextStep(t, 0, 0), "a fresh attack starts at 1");
            Assert.AreEqual(0, MeleeRules.NextStep(t, 1, 595000), "too soon: under 70 % of step 1");
            Assert.AreEqual(2, MeleeRules.NextStep(t, 1, 595400));
            Assert.AreEqual(3, MeleeRules.NextStep(t, 2, 600000));
            Assert.AreEqual(0, MeleeRules.NextStep(t, 3, 1582148), "step 3, then the 0.25 s pause");
            Assert.AreEqual(1, MeleeRules.NextStep(t, 3, 1582149), "back to 1");
        }

        [Test]
        public void TheServer_ChecksTheValidationRange_AndTheArcIsInFrontWithinReach()
        {
            MeleeTiming t = T;
            HitProxy p = HitProxy.Player;
            Assert.IsTrue(MeleeRules.WithinValidation(t, int3.zero, new int3(2700, 0, 0)));
            Assert.IsFalse(MeleeRules.WithinValidation(t, int3.zero, new int3(2701, 0, 0)));
            Assert.IsTrue(MeleeRules.InArc(t, int3.zero, 0, new int3(0, 0, 1500), p), "straight ahead (yaw 0 = +z)");
            Assert.IsFalse(MeleeRules.InArc(t, int3.zero, 0, new int3(0, 0, -1500), p), "behind");
            Assert.IsTrue(MeleeRules.InArc(t, int3.zero, 0, new int3(1200, 0, 1200), p), "45 degrees off");
            Assert.IsFalse(MeleeRules.InArc(t, int3.zero, 0, new int3(1900, 0, 500), p), "76 degrees off: out of the 120 degree arc");
            Assert.IsFalse(MeleeRules.InArc(t, int3.zero, 0, new int3(0, 0, 2200), p), "beyond reach + radius (2.1 m)");
            Assert.IsTrue(MeleeRules.InArc(t, int3.zero, 9000, new int3(1500, 0, 0), p), "facing +x");
        }

        static PositionHistory Standing(int3 feet, uint from = 100, uint to = 120)
        {
            var ring = new PositionHistory();
            for (uint k = from; k < to; k++) PositionHistoryRules.Record(ref ring, new NetworkTick(k), feet);
            return ring;
        }

        [Test]
        public void ASwing_LandsOnTheTargetAndTheOthersInTheArc_WhereTheAttackerSawThem()
        {
            MeleeTiming t = T;
            HitProxy p = HitProxy.Player;
            var now = new NetworkTick(119);
            var target = new PositionHistory();
            for (uint k = 100; k < 120; k++) PositionHistoryRules.Record(ref target, new NetworkTick(k), new int3(0, 0, 1000 + 100 * (int)(k - 100)));
            using var others = new NativeArray<PositionHistory>(new[]
            {
                Standing(new int3(800, 0, 1200)),    // in front, beside the target: caught
                Standing(new int3(0, 0, -1200)),     // behind: missed
                Standing(new int3(0, 0, 1000), 117), // there only from 117: not seen at 110
            }, Allocator.Temp);
            var hit = new NativeList<int>(Allocator.Temp);
            Assert.IsTrue(MeleeRules.Resolve(t, int3.zero, 0, now, new NetworkTick(110), target, new int3(0, 0, 2400), others, p, out bool targetHit, ref hit));
            Assert.IsTrue(targetHit, "seen at 2.0 m, now at 2.4 m: within reach then, within validation now");
            CollectionAssert.AreEqual(new[] { 0 }, hit.AsArray().ToArray(), "only the one in the arc on the seen tick");

            hit.Clear();
            Assert.IsTrue(MeleeRules.Resolve(t, int3.zero, 0, now, new NetworkTick(110), target, new int3(0, 0, 3000), others, p, out targetHit, ref hit));
            Assert.IsFalse(targetHit, "it got away past the validation range before the hit was applied");

            hit.Clear();
            Assert.IsFalse(MeleeRules.Resolve(t, int3.zero, 0, now, new NetworkTick(100), target, new int3(0, 0, 2400), others, p, out targetHit, ref hit));
            Assert.IsFalse(targetHit, "a claim older than the ring hits nobody");
            Assert.AreEqual(0, hit.Length);
            hit.Dispose();
        }

        [Test]
        public void TheComboLoop_GoesLikeNgos_StepByStep()
        {
            // NGO ComboLoopRoutine: wait for reach, 1 -> 2 -> 3 each for its whole length, the 0.25 s pause, then round again
            MeleeTiming t = T;
            const int rate = 30;
            int s1 = MeleeRules.Ticks(MeleeRules.StrikeMicros(t, 1), rate), s2 = MeleeRules.Ticks(MeleeRules.StrikeMicros(t, 2), rate);
            int s3p = MeleeRules.Ticks(MeleeRules.StrikeMicros(t, 3) + t.PostComboMicros, rate);
            Assert.AreEqual(26, s1, "0.850 s at 30 Hz, rounded up");
            var s = new CombatState();
            MeleeRules.Begin(ref s, 7);
            uint now = 100;
            MeleeRules.Tick(t, ref s, new NetworkTick(now), rate, true, false, false);
            Assert.AreEqual(0, s.Step, "out of range: the walk in, no swing");
            MeleeRules.Tick(t, ref s, new NetworkTick(++now), rate, true, true, false);
            Assert.AreEqual(1, s.Step, "in range: strike 1 at once");
            Assert.AreEqual(now, s.StepStartTick.TickIndexForValidTick);
            uint start = now;
            for (now = start + 1; now < start + (uint)s1; now++)
            {
                MeleeRules.Tick(t, ref s, new NetworkTick(now), rate, true, true, false);
                Assert.AreEqual(1, s.Step, "strike 1 runs its whole length");
            }
            MeleeRules.Tick(t, ref s, new NetworkTick(now), rate, true, true, false);
            Assert.AreEqual(2, s.Step, "then strike 2 straight away");
            start = now;
            now = start + (uint)s2;
            MeleeRules.Tick(t, ref s, new NetworkTick(now), rate, true, true, false);
            Assert.AreEqual(3, s.Step);
            start = now;
            MeleeRules.Tick(t, ref s, new NetworkTick(start + (uint)s3p - 1), rate, true, true, false);
            Assert.AreEqual(3, s.Step, "strike 3 and the pause after it");
            MeleeRules.Tick(t, ref s, new NetworkTick(start + (uint)s3p), rate, true, true, false);
            Assert.AreEqual(1, s.Step, "round again from strike 1");
            Assert.AreEqual(7, s.TargetGhostId);
        }

        [Test]
        public void TheCombo_ChasesATargetThatLeft_AndEndsWithItsTarget()
        {
            MeleeTiming t = T;
            const int rate = 30;
            var s = new CombatState();
            MeleeRules.Begin(ref s, 7);
            MeleeRules.Tick(t, ref s, new NetworkTick(100), rate, true, true, false);
            uint end1 = 100 + (uint)MeleeRules.Ticks(MeleeRules.StrikeMicros(t, 1), rate);
            MeleeRules.Tick(t, ref s, new NetworkTick(end1), rate, true, false, true);
            Assert.AreEqual(0, s.Step, "it walked off after strike 1: chased, no strike 2 from where it stood");
            Assert.AreEqual(7, s.TargetGhostId, "still the target");
            MeleeRules.Tick(t, ref s, new NetworkTick(end1 + 5), rate, true, true, false);
            Assert.AreEqual(1, s.Step, "caught up: the combo restarts at 1");

            MeleeRules.Begin(ref s, 7);
            Assert.AreEqual(1, s.Step, "the same target again keeps the combo under way");
            MeleeRules.Tick(t, ref s, new NetworkTick(end1 + 6), rate, false, true, false);
            Assert.AreEqual(0, s.TargetGhostId, "a dead target ends the attack");
            MeleeRules.Begin(ref s, 9);
            MeleeRules.Cancel(ref s);
            Assert.AreEqual(0, s.TargetGhostId, "cancel ends it");
        }

        [Test]
        public void TheHit_IsDueAtTheWindowsEnd_OncePerSwing()
        {
            // NGO EnsureTargetStruck: the target is struck by the END of the swing window, once per swing
            MeleeTiming t = T;
            const int rate = 30;
            var s = new CombatState();
            MeleeRules.Begin(ref s, 7);
            MeleeRules.Tick(t, ref s, new NetworkTick(100), rate, true, true, false);
            int at = MeleeRules.Ticks(MeleeRules.HitAfterMicros(t, 1), rate);
            Assert.AreEqual(17, at, "0.553 s of strike 1 at 30 Hz, rounded up");
            Assert.IsFalse(MeleeRules.HitDue(t, s, new NetworkTick(100 + (uint)at - 1), rate), "not yet: the window is still open");
            Assert.IsTrue(MeleeRules.HitDue(t, s, new NetworkTick(100 + (uint)at), rate), "the window's end");
            s.SwingHit = 1;
            Assert.IsFalse(MeleeRules.HitDue(t, s, new NetworkTick(100 + (uint)at + 1), rate), "once per swing");
            Assert.IsFalse(MeleeRules.HitDue(t, default, new NetworkTick(200), rate), "no attack, no hit");
        }

        [Test]
        public void TheHitRing_KeepsTheNewest_NumberedOnce()
        {
            // M4.4h: the attacker's ring of landed hits (NGO DamageFeedbackRpc / SpawnImpactVfxClientRpc as data on the ghost)
            using var world = new Unity.Entities.World("hit ring");
            var em = world.EntityManager;
            var e = em.CreateEntity();
            em.AddBuffer<HitEvent>(e);
            var seq = new HitEventSeq();
            for (int i = 1; i <= HitEventRing.Capacity + 2; i++)
                HitEventRing.Append(em.GetBuffer<HitEvent>(e), ref seq, new HitEvent { TargetGhostId = 7, DamageMilli = i * 1000, Step = (byte)(1 + i % 3) });
            var ring = em.GetBuffer<HitEvent>(e);
            Assert.AreEqual(HitEventRing.Capacity, ring.Length, "only the newest are kept");
            Assert.AreEqual(3u, ring[0].Seq, "the two oldest dropped");
            Assert.AreEqual((uint)(HitEventRing.Capacity + 2), HitEventRing.Newest(ring));
            Assert.AreEqual((HitEventRing.Capacity + 2) * 1000, ring[ring.Length - 1].DamageMilli);
            for (int i = 1; i < ring.Length; i++) Assert.AreEqual(ring[i - 1].Seq + 1, ring[i].Seq, "numbered one by one");
            Assert.AreEqual(1, HitEventRing.Critical);
            Assert.AreEqual(2, HitEventRing.Evaded, "NGO's bits");
        }

        [Test]
        public void TheCastRing_KeepsTheNewestFour()
        {
            // M4.5c: a player's casts for the other clients (NGO TriggerSkillCastClientRpc as data on the ghost)
            using var world = new Unity.Entities.World("cast ring");
            var em = world.EntityManager;
            var e = em.CreateEntity();
            em.AddBuffer<CastEvent>(e);
            var seq = new CastEventSeq();
            for (int i = 1; i <= 6; i++)
                CastEventRing.Append(em.GetBuffer<CastEvent>(e), ref seq, new CastEvent { SkillId = new Unity.Collections.FixedString64Bytes("skill" + i), Rank = 3 });
            var ring = em.GetBuffer<CastEvent>(e);
            Assert.AreEqual(CastEventRing.Capacity, ring.Length);
            Assert.AreEqual(3u, ring[0].Seq);
            Assert.AreEqual("skill6", ring[ring.Length - 1].SkillId.ToString());
            Assert.AreEqual(6u, CastEventRing.Newest(ring));
        }

        [Test]
        public void TheSquadBuffRing_KeepsTheNewestFour_WithTheGiver()
        {
            // M4.5g: a Squad member's Stance given to this player, for every client (NGO SquadBuffClientRpc as data on the ghost)
            using var world = new Unity.Entities.World("squad buff ring");
            var em = world.EntityManager;
            var e = em.CreateEntity();
            em.AddBuffer<SquadBuffEvent>(e);
            var seq = new SquadBuffSeq();
            for (int i = 1; i <= 5; i++)
                SquadBuffRing.Append(em.GetBuffer<SquadBuffEvent>(e), ref seq, new SquadBuffEvent
                {
                    SkillId = new Unity.Collections.FixedString64Bytes("stance" + i), Rank = 20, Giver = new Unity.Collections.FixedString32Bytes("Talwyn"),
                });
            var ring = em.GetBuffer<SquadBuffEvent>(e);
            Assert.AreEqual(SquadBuffRing.Capacity, ring.Length);
            Assert.AreEqual(2u, ring[0].Seq);
            Assert.AreEqual("stance5", ring[ring.Length - 1].SkillId.ToString());
            Assert.AreEqual("Talwyn", ring[ring.Length - 1].Giver.ToString());
            Assert.AreEqual(5u, SquadBuffRing.Newest(ring));
        }
    }
}
