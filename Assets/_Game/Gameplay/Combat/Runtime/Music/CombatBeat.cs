using UnityEngine;

namespace KryticalNova.Audio
{
    /// <summary>
    /// The beat of the fight as this machine's player hears it, for everything that keeps time to it: Cadence, the
    /// pet, the beat marker, lights and props. It is live only while the local player is in a fight (design 17: a
    /// music mechanic shows only in the part of the game it belongs to); out of one <see cref="Live01"/> is 0 and
    /// nothing should move. Written once a frame by <see cref="CombatMusicPlayer"/>, before anything reads it. In a
    /// scene with no fight music it simply stays at rest.
    /// </summary>
    public static class CombatBeat
    {
        /// <summary>0 out of a fight, 1 in one, fading between. Scale every beat effect by it.</summary>
        public static float Live01 { get; private set; }

        /// <summary>True while a track with a beat is sounding. False between two tracks, and out of a fight.</summary>
        public static bool HasBeat { get; private set; }

        /// <summary>What is playing, or null.</summary>
        public static MusicTrack Track { get; private set; }

        /// <summary>Beats since the track's first, with the fraction: 12.5 is half way from beat 12 to beat 13.</summary>
        public static double Beats { get; private set; }

        /// <summary>The beat that last sounded.</summary>
        public static long Index { get; private set; }

        /// <summary>How far through the current beat: 0 on it, towards 1 before the next.</summary>
        public static float Phase01 { get; private set; }

        /// <summary>True when the beat that last sounded was the first of a bar.</summary>
        public static bool OnBar { get; private set; }

        /// <summary>Seconds from one beat to the next of what is playing.</summary>
        public static double BeatSeconds { get; private set; }

        /// <summary>
        /// Seconds the beat this player keeps time to runs behind the zone's shared clock: how far the sound sits from
        /// that clock, plus the player's own offset. A moment the player judged against the beat is this much earlier
        /// on the shared clock, which is how it is told to the server.
        /// </summary>
        public static double ClockLagSeconds { get; private set; }

        /// <summary>
        /// How much later the player hears a sound than the game plays it, in seconds (Settings: Beat timing offset).
        /// The beat everything keeps time to is moved by it, so that it falls where the player hears it.
        /// </summary>
        public static double OutputDelaySeconds { get; set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand. The offset is the player's
        // setting and is kept.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Rest(0f);

        /// <summary>1 on the beat, falling away through it, already scaled by <see cref="Live01"/>. 0 with no beat.</summary>
        public static float Pulse(float sharpness = 6f) => HasBeat ? BeatMath.Pulse(Phase01, sharpness) * Live01 : 0f;

        /// <summary>
        /// Seconds between this moment and the nearest beat, as the player hears it: negative before the beat.
        /// False when no beat is sounding for this player.
        /// </summary>
        public static bool TryError(out double seconds)
        {
            seconds = 0.0;
            if (!HasBeat || Live01 <= 0f) return false;
            double offBeat = Beats - System.Math.Round(Beats, System.MidpointRounding.AwayFromZero);
            seconds = offBeat * BeatSeconds;
            return true;
        }

        internal static void Set(float live, MusicTrack track, double heardPosition, double firstBeat, double clockPosition = 0.0)
        {
            Live01 = Mathf.Clamp01(live);
            Track = track;
            HasBeat = track != null;
            ClockLagSeconds = HasBeat ? clockPosition - (heardPosition - OutputDelaySeconds) : 0.0;
            if (!HasBeat)
            {
                Beats = 0.0;
                Index = 0;
                Phase01 = 0f;
                OnBar = false;
                BeatSeconds = 0.0;
                return;
            }
            BeatSeconds = track.BeatSeconds;
            double position = heardPosition - OutputDelaySeconds;
            Beats = BeatMath.Beats(position, firstBeat, BeatSeconds);
            Index = BeatMath.BeatIndex(position, firstBeat, BeatSeconds);
            Phase01 = (float)BeatMath.Phase01(position, firstBeat, BeatSeconds);
            OnBar = BeatMath.IsBarBeat(Index, track.barBeat, track.beatsPerBar);
        }

        internal static void Rest(float live) => Set(live, null, 0.0, 0.0);
    }
}
