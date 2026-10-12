using System;
using System.IO;
using System.Text;
using KryticalNova.Diagnostics;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The performance log's own parts, without a game running: the ring that keeps the last markers, the lines a
    /// log is made of, and the folder that keeps only the newest files.
    /// </summary>
    public sealed class PerformanceLogTests
    {
        private const long TicksPerSecond = 1000;

        [Test]
        public void TheRingKeepsTheNewestMarkersInOrder()
        {
            var ring = new MarkerRing(4);
            for (int i = 0; i < 10; i++) ring.Add("m" + i, null, PerformanceLog.NoNumber, -1f, false, i);

            Assert.AreEqual(10, ring.Made);
            Assert.AreEqual(6, ring.Oldest, "ten made, four kept: the oldest held is number 6");
            Assert.IsFalse(ring.TryGet(5, out _), "a marker written over is gone");
            Assert.IsFalse(ring.TryGet(10, out _), "a marker not made yet is not there");
            for (long i = 6; i < 10; i++)
            {
                Assert.IsTrue(ring.TryGet(i, out MarkerRing.Marker marker));
                Assert.AreEqual("m" + i, marker.Name);
                Assert.AreEqual(i, marker.Timestamp);
            }
        }

        [Test]
        public void ClearingTheRingStartsTheNumbersAgain()
        {
            var ring = new MarkerRing(4);
            ring.Add("a", null, PerformanceLog.NoNumber, -1f, false, 1);
            ring.Clear();
            Assert.AreEqual(0, ring.Made);
            Assert.IsFalse(ring.TryGet(0, out _));
        }

        [Test]
        public void NothingIsKeptWhileNothingRecords()
        {
            // No recorder runs in an EditMode test: the game's calls must cost nothing and leave nothing behind.
            Assume.That(PerformanceLog.IsRecording, Is.False);
            long before = PerformanceLog.Markers.Made;
            PerformanceLog.Mark("test.mark", "detail", 3);
            PerformanceLog.Change("test.change", "detail");
            using (PerformanceLog.Time("test.time")) { }
            PerformanceLog.Time("test.time").End(5);
            Assert.AreEqual(before, PerformanceLog.Markers.Made);
        }

        [Test]
        public void ASlowFrameCarriesItsPhasesAndWhatCameBefore()
        {
            var ring = new MarkerRing(8);
            ring.Add("old", null, PerformanceLog.NoNumber, -1f, false, 1000);               // 4 s before: too old
            ring.Add("save.player", null, PerformanceLog.NoNumber, 14.5f, false, 4700);     // 0.3 s before
            ring.Add("skill.cast", "Stroke", PerformanceLog.NoNumber, -1f, false, 4950);    // 0.05 s before
            ring.Add("later", null, PerformanceLog.NoNumber, -1f, false, 5100);             // after the frame ended
            var frame = new PerformanceLogText.SlowFrame
            {
                Time = 12.3456, Frame = 777, Ms = 26.7, Collection = true, Loading = false, SplitCertain = true,
                MainMs = 24.1, PresentWaitMs = 0.4, RenderMs = 2.2, GpuMs = 3.6, EndedAt = 5000,
            };
            var text = new StringBuilder();
            PerformanceLogText.Append(text, frame, new[] { "Update", "PreLateUpdate", "PostLateUpdate", "Idle" }, new[] { 2f, 18.5f, 4f, 0f }, ring, TicksPerSecond);

            string[] cells = text.ToString().TrimEnd('\n').Split('\t');
            Assert.AreEqual(PerformanceLogText.SlowFrameColumns.Split('\t').Length, cells.Length, text.ToString());
            Assert.AreEqual("-", cells[12], "no steps were given");
            Assert.AreEqual("F", cells[0]);
            Assert.AreEqual("12.346", cells[1]);
            Assert.AreEqual("777", cells[2]);
            Assert.AreEqual("26.7", cells[3]);
            Assert.AreEqual("gc", cells[4]);
            Assert.AreEqual("play", cells[5]);
            Assert.AreEqual("24.1", cells[6]);
            Assert.AreEqual("3.6", cells[9]);
            Assert.AreEqual("PreLateUpdate=18.5 PostLateUpdate=4.0 Update=2.0 other=2.2", cells[10], "largest first, a phase that took nothing left out, the rest as 'other'");
            Assert.AreEqual("save.player 14.5ms @0.30, skill.cast:Stroke @0.05", cells[11], "only the two seconds before the frame ended, oldest first");
        }

        [Test]
        public void ASlowFrameNamesTheStepsThatTookLongestAndTheCameras()
        {
            var frame = new PerformanceLogText.SlowFrame { Ms = 40, SplitCertain = true, MainMs = -1, PresentWaitMs = -1, RenderMs = -1, GpuMs = -1, EndedAt = 5000 };
            string[] names = { "Update.Scripts", "PreLateUpdate.Animation", "PreLateUpdate.Scripts", "PostLateUpdate.Layout", "PostLateUpdate.Draw", "A", "B", "C", "D" };
            float[] ms = { 2f, 0.1f, 7.5f, 2f, 21f, 0.4f, 0.5f, 0.6f, 0.29f };
            var text = new StringBuilder();
            PerformanceLogText.Append(text, frame, null, null, null, TicksPerSecond, names, ms, "camera:Main_Camera=18.2 camera:Portrait=1.1");
            string steps = text.ToString().TrimEnd('\n').Split('\t')[12];
            Assert.AreEqual("PostLateUpdate.Draw=21.0 PreLateUpdate.Scripts=7.5 Update.Scripts=2.0 PostLateUpdate.Layout=2.0 C=0.6 B=0.5 camera:Main_Camera=18.2 camera:Portrait=1.1",
                steps, "the six longest, longest first (two of a length in their own order), none under " + PerformanceLogText.StepWorthNamingMs + " ms, then the cameras");
        }

        [Test]
        public void EveryFrameIsCountedInABinOfItsLength()
        {
            Assert.AreEqual(0, PerformanceLogText.BinOf(0.1));
            Assert.AreEqual(16, PerformanceLogText.BinOf(4.0));
            Assert.AreEqual(4.0, PerformanceLogText.BinFromMs(PerformanceLogText.BinOf(4.2)), 1e-9, "a quarter of a millisecond wide up to 50 ms");
            Assert.AreEqual(50.0, PerformanceLogText.BinFromMs(PerformanceLogText.BinOf(54.9)), 1e-9, "5 ms wide after");
            Assert.AreEqual(PerformanceLogText.Bins - 1, PerformanceLogText.BinOf(5000), "anything longer than a second in the last");
            for (int bin = 0; bin < PerformanceLogText.Bins; bin++)
                Assert.AreEqual(bin, PerformanceLogText.BinOf(PerformanceLogText.BinFromMs(bin) + 0.01), "bin " + bin + " holds its own start");

            var bins = new int[PerformanceLogText.Bins];
            bins[PerformanceLogText.BinOf(4.0)] = 900;
            bins[PerformanceLogText.BinOf(4.3)] = 90;
            bins[PerformanceLogText.BinOf(61.0)] = 1;
            var text = new StringBuilder();
            PerformanceLogText.AppendHistogram(text, 75.25, 3, "bench.fight", 991, 4.1, 61.0, 1, 12, bins);
            string[] cells = text.ToString().TrimEnd('\n').Split('\t');
            Assert.AreEqual(PerformanceLogText.HistogramColumns.Split('\t').Length, cells.Length, text.ToString());
            Assert.AreEqual("H\t75.3\t3\tbench.fight\t991\t4.1\t61.0\t1\t12\t4:900 4.25:90 60:1", text.ToString().TrimEnd('\n'));
        }

        [Test]
        public void ALineListsOnlyTheNewestMarkersWhenALotHappened()
        {
            var ring = new MarkerRing(64);
            for (int i = 0; i < 20; i++) ring.Add("m" + i, null, PerformanceLog.NoNumber, -1f, false, 4000 + i);
            var frame = new PerformanceLogText.SlowFrame { Ms = 25, SplitCertain = true, MainMs = -1, PresentWaitMs = -1, RenderMs = -1, GpuMs = -1, EndedAt = 5000 };
            var text = new StringBuilder();
            PerformanceLogText.Append(text, frame, null, null, ring, TicksPerSecond);
            string before = text.ToString().TrimEnd('\n').Split('\t')[11];
            Assert.IsTrue(before.StartsWith("(8 earlier), m8 @"), before);
            Assert.IsTrue(before.Contains("m19 @"), before);
            Assert.IsFalse(before.Contains("m7 @"), before);
        }

        [Test]
        public void AnUnsureOrUnknownSplitIsSaidSo()
        {
            var unsure = new PerformanceLogText.SlowFrame { Ms = 30, SplitCertain = false, MainMs = 28, PresentWaitMs = 0, RenderMs = 1, GpuMs = 4 };
            var text = new StringBuilder();
            PerformanceLogText.Append(text, unsure, null, null, null, TicksPerSecond);
            string[] cells = text.ToString().TrimEnd('\n').Split('\t');
            Assert.AreEqual("~28.0", cells[6]);
            Assert.AreEqual("other=30.0", cells[10]);
            Assert.AreEqual("-", cells[11]);

            var unknown = new PerformanceLogText.SlowFrame { Ms = 30, SplitCertain = true, MainMs = -1, PresentWaitMs = -1, RenderMs = -1, GpuMs = -1 };
            text.Clear();
            PerformanceLogText.Append(text, unknown, null, null, null, TicksPerSecond);
            Assert.AreEqual("-", text.ToString().Split('\t')[6]);
        }

        [Test]
        public void ASecondIsOneLineOfTwentyCellsWhateverThePcsLanguage()
        {
            var culture = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                // A PC that writes decimals with a comma must write the same file.
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
                var second = new PerformanceLogText.Second
                {
                    Time = 61.04, Scene = "Zone01\tStarterTown", Frames = 174, OverTwentyMs = 1, MeanMs = 5.74, SlowestMs = 26.7,
                    MainMs = 5.4, RenderMs = 1.8, GpuMs = -1, SetPass = 288, Triangles = 550408, MemoryMb = 1884, ScriptMemoryMb = 123,
                    Collections = 12, Players = 1, NetworkedObjects = 4, CameraX = -12.4f, CameraZ = 8.6f, LogMicrosecondsAFrame = 3.21,
                };
                var text = new StringBuilder();
                PerformanceLogText.Append(text, second);
                string line = text.ToString();
                Assert.IsTrue(line.EndsWith("\n"));
                string[] cells = line.TrimEnd('\n').Split('\t');
                Assert.AreEqual(PerformanceLogText.SecondColumns.Split('\t').Length, cells.Length, "as many cells as the header names");
                Assert.AreEqual("S", cells[0]);
                Assert.AreEqual("61.0", cells[1]);
                Assert.AreEqual("Zone01 StarterTown", cells[2], "a tab in a value cannot break the line");
                Assert.AreEqual("5.74", cells[4]);
                Assert.AreEqual("-", cells[9], "a GPU time that was not reported");
                Assert.AreEqual("3.2", cells[19]);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = culture;
            }
        }

        [Test]
        public void MarkersAndChangesAreDifferentLines()
        {
            var text = new StringBuilder();
            PerformanceLogText.Append(text, 3.5, new MarkerRing.Marker { Name = "save.file", Detail = "accounts/3f2a", Number = 20480, Milliseconds = 6.25f });
            PerformanceLogText.Append(text, 4.0, new MarkerRing.Marker { Name = "drop.appeared", Number = PerformanceLog.NoNumber, Milliseconds = -1f });
            PerformanceLogText.Append(text, 5.0, new MarkerRing.Marker { Name = "graphics", Detail = "preset=Ultra", IsChange = true });
            string[] lines = text.ToString().TrimEnd('\n').Split('\n');
            Assert.AreEqual("E\t3.500\tsave.file\t6.25\taccounts/3f2a\t20480", lines[0]);
            Assert.AreEqual("E\t4.000\tdrop.appeared\t-\t-\t-", lines[1]);
            Assert.AreEqual("C\t5.000\tgraphics\tpreset=Ultra", lines[2]);
        }

        [Test]
        public void TheFolderKeepsOnlyTheNewestLogs()
        {
            string folder = Path.Combine(Path.GetTempPath(), "kn_perflog_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var start = new DateTime(2026, 10, 11, 9, 0, 0);
                for (int i = 0; i < 6; i++) File.WriteAllText(Path.Combine(folder, PerformanceLogText.FileName(start.AddMinutes(i))), "x");
                File.WriteAllText(Path.Combine(folder, "notes.txt"), "not a log");

                Assert.AreEqual(3, PerformanceLogText.Prune(folder, 3));
                Assert.IsFalse(File.Exists(Path.Combine(folder, PerformanceLogText.FileName(start))), "the oldest goes first");
                Assert.IsTrue(File.Exists(Path.Combine(folder, PerformanceLogText.FileName(start.AddMinutes(5)))), "the newest stays");
                Assert.IsTrue(File.Exists(Path.Combine(folder, "notes.txt")), "a file that is not a log is left alone");
                Assert.AreEqual(0, PerformanceLogText.Prune(folder, 3), "nothing more to delete");
                Assert.AreEqual(0, PerformanceLogText.Prune(Path.Combine(folder, "missing"), 3));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
