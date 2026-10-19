using System;
using System.IO;
using System.Threading;
using KryticalNova.Persistence;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The game's side of the save service (online phase 4, milestone 1), against a real service and database on this PC:
    /// the tests' own (SaveService --test: port 7802, database krytical_nova_test), never the real saves. Skipped (not
    /// failed) where it is not running. Every key used here is under world/test-service- and removed afterwards.
    /// </summary>
    public sealed class ServiceSaveStoreTests
    {
        private const string Url = "http://127.0.0.1:7802"; // the tests' own save service (SaveService --test), never the real saves
        private static readonly string KeyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KryticalNova-Secrets", "save-service-key.txt");

        private ServiceSaveStore a, b;
        private string key, owner;

        [SetUp]
        public void Connect()
        {
            if (!File.Exists(KeyFile)) Assert.Ignore("No save service key on this PC.");
            string secret = File.ReadAllText(KeyFile).Trim();
            a = new ServiceSaveStore(Url, secret, "test-server-a");
            if (!a.Reachable(out string problem)) { a.Dispose(); a = null; Assert.Ignore("The save service is not running: " + problem); }
            b = new ServiceSaveStore(Url, secret, "test-server-b");
            string id = Guid.NewGuid().ToString("N").Substring(0, 8);
            key = "world/test-service-" + id;
            owner = "accounts/test-service-" + id;
        }

        [TearDown]
        public void Clean()
        {
            a?.Delete(key);
            a?.Dispose();
            b?.Dispose();
        }

        [Test]
        public void AWriteComesBackAfterTheFlush()
        {
            Assert.IsFalse(a.TryRead(key, out _));
            a.Write(key, "{\"gold\":5}");
            Assert.IsTrue(a.TryRead(key, out string waiting), "what is waiting is the truth");
            Assert.AreEqual("{\"gold\":5}", waiting);
            a.Flush();
            Assert.IsTrue(b.TryRead(key, out string stored), "another server reads it from the service");
            Assert.AreEqual("{\"gold\":5}", stored);
            CollectionAssert.Contains(b.Keys("world/test-service-"), key);
        }

        [Test]
        public void TextWithQuotesAndLinesSurvives()
        {
            const string text = "{\"name\":\"Bo \\\"the\\\" Bold\",\"note\":\"line one\\nline two\",\"path\":\"C:\\\\x\"}";
            a.Write(key, text);
            a.Flush();
            Assert.IsTrue(b.TryRead(key, out string stored));
            Assert.AreEqual(text, stored);
        }

        [Test]
        public void AStaleServerCannotOverwriteNewerSaves()
        {
            a.Write(key, "{\"v\":1}");
            a.Flush();
            Assert.IsTrue(b.TryRead(key, out _));   // B knows version 1
            a.Write(key, "{\"v\":2}");                // A moves it to version 2
            a.Flush();
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("was changed elsewhere"));
            b.Write(key, "{\"v\":\"stale\"}");        // B still thinks it is at version 1
            b.Flush();
            var fresh = new ServiceSaveStore(Url, File.ReadAllText(KeyFile).Trim(), "test-reader");
            Assert.IsTrue(fresh.TryRead(key, out string kept));
            fresh.Dispose();
            Assert.AreEqual("{\"v\":2}", kept, "the newer save stays");
        }

        [Test]
        public void OneServerAtATimeHoldsAnAccount()
        {
            Assert.IsTrue(a.TryLock(owner, out _), "A takes the account");
            Assert.IsFalse(b.TryLock(owner, out string heldBy), "B is refused while A holds it");
            Assert.AreEqual("test-server-a", heldBy);
            Assert.IsTrue(a.TryLock(owner, out _), "A may take it again (renew)");
            a.Unlock(owner);
            // The unlock waits for A's saves first, off the game's thread.
            bool taken = false;
            for (int i = 0; i < 50 && !taken; i++)
            {
                taken = b.TryLock(owner, out _);
                if (!taken) Thread.Sleep(100);
            }
            Assert.IsTrue(taken, "B gets it once A lets go");
            b.Unlock(owner);
        }

        [Test]
        public void TheHealthCheckSeesTheServiceAndTheDatabase()
        {
            for (int i = 0; i < 30 && a.Health == "checking"; i++) Thread.Sleep(100);
            Assert.IsTrue(a.Healthy, a.Health);
            StringAssert.StartsWith("ok, ", a.Health);
        }

        [Test]
        public void AWrongKeyIsRefused()
        {
            var stranger = new ServiceSaveStore(Url, new string('x', 64), "stranger");
            Assert.IsFalse(stranger.Reachable(out string problem));
            StringAssert.Contains("refused", problem);
            stranger.Dispose();
        }
    }
}
