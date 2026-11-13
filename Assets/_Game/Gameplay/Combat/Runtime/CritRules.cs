using KryticalNova.Progression;

namespace KryticalNova.Combat
{
    /// <summary>How one hit of a skill may crit (build 17, item 7.3).</summary>
    public enum CritMode : byte
    {
        /// <summary>The normal crit roll.</summary>
        Roll,
        /// <summary>This hit can never crit.</summary>
        Never,
        /// <summary>This hit is a guaranteed crit (no roll).</summary>
        Always,
    }

    /// <summary>
    /// The combo crit rule (owner, 13 Nov 2026): a one-hit skill pressed OFF the beat rolls for a crit as usual; on the beat the one-hit and
    /// the two-hit can't crit; the two-hit and three-hit can't crit at all, EXCEPT a Redline three-hit pressed on the beat
    /// (<see cref="RedlineRules.HoldsForEveryHit"/>), whose every hit is a guaranteed crit. Area attacks, wards and the basic strikes keep
    /// the normal roll. The server asks this before every hit's crit roll (PlayerSkillAttacks).
    /// </summary>
    public static class CritRules
    {
        /// <param name="kind">The skill's kind (<see cref="SkillKinds.Of"/>).</param>
        /// <param name="onBeat">The cast was pressed on the beat (Cadence).</param>
        /// <param name="redlineCombo">The Redline charge is held for every hit of this cast (<see cref="RedlineRules.HoldsForEveryHit"/>).</param>
        public static CritMode ModeFor(SkillKind kind, bool onBeat, bool redlineCombo)
        {
            switch (kind)
            {
                case SkillKind.SingleHit: return onBeat ? CritMode.Never : CritMode.Roll;
                case SkillKind.TwoHit: return CritMode.Never;
                case SkillKind.ThreeHit: return redlineCombo && onBeat ? CritMode.Always : CritMode.Never;
                default: return CritMode.Roll;
            }
        }

        /// <summary>
        /// Hit <paramref name="hitIndex"/> of <paramref name="hitCount"/>: the mode is the cast's, the same for every hit (all three hits of
        /// the on-beat Redline three-hit are crits).
        /// </summary>
        public static CritMode ModeFor(SkillKind kind, int hitIndex, int hitCount, bool onBeat, bool redlineCombo) =>
            hitIndex < 0 || hitIndex >= hitCount ? CritMode.Never : ModeFor(kind, onBeat, redlineCombo);
    }
}
