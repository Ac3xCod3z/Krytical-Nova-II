using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>The Mount Companion's fight numbers as the pure rules take them (from <see cref="MountSettings.CompanionFight"/>; phase 3).</summary>
    public struct CompanionFightTuning
    {
        /// <summary>It fights only Hostiles this close to its owner, metres.</summary>
        public float Range;
        /// <summary>How often the server looks for its fight, seconds.</summary>
        public float LookSeconds;
        /// <summary>DPS: seconds between two strikes; the reach past the Hostile's edge; the share of the owner's roll; its own damage buff.</summary>
        public float StrikeSeconds, Reach, DamageShare, DamageBuff;
        /// <summary>HEALER: seconds between two pulses; the share of the owner's max HP a pulse heals; heals out of a fight too.</summary>
        public float PulseSeconds, HealShare;
        public bool HealOutOfFight;

        /// <summary>The plan's proposed numbers, the same as a new <see cref="MountSettings"/>.</summary>
        public static CompanionFightTuning Default => new CompanionFightTuning
        {
            Range = 12f, LookSeconds = 0.25f, StrikeSeconds = 1.6f, Reach = 2.2f, DamageShare = 0.35f, DamageBuff = 0.15f,
            PulseSeconds = 2f, HealShare = 0.015f, HealOutOfFight = false,
        };
    }

    /// <summary>
    /// The Mount Companion's DPS and HEALER stances (day list item 19, phase 3), free of networking so they can be tested. Both are PvE only:
    /// in a PvP zone, a duel or a Crossfire battlefield the stance acts as FREE (FREE stays the only PvP stance) and comes back when the owner
    /// leaves. Neither ever takes threat: DPS strikes are dealt in the owner's name with no threat, HEALER deals no damage. Which Hostile it
    /// fights is <c>CompanionFightPick</c> (Autopilot's target logic inside its short range, run as a Burst job on the server).
    /// </summary>
    public static class CompanionFightRules
    {
        /// <summary>The stances built so far (TANK comes in phase 4).</summary>
        public static bool IsBuilt(CompanionStance stance) => stance == CompanionStance.Free || stance == CompanionStance.Dps || stance == CompanionStance.Healer;

        /// <summary>A stance that fights or heals (not FREE).</summary>
        public static bool IsCombat(CompanionStance stance) => stance == CompanionStance.Dps || stance == CompanionStance.Healer || stance == CompanionStance.Tank;

        /// <summary>The stance it acts in: a combat stance acts as FREE while its owner is in PvP (a PvP zone, a duel, a Crossfire battlefield).</summary>
        public static CompanionStance Effective(CompanionStance chosen, bool ownerInPvp) => ownerInPvp && IsCombat(chosen) ? CompanionStance.Free : chosen;

        /// <summary>What one DPS strike deals: the owner's own roll on that Hostile times its share and its damage buff.</summary>
        public static float StrikeScale(in CompanionFightTuning t) => Mathf.Max(0f, t.DamageShare) * (1f + Mathf.Max(0f, t.DamageBuff));

        /// <summary>HP one HEALER pulse gives back to an owner of <paramref name="ownerMaxHp"/>.</summary>
        public static float PulseHeal(float ownerMaxHp, in CompanionFightTuning t) => Mathf.Max(0f, ownerMaxHp) * Mathf.Clamp01(t.HealShare);

        /// <summary>HEALER heals now: the owner is hurt and alive, and in a fight with a Hostile unless it heals out of a fight too.</summary>
        public static bool ShouldHeal(bool inFight, bool ownerAlive, float hp, float maxHp, in CompanionFightTuning t) =>
            ownerAlive && maxHp > 0f && hp < maxHp && (inFight || t.HealOutOfFight);

        /// <summary>Where a DPS companion stands to strike: on its own side of the Hostile, a little inside its reach of the Hostile's edge.</summary>
        public static Vector3 StrikeSpot(Vector3 from, Vector3 hostile, float hostileRadius, in CompanionFightTuning t)
        {
            Vector3 away = from - hostile;
            away.y = 0f;
            float flat = away.magnitude;
            Vector3 dir = flat > 1e-3f ? away / flat : Vector3.back;
            float keep = Mathf.Max(0f, hostileRadius) + Mathf.Max(0.3f, t.Reach) * 0.8f;
            return new Vector3(hostile.x + dir.x * keep, from.y, hostile.z + dir.z * keep);
        }

        /// <summary>A DPS companion at <paramref name="at"/> can strike the Hostile (flat distance to its edge within the reach).</summary>
        public static bool InReach(Vector3 at, Vector3 hostile, float hostileRadius, in CompanionFightTuning t) =>
            CompanionRules.Flat(at, hostile) <= Mathf.Max(0f, hostileRadius) + Mathf.Max(0.3f, t.Reach);

        /// <summary>
        /// One DPS strike from the owner's own roll on the Hostile: every damage part scaled by <see cref="StrikeScale"/>, dealt with no threat
        /// (<see cref="Core.DamageResult.NoThreat"/>) so it never draws aggro; an evaded roll stays a miss.
        /// </summary>
        public static Core.DamageResult Strike(Core.DamageResult ownerRoll, in CompanionFightTuning t)
        {
            float k = StrikeScale(t);
            Core.DamageResult r = ownerRoll;
            r.DamageDealt *= k;
            r.RawPhysicalDamage *= k;
            r.MitigatedPhysicalDamage *= k;
            r.NetElementalDamage *= k;
            r.IsReflected = false;      // a reflect would hurt the owner for the mount's strike
            r.ReflectedDamage = 0f;
            r.ShieldShattered = false;
            r.NoThreat = true;
            return r;
        }
    }
}
