using System;
using System.Collections.Generic;
using System.IO;
using KryticalNova.Core;
using KryticalNova.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Online phase 4, milestone 3: a change of several saves (a trade, a Kiosk sale, a Crew founding) is written whole or
    /// not at all, with the ledger lines that say what moved, and the ledger is kept, read and trimmed.
    /// </summary>
    public sealed class TransactionLedgerTests
    {
        private const string Seller = "0123456789abcdef0123456789abcdef";
        private const string Buyer = "fedcba9876543210fedcba9876543210";
        private const string SellerCharacter = "11111111111111111111111111111111";
        private const string BuyerCharacter = "22222222222222222222222222222222";

        private ISaveStore previousStore;
        private MemorySaveStore memory;
        private string root;

        [Serializable]
        private sealed class Shelf
        {
            public List<string> items = new List<string>();
        }

        [SetUp]
        public void SetUp()
        {
            previousStore = ServerSave.Store;
            memory = new MemorySaveStore();
            ServerSave.Store = memory;
            root = Path.Combine(Application.temporaryCachePath, "kn-ledger-test-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            ServerSave.Store = previousStore;
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        private static AccountSave Account(string id, string character, long creds)
        {
            AccountSave account = ServerSave.LoadAccount(id);
            var save = new CharacterSave { characterId = character, name = id == Seller ? "Tolan" : "Kael" };
            save.Set("wallet", "{\"creds\":" + creds + "}");
            account.characters.Add(save);
            return account;
        }

        [Test]
        public void TwoPlayersAWorldDocumentAndTheirLedgerLinesAreOneChange()
        {
            AccountSave seller = Account(Seller, SellerCharacter, 100);
            AccountSave buyer = Account(Buyer, BuyerCharacter, 500);
            ServerSave.World<Shelf>("test-together").items.Add("Arc Blade");
            Assert.IsTrue(ServerSave.WriteTogether(new[] { seller, buyer }, "test-together"));
            int before = memory.Batches;

            // A sale: the buyer pays, the seller is paid, the shelf empties, and each side has its line.
            seller.Find(SellerCharacter).Set("wallet", "{\"creds\":400}");
            buyer.Find(BuyerCharacter).Set("wallet", "{\"creds\":200}");
            ServerSave.World<Shelf>("test-together").items.Clear();
            ServerSave.Ledger(LedgerEntry.For(LedgerKinds.KioskBuy, Buyer, BuyerCharacter, "Kael").With(1203, 1, "Arc Blade"));
            ServerSave.Ledger(LedgerEntry.For(LedgerKinds.KioskCollect, Seller, SellerCharacter, "Tolan"));
            Assert.AreEqual(0, memory.ReadLedger(null, 0, 10).Count, "a ledger line waits for the save of the change it describes");

            Assert.IsTrue(ServerSave.WriteTogether(new[] { seller, buyer }, "test-together"));
            Assert.AreEqual(before + 1, memory.Batches, "both players, the shelf and the lines went as one change");
            Assert.IsTrue(memory.TryRead(ServerSave.CharacterPartKey(Seller, SellerCharacter, "wallet"), out string sellerWallet));
            Assert.AreEqual("{\"creds\":400}", sellerWallet);
            Assert.IsTrue(memory.TryRead(ServerSave.CharacterPartKey(Buyer, BuyerCharacter, "wallet"), out string buyerWallet));
            Assert.AreEqual("{\"creds\":200}", buyerWallet);
            Assert.IsTrue(memory.TryRead("world/test-together", out string shelf));
            StringAssert.DoesNotContain("Arc Blade", shelf);
            Assert.AreEqual(2, memory.ReadLedger(null, 0, 10).Count);
            Assert.AreEqual(1, memory.ReadLedger(Buyer, 0, 10).Count);

            Assert.IsFalse(ServerSave.WriteTogether(new[] { seller, buyer }, "test-together"), "nothing changed: nothing written");
        }

        [Test]
        public void ATallyAddsUpUntilTheSave()
        {
            AccountSave account = Account(Seller, SellerCharacter, 0);
            ServerSave.WriteAccount(account);
            for (int i = 0; i < 5; i++)
            {
                ServerSave.Tally(LedgerEntry.For(LedgerKinds.CredsPrefix + "hostilekill", Seller, SellerCharacter, "Tolan"), 30, 0, 0, null);
                ServerSave.Tally(LedgerEntry.For(LedgerKinds.Loot, Seller, SellerCharacter, "Tolan"), 0, 40, 2, "Stim S");
            }
            ServerSave.Tally(LedgerEntry.For(LedgerKinds.CredsPrefix + "vendor", Seller, SellerCharacter, "Tolan"), -120, 0, 0, null);
            Assert.IsTrue(ServerSave.HasPendingLedger(Seller));
            Assert.IsTrue(ServerSave.WriteAccount(account), "waiting lines are a reason to save on their own");

            List<LedgerEntry> lines = memory.ReadLedger(Seller, 0, 10);
            Assert.AreEqual(3, lines.Count, "one line per kind, not one per movement");
            LedgerEntry kills = lines.Find(l => l.kind == "creds.hostilekill");
            Assert.AreEqual(150, kills.creds);
            LedgerEntry loot = lines.Find(l => l.kind == LedgerKinds.Loot);
            Assert.AreEqual(1, loot.items.Count);
            Assert.AreEqual(10, loot.items[0].count);
            long net = 0;
            foreach (LedgerEntry line in lines) net += line.creds;
            Assert.AreEqual(150 - 120, net, "the ledger's Creds add up to the wallet's change");
            Assert.IsFalse(ServerSave.HasPendingLedger(Seller));
        }

        [Test]
        public void FileStore_ABatchIsWholeAndItsLedgerIsKeptByDay()
        {
            var store = new FileSaveStore(root);
            var batch = new SaveBatch();
            batch.Write("accounts/" + Seller, "{\"a\":1}");
            batch.Write("accounts/" + Buyer, "{\"b\":1}");
            LedgerEntry old = LedgerEntry.For(LedgerKinds.Trade, Seller, SellerCharacter, "Tolan").With(1203, -1, "Arc Blade");
            old.utc = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc).Ticks;
            LedgerEntry now = LedgerEntry.For(LedgerKinds.Trade, Buyer, BuyerCharacter, "Kael").With(1203, 1, "Arc Blade");
            batch.Ledger.Add(old);
            batch.Ledger.Add(now);
            store.WriteBatch(batch);

            Assert.IsTrue(store.TryRead("accounts/" + Seller, out _));
            Assert.IsTrue(store.TryRead("accounts/" + Buyer, out _));
            Assert.AreEqual(0, Directory.GetFiles(Path.Combine(root, "_journal")).Length, "the journal goes once the change is in place");
            CollectionAssert.AreEquivalent(new[] { "accounts/" + Buyer, "accounts/" + Seller }, store.Keys(string.Empty), "journal and ledger files are not saves");
            List<LedgerEntry> all = store.ReadLedger(null, 0, 10);
            Assert.AreEqual(2, all.Count);
            Assert.AreEqual(now.id, all[0].id, "newest first");
            Assert.AreEqual(1, store.ReadLedger(Seller, 0, 10).Count);

            Assert.AreEqual(1, store.PruneLedger(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc).Ticks), "the old day goes");
            Assert.AreEqual(1, store.ReadLedger(null, 0, 10).Count);
        }

        [Test]
        public void FileStore_AChangeAStopCutShortIsFinishedFromItsJournal_AndItsLedgerIsNotCountedTwice()
        {
            // As a stop leaves it: the journal written whole, the change not yet (or only partly) in place.
            LedgerEntry line = LedgerEntry.For(LedgerKinds.Trade, Seller, SellerCharacter, "Tolan").With(1203, -1, "Arc Blade");
            string journal = "{\"docs\":[{\"key\":\"accounts/" + Seller + "\",\"text\":\"{\\\"a\\\":2}\",\"delete\":false}," +
                             "{\"key\":\"accounts/" + Buyer + "\",\"text\":\"\",\"delete\":true}]," +
                             "\"ledger\":[" + JsonUtility.ToJson(line) + "]}";
            string folder = Path.Combine(root, "_journal");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(root, "half-written.json"), "x"); // unrelated
            Directory.CreateDirectory(Path.Combine(root, "accounts"));
            File.WriteAllText(Path.Combine(root, "accounts", Buyer + ".json"), "{\"b\":1}");
            File.WriteAllText(Path.Combine(folder, "0000000000000000001-000001.journal"), journal);
            // A journal a stop cut off while it was being written was never a change: nothing of it is applied.
            File.WriteAllText(Path.Combine(folder, "0000000000000000002-000002.journal.tmp"), "{\"docs\":[{\"key\":\"accounts/" + Seller + "\",\"text\":\"WRONG\"");

            var store = new FileSaveStore(root);
            Assert.IsTrue(store.TryRead("accounts/" + Seller, out string seller));
            Assert.AreEqual("{\"a\":2}", seller, "the change was finished");
            Assert.IsFalse(store.TryRead("accounts/" + Buyer, out _), "its delete too");
            Assert.AreEqual(0, Directory.GetFiles(folder).Length, "the journal and the cut-off one are gone");
            Assert.AreEqual(1, store.ReadLedger(null, 0, 10).Count);

            // A stop again before the journal was deleted: replayed a second time, the line is still there once.
            File.WriteAllText(Path.Combine(folder, "0000000000000000003-000003.journal"), journal);
            store = new FileSaveStore(root);
            Assert.AreEqual(1, store.ReadLedger(null, 0, 10).Count, "a replayed line is not written twice");
        }

        [Test]
        public void FileStore_InTheBackground_KeepsTheOrderOfChanges()
        {
            var store = new FileSaveStore(root, background: true);
            try
            {
                for (int i = 0; i < 20; i++)
                {
                    var batch = new SaveBatch();
                    batch.Write("accounts/" + Seller, "{\"n\":" + i + "}");
                    batch.Write("accounts/" + Buyer, "{\"n\":" + i + "}");
                    batch.Ledger.Add(LedgerEntry.For(LedgerKinds.Trade, Seller, SellerCharacter, "Tolan"));
                    store.WriteBatch(batch);
                }
                Assert.IsTrue(store.TryRead("accounts/" + Buyer, out string waiting));
                Assert.AreEqual("{\"n\":19}", waiting, "a read gives the newest change handed over");
                store.Flush();
                Assert.AreEqual("{\"n\":19}", File.ReadAllText(Path.Combine(root, "accounts", Seller + ".json")));
                Assert.AreEqual(20, store.ReadLedger(null, 0, 100).Count);
            }
            finally
            {
                store.Dispose();
            }
        }

        [Test]
        public void ALedgerLineReadsAsOneSentence()
        {
            LedgerEntry line = LedgerEntry.For(LedgerKinds.Trade, Seller, SellerCharacter, "Tolan").With(1203, -1, "Arc Blade").With(40, 3, "Stim S");
            line.otherName = "Kael";
            line.note = "gave 500 Creds, received 0 Creds";
            string text = line.Describe();
            StringAssert.Contains("trade Tolan: -1 Arc Blade (1203), +3 Stim S (40) with Kael (gave 500 Creds", text);
        }
    }
}
