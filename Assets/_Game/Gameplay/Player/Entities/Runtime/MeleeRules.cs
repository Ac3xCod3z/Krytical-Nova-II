using Unity.Collections;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// The three-step combo's numbers as the shipped Player prefab has them (NGO's PlayerMeleeCombat, Player.prefab:713-736; M4.4), in
    /// whole units for the entity netcode. The clip lengths come from the three Grruzam katana clips (baked; this struct only carries them).
    /// </summary>
    public struct MeleeTiming
    {
        /// <summary>Each step's clip length (microseconds) and play speed (thousandths): a strike lasts length / speed.</summary>
        public int3 ClipMicros, SpeedMilli;
        /// <summary>Each step's swing window, as thousandths of its clip (the hit lands at the window's END: owner, 4 Nov 2026).</summary>
        public int3 WindowStartPermille, WindowEndPermille;
        /// <summary>Each step's damage multiplier, thousandths (NGO: 1.0 / 1.15 / 1.35).</summary>
        public int3 StepMultiplierPermille;
        public int PostComboMicros, ReachMm, ValidationMm, ReachSlackMm, MinCadencePermille, ArcHalfAngleCdeg;
        /// <summary>The draw (NGO PlayerAnimancerMotor equipWindow 0-0.68 s, Player.prefab): strike 1 waits this long after it began.</summary>
        public int DrawMicros;

        /// <summary>The prefab's numbers (NGO file:line in M4-combat.md section 5), with the clip lengths given.</summary>
        public static MeleeTiming Prefab(int3 clipMicros) => new MeleeTiming
        {
            ClipMicros = clipMicros,
            SpeedMilli = new int3(1411, 1300, 1126),
            WindowStartPermille = new int3(220, 100, 330),
            WindowEndPermille = new int3(650, 300, 700),
            StepMultiplierPermille = new int3(1000, 1150, 1350),
            PostComboMicros = 250000,
            ReachMm = 1600,
            ValidationMm = 2700,
            ReachSlackMm = 500,
            MinCadencePermille = 700,
            // NGO's blade hits what is under it in front; the arc stands in for it on a server with no animated blade (owner pick:
            // target + nearby arc). Half of 120 degrees.
            ArcHalfAngleCdeg = 6000,
            DrawMicros = 680000,
        };
    }

    /// <summary>The melee rules, pure (EditMode-tested): strike length, the hit moment, the combo order and cadence, who a swing hits.</summary>
    public static class MeleeRules
    {
        static int Of(int3 v, int step) => step == 1 ? v.x : step == 2 ? v.y : v.z;

        /// <summary>How long strike <paramref name="step"/> (1-3) lasts: its clip at its speed (NGO PlayerMeleeCombat:436-441).</summary>
        public static int StrikeMicros(in MeleeTiming t, int step)
        {
            int speed = math.max(1, Of(t.SpeedMilli, step));
            return (int)((long)Of(t.ClipMicros, step) * 1000 / speed);
        }

        /// <summary>When strike <paramref name="step"/> lands after it starts: the END of its swing window (NGO's EnsureTargetStruck point).</summary>
        public static int HitAfterMicros(in MeleeTiming t, int step) =>
            (int)((long)StrikeMicros(t, step) * Of(t.WindowEndPermille, step) / 1000);

        /// <summary>The damage multiplier of a step, as a float for CombatMath's skill multiplier.</summary>
        public static float StepMultiplier(in MeleeTiming t, int step) => Of(t.StepMultiplierPermille, step) / 1000f;

        /// <summary>
        /// The step that may start now: 1 after 3 (once the post-combo pause is over) or after nothing, otherwise the next; 0 when it is too
        /// soon (a step may not start before 70 % of the last one, NGO:462-468; after step 3 the whole strike plus the pause).
        /// </summary>
        public static int NextStep(in MeleeTiming t, int lastStep, long sinceLastStartMicros)
        {
            if (lastStep < 1 || lastStep > 3) return 1;
            int last = StrikeMicros(t, lastStep);
            if (lastStep == 3) return sinceLastStartMicros >= last + t.PostComboMicros ? 1 : 0;
            return sinceLastStartMicros * 1000 >= (long)last * t.MinCadencePermille ? lastStep + 1 : 0;
        }

        /// <summary>The server's check when a hit is applied (NGO:759-764): centre to centre within the validation range, ground plane.</summary>
        public static bool WithinValidation(in MeleeTiming t, int3 attackerFeetMm, int3 targetFeetMm)
        {
            long dx = attackerFeetMm.x - targetFeetMm.x, dz = attackerFeetMm.z - targetFeetMm.z;
            return dx * dx + dz * dz <= (long)t.ValidationMm * t.ValidationMm;
        }

        /// <summary>
        /// A target in the arc in front of the attacker (its yaw, hundredths of a degree, 0 = +z) and within reach of its capsule. The
        /// attacker's own target is tested by HitTestAtTick and the validation range; the arc picks the others the blade would have caught.
        /// </summary>
        public static bool InArc(in MeleeTiming t, int3 attackerFeetMm, int yawCdeg, int3 otherFeetMm, HitProxy proxy)
        {
            if (!PositionHistoryRules.WithinReach(attackerFeetMm, otherFeetMm, proxy, t.ReachMm)) return false;
            float dx = otherFeetMm.x - attackerFeetMm.x, dz = otherFeetMm.z - attackerFeetMm.z;
            if (dx * dx + dz * dz < 1f) return true;   // on top of the attacker
            float yaw = math.radians(yawCdeg / 100f);
            float2 facing = new float2(math.sin(yaw), math.cos(yaw));
            float cos = math.dot(math.normalize(new float2(dx, dz)), facing);
            return cos >= math.cos(math.radians(t.ArcHalfAngleCdeg / 100f));
        }

        /// <summary>
        /// Who strike lands on, tested where the attacker saw them (<paramref name="seen"/>, its interpolation tick; lag compensation): the
        /// target when its ring says it was within reach then and it is within validation now, and every other candidate in the arc then.
        /// Indexes of <paramref name="others"/> that are hit go into <paramref name="hit"/>. False with nothing added when the claim is
        /// older than the ring (refused).
        /// </summary>
        public static bool Resolve(in MeleeTiming t, int3 attackerFeetMm, int yawCdeg, NetworkTick now, NetworkTick seen, in PositionHistory target,
            int3 targetNowMm, NativeArray<PositionHistory> others, HitProxy proxy, out bool targetHit, ref NativeList<int> hit) =>
            Resolve(in t, attackerFeetMm, yawCdeg, now, seen, in target, targetNowMm, proxy, others, default, proxy, out targetHit, ref hit);

        /// <summary>
        /// The same with each body's own capsule (M4.2b: a Strike Dummy is not a player's size): <paramref name="targetProxy"/> for the
        /// target, <paramref name="otherProxies"/>[i] for each other (not created = <paramref name="otherProxy"/> for all).
        /// </summary>
        public static bool Resolve(in MeleeTiming t, int3 attackerFeetMm, int yawCdeg, NetworkTick now, NetworkTick seen, in PositionHistory target,
            int3 targetNowMm, HitProxy targetProxy, NativeArray<PositionHistory> others, NativeArray<HitProxy> otherProxies, HitProxy otherProxy,
            out bool targetHit, ref NativeList<int> hit)
        {
            targetHit = PositionHistoryRules.HitTestAtTick(in target, targetProxy, now, seen, attackerFeetMm, t.ReachMm, out bool refused);
            if (refused) { targetHit = false; return false; }
            NetworkTick at = PositionHistoryRules.TestTick(now, seen, out _);
            // A claim for this very tick (no view delay: "Play DOTS here", owner retest 8 Nov 2026: every strike on the dummy missed) finds
            // no sample yet, as the ring is written at the tick's end: the target is where it stands now.
            if (!targetHit && at.Equals(now) && !PositionHistoryRules.TryFeetAt(in target, at, out _))
                targetHit = PositionHistoryRules.WithinReach(attackerFeetMm, targetNowMm, targetProxy, t.ReachMm);
            targetHit = targetHit && WithinValidation(in t, attackerFeetMm, targetNowMm);
            for (int i = 0; i < others.Length; i++)
            {
                HitProxy p = otherProxies.IsCreated && i < otherProxies.Length ? otherProxies[i] : otherProxy;
                if (FeetForTest(others[i], at, now, out int3 feet) && InArc(in t, attackerFeetMm, yawCdeg, feet, p)) hit.Add(i);
            }
            return true;
        }

        /// <summary>Where a ghost stood on the tested tick; for this very tick (not written yet) its newest sample, one tick old.</summary>
        static bool FeetForTest(in PositionHistory ring, NetworkTick at, NetworkTick now, out int3 feet)
        {
            if (PositionHistoryRules.TryFeetAt(in ring, at, out feet)) return true;
            if (!at.Equals(now) || ring.Samples.Length == 0) return false;
            PositionSample newest = ring.Samples[ring.Samples.Length - 1];
            if (now.TickIndexForValidTick - newest.Tick > 1) return false;   // gone for longer than a tick: not there
            feet = newest.FeetMm;
            return true;
        }

        /// <summary>NGO ReachHeightSlack (PlayerMeleeCombat:631): height counts once it is more than a slope or a step.</summary>
        public const int ReachHeightSlackMm = 1200;

        // NGO ReachOffset (PlayerMeleeCombat:625): the way to the target as reach counts it
        static long ReachSquared(int3 attackerMm, int3 targetMm)
        {
            int3 d = targetMm - attackerMm;
            long y = math.max(0, math.abs(d.y) - ReachHeightSlackMm);
            return (long)d.x * d.x + (long)d.z * d.z + y * y;
        }

        /// <summary>NGO IsWithinStrikingRange (PlayerMeleeCombat:607): within the striking range (1.6 m).</summary>
        public static bool InStrikingRange(in MeleeTiming t, int3 attackerMm, int3 targetMm) =>
            ReachSquared(attackerMm, targetMm) <= (long)t.ReachMm * t.ReachMm;

        /// <summary>NGO HasLeftReach (PlayerMeleeCombat:614): further than the range plus the combo's reach slack (0.5 m).</summary>
        public static bool LeftReach(in MeleeTiming t, int3 attackerMm, int3 targetMm)
        {
            long reach = t.ReachMm + t.ReachSlackMm;
            return ReachSquared(attackerMm, targetMm) > reach * reach;
        }

        /// <summary>
        /// True on the server tick the strike under way lands (M4.4e): the END of its swing window (NGO EnsureTargetStruck), once per
        /// swing (NGO serverSwingHitTargets).
        /// </summary>
        public static bool HitDue(in MeleeTiming t, in CombatState s, NetworkTick now, int tickRate) =>
            s.Step > 0 && s.SwingHit == 0 && s.TargetGhostId != 0
            && now.TicksSince(s.StepStartTick) >= Ticks(HitAfterMicros(t, s.Step), tickRate);

        /// <summary>NGO approachInset (Player.prefab, 0.15 m): the walk in stops this far inside the striking range.</summary>
        public const int ApproachInsetMm = 150;

        /// <summary>NGO ApproachRepathInterval (0.1 s): how often the walk in is aimed again at a target that moves.</summary>
        public const int ApproachRepathMicros = 100000;

        /// <summary>
        /// Where the server walks the attacker to (NGO ApproachPoint, PlayerMeleeCombat:656): on the attacker's side of the target, the
        /// striking range less the inset away from it, on the ground plane; at the target itself when they stand on the same spot.
        /// </summary>
        public static int3 ApproachPointMm(in MeleeTiming t, int3 attackerMm, int3 targetMm)
        {
            int dx = attackerMm.x - targetMm.x, dz = attackerMm.z - targetMm.z;
            long lengthSq = (long)dx * dx + (long)dz * dz;
            if (lengthSq < 100) return targetMm; // under 1 cm (NGO: 0.0001 m squared)
            int ring = math.max(100, t.ReachMm - ApproachInsetMm);
            double length = math.sqrt((double)lengthSq);
            return new int3(targetMm.x + (int)math.round(dx * ring / length), targetMm.y, targetMm.z + (int)math.round(dz * ring / length));
        }

        /// <summary>
        /// NGO FaceTarget (PlayerMeleeCombat:698): the yaw, hundredths of a degree 0..35999 (0 = +z, as <see cref="InArc"/> reads it), that faces
        /// from <paramref name="fromMm"/> toward <paramref name="toMm"/> on the ground plane; <paramref name="currentCdeg"/> when they stand
        /// within 10 cm (NGO: 0.01 m squared).
        /// </summary>
        public static int YawTowardCdeg(int3 fromMm, int3 toMm, int currentCdeg)
        {
            long dx = toMm.x - fromMm.x, dz = toMm.z - fromMm.z;
            if (dx * dx + dz * dz <= 100L * 100L) return currentCdeg;
            int cdeg = (int)math.round(math.degrees(math.atan2((double)dx, (double)dz)) * 100.0);
            return ((cdeg % 36000) + 36000) % 36000;
        }

        /// <summary>A length in microseconds as whole ticks, rounded up (a strike never ends early).</summary>
        public static int Ticks(int micros, int tickRate) => (int)(((long)micros * tickRate + 999999) / 1000000);

        /// <summary>The owner's attack order (NGO StartAttackTarget, PlayerMeleeCombat:176): the target, the combo from its start.</summary>
        public static void Begin(ref CombatState s, int targetGhostId)
        {
            if (s.TargetGhostId == targetGhostId && s.Step > 0) return; // the same target again: the combo under way goes on
            s = new CombatState { TargetGhostId = targetGhostId, WeaponDrawn = s.WeaponDrawn, DrawnTick = s.DrawnTick };
        }

        /// <summary>The attack ends (NGO CancelAttack, PlayerMeleeCombat:222): no target, no step; the weapon stays as it is.</summary>
        public static void Cancel(ref CombatState s) => s = new CombatState { WeaponDrawn = s.WeaponDrawn, DrawnTick = s.DrawnTick };

        /// <summary>Draws the weapon from <paramref name="now"/> (an attack, an attack skill, the key); nothing when it is already drawn.</summary>
        public static void Draw(ref CombatState s, NetworkTick now)
        {
            if (s.WeaponDrawn != 0) return;
            s.WeaponDrawn = 1;
            s.DrawnTick = now;
        }

        /// <summary>The key (NGO RequestToggleWeaponDraw): drawn -> sheathed (the attack ends, NGO's key cannot swing), sheathed -> drawn.</summary>
        public static void Toggle(ref CombatState s, NetworkTick now)
        {
            if (s.WeaponDrawn == 0) { Draw(ref s, now); return; }
            Sheathe(ref s);
        }

        /// <summary>The weapon is put away (the key's sheathe half, an emote): the attack ends; nothing when it is sheathed already.</summary>
        public static void Sheathe(ref CombatState s)
        {
            if (s.WeaponDrawn == 0) return;
            Cancel(ref s);
            s.WeaponDrawn = 0;
            s.DrawnTick = default;
        }

        /// <summary>The draw has finished (or was never timed): strike 1 may start (NGO: the combo loop waits while Equipping).</summary>
        public static bool DrawDone(in MeleeTiming t, in CombatState s, NetworkTick now, int tickRate) =>
            s.WeaponDrawn != 0 && (!s.DrawnTick.IsValid || now.TicksSince(s.DrawnTick) >= Ticks(t.DrawMicros, tickRate));

        /// <summary>
        /// One tick of NGO's ComboLoopRoutine (PlayerMeleeCombat:249-330): while the target lives, step 0 waits until it is within striking
        /// range (the walk in is the server's), then strikes 1, 2 and 3 follow each other, each for its whole length; a target that has
        /// left the reach after a strike is chased (back to step 0, the combo restarts at 1); after strike 3 the post-combo pause, then
        /// the loop again. A dead or missing target ends the attack.
        /// </summary>
        public static void Tick(in MeleeTiming t, ref CombatState s, NetworkTick now, int tickRate, bool targetAlive, bool inStrikingRange, bool leftReach)
        {
            if (s.TargetGhostId == 0) return;
            if (!targetAlive) { Cancel(ref s); return; }
            Draw(ref s, now);   // NGO StartAttackTarget draws on the order, so the draw runs during the walk in
            if (s.Step > 0)
            {
                int length = StrikeMicros(t, s.Step) + (s.Step == 3 ? t.PostComboMicros : 0);
                if (now.TicksSince(s.StepStartTick) < Ticks(length, tickRate)) return; // the strike (or the pause after 3) is under way
                if (s.Step == 3 || leftReach) s.Step = 0; // the loop goes round: walk in again if need be, then strike 1
                else { StartStep(in t, ref s, s.Step + 1, now, tickRate); return; }
            }
            if (inStrikingRange && DrawDone(in t, in s, now, tickRate)) StartStep(in t, ref s, 1, now, tickRate);
        }

        static void StartStep(in MeleeTiming t, ref CombatState s, int step, NetworkTick now, int tickRate)
        {
            s.Step = (byte)step;
            s.StepStartTick = now;
            var earliest = now;
            earliest.Add((uint)Ticks((int)((long)StrikeMicros(t, step) * t.MinCadencePermille / 1000), tickRate));
            s.NextStepEarliest = earliest;
            s.SwingHit = 0;
        }
    }
}
