using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// The fight music's beat as Cadence reads it (design 17). Audio's <c>MusicConductor</c> is the clock there is; Combat
    /// names only this, so the rules of a fight do not depend on how its music is played.
    /// </summary>
    public interface IBeatClock
    {
        /// <summary>The zone's shared clock: server time when networked, local time offline.</summary>
        double Now { get; }

        /// <summary>This player's game hears a beat now (in a fight, with fight music playing).</summary>
        bool HasBeat { get; }

        /// <summary>How fully the beat is heard, 0 to 1: it fades in and out with the fight music.</summary>
        float Live01 { get; }

        /// <summary>How far this player's ears are behind the shared clock, in seconds.</summary>
        double ClockLagSeconds { get; }

        /// <summary>True where the fight music, and Cadence, are only for players in a Callout (<see cref="PlayerCombatState.InFightFor"/>).</summary>
        bool CalloutsOnly { get; }

        /// <summary>
        /// The beat at shared-clock time <paramref name="at"/>: the track playing then, how far the moment is from that
        /// track's nearest beat (negative is early) and how many beats it is since the track's first. False between two
        /// tracks and where there is no fight music.
        /// </summary>
        bool TryBeatAt(double at, out ScriptableObject track, out double errorSeconds, out double beats);
    }

    /// <summary>Where Combat finds the beat clock. Empty where there is none (a scene without a conductor).</summary>
    public static class BeatClock
    {
        public static IBeatClock Current { get; private set; }

        public static void Register(IBeatClock clock) => Current = clock;

        public static void Unregister(IBeatClock clock)
        {
            if (ReferenceEquals(Current, clock)) Current = null;
        }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
