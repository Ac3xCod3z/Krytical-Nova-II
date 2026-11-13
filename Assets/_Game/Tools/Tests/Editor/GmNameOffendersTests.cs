using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.EditorTools;
using KryticalNova.Persistence;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>Build 17, item 1: the read-only list of saved characters whose names break the "no GM" rule.</summary>
    public sealed class GmNameOffendersTests
    {
        [Test]
        public void TheListNamesOnlyTheCharactersThatBreakTheRule()
        {
            var memory = new MemorySaveStore();
            var account = new AccountSave { accountId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" };
            account.characters.Add(new CharacterSave { characterId = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", name = "Tolan" });
            account.characters.Add(new CharacterSave { characterId = "cccccccccccccccccccccccccccccccc", name = "Gmbob" });
            account.characters.Add(new CharacterSave { characterId = "dddddddddddddddddddddddddddddddd", name = "Magma" });
            foreach (KeyValuePair<string, string> doc in ServerSave.SplitDocuments(account)) memory.Write(doc.Key, doc.Value);

            List<string> found = GmNameOffenders.Find(memory);
            Assert.AreEqual(2, found.Count, string.Join("\n", found));
            StringAssert.StartsWith("Gmbob (", found[0]);
            StringAssert.StartsWith("Magma (", found[1]);
            StringAssert.Contains("2 saved character(s)", GmNameOffenders.Report(found));
        }
    }
}
