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
        public void YawToward_FacesTheTarget_OnTheGroundPlane_AsTheArcReadsIt()
        {
            // owner's 2-player test, 6 Nov 2026, item 12: NGO FaceTarget at each strike step; 0 = +z, 9000 = +x (MeleeRules.InArc)
            Assert.AreEqual(0, MeleeRules.YawTowardCdeg(int3.zero, new int3(0, 0, 2000), 4500));
            Assert.AreEqual(9000, MeleeRules.YawTowardCdeg(int3.zero, new int3(2000, 0, 0), 0));
            Assert.AreEqual(18000, MeleeRules.YawTowardCdeg(int3.zero, new int3(0, 0, -2000), 0));
            Assert.AreEqual(27000, MeleeRules.YawTowardCdeg(int3.zero, new int3(-2000, 0, 0), 0));
            Assert.AreEqual(4500, MeleeRules.YawTowardCdeg(new int3(1000, 0, 1000), new int3(2000, 5000, 2000), 0), "height does not count");
            Assert.AreEqual(1234, MeleeRules.YawTowardCdeg(int3.zero, new int3(50, 0, 50), 1234), "within 10 cm: as it was");
            int yaw = MeleeRules.YawTowardCdeg(int3.zero, new int3(1500, 0, 800), 0);
            float rad = math.radians(yaw / 100f);
            Assert.AreEqual(1f, math.dot(math.normalize(new float2(1500, 800)), new float2(math.sin(rad), math.cos(rad))), 1e-3f, "dead ahead");
        }

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

        [Test]
        public void ASwing_TestsEachBodyWithItsOwnCapsule()
        {
            // M4.2b: the Strike Dummy is 0.4 m across (its scene capsule, scale 0.8), a player 0.5 m; reach 1.6 m + the radius
            MeleeTiming t = T;
            var now = new NetworkTick(119);
            var dummy = new HitProxy { RadiusMm = 400, HeightMm = 2000 };
            PositionHistory target = Standing(new int3(0, 0, 2050));
            using var others = new NativeArray<PositionHistory>(new[] { Standing(new int3(300, 0, 2050)), Standing(new int3(-300, 0, 2050)) }, Allocator.Temp);
            using var proxies = new NativeArray<HitProxy>(new[] { HitProxy.Player, dummy }, Allocator.Temp);
            var hit = new NativeList<int>(Allocator.Temp);

            Assert.IsTrue(MeleeRules.Resolve(t, int3.zero, 0, now, new NetworkTick(110), target, new int3(0, 0, 2050), HitProxy.Player, others, proxies,
                HitProxy.Player, out bool targetHit, ref hit));
            Assert.IsTrue(targetHit, "2.05 m: within 1.6 + 0.5");
            CollectionAssert.AreEqual(new[] { 0 }, hit.AsArray().ToArray(), "the player-sized one is caught, the dummy-sized one is not");

            hit.Clear();
            Assert.IsTrue(MeleeRules.Resolve(t, int3.zero, 0, now, new NetworkTick(110), target, new int3(0, 0, 2050), dummy, others, proxies,
                HitProxy.Player, out targetHit, ref hit));
            Assert.IsFalse(targetHit, "a dummy at 2.05 m: beyond 1.6 + 0.4");

            hit.Clear();
            Assert.IsTrue(MeleeRules.Resolve(t, int3.zero, 0, now, new NetworkTick(110), target, new int3(0, 0, 2050), dummy, others, default,
                HitProxy.Player, out _, ref hit));
            CollectionAssert.AreEqual(new[] { 0, 1 }, hit.AsArray().ToArray(), "no list: every other body has the given capsule");
            hit.Dispose();
        }

        static PositionHistory Standing(int3 feet, uint from = 100, uint to = 120)
        {
            var ring = new PositionHistory();
            for (uint k = from; k < to; k++) PositionHistoryRules.Record(ref ring, new NetworkTick(k), feet);
            return ring;
        }

        [Test]
        public void ASwingSeenOnThisVeryTick_Lands_ThoughTheRingHasNoSampleOfItYet()
        {
            // owner retest 8 Nov 2026 (probe-x6-3: "seen 0 ticks back ... target hit False" on every strike at the dummy, 1.45 m away)
            var now = new NetworkTick(120);
            PositionHistory target = Standing(new int3(0, 0, 1400));   // samples 100-119: the ring is written at the tick's end
            using var others = new NativeArray<PositionHistory>(new[] { Standing(new int3(500, 0, 1300)) }, Allocator.Temp);
            var hit = new NativeList<int>(Allocator.Temp);
            Assert.IsTrue(MeleeRules.Resolve(T, int3.zero, 0, now, now, target, new int3(0, 0, 1400), others, HitProxy.Player, out bool targetHit, ref hit));
            Assert.IsTrue(targetHit, "the target where it stands now");
            CollectionAssert.AreEqual(new[] { 0 }, hit.AsArray().ToArray(), "the other in the arc at its newest sample");
            hit.Dispose();
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
            var s = new CombatState { WeaponDrawn = 1 };   // drawn already: the draw wait has its own test
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
        public void StrikeOne_WaitsForTheDraw_TheStanceOutlivesTheAttack_AndTheKeyToggles()
        {
            // NGO: StartAttackTarget draws (PlayerMeleeCombat:189-191) and the combo loop waits while Equipping (:295-299), 0.68 s
            MeleeTiming t = T;
            const int rate = 30;
            int draw = MeleeRules.Ticks(t.DrawMicros, rate);
            Assert.AreEqual(21, draw, "0.68 s at 30 Hz, rounded up");
            var s = new CombatState();
            MeleeRules.Begin(ref s, 7);
            MeleeRules.Tick(t, ref s, new NetworkTick(100), rate, true, false, false);
            Assert.AreEqual(1, s.WeaponDrawn, "the order draws the weapon, during the walk in");
            MeleeRules.Tick(t, ref s, new NetworkTick(100 + (uint)draw - 1), rate, true, true, false);
            Assert.AreEqual(0, s.Step, "in reach, but the draw is still under way");
            MeleeRules.Tick(t, ref s, new NetworkTick(100 + (uint)draw), rate, true, true, false);
            Assert.AreEqual(1, s.Step, "strike 1 once drawn");

            MeleeRules.Cancel(ref s);
            Assert.AreEqual(1, s.WeaponDrawn, "the attack ends; the weapon stays in the hand");
            MeleeRules.Begin(ref s, 9);
            MeleeRules.Tick(t, ref s, new NetworkTick(300), rate, true, true, false);
            Assert.AreEqual(1, s.Step, "drawn already: strike 1 at once");

            MeleeRules.Toggle(ref s, new NetworkTick(400));
            Assert.AreEqual(0, s.WeaponDrawn, "the key sheathes");
            Assert.AreEqual(0, s.TargetGhostId, "and ends the attack");
            MeleeRules.Toggle(ref s, new NetworkTick(410));
            Assert.AreEqual(1, s.WeaponDrawn, "the key draws");
            Assert.IsFalse(MeleeRules.DrawDone(t, s, new NetworkTick(410 + (uint)draw - 1), rate));
            Assert.IsTrue(MeleeRules.DrawDone(t, s, new NetworkTick(410 + (uint)draw), rate));
        }

        [Test]
        public void TheCombo_ChasesATargetThatLeft_AndEndsWithItsTarget()
        {
            MeleeTiming t = T;
            const int rate = 30;
            var s = new CombatState { WeaponDrawn = 1 };   // drawn already: the draw wait has its own test
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
            var s = new CombatState { WeaponDrawn = 1 };   // drawn already: the draw wait has its own test
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
        public void FortyHits_ComeThroughTheQueueAFewATick_AndAClientReadingEverySecondTickSeesEveryOne()
        {
            // M4 step 7 (owner, 5 Nov: an area skill hits every body, nothing may be lost): 4 a tick into a ring of 16
            using var world = new Unity.Entities.World("hit queue");
            var em = world.EntityManager;
            var e = em.CreateEntity();
            em.AddBuffer<HitEvent>(e);
            var seq = new HitEventSeq();
            var waiting = new System.Collections.Generic.Queue<HitEvent>();
            for (int i = 1; i <= 40; i++) waiting.Enqueue(new HitEvent { TargetGhostId = i, DamageMilli = i * 1000 });
            var seen = new System.Collections.Generic.HashSet<int>();
            uint last = 0;
            for (int tick = 0; tick < 40 && (waiting.Count > 0 || seen.Count < 40); tick++)
            {
                Assert.LessOrEqual(HitEventRing.Drain(waiting, em.GetBuffer<HitEvent>(e), ref seq), HitEventRing.PerTick);
                if (tick % 2 != 1) continue;   // a snapshot every second tick
                var ring = em.GetBuffer<HitEvent>(e);
                for (int i = 0; i < ring.Length; i++)
                {
                    if (ring[i].Seq <= last) continue;
                    Assert.AreEqual(last + 1, ring[i].Seq, "none skipped");
                    last = ring[i].Seq;
                    seen.Add(ring[i].TargetGhostId);
                }
            }
            Assert.AreEqual(40, seen.Count, "every hit shown");
            Assert.AreEqual(0, waiting.Count);
        }

        [Test]
        public void ALongLabel_IsCutOnAWholeCharacter_AndAShortOneIsKept()
        {
            Assert.AreEqual("Lightning Flash (2/3)", HitEventRing.FitLabel("Lightning Flash (2/3)").ToString());
            string cut = HitEventRing.FitLabel("A very long skill name that will not fit (3/3)").ToString();
            Assert.IsTrue("A very long skill name that will not fit (3/3)".StartsWith(cut), "its start");
            Assert.LessOrEqual(System.Text.Encoding.UTF8.GetByteCount(cut), 29);
            string emoji = "Storm \U0001F525\U0001F525\U0001F525\U0001F525\U0001F525\U0001F525\U0001F525";
            string cutEmoji = HitEventRing.FitLabel(emoji).ToString();
            Assert.IsTrue(emoji.StartsWith(cutEmoji), "whole characters only: " + cutEmoji);
            Assert.IsFalse(cutEmoji.Contains("\uFFFD"), "no broken character");
            Assert.IsFalse(char.IsHighSurrogate(cutEmoji[cutEmoji.Length - 1]), "never half of a pair");
            Assert.AreEqual(string.Empty, HitEventRing.FitLabel(null).ToString());
        }

        [Test]
        public void TheSkillHitRing_KeepsTheNewestSix()
        {
            // M4 step 7: NGO SkillHitVfxClientRpc as data on the attacker's ghost
            using var world = new Unity.Entities.World("skill hit ring");
            var em = world.EntityManager;
            var e = em.CreateEntity();
            em.AddBuffer<SkillHitEvent>(e);
            var seq = new SkillHitSeq();
            for (int i = 1; i <= 8; i++)
                SkillHitRing.Append(em.GetBuffer<SkillHitEvent>(e), ref seq, new SkillHitEvent { SkillId = new FixedString64Bytes("hit" + i), XMm = i });
            var ring = em.GetBuffer<SkillHitEvent>(e);
            Assert.AreEqual(SkillHitRing.Capacity, ring.Length);
            Assert.AreEqual(3u, ring[0].Seq);
            Assert.AreEqual("hit8", ring[ring.Length - 1].SkillId.ToString());
            Assert.AreEqual(8u, SkillHitRing.Newest(ring));
        }

        [Test]
        public void ThePvpSwitch_IsTakenOnFirstSightAndOnEachChange_NeverAgainForTheSameValue()
        {
            // M4 8c: a host's shared switch is never set back by a snapshot that has not caught up with a GM's change
            int seen = PvpSwitchWatch.Unseen;
            Assert.IsTrue(PvpSwitchWatch.Changed(ref seen, 1), "first sight");
            Assert.IsFalse(PvpSwitchWatch.Changed(ref seen, 1), "the same value: left alone");
            Assert.IsTrue(PvpSwitchWatch.Changed(ref seen, 0), "gm.pvp off reached this game");
            Assert.IsFalse(PvpSwitchWatch.Changed(ref seen, 0));
        }

        [Test]
        public void OffSolidGround_IsAJumpFromItsTakeOff_AWall_Water_AVaultLeg_NotTheCrouchNorTheGround()
        {
            // owner rule (5 Nov 2026) + answer: the crouch before take-off is a fall; from take-off on, a de-rez
            var curves = PlayerMotionCurves.Default;
            int tickMicros = 33333;
            var press = new NetworkTick(1000);
            var jump = new MotionPlan { Kind = MotionKind.Jump, PlanSeq = 5, StartTick = press };
            NetworkTick At(long micros) { var t = press; t.Add((uint)(micros / tickMicros)); return t; }
            Assert.IsFalse(DeathGround.OffTheGround(default, curves, At(0), tickMicros, default, default), "flat ground");
            Assert.IsFalse(DeathGround.OffTheGround(jump, curves, press, tickMicros, default, default), "the press: feet still down");
            Assert.IsFalse(DeathGround.OffTheGround(jump, curves, At(curves.TakeoffMicros - tickMicros), tickMicros, default, default), "the crouch");
            Assert.IsTrue(DeathGround.OffTheGround(jump, curves, At(curves.TakeoffMicros + tickMicros), tickMicros, default, default), "just after take-off");
            Assert.IsTrue(DeathGround.OffTheGround(jump, curves, At(curves.ApexMicros), tickMicros, default, default), "mid-jump");
            Assert.IsFalse(DeathGround.OffTheGround(jump, curves, At(curves.LandMicros + 3 * tickMicros), tickMicros, default, default), "landed");
            Assert.IsTrue(DeathGround.OffTheGround(default, curves, press, tickMicros, new MoveState { Wall = 1 }, default), "on a wall");
            Assert.IsTrue(DeathGround.OffTheGround(default, curves, press, tickMicros, new MoveState { Wall = 2 }, default), "in a tree");
            Assert.IsTrue(DeathGround.OffTheGround(default, curves, press, tickMicros, new MoveState { Water = 1 }, default), "on water");
            var vault = new VaultPlan { Seq = 9, LegStartMm = 1000, LegLengthMm = 2000, AirMicros = 500000 };
            Assert.IsTrue(DeathGround.OffTheGround(default, curves, press, tickMicros, new MoveState { OrderSeq = 9, ProgressMm = 1500 }, vault), "in the vault");
            Assert.IsFalse(DeathGround.OffTheGround(default, curves, press, tickMicros, new MoveState { OrderSeq = 9, ProgressMm = 500 }, vault), "walking to it");
        }

        [Test]
        public void TheKnock_GoesFromTheBodyToTheLanding_ThrowsACriticalBodyUp_DropsAnAirborneOne_AndTurnsOnlyToABlowFromTheFront()
        {
            // M4 6b-2: NGO's death warp as the ghost's plan (straight travel here; the game bakes the clip's own)
            var curves = DeathKnockCurves.Default;   // 1.2 s, impact 0.5 s, lift 550 mm, turn 0.12 s
            var plan = new KnockPlan
            {
                Seq = 1, Chain = KnockFlight.Chain(true, false), StartTick = new NetworkTick(100),
                FromXMm = 0, FromYMm = 0, FromZMm = 0, ToXMm = 0, ToYMm = 0, ToZMm = 2000, FromYawCdeg = 0, YawCdeg = 18000,
            };
            Assert.AreEqual(new int3(0, 0, 0), KnockFlight.RootAt(plan, curves, 0));
            Assert.AreEqual(new int3(0, 0, 1000), KnockFlight.RootAt(plan, curves, 600000), "half the clip, half the way");
            Assert.AreEqual(new int3(0, 0, 2000), KnockFlight.RootAt(plan, curves, 1200000), "on the landing");
            Assert.AreEqual(new int3(0, 0, 2000), KnockFlight.RootAt(plan, curves, 5000000), "and stays");
            Assert.AreEqual(0, KnockFlight.YawAt(plan, curves, 0));
            Assert.AreEqual(18000, KnockFlight.YawAt(plan, curves, 120000), "turned to the blow in 0.12 s");
            Assert.IsTrue(KnockFlight.Moves(plan, curves, new NetworkTick(100 + 36), 33333), "moving within the clip");
            Assert.IsFalse(KnockFlight.Moves(plan, curves, new NetworkTick(100 + 40), 33333), "done after it");
            Assert.IsFalse(KnockFlight.Moves(default, curves, new NetworkTick(100), 33333), "no plan, no knock");

            var behind = plan;
            behind.Chain = KnockFlight.Chain(false, false);
            Assert.AreEqual(18000, KnockFlight.YawAt(behind, curves, 0), "a blow from behind never turns the body");

            var crit = plan;
            crit.Chain = KnockFlight.Chain(true, true);
            Assert.AreEqual(550, KnockFlight.RootAt(crit, curves, 250000).y, "the arc's top at half the impact time");
            Assert.AreEqual(0, KnockFlight.RootAt(crit, curves, 500000).y, "down at the impact");

            var slope = plan;
            slope.FromYMm = 3000;
            Assert.AreEqual(1500, KnockFlight.RootAt(slope, curves, 600000).y, "the height follows the travel: no gravity drop (owner, 5 Nov)");
            Assert.AreEqual(0, KnockFlight.RootAt(slope, curves, 1200000).y, "on the landing");
        }

        [Test]
        public void ADeathState_IsAFallOnceAReturnOnce_AndALateViewLiesDown()
        {
            // M4 6b: NGO's DeathClientRpc / RevivedClientRpc once each, and a late joiner's netPose without the fall
            var down = new DeathState { Seq = 1, Pose = 2 };
            Assert.AreEqual(DeathShow.LieDown, DeathStates.What(false, 0, down), "met already down: lying there");
            Assert.AreEqual(DeathShow.Nothing, DeathStates.What(false, 0, default), "met alive: nothing");
            Assert.AreEqual(DeathShow.Fall, DeathStates.What(true, 0, down), "a new death: the fall");
            Assert.AreEqual(DeathShow.Nothing, DeathStates.What(true, 1, down), "the same one: once");
            Assert.AreEqual(DeathShow.GetUp, DeathStates.What(true, 1, new DeathState { Seq = 2 }), "back on its feet");
        }

        [Test]
        public void ARunningBuffsClock_GoesToAnEndTick_AndBack()
        {
            // M4.5i: seconds left travel as the server tick the buff runs out on
            var now = new Unity.NetCode.NetworkTick(1000);
            uint end = RunningBuffs.EndTick(now, 64f, 30);
            Assert.AreEqual(new Unity.NetCode.NetworkTick(1000u + 64u * 30u).SerializedData, end);
            Assert.AreEqual(64f, RunningBuffs.SecondsLeft(now, end, 30), 1e-4);
            var later = new Unity.NetCode.NetworkTick(1000u + 30u * 10u);
            Assert.AreEqual(54f, RunningBuffs.SecondsLeft(later, end, 30), 1e-4, "ten seconds on");
            Assert.LessOrEqual(RunningBuffs.SecondsLeft(new Unity.NetCode.NetworkTick(1000u + 64u * 30u + 5u), end, 30), 0f, "run out");
            Assert.AreEqual(new Unity.NetCode.NetworkTick(1002u).SerializedData, RunningBuffs.EndTick(now, 0.04f, 30), "rounded up: never ends early");
            Assert.AreEqual(0f, RunningBuffs.SecondsLeft(now, 0u, 30), "no tick, nothing left");
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
