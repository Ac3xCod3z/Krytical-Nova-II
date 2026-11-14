using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// The Mount Companion's fight numbers as the pure rules take them (from <see cref="MountSettings.CompanionFight"/>; phases 3-4). The
    /// stance numbers grow with the mount's growth like a pet's stat (owner, 14 Nov 2026): each has its value at 0 % (the plain field) and at
    /// 100 % (the *Full field); <see cref="AtGrowth"/> gives the tuning of one growth with the plain fields lerped. The ride stats are not here
    /// and do not change with a stance.
    /// </summary>
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
        /// <summary>TANK (phase 4): seconds between two taunts; the radius around the companion a taunt also pulls Hostiles after the owner from; its defence buff.</summary>
        public float TauntSeconds, TauntRadius, DefenceBuff;
        /// <summary>TANK: its max HP times this while it acts as TANK.</summary>
        public float TankHpMultiplier;
        /// <summary>The stance numbers at 100 % growth (DamageShare, HealShare, DefenceBuff and TankHpMultiplier are the 0 % ones).</summary>
        public float DamageShareFull, HealShareFull, DefenceBuffFull, TankHpMultiplierFull;

        /// <summary>This tuning at <paramref name="growthPercent"/> (0-100): each stance number lerped from its 0 % value to its 100 % one.</summary>
        public CompanionFightTuning AtGrowth(int growthPercent)
        {
            float g = Mathf.Clamp(growthPercent, 0, 100) / 100f;
            CompanionFightTuning t = this;
            t.DamageShare = Mathf.Lerp(DamageShare, DamageShareFull, g);
            t.HealShare = Mathf.Lerp(HealShare, HealShareFull, g);
            t.DefenceBuff = Mathf.Lerp(DefenceBuff, DefenceBuffFull, g);
            t.TankHpMultiplier = Mathf.Lerp(TankHpMultiplier, TankHpMultiplierFull, g);
            return t;
        }

        /// <summary>The plan's proposed numbers, the same as a new <see cref="MountSettings"/>.</summary>
        public static CompanionFightTuning Default => new CompanionFightTuning
        {
            Range = 12f, LookSeconds = 0.25f, StrikeSeconds = 1.6f, Reach = 2.2f, DamageShare = 0.35f, DamageBuff = 0.15f,
            PulseSeconds = 2f, HealShare = 0.015f, HealOutOfFight = false,
            TauntSeconds = 6f, TauntRadius = 8f, DefenceBuff = 0.4f, TankHpMultiplier = 1f,
            DamageShareFull = 0.61f, HealShareFull = 0.03f, DefenceBuffFull = 0.7f, TankHpMultiplierFull = 2f,
        };
    }

    /// <summary>
    /// The Mount Companion's DPS and HEALER stances (day list item 19, phase 3) and its TANK stance (phase 4), free of networking so they can be tested. Both are PvE only:
    /// in a PvP zone, a duel or a Crossfire battlefield the stance acts as FREE (FREE stays the only PvP stance) and comes back when the owner
    /// leaves. Neither DPS nor HEALER ever takes threat: DPS strikes are dealt in the owner's name with no threat, HEALER deals no damage.
    /// TANK takes it on purpose: it taunts the Hostiles fighting its owner so they turn on it, takes less damage, and deals none. Which Hostile it
    /// fights is <c>CompanionFightPick</c> (Autopilot's target logic inside its short range, run as a Burst job on the server).
    /// </summary>
    public static class CompanionFightRules
    {
        /// <summary>The stances built: FREE, DPS and HEALER (phase 3), TANK (phase 4).</summary>
        public static bool IsBuilt(CompanionStance stance) =>
            stance == CompanionStance.Free || stance == CompanionStance.Dps || stance == CompanionStance.Healer || stance == CompanionStance.Tank;

        /// <summary>
        /// TANK (phase 4): the share of its HP a blow costs while it acts in the stance, against its plain max HP: (1 - its defence buff, never
        /// below 10 %) / its HP multiplier (HP is kept as a share of the max, so a bigger max and a smaller cost are the same thing, and a switch
        /// of stance never changes the HP shown). 1 in every other stance (a TANK in PvP acts as FREE, so a player's hit is never lessened).
        /// </summary>
        public static float TakenScale(CompanionStance acting, in CompanionFightTuning t) =>
            acting == CompanionStance.Tank ? (1f - Mathf.Clamp(t.DefenceBuff, 0f, 0.9f)) / Mathf.Max(1f, t.TankHpMultiplier) : 1f;

        /// <summary>The Mount window's tooltip of a stance, with its numbers at <paramref name="growthPercent"/> (<paramref name="atGrowth"/> is the tuning there).</summary>
        public static string StanceTip(CompanionStance stance, in CompanionFightTuning atGrowth, int growthPercent)
        {
            string at = $"At {Mathf.Clamp(growthPercent, 0, 100)} % growth";
            switch (stance)
            {
                case CompanionStance.Tank:
                    return $"TANK: it taunts the Hostiles you fight so they turn on it. {at}: it takes {Mathf.RoundToInt(Mathf.Clamp(atGrowth.DefenceBuff, 0f, 0.9f) * 100f)} % less damage " +
                           $"and has x{atGrowth.TankHpMultiplier:0.#} HP. It deals no damage. Grows with the mount. PvE only: in PvP it acts as FREE.";
                case CompanionStance.Dps:
                    return $"DPS: it helps you kill what you fight. {at}: each strike is about {Mathf.RoundToInt(StrikeScale(atGrowth) * 100f)} % of your own. " +
                           "It never draws aggro and never heals. Grows with the mount. PvE only: in PvP it acts as FREE.";
                case CompanionStance.Healer:
                    return $"HEALER: it heals you over time while you fight. {at}: {atGrowth.HealShare * 100f:0.##} % of your max HP every {atGrowth.PulseSeconds:0.#} s. " +
                           "It deals no damage and never draws aggro. Grows with the mount. PvE only: in PvP it acts as FREE.";
                default:
                    return "FREE: no combat. It keeps the riding bonus and may come along in PvP.";
            }
        }

        /// <summary>TANK: a taunt is due (its timer ran out and it has a Hostile to taunt).</summary>
        public static bool TauntDue(bool hasFight, float now, float nextTauntAt) => hasFight && now >= nextTauntAt;

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
