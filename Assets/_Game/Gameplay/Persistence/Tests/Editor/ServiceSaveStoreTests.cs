using System;
using System.IO;
using System.Threading;
using KryticalNova.Core;
using KryticalNova.Persistence;
using NUnit.Framework;
using UnityEngine;

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
        public void ABackupHoldsTheDatabasesSaves()
        {
            a.Write(key, "{\"backed\":\"up\"}");
            a.Flush();
            string root = Path.Combine(Path.GetTempPath(), "kn-backup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            ISaveStore was = ServerSave.Store;
            try
            {
                ServerSave.Store = a;
                string line = ServerBackups.MakeNow(Path.Combine(root, "save"), Path.Combine(root, "backups"), 2, "test");
                StringAssert.Contains("saves from the database", line);
                string zip = Directory.GetFiles(Path.Combine(root, "backups"), "*.zip")[0];
                using (var archive = System.IO.Compression.ZipFile.OpenRead(zip))
                {
                    var entry = archive.GetEntry("database/" + key + ".json");
                    Assert.IsNotNull(entry, "the save is in the zip, in a save folder's layout");
                    using (var reader = new StreamReader(entry.Open())) Assert.AreEqual("{\"backed\":\"up\"}", reader.ReadToEnd());
                }
            }
            finally
            {
                ServerSave.Store = was; // a is disposed by the swap; TearDown makes nothing of it
                a = null;
                if (Directory.Exists(root)) Directory.Delete(root, true);
                b.Delete(key);
                b.Flush();
            }
        }

        [Test]
        public void LedgerLinesAreKeptWithTheirBatch()
        {
            // Milestone 3: the lines go in the same transaction as the saves they describe.
            string account = "test-ledger-" + key.Substring(key.Length - 8);
            var batch = new SaveBatch();
            batch.Write(key, "{\"sold\":1}");
            batch.Ledger.Add(LedgerEntry.For(LedgerKinds.KioskBuy, account, "c", "Tester").With(1203, 1, "Arc Blade"));
            batch.Ledger.Add(LedgerEntry.For(LedgerKinds.CredsPrefix + "kioskpurchase", account, "c", "Tester"));
            batch.Ledger[1].creds = -250;
            a.WriteBatch(batch);
            a.Flush();
            Assert.IsTrue(b.TryRead(key, out _));
            var lines = b.ReadLedger(account, DateTime.UtcNow.AddMinutes(-5).Ticks, 10);
            Assert.AreEqual(2, lines.Count);
            Assert.IsTrue(lines.Exists(l => l.kind == LedgerKinds.KioskBuy && l.items.Count == 1 && l.items[0].item == 1203));
            Assert.IsTrue(lines.Exists(l => l.creds == -250));
            Assert.AreEqual(0, b.ReadLedger(account + "-nobody", 0, 10).Count);
        }

        [Test]
        public void ABatchSentAgainAfterALostAnswerIsAppliedOnce()
        {
            // The answer to a batch was lost (a timeout): the server sends the same batch again. It must be taken as done,
            // neither applied twice nor refused as stale (which would turn away every later save of that player).
            string secret = File.ReadAllText(KeyFile).Trim();
            string id = Guid.NewGuid().ToString("N");
            string body = "{\"id\":\"" + id + "\",\"writes\":[{\"key\":\"" + key + "\",\"body\":\"{}\",\"expected\":0}],\"deletes\":[]," +
                          "\"ledger\":[" + JsonUtility.ToJson(LedgerEntry.For(LedgerKinds.Trade, "test-resend-" + id.Substring(0, 8), "c", "Tester")) + "]}";
            using (var http = new System.Net.Http.HttpClient { BaseAddress = new Uri(Url + "/") })
            {
                http.DefaultRequestHeaders.Add("X-KN-Key", secret);
                for (int i = 0; i < 2; i++)
                {
                    var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
                    using (var answer = http.PostAsync("batch", content).GetAwaiter().GetResult())
                        Assert.AreEqual(System.Net.HttpStatusCode.OK, answer.StatusCode, $"send {i + 1} is done");
                }
                using (var doc = http.GetAsync("doc?key=" + Uri.EscapeDataString(key)).GetAwaiter().GetResult())
                    StringAssert.Contains("\"version\":1", doc.Content.ReadAsStringAsync().GetAwaiter().GetResult(), "written once");
            }
            Assert.AreEqual(1, b.ReadLedger("test-resend-" + id.Substring(0, 8), 0, 10).Count, "its ledger line once");
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
