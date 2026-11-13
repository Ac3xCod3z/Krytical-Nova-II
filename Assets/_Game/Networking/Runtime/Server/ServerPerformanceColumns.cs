using System;
using System.Collections.Generic;
using System.Globalization;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The columns of the server's performance CSV (<c>ServerPerformanceLog</c>, server builds only), kept here so the order is tested in
    /// the Editor and read by <c>Tools/Editor/Build/ReadPerformanceLog.py --server</c>. Wave 4 (12 Nov 2026) appended tick p50 / p99,
    /// brains, relevancy, process RAM, entities, ghosts and bytes out at the END, so older readers keep their columns. A gauge nobody
    /// published (relevancy before it lands, bytes out while the transport does not say) is written blank.
    /// </summary>
    public static class ServerPerformanceColumns
    {
        public static readonly string[] Names =
        {
            "time", "players", "fps", "busy_avg_ms", "busy_p95_ms", "busy_worst_ms", "slow_frames", "frames", "managed_mb", "reserved_mb",
            "late_hits", "gone_targets", "worst_ping_ms",
            // wave 4 soak
            "tick_p50_ms", "tick_p99_ms", "brains_ms", "relevancy_ms", "private_mb", "entities", "ghosts", "bytes_out_per_s",
        };

        public static string Header => string.Join(",", Names);

        /// <summary>The value at <paramref name="share"/> (0.5, 0.99) of an ascending list; 0 when empty.</summary>
        public static float Percentile(List<float> sorted, float share)
        {
            if (sorted == null || sorted.Count == 0) return 0f;
            int at = (int)(sorted.Count * share);
            return sorted[Math.Max(0, Math.Min(sorted.Count - 1, at))];
        }

        /// <summary>A gauge for the CSV: blank when nobody published it.</summary>
        public static string Cell(bool known, double value, string format) =>
            known ? value.ToString(format, CultureInfo.InvariantCulture) : string.Empty;
    }
}
