using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace KryticalNova.Diagnostics
{
    /// <summary>
    /// The lines of a performance log, and the housekeeping of its folder. A line is tab-separated and starts with
    /// its kind: <c>S</c> one second of play, <c>F</c> a slow frame, <c>E</c> something the game did, <c>C</c> a
    /// change of settings, scene or session, <c>H</c> how long every frame of a section took, as a count for each
    /// length; <c>#</c> is the header. Numbers are written the same on every PC
    /// (a point for the decimals). No Unity in here, so it is tested without a scene.
    /// </summary>
    public static class PerformanceLogText
    {
        public const string FilePrefix = "perf_", FileExtension = ".txt";
        /// <summary>Markers this close before a slow frame's end are listed on its line.</summary>
        public const double MarkersBeforeSeconds = 2.0;
        /// <summary>At most this many of them, the newest: a line stays readable when a lot happened at once.</summary>
        public const int MarkersOnALine = 12;

        public const string SecondColumns =
            "# S\ttime s\tscene\tframes\tmean ms\tslowest ms\tover 20 ms\tmain thread ms\trender thread ms\tGPU ms\tset-pass\ttriangles" +
            "\tmemory MB\tscript memory MB\tcollections so far\tplayers\tnetworked objects\tcamera x\tcamera z\tlog cost us/frame\tbatches\tdraw calls";
        public const string SlowFrameColumns =
            "# F\ttime s\tframe\tms\tcollection in it (gc / -)\tloading or play\tmain thread ms\tof which waiting for the screen ms\trender thread ms\tGPU ms" +
            "\twhere the time went (phase=ms, largest first; other = between phases)\twhat came before (name ms @seconds before the frame ended)" +
            "\twhich steps of those phases (Phase.Step=ms, largest first) and which cameras (camera:Name=ms, the main thread's share)";
        public const string HistogramColumns =
            "# H\ttime s\tsection number\tsection\tframes of play\tseconds of play\tslowest ms\tover 20 ms\tframes while loading (not counted)" +
            "\tframes by length (from-ms:count; 0.25 ms wide up to 50 ms, 5 ms wide after; a section's last H line is the whole of it)";
        /// <summary>Steps named on a slow frame's line, at most, and the least a step must have taken to be named.</summary>
        public const int StepsOnALine = 6;
        public const float StepWorthNamingMs = 0.3f;

        // Frame lengths are counted in bins: fine where the game should be, coarse where it should never be.
        public const double FineBinMs = 0.25, FineUpToMs = 50.0, CoarseBinMs = 5.0, CoarseUpToMs = 1000.0;
        public const int FineBins = 200, CoarseBins = 190;
        public const int Bins = FineBins + CoarseBins + 1; // and one for anything longer

        /// <summary>The bin a frame of this length is counted in.</summary>
        public static int BinOf(double ms)
        {
            if (ms < 0) return 0;
            if (ms < FineUpToMs) return (int)(ms / FineBinMs);
            if (ms < CoarseUpToMs) return FineBins + (int)((ms - FineUpToMs) / CoarseBinMs);
            return Bins - 1;
        }

        /// <summary>The shortest frame a bin counts.</summary>
        public static double BinFromMs(int bin) =>
            bin < FineBins ? bin * FineBinMs : bin < Bins - 1 ? FineUpToMs + (bin - FineBins) * CoarseBinMs : CoarseUpToMs;

        /// <summary>How long every frame of a section took: its count, its sums and the bins that hold any.</summary>
        public static void AppendHistogram(StringBuilder text, double time, int serial, string section, long frames, double seconds, double slowestMs,
            long overTwentyMs, long loadingFrames, int[] bins)
        {
            text.Append("H\t").Append(Number(time, "0.0")).Append('\t').Append(serial).Append('\t').Append(Clean(section))
                .Append('\t').Append(frames).Append('\t').Append(Number(seconds, "0.0")).Append('\t').Append(Number(slowestMs, "0.0"))
                .Append('\t').Append(overTwentyMs).Append('\t').Append(loadingFrames).Append('\t');
            bool any = false;
            for (int i = 0; bins != null && i < bins.Length; i++)
            {
                if (bins[i] <= 0) continue;
                if (any) text.Append(' ');
                text.Append(Number(BinFromMs(i), "0.##")).Append(':').Append(bins[i]);
                any = true;
            }
            if (!any) text.Append('-');
            text.Append('\n');
        }
        public const string MarkerColumns = "# E\ttime s (a timed one: when it ended)\tname\tms (- = it only happened)\tdetail\tnumber";
        public const string ChangeColumns = "# C\ttime s\twhat changed\tdetail";

        /// <summary>One second of play.</summary>
        public struct Second
        {
            public double Time;
            public string Scene;
            public int Frames, OverTwentyMs;
            public double MeanMs, SlowestMs, MainMs, RenderMs, GpuMs;   // a negative thread or GPU time: not reported
            public long SetPass, Triangles, MemoryMb, ScriptMemoryMb, Batches, DrawCalls;
            public int Collections, Players, NetworkedObjects;
            public float CameraX, CameraZ;
            public double LogMicrosecondsAFrame;
        }

        /// <summary>A frame that took too long.</summary>
        public struct SlowFrame
        {
            public double Time;
            public long Frame;
            public double Ms;
            public bool Collection, Loading;
            /// <summary>True when the split below was matched to this frame with certainty; false marks it "~".</summary>
            public bool SplitCertain;
            public double MainMs, PresentWaitMs, RenderMs, GpuMs;      // negative: not known
            /// <summary>Stopwatch ticks at which the frame ended, to find the markers before it.</summary>
            public long EndedAt;
        }

        public static void Append(StringBuilder text, in Second s)
        {
            text.Append("S\t").Append(Number(s.Time, "0.0")).Append('\t').Append(Clean(s.Scene)).Append('\t').Append(s.Frames)
                .Append('\t').Append(Number(s.MeanMs, "0.00")).Append('\t').Append(Number(s.SlowestMs, "0.0")).Append('\t').Append(s.OverTwentyMs)
                .Append('\t').Append(Milliseconds(s.MainMs)).Append('\t').Append(Milliseconds(s.RenderMs)).Append('\t').Append(Milliseconds(s.GpuMs))
                .Append('\t').Append(s.SetPass).Append('\t').Append(s.Triangles).Append('\t').Append(s.MemoryMb).Append('\t').Append(s.ScriptMemoryMb)
                .Append('\t').Append(s.Collections).Append('\t').Append(s.Players).Append('\t').Append(s.NetworkedObjects)
                .Append('\t').Append(Number(s.CameraX, "0")).Append('\t').Append(Number(s.CameraZ, "0"))
                .Append('\t').Append(Number(s.LogMicrosecondsAFrame, "0.0"))
                .Append('\t').Append(s.Batches).Append('\t').Append(s.DrawCalls).Append('\n');
        }

        /// <summary>
        /// A slow frame, with where its time went and what the game did in the two seconds before it ended.
        /// <paramref name="phaseMs"/> runs beside <paramref name="phaseNames"/>, <paramref name="stepMs"/> beside
        /// <paramref name="stepNames"/>; <paramref name="cameras"/> is written as it is given.
        /// </summary>
        public static void Append(StringBuilder text, in SlowFrame f, string[] phaseNames, float[] phaseMs, MarkerRing markers, long ticksPerSecond,
            string[] stepNames = null, float[] stepMs = null, string cameras = null)
        {
            text.Append("F\t").Append(Number(f.Time, "0.000")).Append('\t').Append(f.Frame).Append('\t').Append(Number(f.Ms, "0.0"))
                .Append('\t').Append(f.Collection ? "gc" : "-").Append('\t').Append(f.Loading ? "loading" : "play").Append('\t');
            string unsure = f.SplitCertain ? string.Empty : "~";
            text.Append(unsure).Append(Milliseconds(f.MainMs)).Append('\t').Append(unsure).Append(Milliseconds(f.PresentWaitMs))
                .Append('\t').Append(unsure).Append(Milliseconds(f.RenderMs)).Append('\t').Append(unsure).Append(Milliseconds(f.GpuMs)).Append('\t');

            // Where the time went: the phases, largest first, and what fell between them.
            int count = phaseNames != null && phaseMs != null ? Math.Min(phaseNames.Length, phaseMs.Length) : 0;
            double inPhases = 0;
            for (int i = 0; i < count; i++) inPhases += phaseMs[i];
            Span<int> order = count <= 32 ? stackalloc int[count] : new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            for (int i = 1; i < count; i++)
                for (int j = i; j > 0 && phaseMs[order[j]] > phaseMs[order[j - 1]]; j--) (order[j], order[j - 1]) = (order[j - 1], order[j]);
            bool any = false;
            for (int i = 0; i < count; i++)
            {
                float ms = phaseMs[order[i]];
                if (ms < 0.05f) continue;
                if (any) text.Append(' ');
                text.Append(phaseNames[order[i]]).Append('=').Append(Number(ms, "0.0"));
                any = true;
            }
            double other = f.Ms - inPhases;
            if (other >= 0.05)
            {
                if (any) text.Append(' ');
                text.Append("other=").Append(Number(other, "0.0"));
                any = true;
            }
            if (!any) text.Append('-');
            text.Append('\t');

            // What came before: the markers made in the two seconds up to the frame's end, oldest first.
            any = false;
            if (markers != null && ticksPerSecond > 0)
            {
                long from = f.EndedAt - (long)(MarkersBeforeSeconds * ticksPerSecond);
                long made = markers.Made;
                int inWindow = 0;
                for (long i = markers.Oldest; i < made; i++)
                    if (markers.TryGet(i, out MarkerRing.Marker counted) && counted.Timestamp >= from && counted.Timestamp <= f.EndedAt) inWindow++;
                int skip = Math.Max(0, inWindow - MarkersOnALine);
                if (skip > 0)
                {
                    text.Append('(').Append(skip).Append(" earlier)");
                    any = true;
                }
                for (long i = markers.Oldest; i < made; i++)
                {
                    if (!markers.TryGet(i, out MarkerRing.Marker marker) || marker.Timestamp < from || marker.Timestamp > f.EndedAt) continue;
                    if (skip > 0) { skip--; continue; }
                    if (any) text.Append(", ");
                    text.Append(Clean(marker.Name));
                    if (!string.IsNullOrEmpty(marker.Detail)) text.Append(':').Append(Clean(marker.Detail));
                    if (marker.Milliseconds >= 0f) text.Append(' ').Append(Number(marker.Milliseconds, "0.0")).Append("ms");
                    text.Append(" @").Append(Number((f.EndedAt - marker.Timestamp) / (double)ticksPerSecond, "0.00"));
                    any = true;
                }
            }
            if (!any) text.Append('-');
            text.Append('\t');

            // Which steps: the few that took longest, then the cameras.
            any = false;
            int steps = stepNames != null && stepMs != null ? Math.Min(stepNames.Length, stepMs.Length) : 0;
            float below = float.MaxValue;
            int last = -1;
            for (int named = 0; named < StepsOnALine; named++)
            {
                // The longest not named yet: under the one named before it, or equal to it and further down the list.
                int longest = -1;
                for (int i = 0; i < steps; i++)
                {
                    float ms = stepMs[i];
                    if (ms < StepWorthNamingMs || ms > below || (ms == below && i <= last)) continue;
                    if (longest < 0 || ms > stepMs[longest]) longest = i;
                }
                if (longest < 0) break;
                below = stepMs[longest];
                last = longest;
                if (any) text.Append(' ');
                text.Append(stepNames[longest]).Append('=').Append(Number(below, "0.0"));
                any = true;
            }
            if (!string.IsNullOrEmpty(cameras))
            {
                if (any) text.Append(' ');
                text.Append(Clean(cameras));
                any = true;
            }
            if (!any) text.Append('-');
            text.Append('\n');
        }

        /// <summary>Something the game did (<c>E</c>), or a change of settings, scene or session (<c>C</c>).</summary>
        public static void Append(StringBuilder text, double time, in MarkerRing.Marker marker)
        {
            if (marker.IsChange)
            {
                text.Append("C\t").Append(Number(time, "0.000")).Append('\t').Append(Clean(marker.Name)).Append('\t').Append(Clean(marker.Detail)).Append('\n');
                return;
            }
            text.Append("E\t").Append(Number(time, "0.000")).Append('\t').Append(Clean(marker.Name)).Append('\t')
                .Append(marker.Milliseconds >= 0f ? Number(marker.Milliseconds, "0.00") : "-").Append('\t').Append(Clean(marker.Detail)).Append('\t');
            if (marker.Number != PerformanceLog.NoNumber) text.Append(marker.Number.ToString(CultureInfo.InvariantCulture));
            else text.Append('-');
            text.Append('\n');
        }

        public static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

        private static string Milliseconds(double value) => value >= 0 ? Number(value, "0.0") : "-";

        /// <summary>A value that cannot break the line it is written into.</summary>
        public static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            if (value.IndexOf('\t') < 0 && value.IndexOf('\n') < 0 && value.IndexOf('\r') < 0) return value;
            return value.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        }

        /// <summary>The name of a log started at <paramref name="started"/>.</summary>
        public static string FileName(DateTime started) =>
            FilePrefix + started.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + FileExtension;

        /// <summary>
        /// Deletes the oldest logs of a folder until <paramref name="keep"/> are left. Returns how many it deleted.
        /// Only files named like a log are counted or touched.
        /// </summary>
        public static int Prune(string folder, int keep)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return 0;
            string[] files = Directory.GetFiles(folder, FilePrefix + "*" + FileExtension);
            if (files.Length <= keep) return 0;
            // The name carries the start time, so sorting by name is sorting by age.
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            int deleted = 0;
            for (int i = 0; i < files.Length - Math.Max(0, keep); i++)
            {
                try
                {
                    File.Delete(files[i]);
                    deleted++;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return deleted;
        }
    }
}
