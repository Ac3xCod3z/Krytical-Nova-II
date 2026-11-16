using System;

namespace KryticalNova.Audio
{
    /// <summary>
    /// The arithmetic of keeping time: where a moment falls against a track's beats, and which track of a round is
    /// playing at a moment. No Unity, no state: the server, every client and the tests ask the same questions and
    /// get the same answers.
    /// </summary>
    public static class BeatMath
    {
        /// <summary>Beats since the track's first beat at <paramref name="position"/> seconds into it; negative before it.</summary>
        public static double Beats(double position, double firstBeat, double beatSeconds) =>
            beatSeconds > 0.0 ? (position - firstBeat) / beatSeconds : 0.0;

        /// <summary>The beat that has last sounded: 0 for the first, -1 before it.</summary>
        public static long BeatIndex(double position, double firstBeat, double beatSeconds) =>
            (long)Math.Floor(Beats(position, firstBeat, beatSeconds));

        /// <summary>How far through the current beat, 0 on the beat up to 1 just before the next.</summary>
        public static double Phase01(double position, double firstBeat, double beatSeconds)
        {
            double beats = Beats(position, firstBeat, beatSeconds);
            return beats - Math.Floor(beats);
        }

        /// <summary>
        /// Seconds from the nearest beat: negative when <paramref name="position"/> is before it (early), positive
        /// after (late). Never further than half a beat.
        /// </summary>
        public static double ErrorSeconds(double position, double firstBeat, double beatSeconds)
        {
            if (beatSeconds <= 0.0) return 0.0;
            double beats = Beats(position, firstBeat, beatSeconds);
            return (beats - Math.Round(beats, MidpointRounding.AwayFromZero)) * beatSeconds;
        }

        /// <summary>True when beat <paramref name="index"/> is the first of a bar.</summary>
        public static bool IsBarBeat(long index, int barBeat, int beatsPerBar)
        {
            if (beatsPerBar <= 1) return true;
            long place = (index - barBeat) % beatsPerBar;
            return place == 0;
        }

        /// <summary>
        /// A pulse for things that keep time: 1 on the beat, falling away through it. <paramref name="sharpness"/>
        /// is how fast: at 6 it is nearly gone by the half beat.
        /// </summary>
        public static float Pulse(double phase01, float sharpness) => (float)Math.Exp(-Math.Max(0.0, phase01) * sharpness);

        /// <summary>
        /// Which track of a round is playing <paramref name="elapsed"/> seconds after the round first began, and how
        /// far into it. The tracks play in order with <paramref name="gap"/> seconds of silence after each, round
        /// and round. False in a gap, or when no track has a length.
        /// </summary>
        public static bool Locate(double elapsed, ReadOnlySpan<double> lengths, double gap, out int track, out double position)
        {
            track = -1;
            position = 0.0;
            double round = 0.0;
            for (int i = 0; i < lengths.Length; i++)
                if (lengths[i] > 0.0) round += lengths[i] + Math.Max(0.0, gap);
            if (round <= 0.0) return false;

            double into = elapsed % round;
            if (into < 0.0) into += round;
            for (int i = 0; i < lengths.Length; i++)
            {
                if (lengths[i] <= 0.0) continue;
                if (into < lengths[i])
                {
                    track = i;
                    position = into;
                    return true;
                }
                into -= lengths[i];
                if (into < gap) return false;
                into -= Math.Max(0.0, gap);
            }
            return false;
        }
    }
}
