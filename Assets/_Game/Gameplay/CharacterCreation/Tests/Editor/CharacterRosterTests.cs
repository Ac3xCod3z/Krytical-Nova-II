using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    public sealed class CharacterRosterTests
    {
        private const string StoreProfile = "unittest";

        private static CharacterRecipe Recipe() => new CharacterRecipe
        {
            version = CharacterRecipe.CurrentVersion,
            faction = FactionId.Covenant,
            lineage = 5,
            bodyType = BodyType.Feminine,
            build = -20,
            muscle = 35,
            parts = new ushort[] { 11, 22, 33 },
            palette = 4,
            skinTone = 3,
            colors = new[] { new ColorOverride(1, new Color32(200, 120, 40, 255)) }
        };

        [TearDown]
        public void TearDown() => new PlayerPrefsCharacterStore(StoreProfile).DeleteAll();

        [Test]
        public void NewRosterIsEmpty()
        {
            var roster = new CharacterRoster();
            Assert.AreEqual(0, roster.Count);
            Assert.AreEqual(0, roster.FirstFree);
            Assert.AreEqual(-1, roster.PreferredSlot);
        }

        [Test]
        public void CharacterIsSavedIntoTheSlotThatWasChosen()
        {
            var roster = new CharacterRoster();
            Assert.IsTrue(roster.Set(2, CharacterProfile.Create("Astrid", Recipe())));
            Assert.IsFalse(roster.IsUsed(0));
            Assert.IsFalse(roster.IsUsed(1));
            Assert.IsTrue(roster.IsUsed(2));
            Assert.AreEqual(0, roster.FirstFree);
            Assert.AreEqual(2, roster.PreferredSlot);
        }

        [Test]
        public void LastPlayedSlotIsPreferredAndForgottenWhenDeleted()
        {
            var roster = new CharacterRoster();
            roster.Set(0, CharacterProfile.Create("Vance", Recipe()));
            roster.Set(1, CharacterProfile.Create("Mira", Recipe()));
            roster.MarkPlayed(1);
            Assert.AreEqual(1, roster.PreferredSlot);
            roster.Clear(1);
            Assert.AreEqual(0, roster.PreferredSlot);
            Assert.AreEqual(1, roster.FirstFree);
        }

        [Test]
        public void NameCheckCoversOnlyThePlayersOwnOtherSlots()
        {
            var roster = new CharacterRoster();
            roster.Set(0, CharacterProfile.Create("Vance", Recipe()));
            Assert.IsTrue(roster.IsNameTaken("vance"));
            Assert.IsFalse(roster.IsNameTaken("Vance", exceptSlot: 0));
            Assert.IsFalse(roster.IsNameTaken("Mira"));
        }

        [Test]
        public void TwoCharactersWithTheSameNameStillHaveDifferentIds()
        {
            var a = CharacterProfile.Create("Vance", Recipe());
            var b = CharacterProfile.Create("Vance", Recipe());
            Assert.AreNotEqual(a.id, b.id);
            Assert.AreEqual(4, a.Tag.Length);
        }

        [Test]
        public void StoreRoundTripKeepsSlotsRecipesAndLastPlayed()
        {
            var store = new PlayerPrefsCharacterStore(StoreProfile);
            var roster = new CharacterRoster();
            var recipe = Recipe();
            roster.Set(2, CharacterProfile.Create("Astrid", recipe));
            roster.MarkPlayed(2);
            store.Save(roster);

            var loaded = store.Load();
            Assert.AreEqual(1, loaded.Count);
            Assert.IsFalse(loaded.IsUsed(0));
            Assert.IsTrue(loaded.IsUsed(2));
            Assert.AreEqual(2, loaded.lastPlayedSlot);
            Assert.AreEqual("Astrid", loaded.Get(2).name);
            Assert.AreEqual(roster.Get(2).id, loaded.Get(2).id);
            Assert.IsTrue(recipe.Equals(loaded.Get(2).recipe));
        }

        [Test]
        public void MissingSaveLoadsAsAnEmptyRoster()
        {
            var store = new PlayerPrefsCharacterStore(StoreProfile);
            store.DeleteAll();
            Assert.AreEqual(0, store.Load().Count);
        }

        [Test]
        public void NetworkFormRoundTripsARecipe()
        {
            var recipe = Recipe();
            var state = CharacterNetState.From("Astrid", recipe);
            Assert.AreEqual("Astrid", state.name.ToString());
            Assert.IsTrue(recipe.Equals(state.ToRecipe()));
        }

        [Test]
        public void NetworkFormSurvivesTheWire()
        {
            var state = CharacterNetState.From("Astrid", Recipe());
            using (var writer = new Unity.Netcode.FastBufferWriter(1024, Unity.Collections.Allocator.Temp))
            {
                writer.WriteNetworkSerializable(state);
                using (var reader = new Unity.Netcode.FastBufferReader(writer, Unity.Collections.Allocator.Temp))
                {
                    reader.ReadNetworkSerializable(out CharacterNetState received);
                    Assert.IsTrue(state.Equals(received));
                    Assert.IsTrue(Recipe().Equals(received.ToRecipe()));
                }
            }
        }
    }
}
