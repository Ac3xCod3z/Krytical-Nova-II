using System;
using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>The Mount Companion's numbers as the pure rules take them (from <see cref="MountSettings.Companion"/>).</summary>
    public struct CompanionTuning
    {
        public float RestBehind, RestLeft, MinGap, TrailSeconds;
        public float CatchUpDistance, CatchUpEnd, CatchUpScale;
        public float WarpDistance, TeleportJump, StuckSeconds, WarpCooldown, WalkSpeed;
        public float HpShareAtZero, HpShareAtFull, KnockOutSeconds, ComeBackShare, HomeRegenPerMinute, StimHealShare;

        /// <summary>The plan's proposed numbers (owner picks 1 a, 2 a, 3 a), the same as a new <see cref="MountSettings"/>.</summary>
        public static CompanionTuning Default => new CompanionTuning
        {
            RestBehind = 2.5f, RestLeft = 1.5f, MinGap = 1.2f, TrailSeconds = 0.25f,
            CatchUpDistance = 8f, CatchUpEnd = 3f, CatchUpScale = 1.25f,
            WarpDistance = 30f, TeleportJump = 10f, StuckSeconds = 3f, WarpCooldown = 2f, WalkSpeed = 4f,
            HpShareAtZero = 0.6f, HpShareAtFull = 1f, KnockOutSeconds = 300f, ComeBackShare = 0.3f, HomeRegenPerMinute = 0.02f, StimHealShare = 0.4f,
        };
    }

    /// <summary>What a Mount Companion is doing, as every view sees it (<c>CompanionView.State</c>).</summary>
    public enum CompanionState : byte
    {
        Out = 1,
        /// <summary>It vanished and formed again at its spot (too far, a teleport, stuck).</summary>
        Warping = 2,
        KnockedOut = 3,
        Leaving = 4,
    }

    /// <summary>The Mount Companion's stance, set in the Mount window (phases 1-2: FREE only; the others come in phases 3-4).</summary>
    public enum CompanionStance : byte
    {
        /// <summary>No combat, keeps the riding bonus, allowed in PvP.</summary>
        Free = 0,
        Tank = 1,
        Dps = 2,
        Healer = 3,
    }

    /// <summary>
    /// The Mount Companion's HP, knock-out and rest rules (day list item 19, phase 2), free of networking so they can be tested. HP is held
    /// as permille LOST (0 = full), so a level-up or a growth step never hurts it; time is whole UTC seconds, so a relog cannot skip a rest
    /// and an offline rest counts.
    /// </summary>
    public static class CompanionRules
    {
        public const int Permille = 1000;

        /// <summary>Its max HP: the owner's max HP times a share that grows from <see cref="CompanionTuning.HpShareAtZero"/> to
        /// <see cref="CompanionTuning.HpShareAtFull"/> with growth.</summary>
        public static float MaxHp(float ownerMaxHp, int growthPercent, in CompanionTuning t)
        {
            float g = Mathf.Clamp(growthPercent, 0, 100) / 100f;
            return Mathf.Max(1f, ownerMaxHp * Mathf.Lerp(t.HpShareAtZero, t.HpShareAtFull, g));
        }

        /// <summary>Permille lost right now: at home and past <paramref name="hpAt"/> it regains <see cref="CompanionTuning.HomeRegenPerMinute"/>
        /// of its max a minute; out (or before <paramref name="hpAt"/>, the end of a rest) nothing comes back.</summary>
        public static int LostNow(int lost, long hpAt, long now, bool atHome, in CompanionTuning t)
        {
            lost = Math.Clamp(lost, 0, Permille);
            if (!atHome || now <= hpAt || lost == 0 || t.HomeRegenPerMinute <= 0f) return lost;
            double back = (now - hpAt) / 60.0 * t.HomeRegenPerMinute * Permille;
            return back >= lost ? 0 : lost - (int)Math.Floor(back);
        }

        /// <summary>Writes the regen so far into <paramref name="lost"/> and moves <paramref name="hpAt"/> to now (call before every change).</summary>
        public static void Settle(ref int lost, ref long hpAt, long now, bool atHome, in CompanionTuning t)
        {
            lost = LostNow(lost, hpAt, now, atHome, t);
            if (now > hpAt) hpAt = now;
        }

        /// <summary>A hit of <paramref name="damage"/> on a companion of <paramref name="maxHp"/>; true when it is knocked out.</summary>
        public static bool Hurt(ref int lost, float damage, float maxHp)
        {
            if (damage <= 0f) return lost >= Permille;
            int more = Mathf.CeilToInt(damage / Mathf.Max(1f, maxHp) * Permille);
            lost = Math.Min(Permille, Math.Max(0, lost) + Math.Max(1, more));
            return lost >= Permille;
        }

        /// <summary>Knocked out at <paramref name="now"/>: it rests <see cref="CompanionTuning.KnockOutSeconds"/> and comes back at
        /// <see cref="CompanionTuning.ComeBackShare"/> of its max, regen counting from the end of the rest.</summary>
        public static void KnockOut(long now, in CompanionTuning t, out int lost, out long hpAt, out long restUntil)
        {
            restUntil = now + (long)Math.Ceiling(Math.Max(0f, t.KnockOutSeconds));
            hpAt = restUntil;
            lost = Permille - Mathf.RoundToInt(Mathf.Clamp01(t.ComeBackShare) * Permille);
        }

        /// <summary>Seconds of rest left, 0 when it may be called.</summary>
        public static long RestLeft(long restUntil, long now) => Math.Max(0L, restUntil - now);

        /// <summary>What a Mount Stim leaves lost: <see cref="CompanionTuning.StimHealShare"/> of its max back.</summary>
        public static int StimHealed(int lost, in CompanionTuning t) =>
            Math.Max(0, Math.Clamp(lost, 0, Permille) - Mathf.RoundToInt(Mathf.Clamp01(t.StimHealShare) * Permille));

        /// <summary>"3:12" for a rest of 192 s.</summary>
        public static string Clock(long seconds)
        {
            seconds = Math.Max(0L, seconds);
            return $"{seconds / 60}:{seconds % 60:00}";
        }

        /// <summary>Where it rests: <see cref="CompanionTuning.RestBehind"/> behind the owner and <see cref="CompanionTuning.RestLeft"/> to the left.</summary>
        public static Vector3 RestSpot(Vector3 owner, float ownerYawDegrees, in CompanionTuning t)
        {
            Quaternion facing = Quaternion.Euler(0f, ownerYawDegrees, 0f);
            return owner - facing * Vector3.forward * t.RestBehind - facing * Vector3.right * t.RestLeft;
        }

        internal static float Flat(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }
    }

    /// <summary>
    /// The server's follow of one Mount Companion (phase 1): it walks the owner's path (a breadcrumb trail sampled every
    /// <see cref="CompanionTuning.TrailSeconds"/>, so it stays on ground the owner walked and needs no NavMesh while moving), at the owner's
    /// speed up to its own, faster when far behind, keeps <see cref="CompanionTuning.MinGap"/> from the owner, rests behind-left when the
    /// owner stops, and warps to its spot when too far, after a teleport or when stuck (at most one warp per
    /// <see cref="CompanionTuning.WarpCooldown"/>). Pure: tested in EditMode; the server system feeds it the owner's ghost.
    /// </summary>
    public sealed class CompanionFollower
    {
        public const int TrailCapacity = 16;
        /// <summary>A trail point this close is reached.</summary>
        const float Reached = 1f;

        readonly Vector3[] trail = new Vector3[TrailCapacity];
        int head, count;
        Vector3 lastSample;
        bool sampled, catchingUp;
        float sampleTimer, stuckTimer, bestGap = float.MaxValue, nextWarpAt = float.NegativeInfinity;

        public Vector3 Position { get; private set; }
        /// <summary>Facing, degrees about Y.</summary>
        public float Yaw { get; private set; }
        /// <summary>Ground speed of the last step, m/s.</summary>
        public float Speed { get; private set; }
        public int TrailCount => count;
        public bool CatchingUp => catchingUp;
        public int Warps { get; private set; }

        public enum Result { Moved, Warped }

        /// <summary>Puts it at <paramref name="at"/> (a summon, a warp); the trail starts again.</summary>
        public void Place(Vector3 at, float yaw)
        {
            Position = at;
            Yaw = yaw;
            Speed = 0f;
            head = count = 0;
            sampled = false;
            catchingUp = false;
            stuckTimer = 0f;
            bestGap = float.MaxValue;
        }

        /// <param name="maxSpeed">The mount's own run speed at its growth (before the catch-up scale).</param>
        public Result Step(Vector3 owner, float ownerYaw, float ownerSpeed, float maxSpeed, float dt, float now, in CompanionTuning t)
        {
            if (dt <= 0f) return Result.Moved;
            bool teleported = false;
            if (!sampled) { lastSample = owner; sampled = true; sampleTimer = 0f; }
            if (CompanionRules.Flat(owner, lastSample) > t.TeleportJump) teleported = true;
            sampleTimer += dt;
            if (!teleported && sampleTimer >= t.TrailSeconds)
            {
                sampleTimer = 0f;
                if (CompanionRules.Flat(owner, lastSample) > 0.3f) Push(owner);
                lastSample = owner;
            }

            Vector3 rest = CompanionRules.RestSpot(owner, ownerYaw, t);
            float fromOwner = CompanionRules.Flat(Position, owner);
            if ((teleported || fromOwner > t.WarpDistance || stuckTimer >= t.StuckSeconds) && now >= nextWarpAt)
            {
                nextWarpAt = now + t.WarpCooldown;
                Place(rest, ownerYaw);
                lastSample = owner;
                sampled = true;
                Warps++;
                return Result.Warped;
            }

            // walk the path until it is inside its resting distance of the owner, then go to the spot
            float restRadius = CompanionRules.Flat(rest, owner);
            while (count > 0 && (CompanionRules.Flat(Position, Front) < Reached || CompanionRules.Flat(Front, owner) <= restRadius)) Pop();
            Vector3 target = count > 0 ? Front : rest;

            float gap = CompanionRules.Flat(Position, rest);
            if (gap > t.CatchUpDistance) catchingUp = true;
            else if (gap < t.CatchUpEnd) catchingUp = false;
            float speed = Mathf.Max(ownerSpeed, t.WalkSpeed);
            float cap = Mathf.Max(t.WalkSpeed, maxSpeed);
            if (catchingUp) { speed *= t.CatchUpScale; cap *= t.CatchUpScale; }
            speed = Mathf.Min(speed, cap);

            Vector3 to = target - Position;
            float dist = to.magnitude;
            float move = Mathf.Min(speed * dt, dist);
            Vector3 before = Position;
            Vector3 next = dist > 1e-4f ? Position + to / dist * move : Position;

            // never closer than the gap to the owner
            Vector3 away = next - owner;
            away.y = 0f;
            float flat = away.magnitude;
            if (flat < t.MinGap)
            {
                Vector3 dir = flat > 1e-3f ? away / flat : Quaternion.Euler(0f, ownerYaw, 0f) * Vector3.back;
                next = new Vector3(owner.x + dir.x * t.MinGap, next.y, owner.z + dir.z * t.MinGap);
            }
            Position = next;
            float travelled = CompanionRules.Flat(before, next);
            Speed = travelled / dt;
            if (travelled > 0.01f) Yaw = Mathf.Atan2(next.x - before.x, next.z - before.z) * Mathf.Rad2Deg;
            else if (gap < 0.5f) Yaw = Mathf.MoveTowardsAngle(Yaw, ownerYaw, 360f * dt);

            // stuck: no progress towards its spot while it should be closing in
            if (gap > t.CatchUpEnd && ownerSpeed < 0.5f)
            {
                if (gap < bestGap - 0.1f) { bestGap = gap; stuckTimer = 0f; }
                else stuckTimer += dt;
            }
            else { bestGap = gap; stuckTimer = 0f; }
            return Result.Moved;
        }

        Vector3 Front => trail[head];

        void Push(Vector3 p)
        {
            if (count == TrailCapacity) Pop();
            trail[(head + count) % TrailCapacity] = p;
            count++;
        }

        void Pop()
        {
            head = (head + 1) % TrailCapacity;
            count--;
        }
    }
}
