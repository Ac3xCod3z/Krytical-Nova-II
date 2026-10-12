using System.Collections.Generic;
using System.IO;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Persistence;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    public sealed class SaveStoreTests
    {
        private const string Account = "0123456789abcdef0123456789abcdef";
        private const string Character = "fedcba9876543210fedcba9876543210";

        private ISaveStore previousStore;
        private MemorySaveStore memory;

        [SetUp]
        public void SetUp()
        {
            previousStore = ServerSave.Store;
            memory = new MemorySaveStore();
            ServerSave.Store = memory;
        }

        [TearDown]
        public void TearDown() => ServerSave.Store = previousStore;

        [Test]
        public void Keys_CannotLeaveTheSaveFolder()
        {
            Assert.IsTrue(SaveKeys.IsValid("accounts/" + Account));
            Assert.IsTrue(SaveKeys.IsValid("world/crews"));
            Assert.IsFalse(SaveKeys.IsValid("../secrets"));
            Assert.IsFalse(SaveKeys.IsValid("accounts//x"));
            Assert.IsFalse(SaveKeys.IsValid("/accounts"));
            Assert.IsFalse(SaveKeys.IsValid("accounts/"));
            Assert.IsFalse(SaveKeys.IsValid("C:/windows"));
            Assert.IsFalse(SaveKeys.IsValid("World/Crews"), "keys are lower-case, so two spellings never name two files");
            Assert.IsFalse(SaveKeys.IsValid(string.Empty));
        }

        [Test]
        public void Ids_AreThirtyTwoLowerCaseHexCharacters()
        {
            Assert.IsTrue(SaveKeys.IsId(Account));
            Assert.IsFalse(SaveKeys.IsId(Account.ToUpperInvariant()));
            Assert.IsFalse(SaveKeys.IsId(Account.Substring(1)));
            Assert.IsFalse(SaveKeys.IsId("0123456789abcdef0123456789abcdeg"));
            Assert.IsFalse(SaveKeys.IsId(null));
        }

        [Test]
        public void FileStore_WritesReadsListsAndDeletes()
        {
            string root = Path.Combine(Application.temporaryCachePath, "kn-save-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileSaveStore(root);
                Assert.IsFalse(store.TryRead("world/crews", out _));
                store.Write("world/crews", "{\"a\":1}");
                store.Write("world/crews", "{\"a\":2}"); // replaces
                store.Write("accounts/" + Account, "{}");
                Assert.IsTrue(store.TryRead("world/crews", out string json));
                Assert.AreEqual("{\"a\":2}", json);
                CollectionAssert.AreEqual(new[] { "accounts/" + Account }, store.Keys("accounts/"));
                Assert.AreEqual(2, store.Keys(string.Empty).Count);
                Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length, "no temporary file is left behind");
                store.Delete("world/crews");
                Assert.IsFalse(store.TryRead("world/crews", out _));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void FileStore_InTheBackground_GivesBackWhatWaits_AndPutsTheNewestOnDisk()
        {
            string root = Path.Combine(Application.temporaryCachePath, "kn-save-test-" + System.Guid.NewGuid().ToString("N"));
            var store = new FileSaveStore(root, background: true);
            try
            {
                for (int i = 0; i < 50; i++) store.Write("world/crews", "{\"a\":" + i + "}");
                store.Write("accounts/" + Account, "{}");
                // Before the disk has it, a read gives the newest text handed over.
                Assert.IsTrue(store.TryRead("world/crews", out string waiting));
                Assert.AreEqual("{\"a\":49}", waiting);

                store.Flush();
                Assert.AreEqual("{\"a\":49}", File.ReadAllText(Path.Combine(root, "world", "crews.json")));
                Assert.AreEqual(2, store.Keys(string.Empty).Count);
                Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length, "no temporary file is left behind");

                // A delete takes out what waits as well as what is on disk.
                store.Write("world/crews", "{\"a\":50}");
                store.Delete("world/crews");
                store.Flush();
                Assert.IsFalse(store.TryRead("world/crews", out _));
                Assert.IsFalse(File.Exists(Path.Combine(root, "world", "crews.json")));

                // Closed, a write is on disk when it returns.
                store.Dispose();
                store.Write("world/late", "{\"b\":1}");
                Assert.AreEqual("{\"b\":1}", File.ReadAllText(Path.Combine(root, "world", "late.json")));
            }
            finally
            {
                store.Dispose();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void Account_RoundTripsWithEveryPartAndItemData()
        {
            var stack = new ItemStack(8001, 1, ulong.MaxValue - 5);
            var progression = new ProgressionState { level = 57, experience = 1234, statPoints = 3, skillPoints = 9, strength = 40, vitality = 12, agility = 7, spirit = 30 };

            var account = ServerSave.LoadAccount(Account);
            Assert.AreEqual(0, account.characters.Count, "an unknown account starts empty");
            var character = new CharacterSave { characterId = Character, name = "Vex", faction = 2, level = 57 };
            Assert.IsTrue(character.Set("item", JsonUtility.ToJson(stack)));
            Assert.IsTrue(character.Set("progression", JsonUtility.ToJson(progression)));
            account.characters.Add(character);
            Assert.IsTrue(account.Set("deepvault", "{\"creds\":5}"));
            Assert.IsTrue(ServerSave.WriteAccount(account));

            var loaded = ServerSave.LoadAccount(Account);
            var again = loaded.Find(Character);
            Assert.IsNotNull(again);
            Assert.AreEqual("Vex", again.name);
            Assert.AreEqual(stack, JsonUtility.FromJson<ItemStack>(again.Get("item")), "64-bit item data survives the JSON round trip");
            Assert.AreEqual(progression, JsonUtility.FromJson<ProgressionState>(again.Get("progression")));
            Assert.AreEqual("{\"creds\":5}", loaded.Get("deepvault"));
            Assert.IsNull(again.Get("nothing"), "a part that was never saved reads as null");
        }

        [Test]
        public void Account_IsWrittenOnlyWhenItChanged()
        {
            var account = ServerSave.LoadAccount(Account);
            account.characters.Add(new CharacterSave { characterId = Character, name = "Vex" });
            Assert.IsTrue(ServerSave.WriteAccount(account));
            int writes = memory.Writes;
            Assert.IsFalse(ServerSave.WriteAccount(account));
            Assert.AreEqual(writes, memory.Writes);

            var character = account.Find(Character);
            Assert.IsTrue(character.Set("wallet", "{\"creds\":10}"));
            Assert.IsFalse(character.Set("wallet", "{\"creds\":10}"), "the same document is not a change");
            Assert.IsTrue(ServerSave.WriteAccount(account));
            Assert.IsTrue(character.Set("wallet", null), "an empty document removes the part");
            Assert.IsNull(character.Get("wallet"));
        }

        [System.Serializable]
        private sealed class Ledger
        {
            public List<string> lines = new List<string>();
        }

        [Test]
        public void WorldDocument_IsLoadedOnceAndSavedOnRequest()
        {
            var ledger = ServerSave.World<Ledger>("test-ledger");
            Assert.AreSame(ledger, ServerSave.World<Ledger>("test-ledger"));
            ledger.lines.Add("first");
            ServerSave.SaveWorld("test-ledger");
            Assert.IsTrue(memory.TryRead("world/test-ledger", out string json));
            StringAssert.Contains("first", json);

            // A fresh store (a restart) reads what was written.
            var restarted = new MemorySaveStore();
            restarted.Write("world/test-ledger", json);
            ServerSave.Store = restarted;
            Assert.AreEqual(1, ServerSave.World<Ledger>("test-ledger").lines.Count);
        }

        [Test]
        public void OfflineAccountEdit_ChangesTheStoredAccount()
        {
            Assert.IsTrue(ServerSave.EditOfflineAccount(Account, account => account.Set("mail", "{\"n\":1}")));
            Assert.AreEqual("{\"n\":1}", ServerSave.LoadAccount(Account).Get("mail"));
            Assert.IsFalse(ServerSave.EditOfflineAccount("not-an-id", _ => { }));
        }
    }
}
