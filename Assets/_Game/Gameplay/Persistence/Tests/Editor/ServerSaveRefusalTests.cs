using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KryticalNova.Core;
using KryticalNova.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KryticalNova.Tests
{
    /// <summary>
    /// A2 (lead and owner, 12 Nov 2026): a save the service refuses is forgotten whole, so the next save sends the server's whole copy
    /// (every part, refused deletes again, its ledger lines with it); an account this server has let go of is sent again at once.
    /// Runs against the tests' own save service (SaveService --test: port 7802, database krytical_nova_test), never the real saves.
    /// </summary>
    public sealed class ServerSaveRefusalTests
    {
        private const string Url = "http://127.0.0.1:7802";
        private static readonly string KeyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KryticalNova-Secrets", "save-service-key.txt");

        private ISaveStore previousStore;
        private ServiceSaveStore server, other;
        private string account, character, world;
        private string secret;

        [SetUp]
        public void SetUp()
        {
            if (!File.Exists(KeyFile)) Assert.Ignore("No save service key on this PC.");
            secret = File.ReadAllText(KeyFile).Trim();
            var probe = new ServiceSaveStore(Url, secret, "test-refusal-probe");
            bool up = probe.Reachable(out string problem);
            probe.Dispose();
            if (!up) Assert.Ignore("The save service is not running: " + problem);
            previousStore = ServerSave.Store;
            server = new ServiceSaveStore(Url, secret, "test-refusal-server");
            ServerSave.Store = server;
            other = new ServiceSaveStore(Url, secret, "test-refusal-other");
            account = Guid.NewGuid().ToString("N");
            character = Guid.NewGuid().ToString("N");
            world = "test-refusal-" + account.Substring(0, 8);
        }

        [TearDown]
        public void TearDown()
        {
            if (server == null) return;
            // Everything this test left goes in ONE change, and the wait is long enough for it. Before (12 Nov 2026) each key was a
            // save of its own: after the 5,000-name test that was 5,000 trips, more than the 15 s Flush and the 15 s of the store's
            // own close together, so the store was disposed with saves still in line.
            bool empty = server.Flush(60f);
            var clean = new SaveBatch();
            clean.Deletes.AddRange(server.Keys(ServerSave.AccountsPrefix + account).Concat(server.Keys(ServerSave.WorldPrefix + world)));
            server.WriteBatch(clean);
            empty &= server.Flush(60f);
            ServerSave.Store = previousStore;   // disposes the test store
            other?.Dispose();
            Assert.IsTrue(empty, "the test store was closed with saves still in line: the save service did not take them in 60 s");
        }

        private string Part(string name) => ServerSave.CharacterPartKey(account, character, name);

        private AccountSave TwoParts()
        {
            AccountSave save = ServerSave.LoadAccount(account);
            var c = new CharacterSave { characterId = character, name = "Refusal" };
            c.Set("wallet", "{\"creds\":1}");
            c.Set("bag", "{\"items\":1}");
            save.characters.Add(c);
            return save;
        }

        // Changes the keys behind the server's back (another writer), so the server's next save of them is refused.
        private void ChangeElsewhere(params string[] keys)
        {
            foreach (string key in keys)
            {
                Assert.IsTrue(other.TryRead(key, out _), key + " is on the service");
                other.Write(key, "{\"elsewhere\":true}");
            }
            other.Flush();
        }

        private string ReadFresh(string key)
        {
            var fresh = new ServiceSaveStore(Url, secret, "test-refusal-reader");
            fresh.TryRead(key, out string text);
            fresh.Dispose();
            return text;
        }

        private static void ExpectRefusal() => LogAssert.Expect(LogType.Error, new Regex("was changed elsewhere"));

        [Test]
        public void AnAccountRefusedInTwoParts_IsSentWholeNext()
        {
            AccountSave save = TwoParts();
            Assert.IsTrue(ServerSave.WriteAccount(save));
            server.Flush();
            ChangeElsewhere(Part("wallet"), Part("bag"));

            save.Find(character).Set("wallet", "{\"creds\":2}");   // the server changes part 1 only
            ExpectRefusal();
            Assert.IsTrue(ServerSave.WriteAccount(save));
            server.Flush();
            Assert.AreEqual("{\"elsewhere\":true}", ReadFresh(Part("wallet")), "refused: the other copy stays this once");

            Assert.IsTrue(ServerSave.WriteAccount(save), "after a refusal everything counts as changed");
            server.Flush();
            Assert.AreEqual("{\"creds\":2}", ReadFresh(Part("wallet")));
            Assert.AreEqual("{\"items\":1}", ReadFresh(Part("bag")), "the part the server never changed is the server's again: the copy went whole");
        }

        [Test]
        public void AWorldDocumentRefused_SendsItsNewEntryAndItsDeleteAgain()
        {
            NameIndex names = ServerSave.World<NameIndex>(world);
            names.Put("Xander", account, "x");
            names.Put("Yvonne", account, "y");
            ServerSave.SaveWorld(world);
            server.Flush();
            List<string> entries = server.Keys(ServerSave.WorldPrefix + world + "/").ToList();
            Assert.AreEqual(2, entries.Count);
            string xKey = entries.First(k => (ReadFresh(k) ?? "").Contains("Xander"));
            ChangeElsewhere(xKey, ServerSave.WorldPrefix + world);   // the header too, so the batch below is refused

            names.Put("Zed", account, "z");
            names.Drop("Yvonne", account, "y");
            ExpectRefusal();
            ServerSave.SaveWorld(world);
            server.Flush();
            ServerSave.SaveWorld(world);
            server.Flush();

            var texts = server.Keys(ServerSave.WorldPrefix + world + "/").Select(ReadFresh).ToList();
            Assert.IsTrue(texts.Any(t => t != null && t.Contains("Xander")), "the server's x is back");
            Assert.IsTrue(texts.Any(t => t != null && t.Contains("Zed")), "the refused new entry was sent again");
            Assert.IsFalse(texts.Any(t => t != null && t.Contains("Yvonne")), "the refused delete was sent again");
            Assert.IsFalse(texts.Any(t => t != null && t.Contains("elsewhere")));
        }

        [Test]
        public void TheRefusedChange_IsToldRefused()
        {
            AccountSave save = TwoParts();
            Assert.IsTrue(ServerSave.WriteAccount(save));
            server.Flush();
            ChangeElsewhere(Part("wallet"));
            save.Find(character).Set("wallet", "{\"creds\":3}");
            bool? told = null;
            ExpectRefusal();
            ServerSave.WriteTogether(new[] { save }, w => told = w);
            server.Flush();
            Assert.AreEqual(false, told, "the rename undo depends on this word");
        }

        [Test]
        public void ARefusedLeaveSave_IsSentAgainAsItWas()
        {
            AccountSave save = TwoParts();
            Assert.IsTrue(ServerSave.WriteAccount(save));
            server.Flush();
            ChangeElsewhere(Part("wallet"), Part("bag"));
            save.Find(character).Set("wallet", "{\"creds\":4}");
            LedgerEntry line = LedgerEntry.For(LedgerKinds.NameChanged, account, character, "Left > Gone");
            ServerSave.Ledger(line);
            ServerSave.LetGo(account);   // the player leaves: this is the leave save
            ExpectRefusal();
            Assert.IsTrue(ServerSave.WriteAccount(save));
            server.Flush();
            Assert.IsTrue(ServerSave.HasRefusals);

            LogAssert.Expect(LogType.Warning, new Regex("sent again as it was"));
            ServerSave.ApplyRefusals();
            server.Flush();
            Assert.AreEqual("{\"creds\":4}", ReadFresh(Part("wallet")), "the part the leave save carried is the server's");
            Assert.AreEqual("{\"elsewhere\":true}", ReadFresh(Part("bag")), "known leftover: a part the batch did not carry stays");
            Assert.AreEqual(1, server.ReadLedger(account, 0, 50).Count(l => l.kind == LedgerKinds.NameChanged), "the ledger line once");
        }

        [Test]
        public void AnUndoneRename_LeavesNoNameChangeLine()
        {
            AccountSave save = TwoParts();
            Assert.IsTrue(ServerSave.WriteAccount(save));
            server.Flush();
            ChangeElsewhere(Part("wallet"));
            save.Find(character).Set("wallet", "{\"creds\":5}");
            LedgerEntry line = LedgerEntry.For(LedgerKinds.NameChanged, account, character, "Old > New");
            ServerSave.Ledger(line);
            ExpectRefusal();
            Assert.IsTrue(ServerSave.WriteAccount(save));
            server.Flush();
            ServerSave.ApplyRefusals();                       // as PlayerRename.FinishSave
            Assert.IsTrue(ServerSave.Unledger(line), "the refused line was waiting again");
            ServerSave.WriteAccount(save);
            server.Flush();
            Assert.AreEqual(0, server.ReadLedger(account, 0, 50).Count(l => l.kind == LedgerKinds.NameChanged));
        }

        [Test]
        public void AForwardFinishedRename_LeavesExactlyOneLine()
        {
            AccountSave save = TwoParts();
            Assert.IsTrue(ServerSave.WriteAccount(save));
            server.Flush();
            ChangeElsewhere(Part("wallet"));
            save.Find(character).Set("wallet", "{\"creds\":6}");
            LedgerEntry line = LedgerEntry.For(LedgerKinds.NameChanged, account, character, "Old > New");
            ServerSave.Ledger(line);
            ServerSave.LetGo(account);
            ExpectRefusal();
            Assert.IsTrue(ServerSave.WriteAccount(save));
            server.Flush();
            var left = new PlayerRename.LeftRename("Old", "New", account, character, line);
            PlayerRename.KeepLeftRename(left);
            left.Tell(false);
            LogAssert.Expect(LogType.Warning, new Regex("sent again as it was"));
            LogAssert.Expect(LogType.Warning, new Regex("finished forward"));
            ServerSave.ApplyRefusals();
            PlayerRename.FinishLeftRenames(() => new NameIndex(), () => { }, ServerSave.Ledger, ServerSave.IsLedgerWaiting);
            ServerSave.FlushAll();
            server.Flush();
            Assert.AreEqual(1, server.ReadLedger(account, 0, 50).Count(l => l.kind == LedgerKinds.NameChanged));
        }

        // Found by its first run (12 Nov 2026): the service took at most 500 items a batch, so a names list of 5,000 could never be
        // saved whole. Owner's pick: the limit is the service's MaxBatchItems (10,000), and a 400 is final, not retried for ever.
        [Test]
        public void AWholeNamesResend_FitsTheServiceRequest()
        {
            NameIndex names = ServerSave.World<NameIndex>(world);
            for (int i = 0; i < 5000; i++) names.Put("Name" + i.ToString("D5"), account, "c" + i);
            ServerSave.SaveWorld(world);
            Assert.IsTrue(server.Flush(60f));
            string one = server.Keys(ServerSave.WorldPrefix + world + "/").First();
            ChangeElsewhere(one, ServerSave.WorldPrefix + world);
            names.Put("Extra", account, "extra");
            ExpectRefusal();
            ServerSave.SaveWorld(world);
            Assert.IsTrue(server.Flush(60f));
            ServerSave.SaveWorld(world);   // the whole document: 5001 entries in one change
            Assert.IsTrue(server.Flush(60f));
            Assert.AreEqual(5001, server.Keys(ServerSave.WorldPrefix + world + "/").Count, "written in one change");
            Assert.IsFalse((ReadFresh(one) ?? "").Contains("elsewhere"));
        }
    }
}
