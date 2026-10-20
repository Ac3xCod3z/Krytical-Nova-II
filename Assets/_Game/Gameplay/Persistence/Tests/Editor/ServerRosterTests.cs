using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Networking;
using KryticalNova.Persistence;
using KryticalNova.Player;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Online phase 4, milestone 5: the roster on the server. Characters are listed, created, deleted and renamed by
    /// asking the server, names are unique across the game (capitals ignored), and where two characters already shared a
    /// name a GM's keeps it (the GM Tolan stays Tolan), then the higher level.
    /// </summary>
    public sealed class ServerRosterTests
    {
        private const string DatabasePath = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterCreationDatabase.asset";
        private const string Ash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        private const string Bo = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        private const string Cy = "cccccccccccccccccccccccccccccccc";

        private ISaveStore previousStore;
        private MemorySaveStore memory;
        private CharacterCreationDatabase database;

        [SetUp]
        public void SetUp()
        {
            previousStore = ServerSave.Store;
            memory = new MemorySaveStore();
            ServerSave.Store = memory;
            database = AssetDatabase.LoadAssetAtPath<CharacterCreationDatabase>(DatabasePath);
            Assert.IsNotNull(database, DatabasePath);
            ServerRoster.Database = database;
            GmRoster.SetAll(new GmEntry[0]);
        }

        [TearDown]
        public void TearDown()
        {
            ServerRoster.Database = null;
            GmRoster.SetAll(new GmEntry[0]);
            ServerSave.Store = previousStore;
        }

        private CharacterRecipe Look(FactionId faction = FactionId.Hegemony)
        {
            var lineages = new List<LineageDefinition>();
            database.GetLineages(faction, lineages);
            Assert.IsNotEmpty(lineages);
            return RecipeEdit.Default(lineages[0], BodyType.Feminine);
        }

        private RosterReply Create(string account, string name, int slot = -1) =>
            ServerRoster.Handle(account, "", new RosterRequest { op = RosterRequest.Create, name = name, recipe = Look(), slot = slot });

        private static RosterReply List(string account) => ServerRoster.Handle(account, "", new RosterRequest { op = RosterRequest.List });

        [Test]
        public void ACharacterMadeOnTheServerIsListedWithItsLook()
        {
            RosterReply made = Create(Ash, "tolan", 1);
            Assert.IsTrue(made.ok, made.message);
            Assert.IsTrue(SaveKeys.IsId(made.characterId));

            RosterReply listed = List(Ash);
            Assert.IsTrue(listed.ok, listed.message);
            Assert.AreEqual(1, listed.characters.Count);
            RosterCharacter tolan = listed.characters[0];
            Assert.AreEqual("Tolan", tolan.name, "names are kept the way they are shown");
            Assert.AreEqual(1, tolan.slot, "the slot asked for");
            Assert.AreEqual(made.characterId, tolan.id);
            Assert.IsTrue(tolan.recipe.Equals(Look()), "the look the server keeps is the one it was made with");

            // The character it made plays: its saved header and look are in the store.
            AccountSave saved = ServerSave.ReadAccountFrom(memory, Ash);
            Assert.IsNull(PlayerSaveAgent.NotOnRoster(saved.Find(made.characterId), "Tolan"));
            Assert.IsNotNull(PlayerSaveAgent.NotOnRoster(saved.Find(made.characterId), "Someone"), "under its own name only");
            Assert.IsNotNull(PlayerSaveAgent.NotOnRoster(null, "Tolan"), "a character the server did not make does not play");
        }

        [Test]
        public void ANameIsTakenOnceInTheWholeGameWhateverItsCapitals()
        {
            Assert.IsTrue(Create(Ash, "Tolan").ok);
            RosterReply second = Create(Bo, "TOLAN");
            Assert.IsFalse(second.ok);
            Assert.AreEqual(CharacterNameRules.ReasonTakenInGame, second.message);
            Assert.IsFalse(Create(Ash, "tolan").ok, "not twice on one account either");
            Assert.IsTrue(Create(Bo, "Kael").ok);
        }

        [Test]
        public void NamesAreCheckedByTheRulesAndSlotsRunOut()
        {
            Assert.AreEqual(CharacterNameRules.ReasonLettersOnly, Create(Ash, "To lan").message);
            Assert.AreEqual(CharacterNameRules.ReasonTooShort, Create(Ash, "Al").message);
            Assert.IsTrue(Create(Ash, "Arin").ok);
            Assert.IsTrue(Create(Ash, "Brin").ok);
            Assert.IsTrue(Create(Ash, "Corin").ok);
            RosterReply full = Create(Ash, "Dorin");
            Assert.IsFalse(full.ok);
            Assert.AreEqual(3, full.characters.Count, "a refusal still carries the list");

            var bad = Look();
            bad.lineage = 250;
            RosterReply refused = ServerRoster.Handle(Bo, "", new RosterRequest { op = RosterRequest.Create, name = "Ezra", recipe = bad });
            Assert.IsFalse(refused.ok, "a look the rules reject is not made");
        }

        [Test]
        public void DeletingFreesTheNameAndKeepsALedgerLine()
        {
            RosterReply made = Create(Ash, "Tolan");
            RosterReply deleted = ServerRoster.Handle(Ash, "", new RosterRequest { op = RosterRequest.Delete, characterId = made.characterId });
            Assert.IsTrue(deleted.ok, deleted.message);
            Assert.AreEqual(0, deleted.characters.Count);
            Assert.AreEqual(0, ServerSave.ReadAccountFrom(memory, Ash).characters.Count);
            Assert.IsTrue(Create(Bo, "Tolan").ok, "the name is free again");

            List<LedgerEntry> lines = memory.ReadLedger(Ash, 0, 10);
            Assert.AreEqual(1, lines.Count);
            Assert.AreEqual(LedgerKinds.CharacterDeleted, lines[0].kind);
            Assert.AreEqual("Tolan", lines[0].name);

            RosterReply other = ServerRoster.Handle(Bo, "", new RosterRequest { op = RosterRequest.Delete, characterId = made.characterId });
            Assert.IsFalse(other.ok, "another account's character is not found");
        }

        // Accounts saved before the server kept the roster, sharing names as they could then.
        private void Saved(string account, params (string name, int level, long savedUtc)[] characters)
        {
            var save = new AccountSave { accountId = account };
            foreach (var c in characters)
            {
                var character = new CharacterSave { characterId = System.Guid.NewGuid().ToString("N"), name = c.name, level = c.level, savedUtc = c.savedUtc };
                character.Set(PlayerIdentity.LookSaveKey, PlayerIdentity.LookJson(Look()));
                save.characters.Add(character);
            }
            foreach (KeyValuePair<string, string> doc in ServerSave.SplitDocuments(save)) memory.Write(doc.Key, doc.Value);
        }

        [Test]
        public void WhereNamesWereSharedAGmKeepsTheNameThenTheHigherLevel()
        {
            Saved(Ash, ("Tolan", 5, 300));          // the GM's: keeps Tolan even at a lower level
            Saved(Bo, ("Tolan", 40, 100), ("Kael", 10, 100));
            Saved(Cy, ("kael", 12, 200), ("Rhea", 1, 100));
            GmRoster.SetAll(new[] { new GmEntry { name = "TestGM", account = Ash } });

            RosterReply bo = List(Bo);
            Assert.IsTrue(bo.characters.Find(c => c.name == "Tolan").mustRename, "the GM's Tolan stays Tolan");
            Assert.IsTrue(bo.characters.Find(c => c.name == "Kael").mustRename, "the higher level keeps Kael");
            Assert.IsFalse(List(Ash).characters[0].mustRename);
            RosterReply cy = List(Cy);
            Assert.IsFalse(cy.characters.Find(c => c.name == "kael").mustRename);
            Assert.IsFalse(cy.characters.Find(c => c.name == "Rhea").mustRename);

            // A character to rename does not play until it is renamed, and only to a free name.
            RosterCharacter boTolan = bo.characters.Find(c => c.name == "Tolan");
            Assert.AreEqual(CharacterNameRules.ReasonMustRename,
                PlayerSaveAgent.NotOnRoster(ServerSave.ReadAccountFrom(memory, Bo).Find(boTolan.id), "Tolan"));
            RosterReply taken = ServerRoster.Handle(Bo, "", new RosterRequest { op = RosterRequest.Rename, characterId = boTolan.id, name = "Rhea" });
            Assert.IsFalse(taken.ok);
            RosterReply renamed = ServerRoster.Handle(Bo, "", new RosterRequest { op = RosterRequest.Rename, characterId = boTolan.id, name = "Torvald" });
            Assert.IsTrue(renamed.ok, renamed.message);
            RosterCharacter torvald = renamed.characters.Find(c => c.id == boTolan.id);
            Assert.AreEqual("Torvald", torvald.name);
            Assert.IsFalse(torvald.mustRename);
            Assert.IsNull(PlayerSaveAgent.NotOnRoster(ServerSave.ReadAccountFrom(memory, Bo).Find(boTolan.id), "Torvald"));

            // A character that keeps its name is not renamed here.
            RosterReply kept = ServerRoster.Handle(Ash, "", new RosterRequest { op = RosterRequest.Rename, characterId = List(Ash).characters[0].id, name = "Other" });
            Assert.IsFalse(kept.ok);
            Assert.IsFalse(Create(Cy, "Torvald").ok, "the new name is taken now");
        }

        [Test]
        public void TheNamesAreGatheredOnceAndKeptOneNameAnEntry()
        {
            Saved(Ash, ("Tolan", 5, 300));
            List(Ash);
            Assert.IsTrue(memory.TryRead(ServerSave.WorldEntryKey(ServerRoster.NamesDocument, "tolan"), out string entry));
            StringAssert.Contains(Ash, entry);
            Saved(Bo, ("Tolan", 50, 100)); // saved behind the server's back: not gathered again, the name stays Ash's
            Assert.IsFalse(Create(Cy, "Tolan").ok);
        }

        [Test]
        public void ALookThisPcKeptIsTakenOnlyForACharacterWithoutOne()
        {
            var save = new AccountSave { accountId = Ash };
            var old = new CharacterSave { characterId = System.Guid.NewGuid().ToString("N"), name = "Tolan", level = 3, faction = (int)FactionId.Hegemony };
            save.characters.Add(old);
            foreach (KeyValuePair<string, string> doc in ServerSave.SplitDocuments(save)) memory.Write(doc.Key, doc.Value);

            var request = new RosterRequest { op = RosterRequest.List };
            request.looks.Add(new RosterLook { id = old.characterId, recipe = Look() });
            RosterReply listed = ServerRoster.Handle(Ash, "", request);
            Assert.IsTrue(listed.characters[0].recipe.Equals(Look()));

            var other = Look(FactionId.Covenant);
            request.looks[0].recipe = other;
            Assert.IsTrue(ServerRoster.Handle(Ash, "", request).characters[0].recipe.Equals(Look()), "a kept look is never replaced");
        }

        [Test]
        public void ARosterAnswerFitsTheMessageItTravelsIn()
        {
            Create(Ash, "Arin");
            Create(Ash, "Brin");
            Create(Ash, "Corin");
            string reply = RosterWire.Reply(List(Ash).ToJson());
            Assert.Less(reply.Length, RosterWire.MaxReplyChars, "three characters with their looks");
            Assert.IsTrue(RosterWire.TryReadReply(reply, out string back));
            Assert.AreEqual(3, RosterReply.FromJson(back).characters.Count);

            var create = new RosterRequest { op = RosterRequest.Create, name = "Dorin", recipe = Look() };
            Assert.Less(RosterWire.Pack(create.ToJson()).Length, RosterWire.MaxRequestChars);
        }
    }
}
