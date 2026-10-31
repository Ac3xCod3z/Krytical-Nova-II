using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities.Tests
{
    /// <summary>The integer jump flight (M3b, plan section 53): the shape of NGO's warped jump, as a function of the plan and the tick.</summary>
    public class JumpFlightTests
    {
        static readonly int TickMicros = PlayerMovement.TickMicros(30);

        static MotionPlan Plan(int3 from, int3 to, int apexY, uint startTick = 100) => new MotionPlan
        {
            Kind = MotionKind.Jump,
            PlanSeq = 1,
            StartTick = new NetworkTick(startTick),
            FromXMm = from.x, FromYMm = from.y, FromZMm = from.z,
            ToXMm = to.x, ToYMm = to.y, ToZMm = to.z,
            ApexYMm = apexY,
            YawCdeg = JumpFlight.YawCdeg(from, to),
        };

        [Test]
        public void ALateClick_StartsOnItsOwnTick_WithinTheWindow()
        {
            // GUI run predict-2 (31 Oct): pressed on tick 113, planned by the server on 117
            var curves = PlayerMotionCurves.Default;
            var now = new NetworkTick(117);
            Assert.AreEqual(113u, JumpFlight.ClickStartTick(now, new NetworkTick(113), default, default, curves, TickMicros).TickIndexForValidTick);
            Assert.AreEqual(117u, JumpFlight.ClickStartTick(now, new NetworkTick(117), default, default, curves, TickMicros).TickIndexForValidTick,
                "in time: its own tick");
            // the window is NGO's 0.25 s hold: 7 whole ticks at 30 Hz (never more than the window)
            Assert.AreEqual(110u, JumpFlight.ClickStartTick(now, new NetworkTick(110), default, default, curves, TickMicros).TickIndexForValidTick);
            Assert.AreEqual(117u, JumpFlight.ClickStartTick(now, new NetworkTick(109), default, default, curves, TickMicros).TickIndexForValidTick,
                "8 ticks back is past the window: the server's own tick");
        }

        [Test]
        public void AClaimedTick_OneSecondOld_OrInTheFuture_IsNotTrusted()
        {
            var curves = PlayerMotionCurves.Default;
            var now = new NetworkTick(300);
            Assert.AreEqual(300u, JumpFlight.ClickStartTick(now, new NetworkTick(270), default, default, curves, TickMicros).TickIndexForValidTick,
                "a client claiming a tick 1 s old gets the server's tick");
            Assert.AreEqual(300u, JumpFlight.ClickStartTick(now, new NetworkTick(305), default, default, curves, TickMicros).TickIndexForValidTick,
                "never in the future");
            Assert.AreEqual(300u, JumpFlight.ClickStartTick(now, default, default, default, curves, TickMicros).TickIndexForValidTick,
                "no claim: the server's tick");
        }

        [Test]
        public void ALateClick_NeverStartsBeforeThePreviousJumpsEnd_OrAServerOrder()
        {
            var curves = PlayerMotionCurves.Default;
            int endTicks = (curves.LandMicros + curves.CooldownMicros + TickMicros - 1) / TickMicros;   // 50 at 30 Hz
            var previous = Plan(int3.zero, new int3(3000, 0, 0), 1400, startTick: 100);
            var now = new NetworkTick((uint)(100 + endTicks + 3));
            NetworkTick early = new NetworkTick((uint)(100 + endTicks - 2));
            Assert.AreEqual((uint)(100 + endTicks), JumpFlight.ClickStartTick(now, early, previous, default, curves, TickMicros).TickIndexForValidTick,
                "a claim inside the last jump's cooldown starts at its end");
            var order = new NetworkTick((uint)(100 + endTicks + 2));
            Assert.AreEqual(order.TickIndexForValidTick, JumpFlight.ClickStartTick(now, early, previous, order, curves, TickMicros).TickIndexForValidTick,
                "nor before a server order that started after the press");
        }

        [Test]
        public void TheFeet_StayOnTheStart_UntilTakeoff_AndEndOnTheLanding()
        {
            var curves = PlayerMotionCurves.Default;
            var plan = Plan(new int3(1000, 500, 2000), new int3(5200, -500, 6200), 4000);
            Assert.AreEqual(plan.FromMm, JumpFlight.FeetAt(plan, curves, 0));
            Assert.AreEqual(plan.FromMm, JumpFlight.FeetAt(plan, curves, curves.TakeoffMicros), "the crouch keeps the feet on the ground");
            Assert.AreEqual(plan.ToMm, JumpFlight.FeetAt(plan, curves, curves.LandMicros));
            Assert.AreEqual(plan.ToMm, JumpFlight.FeetAt(plan, curves, curves.LandMicros + 10 * TickMicros), "it stays on the landing");
        }

        [Test]
        public void AtTheApexTime_TheFeetAreAtTheApex_WhereItFallsInTime()
        {
            var curves = PlayerMotionCurves.Default;
            var plan = Plan(new int3(0, 0, 0), new int3(6000, -1000, 0), 1800);
            int3 apex = JumpFlight.FeetAt(plan, curves, curves.ApexMicros);
            Assert.AreEqual(1800, apex.y);
            // even travel over the air time: the apex is (apex - takeoff) / (land - takeoff) of the way across
            long expectedX = 6000L * (curves.ApexMicros - curves.TakeoffMicros) / (curves.LandMicros - curves.TakeoffMicros);
            Assert.AreEqual(expectedX, apex.x, 1);
            Assert.AreEqual(JumpFlight.ApexMm(plan, curves), apex);
        }

        [Test]
        public void TravelAcrossTheGround_NeverGoesBack_AndHeightNeverPassesTheApex()
        {
            var curves = PlayerMotionCurves.Default;
            var plan = Plan(new int3(0, 0, 0), new int3(-3000, 2000, 9000), 4500);
            int lastZ = int.MinValue;
            for (long t = 0; t <= curves.LandMicros + TickMicros; t += 1000)
            {
                int3 feet = JumpFlight.FeetAt(plan, curves, t);
                Assert.GreaterOrEqual(feet.z, lastZ, $"at {t} us");
                Assert.LessOrEqual(feet.y, plan.ApexYMm, $"at {t} us");
                lastZ = feet.z;
            }
        }

        [Test]
        public void TheClipPhases_FallOnTheseTicks_At30Hz()
        {
            // take-off 0.183 s, apex 0.767 s, touchdown 1.407 s: the first full tick at or after each
            var curves = PlayerMotionCurves.Default;
            int FirstTick(int micros) { int t = 0; while ((long)t * TickMicros < micros) t++; return t; }
            Assert.AreEqual(6, FirstTick(curves.TakeoffMicros));
            Assert.AreEqual(24, FirstTick(curves.ApexMicros));
            Assert.AreEqual(43, FirstTick(curves.LandMicros), "lands on the tick after 1.407 s (1.433 s); the body's follow draws between ticks");
            var plan = Plan(int3.zero, new int3(4000, 0, 0), 2000, startTick: 500);
            Assert.IsFalse(JumpFlight.TakenOff(curves, JumpFlight.ElapsedMicros(plan, new NetworkTick(505), TickMicros)));
            Assert.IsTrue(JumpFlight.TakenOff(curves, JumpFlight.ElapsedMicros(plan, new NetworkTick(506), TickMicros)));
            Assert.IsFalse(JumpFlight.Landed(plan, curves, JumpFlight.ElapsedMicros(plan, new NetworkTick(542), TickMicros)));
            Assert.IsTrue(JumpFlight.Landed(plan, curves, JumpFlight.ElapsedMicros(plan, new NetworkTick(543), TickMicros)));
        }

        [Test]
        public void ElapsedTime_IsZeroBeforeThePress_AndWithoutAPlan()
        {
            var plan = Plan(int3.zero, new int3(4000, 0, 0), 2000, startTick: 500);
            Assert.AreEqual(0, JumpFlight.ElapsedMicros(plan, new NetworkTick(499), TickMicros));
            Assert.AreEqual(0, JumpFlight.ElapsedMicros(plan, new NetworkTick(500), TickMicros));
            Assert.AreEqual(3L * TickMicros, JumpFlight.ElapsedMicros(plan, new NetworkTick(503), TickMicros));
            Assert.AreEqual(0, JumpFlight.ElapsedMicros(default, new NetworkTick(503), TickMicros));
        }

        [Test]
        public void ATickSkippedPastTheTouchdown_StillPlacesTheBodyExactlyOnTheLanding()
        {
            // GUI run 1 (31 Oct): a server folding ticks skipped the last flying tick and the body stayed 2-25 cm short
            var curves = PlayerMotionCurves.Default;
            var plan = Plan(int3.zero, new int3(4000, 0, -3000), 1400, startTick: 500);
            foreach (uint t in new uint[] { 541, 546, 560, 900 })
            {
                var tick = new NetworkTick(t);
                Assert.IsTrue(JumpFlight.Places(plan, curves, tick, TickMicros, walkSeq: 0, refusedSeq: 0, orderStart: default), $"tick {t}");
                Assert.AreEqual(t < 543 ? JumpFlight.FeetAt(plan, curves, JumpFlight.ElapsedMicros(plan, tick, TickMicros)) : plan.ToMm,
                    JumpFlight.FeetAt(plan, curves, JumpFlight.ElapsedMicros(plan, tick, TickMicros)));
            }
            Assert.AreEqual(plan.ToMm, JumpFlight.FeetAt(plan, curves, JumpFlight.ElapsedMicros(plan, new NetworkTick(546), TickMicros)),
                "a server that jumps from tick 541 to 546 puts it on the landing");
        }

        [Test]
        public void AfterTheLanding_ANewerWalkOrOrder_TakesTheBodyBack()
        {
            var curves = PlayerMotionCurves.Default;
            var plan = Plan(int3.zero, new int3(4000, 0, 0), 1400, startTick: 500);   // PlanSeq 1
            var landed = new NetworkTick(560);
            Assert.IsTrue(JumpFlight.Places(plan, curves, landed, TickMicros, walkSeq: 0x7FFF, refusedSeq: 0, orderStart: default),
                "the walk before the jump (an older number) does not");
            Assert.IsFalse(JumpFlight.Places(plan, curves, landed, TickMicros, walkSeq: 2, refusedSeq: 0, orderStart: default),
                "a walk clicked after the jump does");
            Assert.IsTrue(JumpFlight.Places(plan, curves, landed, TickMicros, walkSeq: 2, refusedSeq: 2, orderStart: default),
                "a refused walk does not");
            Assert.IsFalse(JumpFlight.Places(plan, curves, landed, TickMicros, walkSeq: 0, refusedSeq: 0, orderStart: new NetworkTick(550)),
                "a server order after the press does");
            Assert.IsTrue(JumpFlight.Places(plan, curves, new NetworkTick(520), TickMicros, walkSeq: 2, refusedSeq: 0, orderStart: default),
                "in the air nothing does (a walk click there is refused)");
            Assert.IsTrue(JumpFlight.SeqNewer(1, 0x7FFF), "the numbers wrap");
            Assert.AreEqual(0, JumpFlight.ClickWalkSeq(new MoveState { OrderSeq = 5000, ServerSeq = 5000 }), "a server order's number is not a click");
            Assert.AreEqual(7, JumpFlight.ClickWalkSeq(new MoveState { OrderSeq = 7, ServerSeq = 5000 }));
        }

        [Test]
        public void TheShareTable_IsExactAtItsSamples_AndStraightBetweenThem()
        {
            var table = new FixedList128Bytes<short>();
            table.Add(0); table.Add(600); table.Add(1000);
            Assert.AreEqual(0, JumpFlight.Share(table, 0, 100));
            Assert.AreEqual(600, JumpFlight.Share(table, 50, 100));
            Assert.AreEqual(1000, JumpFlight.Share(table, 100, 100));
            Assert.AreEqual(300, JumpFlight.Share(table, 25, 100));
            Assert.AreEqual(800, JumpFlight.Share(table, 75, 100));
            Assert.AreEqual(1000, JumpFlight.Share(table, 5, 0), "an empty phase is complete");
        }

        [Test]
        public void TheYaw_FacesTheLanding()
        {
            Assert.AreEqual(0, JumpFlight.YawCdeg(int3.zero, new int3(0, 0, 5000)));
            Assert.AreEqual(9000, JumpFlight.YawCdeg(int3.zero, new int3(5000, 0, 0)));
            Assert.AreEqual(18000, JumpFlight.YawCdeg(int3.zero, new int3(0, 0, -5000)));
            Assert.AreEqual(27000, JumpFlight.YawCdeg(int3.zero, new int3(-5000, 3000, 0)));
        }

        [Test]
        public void CurvesFromAClip_StartAtZero_EndAtAThousand_AndFollowItsHeight()
        {
            // a clip that rises as a sine to the apex and falls as a cosine: the shares follow it, ends exact
            var curves = PlayerMotionCurves.FromClip(0.2f, 0.8f, 1.4f, 0.25f,
                t => t < 0.8f ? math.sin((t - 0.2f) / 0.6f * math.PI / 2f) : math.cos((t - 0.8f) / 0.6f * math.PI / 2f));
            Assert.AreEqual(200000, curves.TakeoffMicros);
            Assert.AreEqual(800000, curves.ApexMicros);
            Assert.AreEqual(1400000, curves.LandMicros);
            Assert.AreEqual(250000, curves.CooldownMicros);
            Assert.AreEqual(PlayerMotionCurves.Samples + 1, curves.Rise.Length);
            Assert.AreEqual(0, curves.Rise[0]);
            Assert.AreEqual(1000, curves.Rise[PlayerMotionCurves.Samples]);
            Assert.AreEqual(0, curves.Fall[0]);
            Assert.AreEqual(1000, curves.Fall[PlayerMotionCurves.Samples]);
            Assert.AreEqual(707, curves.Rise[PlayerMotionCurves.Samples / 2], 2, "sin 45 deg");
            Assert.Throws<System.ArgumentException>(() => PlayerMotionCurves.FromClip(0.8f, 0.2f, 1.4f, 0f, t => t));
        }

        [Test]
        public void TheCrouch_LowersTheDrawnBody_BeforeTakeoff()
        {
            // a clip whose root sinks 0.2 m at mid-crouch and is back at take-off (0.2 s)
            var curves = PlayerMotionCurves.FromClip(0.2f, 0.8f, 1.4f, 0.25f,
                t => t < 0.2f ? -0.2f * math.sin(t / 0.2f * math.PI) : math.sin((t - 0.2f) / 0.6f * math.PI / 2f));
            Assert.AreEqual(PlayerMotionCurves.Samples + 1, curves.Crouch.Length);
            Assert.AreEqual(0, curves.Crouch[0]);
            Assert.AreEqual(-200, JumpFlight.CrouchMm(curves, 100000), 2, "the deepest point of the crouch");
            Assert.AreEqual(0, JumpFlight.CrouchMm(curves, 0));
            Assert.AreEqual(0, JumpFlight.CrouchMm(curves, curves.TakeoffMicros), "this clip is back on its start at the take-off");
            Assert.AreEqual(0, JumpFlight.CrouchMm(PlayerMotionCurves.Default, 100000), "no table, no dip");
        }

        [Test]
        public void TheCrouchesEnd_FadesIntoTheRise_WithoutAStep()
        {
            // a clip whose root is 0.06 m above its start at the take-off (the game's clip: +64 mm)
            var curves = PlayerMotionCurves.FromClip(0.2f, 0.8f, 1.4f, 0.25f,
                t => t < 0.2f ? 0.3f * t : 0.06f + math.sin((t - 0.2f) / 0.6f * math.PI / 2f));
            int before = JumpFlight.CrouchMm(curves, curves.TakeoffMicros - 1);
            int at = JumpFlight.CrouchMm(curves, curves.TakeoffMicros);
            Assert.AreEqual(60, at, 1, "the take-off keeps the clip's height");
            Assert.AreEqual(before, at, 1, "no step at the take-off");
            int mid = JumpFlight.CrouchMm(curves, (curves.TakeoffMicros + curves.ApexMicros) / 2);
            Assert.Less(mid, at, "it fades as the body rises");
            Assert.AreEqual(0, JumpFlight.CrouchMm(curves, curves.ApexMicros), "gone at the apex");
        }
    }
}
