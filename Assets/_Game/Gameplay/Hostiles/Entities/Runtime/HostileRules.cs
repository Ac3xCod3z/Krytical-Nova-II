using Unity.Mathematics;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// The Hostiles mind's rules (pure, Burst). NGO parity where the M5.0 table says so (rows H01, H04, H07, H14), the owner's changes
    /// where it says "replaced by Qn" (Q5 threat switch, Q8 notice).
    /// </summary>
    public static class HostileRules
    {
        public const int ThreatCapacity = 6;

        /// <summary>
        /// H01 (parity): the curve's radius min(z, x + level / y), rounded UP to whole metres as Emerald's detection radius takes it
        /// (HostileBrainLink.cs:109), at least 1 m. <paramref name="curve"/> = HostileStatCurve.aggroRadius (x base, y levels a metre, z cap).
        /// </summary>
        public static float AggroRadius(float3 curve, int level)
        {
            float r = math.min(curve.z, curve.x + (curve.y > 0f ? level / curve.y : 0f));
            return math.max(1f, math.ceil(r));
        }

        /// <summary>H04: walking speed as the curve has it.</summary>
        public static float WalkSpeed(float curveWalk) => curveWalk;

        /// <summary>H04: running speed x + y * level.</summary>
        public static float RunSpeed(float2 curveRun, int level) => curveRun.x + curveRun.y * level;

        /// <summary>Q8: the notice delay in ticks (a species' own seconds, or the default when it has none, i.e. below 0).</summary>
        public static uint NoticeTicks(float speciesSeconds, float defaultSeconds, int tickRate) =>
            (uint)math.max(0, (int)math.ceil((speciesSeconds >= 0f ? speciesSeconds : defaultSeconds) * tickRate - 1e-4f));

        /// <summary>H07 and the reach: within the attack range plus the slack (flat) and not higher than the species reaches above.</summary>
        public static bool InReach(float3 from, float3 targetFeet, float attackRange, float slack, float reachAbove)
        {
            if (targetFeet.y - from.y > reachAbove) return false;
            float2 d = targetFeet.xz - from.xz;
            float reach = attackRange + slack;
            return math.dot(d, d) <= reach * reach;
        }

        /// <summary>The target is within <paramref name="facingDegrees"/> of where the Hostile faces (yaw in radians, 0 = +z).</summary>
        public static bool Facing(float yaw, float3 from, float3 targetFeet, float facingDegrees)
        {
            float2 to = targetFeet.xz - from.xz;
            float len = math.length(to);
            if (len < 1e-4f) return true;
            float2 forward = new float2(math.sin(yaw), math.cos(yaw));
            return math.dot(forward, to / len) >= math.cos(math.radians(facingDegrees));
        }

        /// <summary>The tick a swing started at <paramref name="start"/> commits: after this a dodge no longer cancels it, it can only miss.</summary>
        public static uint CommitTick(uint start, uint windupTicks, float commitFraction) => start + (uint)math.round(commitFraction * windupTicks);

        /// <summary>
        /// Q5 (replaces H03's "never switches"): a challenger takes the Hostile only above 110 % of the current target's threat when it is in
        /// reach (melee), 130 % when it is not.
        /// </summary>
        public static bool ShouldSwitch(float currentThreat, float challengerThreat, bool challengerInReach, float meleeFactor, float rangedFactor) =>
            challengerThreat > currentThreat * (challengerInReach ? meleeFactor : rangedFactor);

        /// <summary>H14 (parity): beyond its zone's radius plus the leash from the zone's centre (flat).</summary>
        public static bool BeyondLeash(float3 position, float2 zoneCentre, float zoneRadius, float leashMetres)
        {
            float limit = zoneRadius + leashMetres;
            return math.lengthsq(position.xz - zoneCentre) > limit * limit;
        }

        /// <summary>A ready attack by weight (NGO's 45/40/15 dice when the weights are those), or -1.</summary>
        public static int PickAbility(Unity.Entities.DynamicBuffer<HostileAbilityRow> abilities, uint now, ref Random rng)
        {
            float total = 0f;
            for (int i = 0; i < abilities.Length; i++) if (abilities[i].ReadyTick <= now) total += math.max(0f, abilities[i].Weight);
            if (total <= 0f) return -1;
            float roll = rng.NextFloat(total);
            for (int i = 0; i < abilities.Length; i++)
            {
                if (abilities[i].ReadyTick > now) continue;
                roll -= math.max(0f, abilities[i].Weight);
                if (roll < 0f) return i;
            }
            return abilities.Length - 1;
        }

        /// <summary>What decides the next mode this tick.</summary>
        public struct ModeInputs
        {
            public byte Mode;
            public byte Behaviour;
            /// <summary>An AttacksOnSight scan found a valid player in the aggro radius.</summary>
            public bool SawPlayer;
            public bool NoticeDone;
            /// <summary>Hit this tick (by a player not on neutral ground).</summary>
            public bool Damaged;
            public bool TargetValid;
            public bool HasOtherThreat;
            public bool BeyondLeash;
            /// <summary>The target has stood beyond the detection radius for the give-up time (Emerald's 5 s, H14).</summary>
            public bool GaveUp;
            public bool AtHome;
            public bool Dead;
        }

        /// <summary>The next mode and whether a return heals at home (the leash does, H14; Emerald's give-up does not).</summary>
        public struct ModeResult
        {
            public byte Mode;
            public bool HealOnHome;
        }

        /// <summary>
        /// The state table (implementation plan M5.3, with the M5.0 parity marks): a hit engages at once from anything but death
        /// (H03 + Q5); Idle/Patrol -> Alert only for AttacksOnSight (H02); Alert -> Engage when noticed (Q8); Engage -> another target
        /// or Return when the target is gone; leash -> Return and heal; give-up -> Return without heal; Return -> Idle at home.
        /// </summary>
        public static ModeResult NextMode(in ModeInputs i)
        {
            if (i.Dead) return new ModeResult { Mode = HostileMode.Dead };
            if (i.Mode == HostileMode.Dead) return new ModeResult { Mode = HostileMode.Dead };
            if (i.Mode == HostileMode.Engage || i.Mode == HostileMode.Alert)
                if (i.BeyondLeash) return new ModeResult { Mode = HostileMode.Return, HealOnHome = true };
            if (i.Damaged && !i.BeyondLeash) return new ModeResult { Mode = HostileMode.Engage };
            switch (i.Mode)
            {
                case HostileMode.Idle:
                case HostileMode.Patrol:
                    if (i.SawPlayer && i.Behaviour == HostileBehaviourKind.AttacksOnSight) return new ModeResult { Mode = HostileMode.Alert };
                    return new ModeResult { Mode = i.Mode };
                case HostileMode.Alert:
                    if (!i.TargetValid) return new ModeResult { Mode = HostileMode.Idle };
                    return new ModeResult { Mode = i.NoticeDone ? HostileMode.Engage : HostileMode.Alert };
                case HostileMode.Engage:
                    if (i.GaveUp) return new ModeResult { Mode = HostileMode.Return, HealOnHome = false };
                    if (!i.TargetValid) return new ModeResult { Mode = i.HasOtherThreat ? HostileMode.Engage : HostileMode.Return };
                    return new ModeResult { Mode = HostileMode.Engage };
                case HostileMode.Return:
                    return new ModeResult { Mode = i.AtHome ? HostileMode.Idle : HostileMode.Return };
                default:
                    return new ModeResult { Mode = i.Mode };
            }
        }
    }
}
