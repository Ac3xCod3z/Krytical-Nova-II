using KryticalNova.Progression;

namespace KryticalNova.Combat
{
    /// <summary>What one hit of a skill did: nothing reached the target, the target evaded it, or it landed.</summary>
    public enum HitOutcome : byte
    {
        /// <summary>No hit was made: the target was gone, dead or out of reach at that moment.</summary>
        None,
        Evaded,
        Landed,
    }

    /// <summary>What becomes of a charge (Redline and the like) after one hit.</summary>
    public enum ChargeStep : byte
    {
        /// <summary>Still held for a later hit.</summary>
        Keep,
        /// <summary>Spent on this hit.</summary>
        Spend,
    }

    /// <summary>
    /// When a charged buff that empowers one hit (Redline) is spent (owner, 16 Oct 2026). A single hit, a two-hit
    /// attack, an area attack and the basic combo spend it on the first hit that lands, as before. A three-hit attack
    /// (every class's S3, S5, level 60 multi and S7) pressed on Cadence keeps it for all three: each hit lands as a
    /// critical with the charge's power, and it is spent with the third. The press decides: pressed off the beat, the
    /// charge goes on the first hit. A hit of a three-hit attack that the target evades spends the charge there and
    /// then, and the hits after it are plain; on a cast on Cadence, so does a hit that cannot be made (the target gone
    /// or out of reach).
    /// </summary>
    public static class RedlineRules
    {
        /// <summary>True when this cast keeps the charge for all its hits, which then land as criticals.</summary>
        public static bool HoldsForEveryHit(SkillKind kind, bool charged, bool onCadence) =>
            charged && onCadence && kind == SkillKind.ThreeHit;

        /// <summary>
        /// What the charge does after hit <paramref name="hitIndex"/> of <paramref name="hitCount"/>. Only asked while
        /// the charge is still there.
        /// </summary>
        public static ChargeStep AfterHit(SkillKind kind, bool holding, HitOutcome outcome, int hitIndex, int hitCount)
        {
            if (holding)
                return outcome != HitOutcome.Landed || hitIndex >= hitCount - 1 ? ChargeStep.Spend : ChargeStep.Keep;
            if (outcome == HitOutcome.Landed) return ChargeStep.Spend;
            // A three-hit attack's evaded hit takes the charge with it; anything else waits for a hit that lands.
            return kind == SkillKind.ThreeHit && outcome == HitOutcome.Evaded ? ChargeStep.Spend : ChargeStep.Keep;
        }
    }
}
