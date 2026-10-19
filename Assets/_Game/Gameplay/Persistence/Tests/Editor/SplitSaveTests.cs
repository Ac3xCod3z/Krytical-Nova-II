using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The account kept in parts (online phase 4, milestone 2): a small header with no game state, one document per part,
    /// a save writing only what changed, a part that is gone deleted, an account in the old one-piece layout read as it is,
    /// kept once and saved in parts from then on.
    /// </summary>
    public sealed class SplitSaveTests
    {
        private const string Account = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string Hero = "11111111111111111111111111111111";
        private const string Alt = "22222222222222222222222222222222";

        private ISaveStore previous;
        private MemorySaveStore memory;

        [SetUp]
        public void UseMemory()
        {
            previous = ServerSave.Store;
            memory = new MemorySaveStore();
            ServerSave.Store = memory;
        }

        [TearDown]
        public void Restore() => ServerSave.Store = previous;

        private static AccountSave TwoCharacters()
        {
            var account = new AccountSave { accountId = Account };
            account.Set("deepvault", "{\"cells\":[1,2,3]}");
            var hero = new CharacterSave { characterId = Hero, name = "Tolan", faction = 1, level = 12, map = "Zone01_StarterTown", x = 1.5f, y = 2f, z = -3f };
            hero.Set("inventory", "{\"pack\":[5,6]}");
            hero.Set("wallet", "{\"creds\":500}");
            hero.Set("vendorWeek", "{\"week\":3}");
            var alt = new CharacterSave { characterId = Alt, name = "Bo", faction = 2, level = 3 };
            alt.Set("wallet", "{\"creds\":7}");
            account.characters.Add(hero);
            account.characters.Add(alt);
            return account;
        }

        [Test]
        public void TheHeaderHoldsNoGameStateAndEachPartHasItsOwnDocument()
        {
            Assert.IsTrue(ServerSave.WriteAccount(TwoCharacters()));
            Assert.IsTrue(memory.TryRead("accounts/" + Account, out string header));
            StringAssert.Contains("\"version\":2", header);
            StringAssert.Contains("Tolan", header);
            StringAssert.Contains("Zone01_StarterTown", header);
            StringAssert.DoesNotContain("creds", header, "no game state in the header");
            Assert.IsTrue(memory.TryRead("accounts/" + Account + "/parts/deepvault", out string vault));
            Assert.AreEqual("{\"cells\":[1,2,3]}", vault);
            Assert.IsTrue(memory.TryRead("characters/" + Hero + "/parts/inventory", out _));
            Assert.IsTrue(memory.TryRead("characters/" + Hero + "/parts/vendorweek", out _), "a part name is kept in lower case");
            Assert.IsTrue(memory.TryRead("characters/" + Alt + "/parts/wallet", out string altWallet));
            Assert.AreEqual("{\"creds\":7}", altWallet);
        }

        [Test]
        public void EverythingComesBack()
        {
            ServerSave.WriteAccount(TwoCharacters());
            ServerSave.Store = memory; // forget what was written: as a new server would
            AccountSave loaded = ServerSave.LoadAccount(Account);
            Assert.AreEqual("{\"cells\":[1,2,3]}", loaded.Get("deepvault"));
            CharacterSave hero = loaded.Find(Hero);
            Assert.AreEqual("Tolan", hero.name);
            Assert.AreEqual(12, hero.level);
            Assert.AreEqual("Zone01_StarterTown", hero.map);
            Assert.AreEqual(-3f, hero.z);
            Assert.AreEqual("{\"pack\":[5,6]}", hero.Get("inventory"));
            Assert.AreEqual("{\"week\":3}", hero.Get("vendorWeek"), "found again under its own name");
            Assert.AreEqual("{\"creds\":7}", loaded.Find(Alt).Get("wallet"));
        }

        [Test]
        public void ASaveWritesOnlyWhatChanged()
        {
            AccountSave account = TwoCharacters();
            ServerSave.WriteAccount(account);
            int before = memory.Writes;
            Assert.IsFalse(ServerSave.WriteAccount(account), "nothing changed: nothing written");
            Assert.AreEqual(before, memory.Writes);

            account.Find(Hero).Set("wallet", "{\"creds\":510}");
            Assert.IsTrue(ServerSave.WriteAccount(account));
            Assert.AreEqual(before + 1, memory.Writes, "one part, and the header did not change");

            account.Find(Hero).level = 13;
            ServerSave.WriteAccount(account);
            Assert.AreEqual(before + 2, memory.Writes, "a level up rewrites only the small header");
        }

        [Test]
        public void APartThatIsGoneIsDeleted()
        {
            AccountSave account = TwoCharacters();
            ServerSave.WriteAccount(account);
            account.Find(Hero).Set("vendorWeek", null);
            ServerSave.WriteAccount(account);
            Assert.IsFalse(memory.TryRead("characters/" + Hero + "/parts/vendorweek", out _));
            Assert.IsTrue(memory.TryRead("characters/" + Hero + "/parts/wallet", out _), "the rest stays");
        }

        [Test]
        public void AnOldOnePieceAccountIsReadKeptAndSavedInParts()
        {
            AccountSave old = TwoCharacters();
            old.version = 1;
            string oneDocument = JsonUtility.ToJson(old);
            memory.Write("accounts/" + Account, oneDocument);

            AccountSave loaded = ServerSave.LoadAccount(Account);
            Assert.AreEqual("{\"pack\":[5,6]}", loaded.Find(Hero).Get("inventory"), "read as it is");
            Assert.IsTrue(memory.TryRead("accounts/" + Account + "-v1", out string kept), "kept once as it was");
            Assert.AreEqual(oneDocument, kept);

            Assert.IsTrue(ServerSave.WriteAccount(loaded), "the next save writes the new layout");
            Assert.IsTrue(memory.TryRead("accounts/" + Account, out string header));
            StringAssert.Contains("\"version\":2", header);
            Assert.IsTrue(memory.TryRead("characters/" + Hero + "/parts/inventory", out _));
            Assert.IsTrue(memory.TryRead("accounts/" + Account + "-v1", out string stillKept));
            Assert.AreEqual(oneDocument, stillKept, "the kept copy is never touched again");

            ServerSave.Store = memory;
            Assert.AreEqual("{\"creds\":500}", ServerSave.LoadAccount(Account).Find(Hero).Get("wallet"), "and it loads from the parts");
        }

        [Test]
        public void ListsSeeAccountsNotTheirParts()
        {
            ServerSave.WriteAccount(TwoCharacters());
            CollectionAssert.AreEqual(new[] { Account }, ServerSave.AccountIds());
            CollectionAssert.AreEqual(new[] { Account }, ServerSave.AccountsWithCharacter("tolan"));
        }
    }
}
