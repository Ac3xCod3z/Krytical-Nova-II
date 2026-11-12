using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Persistence;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Wave 2, 12 Nov 2026: the Name Change Pass's server checks, as NGO's PlayerRename decides them, now also used by the entity
    /// server (RenameSystems). The name: the rules, not one's own name, not a name another character holds (capitals ignored).
    /// The Pack cell: checked by the server at the take; only the cell index and the wished name come from the ask (lead's condition 2).
    /// </summary>
    public sealed class RenameRulesTests
    {
        const string Me = "char-me", Other = "char-other", Account = "acct";
        ItemDefinition m_Pass, m_Stim;

        [SetUp]
        public void SetUp()
        {
            m_Pass = ScriptableObject.CreateInstance<ItemDefinition>();
            m_Pass.useEffect = ItemUseEffect.NameChange;
            m_Pass.displayName = "Name Change Pass";
            m_Stim = ScriptableObject.CreateInstance<ItemDefinition>();
            m_Stim.useEffect = ItemUseEffect.None;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Pass);
            Object.DestroyImmediate(m_Stim);
        }

        static NameIndex Names()
        {
            var names = new NameIndex();
            names.Put("Ace", Account, Me);
            names.Put("Tolan", "acct2", Other);
            return names;
        }

        [Test]
        public void AFreeName_IsTaken_InItsKeptForm()
        {
            Assert.IsNull(PlayerRename.CheckName("  zephyr ", "Ace", Me, Names(), out string clean));
            Assert.AreEqual("Zephyr", clean, "trimmed and capitalised as names are shown");
        }

        [Test]
        public void TheRulesRefuse_WithTheirOwnTexts()
        {
            NameIndex names = Names();
            Assert.AreEqual(CharacterNameRules.ReasonEmpty, PlayerRename.CheckName("", "Ace", Me, names, out _));
            Assert.AreEqual(CharacterNameRules.ReasonLettersOnly, PlayerRename.CheckName("Ab cd", "Ace", Me, names, out _));
            Assert.AreEqual(CharacterNameRules.ReasonLetterFirst, PlayerRename.CheckName("9abc", "Ace", Me, names, out _));
            Assert.AreEqual(CharacterNameRules.ReasonTooShort, PlayerRename.CheckName("Ab", "Ace", Me, names, out _));
            Assert.AreEqual(CharacterNameRules.ReasonTooLong, PlayerRename.CheckName("Abcdefghijklmnopq", "Ace", Me, names, out _));
            Assert.AreEqual(CharacterNameRules.ReasonRepeats, PlayerRename.CheckName("Aaab", "Ace", Me, names, out _));
            Assert.AreEqual(CharacterNameRules.ReasonReserved, PlayerRename.CheckName("Admin", "Ace", Me, names, out _));
        }

        [Test]
        public void TheSameName_IsRefused()
        {
            Assert.AreEqual(PlayerRename.ReasonSame, PlayerRename.CheckName("ace", "Ace", Me, Names(), out _), "normalised first: ace is Ace");
        }

        [Test]
        public void ANameAnotherCharacterHolds_IsRefused_CapitalsIgnored()
        {
            Assert.AreEqual(CharacterNameRules.ReasonTakenInGame, PlayerRename.CheckName("TOLAN", "Ace", Me, Names(), out _));
        }

        [Test]
        public void OnlyTheCapitalsOfOnesOwnName_MayChange()
        {
            var names = new NameIndex();
            names.Put("Acex", Account, Me);
            Assert.IsNull(PlayerRename.CheckName("acex", "AceX", Me, names, out string clean), "the holder is this character");
            Assert.AreEqual("Acex", clean);
        }

        [Test]
        public void ThePassCell_MustHoldANameChangeItem_AtTheTake()
        {
            var pass = new ItemStack(9036, 1);
            Assert.IsNull(PlayerRename.CheckPassCell(3, pass, m_Pass), "a pass in the cell");
            Assert.AreEqual(PlayerRename.ReasonNoPass, PlayerRename.CheckPassCell(3, new ItemStack(9001, 2), m_Stim), "a cell with another item");
            Assert.AreEqual(PlayerRename.ReasonNoPass, PlayerRename.CheckPassCell(3, ItemStack.Empty, null), "an empty cell");
            Assert.AreEqual(PlayerRename.ReasonNoPass, PlayerRename.CheckPassCell(3, ItemStack.Empty, m_Pass), "an empty cell, whatever the catalog says");
            Assert.AreEqual(PlayerRename.ReasonNoPass, PlayerRename.CheckPassCell(3, pass, null), "an item the catalog does not know");
        }

        [Test]
        public void ACellOutsideThePack_IsRefused()
        {
            var pass = new ItemStack(9036, 1);
            Assert.AreEqual(PlayerRename.ReasonNoPass, PlayerRename.CheckPassCell(-1, pass, m_Pass));
            Assert.AreEqual(PlayerRename.ReasonNoPass, PlayerRename.CheckPassCell(PlayerInventory.Capacity, pass, m_Pass));
            Assert.AreEqual(PlayerRename.ReasonNoPass, PlayerRename.CheckPassCell(byte.MaxValue + 1, pass, m_Pass));
        }

        [Test]
        public void TheNamesList_LosesTheOldName_AndGetsTheNew()
        {
            NameIndex names = Names();
            names.Drop("Ace", Account, Me);
            names.Put("Zephyr", Account, Me);
            Assert.IsNull(names.Holder("ace"), "the old name is free again");
            Assert.AreEqual(Me, names.Holder("zephyr").character);
            Assert.AreEqual(Other, names.Holder("Tolan").character, "nobody else's name moves");
        }

        [Test]
        public void TheServersAnswer_ReachesTheDialog()
        {
            string renamed = null, refused = null;
            System.Action<string> onRenamed = n => renamed = n, onRefused = r => refused = r;
            PlayerRename.Renamed += onRenamed;
            PlayerRename.Refused += onRefused;
            try
            {
                PlayerRename.ApplyServerAnswer(true, "Zephyr");
                Assert.AreEqual("Zephyr", renamed);
                PlayerRename.ApplyServerAnswer(false, CharacterNameRules.ReasonTakenInGame);
                Assert.AreEqual(CharacterNameRules.ReasonTakenInGame, refused);
                PlayerRename.ApplyServerAnswer(false, "");
                Assert.AreEqual(PlayerRename.ReasonNotReady, refused, "an empty refusal still says something");
            }
            finally
            {
                PlayerRename.Renamed -= onRenamed;
                PlayerRename.Refused -= onRefused;
            }
        }
    }
}
