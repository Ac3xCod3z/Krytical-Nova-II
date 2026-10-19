using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Online phase 4, milestone 4: a world document of entries (Crews, Contacts, Kiosks, Sponsor links) is kept one entry
    /// at a time, so a change to one entry writes that entry alone.
    /// </summary>
    public sealed class WorldEntriesTests
    {
        private const string Name = "test-guilds";

        [Serializable]
        private sealed class Guild
        {
            public string id;
            public string name;
            public int members;
        }

        [Serializable]
        private sealed class Guilds : IWorldEntries
        {
            public List<Guild> guilds = new List<Guild>();

            public void CollectEntries(List<KeyValuePair<string, string>> into)
            {
                foreach (Guild g in guilds) into.Add(new KeyValuePair<string, string>(g.id, JsonUtility.ToJson(g)));
            }

            public void RestoreEntries(IReadOnlyList<string> texts)
            {
                guilds = new List<Guild>();
                foreach (string text in texts) guilds.Add(JsonUtility.FromJson<Guild>(text));
            }
        }

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

        private static Guild G(string id, string name, int members = 1) => new Guild { id = id, name = name, members = members };

        private static string Entry(string id) => ServerSave.WorldEntryKey(Name, id);

        [Test]
        public void AChangeToOneEntryWritesThatEntryAlone()
        {
            Guilds world = ServerSave.World<Guilds>(Name);
            world.guilds.Add(G("aa01", "Ironhand"));
            world.guilds.Add(G("bb02", "Night Market"));
            world.guilds.Add(G("cc03", "Glasswing"));
            ServerSave.SaveWorld(Name);
            Assert.IsTrue(memory.TryRead("world/" + Name, out string header));
            StringAssert.Contains("\"version\":2", header);
            Assert.AreEqual(3, memory.Keys("world/" + Name + "/").Count, "one document per entry");
            StringAssert.DoesNotContain("Ironhand", header, "the header holds no game state");

            int writes = memory.Writes, batches = memory.Batches;
            world.guilds[1].members = 7;
            ServerSave.SaveWorld(Name);
            Assert.AreEqual(writes + 1, memory.Writes, "only the changed entry was written (not the header, not the others)");
            Assert.AreEqual(batches + 1, memory.Batches);
            Assert.IsTrue(memory.TryRead(Entry("bb02"), out string changed));
            StringAssert.Contains("\"members\":7", changed);

            writes = memory.Writes;
            ServerSave.SaveWorld(Name);
            Assert.AreEqual(writes, memory.Writes, "nothing changed: nothing written");

            // A new entry: the entry and the header. An entry that went: deleted, and the header.
            world.guilds.Add(G("dd04", "Pale Choir"));
            world.guilds.RemoveAt(0);
            writes = memory.Writes;
            ServerSave.SaveWorld(Name);
            Assert.AreEqual(writes + 2, memory.Writes, "the new entry and the header");
            Assert.IsFalse(memory.TryRead(Entry("aa01"), out _), "the entry that went is deleted with the same change");
            Assert.IsTrue(memory.TryRead(Entry("dd04"), out _));
        }

        [Test]
        public void ARestartReadsTheEntriesBackInOrder()
        {
            Guilds world = ServerSave.World<Guilds>(Name);
            world.guilds.Add(G("zz09", "Last"));
            world.guilds.Add(G("aa01", "First", 3));
            ServerSave.SaveWorld(Name);

            var restarted = new MemorySaveStore();
            foreach (string key in memory.Keys(string.Empty)) { memory.TryRead(key, out string text); restarted.Write(key, text); }
            ServerSave.Store = restarted;
            Guilds again = ServerSave.World<Guilds>(Name);
            Assert.AreEqual(2, again.guilds.Count);
            Assert.AreEqual("Last", again.guilds[0].name, "the order is kept");
            Assert.AreEqual(3, again.guilds[1].members);

            int writes = restarted.Writes;
            ServerSave.SaveWorld(Name);
            Assert.AreEqual(writes, restarted.Writes, "what was read is not written again");
        }

        [Test]
        public void ADocumentSavedWholeIsReadKeptOnceAndSavedInEntries()
        {
            // As every server saved it before milestone 4.
            var whole = new Guilds();
            whole.guilds.Add(G("aa01", "Ironhand", 4));
            whole.guilds.Add(G("bb02", "Night Market"));
            string old = JsonUtility.ToJson(whole);
            memory.Write("world/" + Name, old);

            Guilds world = ServerSave.World<Guilds>(Name);
            Assert.AreEqual(2, world.guilds.Count, "read as it is");
            Assert.AreEqual(4, world.guilds[0].members);

            ServerSave.SaveWorld(Name);
            Assert.IsTrue(memory.TryRead("world/" + Name + "-v1", out string kept));
            Assert.AreEqual(old, kept, "the whole document is kept once, as it was");
            Assert.IsTrue(memory.TryRead(Entry("aa01"), out _));
            Assert.IsTrue(memory.TryRead("world/" + Name, out string header));
            StringAssert.Contains("\"version\":2", header);

            var restarted = new MemorySaveStore();
            foreach (string key in memory.Keys(string.Empty)) { memory.TryRead(key, out string text); restarted.Write(key, text); }
            ServerSave.Store = restarted;
            Assert.AreEqual("Night Market", ServerSave.World<Guilds>(Name).guilds[1].name, "and read in entries after");
        }

        [Test]
        public void KeysAreMadeSafeAndNeverShared()
        {
            Guilds world = ServerSave.World<Guilds>(Name);
            world.guilds.Add(G("Some Crew/Name", "A"));
            world.guilds.Add(G("some crew/name", "B"));
            world.guilds.Add(G("", "C"));
            ServerSave.SaveWorld(Name);
            IReadOnlyList<string> keys = memory.Keys("world/" + Name + "/");
            Assert.AreEqual(3, keys.Count, "three entries, three documents");
            foreach (string key in keys) Assert.IsTrue(SaveKeys.IsValid(key), key);

            var restarted = new MemorySaveStore();
            foreach (string key in memory.Keys(string.Empty)) { memory.TryRead(key, out string text); restarted.Write(key, text); }
            ServerSave.Store = restarted;
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, ServerSave.World<Guilds>(Name).guilds.ConvertAll(g => g.name));
        }

        [Test]
        public void APlayerAndAnEntryAreSavedAsOneChange()
        {
            const string account = "0123456789abcdef0123456789abcdef";
            AccountSave save = ServerSave.LoadAccount(account);
            var character = new CharacterSave { characterId = "11111111111111111111111111111111", name = "Tolan" };
            character.Set("wallet", "{\"creds\":100}");
            save.characters.Add(character);
            Guilds world = ServerSave.World<Guilds>(Name);
            world.guilds.Add(G("aa01", "Ironhand"));
            ServerSave.WriteTogether(new[] { save }, Name);

            int batches = memory.Batches;
            character.Set("wallet", "{\"creds\":0}");
            world.guilds[0].members = 2;
            Assert.IsTrue(ServerSave.WriteTogether(new[] { save }, Name));
            Assert.AreEqual(batches + 1, memory.Batches, "the founder's Creds and the Crew in one change");
        }
    }
}
