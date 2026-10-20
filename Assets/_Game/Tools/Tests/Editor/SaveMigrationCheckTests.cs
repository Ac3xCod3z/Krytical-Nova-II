using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.EditorTools;
using KryticalNova.Persistence;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Save migrations, before a release (online phase 4): Check Saves Against This Build runs a copy of saves through
    /// every step and names what would not load, and the parts no system saves any more.
    /// </summary>
    public sealed class SaveMigrationCheckTests
    {
        private sealed class Purse : IMigratingSave
        {
            public int SaveVersion => 3;
            public string Migrate(string json, int from) => json;
        }

        private sealed class Broken : IMigratingSave
        {
            public int SaveVersion => 2;
            public string Migrate(string json, int from) => throw new FormatException("bad data");
        }

        [Test]
        public void TheCheckSaysWhatWouldNotLoadBeforeARelease()
        {
            var memory = new MemorySaveStore();
            var account = new AccountSave { accountId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" };
            var character = new CharacterSave { characterId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", name = "Tolan" };
            character.Set("purse", "{\"gold\":300}");
            character.Set("broken", "{\"a\":1}");
            character.Set("gone", "{\"x\":1}");
            character.Set("plain", "{\"y\":1}");
            account.characters.Add(character);
            foreach (KeyValuePair<string, string> doc in ServerSave.SplitDocuments(account)) memory.Write(doc.Key, doc.Value);

            var parts = new Dictionary<string, IMigratingSave> { ["purse"] = new Purse(), ["broken"] = new Broken(), ["plain"] = null };
            string report = SaveMigrationCheck.Check(memory, parts, out int failed, out int upgraded);
            Assert.AreEqual(1, failed, report);
            Assert.AreEqual(1, upgraded, report);
            StringAssert.Contains("purse 1 to 3: 1 part(s)", report);
            StringAssert.Contains("Tolan", report);
            StringAssert.Contains("gone: 1 part(s)", report, "a part no system saves any more is named");
        }
    }
}
