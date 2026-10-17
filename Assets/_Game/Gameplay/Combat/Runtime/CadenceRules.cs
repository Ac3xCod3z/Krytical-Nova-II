using System;

namespace KryticalNova.Combat
{
    /// <summary>What the server makes of a skill key pressed in a fight.</summary>
    public enum CadenceVerdict : byte
    {
        /// <summary>Cadence is switched off.</summary>
        Off,
        /// <summary>The player is not in a fight: there is no beat to be on.</summary>
        NotInAFight,
        /// <summary>No track with a beat was playing at that moment (between two tracks, a zone with no fight music).</summary>
        NoBeat,
        /// <summary>The press is too old, or from a moment that has not come yet: it is not believed.</summary>
        Stale,
        /// <summary>A fair press, but not on the beat. Nothing is lost.</summary>
        OffBeat,
        /// <summary>On the beat: the next cast of that skill hits a little harder.</summary>
        OnCadence,
    }

    /// <summary>
    /// The rules of Cadence, apart from any object: whether a press is on the beat, whether the server believes it,
    /// how long it is kept. The player's own game says when the key went down, in the zone's shared clock; the
    /// server reads its own beat at that moment and decides.
    /// </summary>
    public static class CadenceRules
    {
        // A press may claim to be this far ahead of the server's clock: the two clocks are never quite the same.
        private const double AheadAllowed = 0.1;

        /// <summary>True when a moment this far from the nearest beat (seconds, either side) is on it.</summary>
        public static bool OnBeat(double errorSeconds, float windowSeconds) => Math.Abs(errorSeconds) <= windowSeconds;

        /// <summary>True when a press said to have happened at <paramref name="pressedAt"/> can be believed at <paramref name="now"/>.</summary>
        public static bool Fresh(double now, double pressedAt, float maxAgeSeconds) =>
            pressedAt <= now + AheadAllowed && now - pressedAt <= maxAgeSeconds;

        /// <summary>
        /// The server's reading of one press. <paramref name="errorSeconds"/> is how far the server's own beat was
        /// from the moment of the press; it is only looked at when <paramref name="hasBeat"/> is true.
        /// </summary>
        public static CadenceVerdict Judge(CadenceSettings settings, bool inFight, bool hasBeat, double now, double pressedAt, double errorSeconds)
        {
            if (settings == null || !settings.enabled) return CadenceVerdict.Off;
            if (!inFight) return CadenceVerdict.NotInAFight;
            if (!Fresh(now, pressedAt, settings.maxPressAgeSeconds)) return CadenceVerdict.Stale;
            if (!hasBeat) return CadenceVerdict.NoBeat;
            return OnBeat(errorSeconds, settings.WindowSeconds) ? CadenceVerdict.OnCadence : CadenceVerdict.OffBeat;
        }

        /// <summary>True while a press earned at <paramref name="earnedAt"/> may still be spent on its skill.</summary>
        public static bool StillKept(float now, float earnedAt, float keepSeconds) => now - earnedAt <= keepSeconds;
    }
}
