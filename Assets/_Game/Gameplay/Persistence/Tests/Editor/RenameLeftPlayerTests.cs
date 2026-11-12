using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Note (a), lead and owner 12 Nov 2026: a player who leaves while the rename's save is out is never undone. A refused answer
    /// finishes the rename forward (the names list keeps the new name, one names save, one name.change ledger line); a written
    /// answer needs nothing more; no answer yet stays kept.
    /// </summary>
    public sealed class RenameLeftPlayerTests
    {
        const string Account = "acct", Character = "char-me";
        NameIndex m_Names;
        int m_Saves;
        List<LedgerEntry> m_Lines;

        [SetUp]
        public void SetUp()
        {
            Drain();   // nothing left from another test
            m_Names = new NameIndex();
            m_Names.Put("Newname", Account, Character);   // as the rename left it in memory
            m_Saves = 0;
            m_Lines = new List<LedgerEntry>();
        }

        int Drain(System.Func<LedgerEntry, bool> waiting = null) => PlayerRename.FinishLeftRenames(() => m_Names, () => m_Saves++, line => m_Lines.Add(line), waiting);

        [Test]
        public void LeftThenRefused_FinishesTheRenameForward()
        {
            var left = new PlayerRename.LeftRename("Oldname", "Newname", Account, Character);
            PlayerRename.KeepLeftRename(left);
            left.Tell(false);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("finished forward"));
            Assert.AreEqual(1, Drain());
            Assert.AreEqual(Character, m_Names.Holder("Newname")?.character, "the names list keeps the new name");
            Assert.IsNull(m_Names.Holder("Oldname"));
            Assert.AreEqual(1, m_Saves, "one names save");
            Assert.AreEqual(1, m_Lines.Count, "one ledger line");
            Assert.AreEqual(LedgerKinds.NameChanged, m_Lines[0].kind);
            Assert.AreEqual(0, PlayerRename.LeftRenamesWaiting);
            Assert.AreEqual(0, Drain(), "done once");
            Assert.AreEqual(1, m_Saves);
        }

        // A2: the refused change's line is on its way again with its account: no second line.
        [Test]
        public void LeftThenRefused_WithItsLineOnItsWayAgain_WritesNoSecondLine()
        {
            LedgerEntry first = LedgerEntry.For(LedgerKinds.NameChanged, Account, Character, "Oldname > Newname");
            var left = new PlayerRename.LeftRename("Oldname", "Newname", Account, Character, first);
            PlayerRename.KeepLeftRename(left);
            left.Tell(false);
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("finished forward"));
            Assert.AreEqual(1, Drain(line => ReferenceEquals(line, first)));
            Assert.AreEqual(0, m_Lines.Count, "the first line goes again; none is added");
            Assert.AreEqual(1, m_Saves);
        }

        [Test]
        public void LeftThenWritten_DoesNothingMore()
        {
            var left = new PlayerRename.LeftRename("Oldname", "Newname", Account, Character);
            PlayerRename.KeepLeftRename(left);
            left.Tell(true);
            Assert.AreEqual(0, Drain());
            Assert.AreEqual(0, m_Saves);
            Assert.AreEqual(0, m_Lines.Count);
            Assert.AreEqual(Character, m_Names.Holder("Newname")?.character);
            Assert.AreEqual(0, PlayerRename.LeftRenamesWaiting);
        }

        [Test]
        public void LeftWithNoAnswerYet_StaysKept()
        {
            var left = new PlayerRename.LeftRename("Oldname", "Newname", Account, Character);
            PlayerRename.KeepLeftRename(left);
            Assert.AreEqual(0, Drain());
            Assert.AreEqual(1, PlayerRename.LeftRenamesWaiting);
            left.Tell(true);
            Drain();
            Assert.AreEqual(0, PlayerRename.LeftRenamesWaiting);
        }
    }
}
