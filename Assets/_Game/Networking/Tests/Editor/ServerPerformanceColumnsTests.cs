using System.Collections.Generic;
using KryticalNova.Diagnostics;
using NUnit.Framework;

namespace KryticalNova.Networking.Tests
{
    /// <summary>Wave 4 soak: the server CSV keeps its old columns first (older readers) and the soak columns after them, in this order.</summary>
    public class ServerPerformanceColumnsTests
    {
        [Test]
        public void TheOldColumnsStayFirst_TheSoakColumnsFollow()
        {
            Assert.AreEqual(
                "time,players,fps,busy_avg_ms,busy_p95_ms,busy_worst_ms,slow_frames,frames,managed_mb,reserved_mb,late_hits,gone_targets,worst_ping_ms," +
                "tick_p50_ms,tick_p99_ms,brains_ms,relevancy_ms,private_mb,entities,ghosts,bytes_out_per_s",
                ServerPerformanceColumns.Header);
        }

        [Test]
        public void TheGaugeNames_AreTheColumnNames()
        {
            CollectionAssert.Contains(ServerPerformanceColumns.Names, PerformanceLog.GaugeNames.BrainsMs);
            CollectionAssert.Contains(ServerPerformanceColumns.Names, PerformanceLog.GaugeNames.RelevancyMs);
            CollectionAssert.Contains(ServerPerformanceColumns.Names, PerformanceLog.GaugeNames.Entities);
            CollectionAssert.Contains(ServerPerformanceColumns.Names, PerformanceLog.GaugeNames.Ghosts);
            CollectionAssert.Contains(ServerPerformanceColumns.Names, PerformanceLog.GaugeNames.Players);
        }

        [Test]
        public void Percentiles_AndBlankCells()
        {
            var sorted = new List<float>();
            for (int i = 1; i <= 100; i++) sorted.Add(i);
            Assert.AreEqual(51f, ServerPerformanceColumns.Percentile(sorted, 0.5f));
            Assert.AreEqual(100f, ServerPerformanceColumns.Percentile(sorted, 0.99f));
            Assert.AreEqual(0f, ServerPerformanceColumns.Percentile(new List<float>(), 0.99f));
            Assert.AreEqual(string.Empty, ServerPerformanceColumns.Cell(false, 3, "0"));
            Assert.AreEqual("0.250", ServerPerformanceColumns.Cell(true, 0.25, "0.000"));
        }

        [Test]
        public void Gauges_AreKeptOnlyWhileOn()
        {
            bool was = PerformanceLog.GaugesOn;
            try
            {
                PerformanceLog.GaugesOn = false;
                PerformanceLog.SetGauge("wave4_test_gauge", 1);
                Assert.IsFalse(PerformanceLog.TryGauge("wave4_test_gauge", out _));
                PerformanceLog.GaugesOn = true;
                PerformanceLog.SetGauge("wave4_test_gauge", 2);
                Assert.IsTrue(PerformanceLog.TryGauge("wave4_test_gauge", out double v));
                Assert.AreEqual(2, v);
            }
            finally { PerformanceLog.GaugesOn = was; }
        }
    }
}
