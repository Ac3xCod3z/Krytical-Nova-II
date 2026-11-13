using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using KryticalNova.Core;
using KryticalNova.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KryticalNova.Tests
{
    /// <summary>
    /// A disposed save store is quiet (12 Nov 2026): after the full EditMode run a disposed store's writer thread still held a save
    /// and tried it once a second for ever, and every later test failed on its error line. Disposing with a save in line and the
    /// service gone ends the writer thread and says once how many saves were left. Needs no save service: it talks to a port where
    /// nothing listens (never one of the real server's, never the tests' service).
    /// </summary>
    public sealed class ServiceSaveStoreDisposeTests
    {
        // Nothing listens on port 1 of this PC; the real lane is 7777 / 7778 / 7801 / 8090 and the tests' service is 7802.
        private const string NobodyThere = "http://127.0.0.1:1";

        private readonly List<string> lines = new List<string>();
        private bool oldIgnore;

        [SetUp]
        public void SetUp()
        {
            lock (lines) lines.Clear();
            oldIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;   // the store's error lines are what this test counts
            Application.logMessageReceivedThreaded += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceivedThreaded -= OnLog;
            LogAssert.ignoreFailingMessages = oldIgnore;
        }

        private void OnLog(string condition, string stack, LogType type)
        {
            if (condition != null && condition.StartsWith("[Save]", StringComparison.Ordinal))
                lock (lines) lines.Add(condition);
        }

        private List<string> Lines() { lock (lines) return lines.ToList(); }

        private static bool WaitUntil(Func<bool> done, double seconds)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < until)
            {
                if (done()) return true;
                Thread.Sleep(50);
            }
            return done();
        }

        [Test]
        public void DisposedWithASaveInLine_AndTheServiceGone_TheWriterEnds_AndSaysItOnce()
        {
            LogAssert.ignoreFailingMessages = true;   // set in the test itself: the flag set in SetUp did not hold for the test body (full run, 12 Nov 2026)
            var store = new ServiceSaveStore(NobodyThere, "no-key", "test-dispose");
            bool? told = null;
            var batch = new SaveBatch();
            batch.Write("world/test-dispose-" + Guid.NewGuid().ToString("N").Substring(0, 8), "{\"never\":true}");
            batch.Done = written => told = written;
            store.WriteBatch(batch);
            Assert.IsTrue(store.WriterRunning, "the save is in line and the writer thread is on it");

            store.Close(0.3f);   // the service never answers: the wait runs out with the save still in line

            Assert.IsTrue(WaitUntil(() => !store.WriterRunning, 10), "the writer thread ends with the store");
            List<string> closing = Lines().Where(l => l.Contains("closed with")).ToList();
            Assert.AreEqual(1, closing.Count, "said once: " + string.Join(" | ", Lines()));
            StringAssert.Contains("closed with 1 save(s)", closing[0]);

            // and it stays quiet: before the fix there was a "Could not save" line every second from here on
            int after = Lines().Count;
            Thread.Sleep(2500);
            Assert.AreEqual(after, Lines().Count, "no line after the store closed: " + string.Join(" | ", Lines().Skip(after)));
            Assert.IsNull(told, "the save was never confirmed either way");

            Assert.IsFalse(store.Flush(5f), "a Flush on a closed store does not wait and says what was left");
            store.Dispose();   // a second close does nothing
            Assert.AreEqual(1, Lines().Count(l => l.Contains("closed with")), "still said once");
        }

        [Test]
        public void ASaveHandedToAClosedStore_IsRefusedOnce_NotQueued()
        {
            LogAssert.ignoreFailingMessages = true;   // set in the test itself: the flag set in SetUp did not hold for the test body (full run, 12 Nov 2026)
            var store = new ServiceSaveStore(NobodyThere, "no-key", "test-dispose");
            store.Close(0.1f);
            Assert.AreEqual(0, Lines().Count(l => l.Contains("closed with")), "nothing was in line: nothing to say");

            bool? told = null;
            var batch = new SaveBatch();
            batch.Write("world/test-dispose-late", "{\"late\":true}");
            batch.Done = written => told = written;
            store.WriteBatch(batch);

            Assert.AreEqual(false, told, "told at once that it was not written");
            Assert.IsFalse(store.WriterRunning, "no thread is started for it");
            Assert.AreEqual(1, Lines().Count(l => l.Contains("already closed")));
            Assert.IsTrue(store.Flush(5f), "nothing is in line, so nothing waits");
        }

        [Test]
        public void AClosedIdleStore_IsSilent()
        {
            LogAssert.ignoreFailingMessages = true;   // set in the test itself: the flag set in SetUp did not hold for the test body (full run, 12 Nov 2026)
            var store = new ServiceSaveStore(NobodyThere, "no-key", "test-dispose");
            store.Dispose();
            Assert.IsFalse(store.WriterRunning);
            Assert.AreEqual(0, Lines().Count, string.Join(" | ", Lines()));
        }
    }
}
