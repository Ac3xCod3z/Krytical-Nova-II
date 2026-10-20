using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Online phase 4: save migrations. A document whose shape changed carries its version as a first field "_v" (none
    /// for version 1), is stepped up one tested step at a time when it loads, and is refused, never reset, when a step
    /// fails or it was saved by a newer build. A copy of saves is checked against this build before a release.
    /// </summary>
    public sealed class SaveMigrationTests
    {
        // Version 1 kept "gold"; version 2 calls it "creds"; version 3 counts creds in hundreds ("hundreds").
        private sealed class Purse : IMigratingSave
        {
            public int SaveVersion => 3;
            public string Migrate(string json, int from)
            {
                if (from == 1) return json.Replace("\"gold\"", "\"creds\"");
                if (from == 2)
                {
                    var two = JsonUtility.FromJson<PurseV2>(json);
                    return JsonUtility.ToJson(new PurseV3 { hundreds = two.creds / 100 });
                }
                throw new InvalidOperationException("no step from " + from);
            }
        }

        [Serializable] private sealed class PurseV2 { public long creds; }
        [Serializable] private sealed class PurseV3 { public long hundreds; }

        private sealed class Broken : IMigratingSave
        {
            public int SaveVersion => 2;
            public string Migrate(string json, int from) => throw new FormatException("bad data");
        }

        [Test]
        public void TheVersionRidesInTheTextAndOnlyFromTwoOn()
        {
            Assert.AreEqual(1, SaveVersions.Of("{\"gold\":5}"), "a text without a stamp is version 1");
            Assert.AreEqual("{\"gold\":5}", SaveVersions.With("{\"gold\":5}", 1), "version 1 carries no stamp");
            string two = SaveVersions.With("{\"gold\":5}", 2);
            Assert.AreEqual("{\"_v\":2,\"gold\":5}", two);
            Assert.AreEqual(2, SaveVersions.Of(two));
            Assert.AreEqual("{\"gold\":5}", SaveVersions.Without(two));
            Assert.AreEqual("{\"_v\":3,\"gold\":5}", SaveVersions.With(two, 3), "a new stamp replaces the old one");
            Assert.AreEqual("{\"_v\":2}", SaveVersions.With("{}", 2));
            Assert.AreEqual("{}", SaveVersions.Without("{\"_v\":2}"));
            Assert.AreEqual(7, JsonUtility.FromJson<PurseV2>(SaveVersions.With("{\"creds\":7}", 4)).creds, "a reader skips the stamp");
        }

        [Test]
        public void AnOldSaveIsSteppedUpOneStepAtATime()
        {
            string upgraded = SaveVersions.Upgrade("{\"gold\":1250}", new Purse());
            Assert.AreEqual(3, SaveVersions.Of(upgraded));
            Assert.AreEqual(12, JsonUtility.FromJson<PurseV3>(upgraded).hundreds);
            Assert.AreEqual(upgraded, SaveVersions.Upgrade(upgraded, new Purse()), "a current save is left as it is");
            Assert.IsNull(SaveVersions.Upgrade(null, new Purse()), "nothing saved stays nothing");
        }

        [Test]
        public void ASaveFromANewerBuildOrAFailingStepIsRefusedNotReset()
        {
            Assert.Throws<InvalidOperationException>(() => SaveVersions.Upgrade("{\"_v\":9,\"hundreds\":1}", new Purse()));
            Assert.Throws<FormatException>(() => SaveVersions.Upgrade("{\"a\":1}", new Broken()));
        }

        // A world document of entries whose entries changed shape.
        [Serializable]
        private sealed class Banks : IWorldEntries, IMigratingSave
        {
            [Serializable] public sealed class Bank { public string id; public long creds; }
            public List<Bank> banks = new List<Bank>();
            public int SaveVersion => 2;
            public string Migrate(string json, int from) => json.Replace("\"gold\"", "\"creds\"");
            public void CollectEntries(List<KeyValuePair<string, string>> into)
            {
                foreach (Bank b in banks) into.Add(new KeyValuePair<string, string>(b.id, JsonUtility.ToJson(b)));
            }
            public void RestoreEntries(IReadOnlyList<string> texts)
            {
                banks = new List<Bank>();
                foreach (string text in texts) banks.Add(JsonUtility.FromJson<Bank>(text));
            }
        }

        [Test]
        public void WorldEntriesAreSteppedUpOnReadingAndStampedOnSaving()
        {
            ISaveStore previous = ServerSave.Store;
            var memory = new MemorySaveStore();
            try
            {
                ServerSave.Store = memory;
                memory.Write("world/test-banks", "{\"version\":2,\"keys\":[\"north\"]}");
                memory.Write(ServerSave.WorldEntryKey("test-banks", "north"), "{\"id\":\"north\",\"gold\":40}");
                Banks banks = ServerSave.World<Banks>("test-banks");
                Assert.AreEqual(40, banks.banks[0].creds, "the old entry was stepped up before it was read");
                ServerSave.SaveWorld("test-banks");
                Assert.IsTrue(memory.TryRead(ServerSave.WorldEntryKey("test-banks", "north"), out string saved));
                Assert.AreEqual(2, SaveVersions.Of(saved), "saved again with its version");
            }
            finally
            {
                ServerSave.Store = previous;
            }
        }
    }
}
