using System;
using System.Diagnostics;

namespace KryticalNova.Diagnostics
{
    /// <summary>
    /// The game's side of the performance log: a line saying what the game was doing, written where a slow frame
    /// can plausibly come from (a save, a redraw of the HUD, a spawn, a window being built), so that a slow frame
    /// in the log has its cause beside it. <see cref="PerformanceLogRecorder"/> writes the file; this only keeps
    /// the last markers in a ring. While nothing records, a call is one test and returns: nothing is stored,
    /// nothing is allocated. Pass names and details that already exist as strings (an id, a key); never build a
    /// string for a marker without asking <see cref="IsRecording"/> first.
    /// </summary>
    public static class PerformanceLog
    {
        /// <summary>The Editor's opt-in (EditorPrefs): a built game always records, Play in the Editor only when this is set.</summary>
        public const string EditorPrefKey = "kn.performanceLog.recordInPlay";
        /// <summary>No number to go with a marker.</summary>
        public const long NoNumber = long.MinValue;

        /// <summary>The last markers made, for whoever writes them out.</summary>
        public static MarkerRing Markers { get; } = new MarkerRing(256);

        /// <summary>True while a file is being written. Ask before building a detail string.</summary>
        public static bool IsRecording { get; internal set; }
        /// <summary>The file being written, or null.</summary>
        public static string FilePath { get; internal set; }

        /// <summary>This happened now.</summary>
        public static void Mark(string name, string detail = null, long number = NoNumber)
        {
            if (!IsRecording) return;
            Markers.Add(name, detail, number, -1f, false);
        }

        /// <summary>
        /// Settings or the session changed (a preset, the upscaler, hosting): the numbers after this line are not
        /// to be compared with the ones before it.
        /// </summary>
        public static void Change(string what, string detail)
        {
            if (!IsRecording) return;
            Markers.Add(what, detail, NoNumber, -1f, true);
        }

        /// <summary>
        /// Times a piece of work: <c>using var timed = PerformanceLog.Time("save");</c> writes its marker when the
        /// block ends. Kept as a plain local instead, it writes one only if <see cref="Scope.End"/> is called.
        /// </summary>
        public static Scope Time(string name, string detail = null, long number = NoNumber) =>
            IsRecording ? new Scope(name, detail, number, Stopwatch.GetTimestamp()) : default;

        /// <summary>A piece of work being timed.</summary>
        public readonly struct Scope : IDisposable
        {
            private readonly string name, detail;
            private readonly long number, started;

            internal Scope(string name, string detail, long number, long started)
            {
                this.name = name;
                this.detail = detail;
                this.number = number;
                this.started = started;
            }

            public void Dispose() => End(number);

            /// <summary>The work is done: writes the marker with how long it took and a number to go with it (things made, bytes written).</summary>
            public void End(long withNumber)
            {
                if (started == 0 || !IsRecording) return;
                long now = Stopwatch.GetTimestamp();
                Markers.Add(name, detail, withNumber, (float)((now - started) * 1000.0 / Stopwatch.Frequency), false, now);
            }
        }

        internal static void Reset()
        {
            Markers.Clear();
            IsRecording = false;
            FilePath = null;
        }
    }
}
