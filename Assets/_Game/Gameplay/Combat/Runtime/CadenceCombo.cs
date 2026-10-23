using KryticalNova.Progression;

namespace KryticalNova.Combat
{
    /// <summary>
    /// The Cadence combo (owner, 22-23 Oct 2026): a one-hit skill on the beat, then a two-hit skill on the beat, then a
    /// three-hit skill on the beat. Each step must come within the combo window of the one before (2 s), or the chain
    /// is broken; so is it by any other skill cast in between, or by a cast off the beat. A charged Redline waits
    /// through steps 1 and 2 and is spent across all three hits of step 3 (<see cref="RedlineRules"/>); when the chain
    /// breaks it goes on the cast that broke it, as it always did. A broken chain may start again at once with a
    /// one-hit skill on the beat.
    /// </summary>
    public static class CadenceCombo
    {
        /// <summary>The last step: the three-hit skill.</summary>
        public const int Top = 3;

        /// <summary>The step a skill of this kind is in the chain: 1, 2 or 3, or 0 for one that has no place in it.</summary>
        public static int StepOf(SkillKind kind)
        {
            switch (kind)
            {
                case SkillKind.SingleHit: return 1;
                case SkillKind.TwoHit: return 2;
                case SkillKind.ThreeHit: return 3;
                default: return 0;
            }
        }

        /// <summary>
        /// The step the chain is on after a cast of <paramref name="kind"/>. <paramref name="step"/> is where it was,
        /// <paramref name="alive"/> whether that was still within the combo window. 0 is no chain.
        /// </summary>
        public static int Next(int step, bool alive, SkillKind kind, bool onCadence)
        {
            if (!onCadence) return 0;
            int cast = StepOf(kind);
            int from = alive && step < Top ? step : 0;
            if (cast == from + 1) return cast;
            return cast == 1 ? 1 : 0; // out of order: a one-hit skill starts a new chain, anything else breaks it
        }

        /// <summary>Whether the chain is still alive at <paramref name="now"/>, its window ending at <paramref name="until"/>.</summary>
        public static bool Alive(float now, float until) => now <= until;
    }
}
