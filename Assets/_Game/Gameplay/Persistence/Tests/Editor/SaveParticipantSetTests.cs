using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Persistence;

namespace KryticalNova.Tests
{
    /// <summary>M3a-2b step 3: the passthrough rule of the save loops (fake participants; real ones need Awake: B2).</summary>
    public class SaveParticipantSetTests
    {
        private class Fake : ISaveParticipant
        {
            public string Key;
            public SaveScope Scope = SaveScope.Character;
            public string Capture = "{\"n\":1}";
            public bool ThrowOnRestore;
            public bool ThrowOnCapture;
            public int Restores, Captures;
            public string Restored = "<never>";
            public string SaveKey => Key;
            public SaveScope SaveScope => Scope;
            public string CaptureSave() { Captures++; if (ThrowOnCapture) throw new InvalidOperationException("capture"); return Capture; }
            public void RestoreSave(string json) { Restores++; Restored = json; if (ThrowOnRestore) throw new InvalidOperationException("restore"); }
        }

        private sealed class Migrating : Fake, IMigratingSave
        {
            public bool Fail;
            public int SaveVersion => 2;
            public string Migrate(string json, int from) { if (Fail) throw new InvalidOperationException("step"); return json; }
        }

        private const string Odd = "{ \"a\" :  1.50 ,\n \"b\":[1 , 2], \"unknown\": {\"x\":true}  }  ";

        private static (AccountSave, CharacterSave) Docs()
        {
            var account = new AccountSave { accountId = "a" };
            var character = new CharacterSave { characterId = "c" };
            account.characters.Add(character);
            return (account, character);
        }

        private static bool Pass(string key) => key.StartsWith("p");

        [Test]
        public void Passthrough_KeepsTheStoredBytes_AndIsNeverDirty()
        {
            var (account, character) = Docs();
            character.Set("p1", Odd);
            account.Set("p2", "{\"_v\":1,\"old\": 007}");
            var p1 = new Fake { Key = "p1", Capture = "{\"different\":true}" };
            var p2 = new Fake { Key = "p2", Scope = SaveScope.Account, Capture = "{}" };
            var set = new ISaveParticipant[] { p1, p2 };

            SaveLoadResult result = SaveParticipantSet.Load(set, account, character, Pass, null);
            bool changed = SaveParticipantSet.Capture(set, account, character, Pass, null);

            Assert.IsTrue(result.Ok);
            Assert.AreEqual(2, result.Passed);
            Assert.AreEqual(0, result.Restored);
            Assert.IsFalse(changed, "a passthrough part is never dirty");
            Assert.AreEqual(Odd, character.Get("p1"), "byte for byte");
            Assert.AreEqual("{\"_v\":1,\"old\": 007}", account.Get("p2"));
            Assert.AreEqual(0, p1.Restores + p1.Captures + p2.Restores + p2.Captures, "a passthrough part is not even asked");
        }

        [Test]
        public void RealParts_AreRestoredAndCapturedWithTheirOwnFormatting()
        {
            var (account, character) = Docs();
            character.Set("r1", Odd);
            var r1 = new Fake { Key = "r1", Capture = "{\"n\":2}" };
            var set = new ISaveParticipant[] { r1 };

            SaveLoadResult result = SaveParticipantSet.Load(set, account, character, Pass, null);
            Assert.IsTrue(result.Ok);
            Assert.AreEqual(1, result.Restored);
            Assert.AreEqual(Odd, r1.Restored);

            Assert.IsTrue(SaveParticipantSet.Capture(set, account, character, Pass, null));
            Assert.AreEqual("{\"n\":2}", character.Get("r1"));
            Assert.IsFalse(SaveParticipantSet.Capture(set, account, character, Pass, null), "the same text again is not a change");
        }

        [Test]
        public void MixedSet_OnlyRealPartsMove()
        {
            var (account, character) = Docs();
            character.Set("p1", Odd);
            character.Set("r1", "{\"n\":1}");
            var p1 = new Fake { Key = "p1", Capture = "{}" };
            var r1 = new Fake { Key = "r1", Capture = "{\"n\":5}" };
            var set = new ISaveParticipant[] { p1, r1 };
            SaveParticipantSet.Load(set, account, character, Pass, null);
            Assert.IsTrue(SaveParticipantSet.Capture(set, account, character, Pass, null));
            Assert.AreEqual(Odd, character.Get("p1"));
            Assert.AreEqual("{\"n\":5}", character.Get("r1"));
        }

        [Test]
        public void ARestoreThatThrows_DoesNotTouchTheOthers()
        {
            var (account, character) = Docs();
            var bad = new Fake { Key = "r1", ThrowOnRestore = true };
            var good = new Fake { Key = "r2" };
            var errors = new List<string>();
            SaveLoadResult result = SaveParticipantSet.Load(new ISaveParticipant[] { bad, good }, account, character, Pass, (k, e) => errors.Add(k));
            Assert.IsTrue(result.Ok);
            Assert.AreEqual(1, good.Restores);
            CollectionAssert.AreEqual(new[] { "r1" }, errors);
            Assert.AreEqual(1, result.Restored);
        }

        [Test]
        public void ACaptureThatThrows_KeepsItsLastSave_AndTheOthersAreWritten()
        {
            var (account, character) = Docs();
            character.Set("r1", "{\"last\":1}");
            var bad = new Fake { Key = "r1", ThrowOnCapture = true };
            var good = new Fake { Key = "r2", Capture = "{\"n\":9}" };
            var errors = new List<string>();
            bool changed = SaveParticipantSet.Capture(new ISaveParticipant[] { bad, good }, account, character, Pass, (k, e) => errors.Add(k));
            Assert.IsTrue(changed);
            Assert.AreEqual("{\"last\":1}", character.Get("r1"));
            Assert.AreEqual("{\"n\":9}", character.Get("r2"));
            CollectionAssert.AreEqual(new[] { "r1" }, errors);
        }

        [Test]
        public void AMigrationFailureOfARealPart_StopsTheLoadBeforeAnythingIsRestored()
        {
            var (account, character) = Docs();
            character.Set("r2", "{\"_v\":1}");
            var first = new Fake { Key = "r1" };
            var failing = new Migrating { Key = "r2", Fail = true };
            SaveLoadResult result = SaveParticipantSet.Load(new ISaveParticipant[] { first, failing }, account, character, Pass, null);
            Assert.IsFalse(result.Ok);
            Assert.AreEqual("r2", result.FailedKey);
            Assert.AreEqual("step", result.FailedMessage);
            Assert.AreEqual(0, first.Restores, "nothing is restored once a part cannot be read");
            Assert.AreEqual("{\"_v\":1}", character.Get("r2"), "the save stays as it is");
        }

        [Test]
        public void APassthroughPartTheBuildCannotRead_StillLoads()
        {
            var (account, character) = Docs();
            character.Set("p1", "{\"_v\":99,\"x\":1}"); // saved by a newer build
            var failing = new Migrating { Key = "p1", Fail = true };
            SaveLoadResult result = SaveParticipantSet.Load(new ISaveParticipant[] { failing }, account, character, Pass, null);
            Assert.IsTrue(result.Ok, "a part that is passed through is never read, so it cannot block the load");
            Assert.AreEqual("{\"_v\":99,\"x\":1}", character.Get("p1"));
        }

        [Test]
        public void NullPolicy_BehavesLikeBefore_EveryPartIsRestoredAndCaptured()
        {
            var (account, character) = Docs();
            character.Set("p1", Odd);
            var p1 = new Fake { Key = "p1", Capture = "{\"n\":3}" };
            var set = new ISaveParticipant[] { p1 };
            Assert.IsTrue(SaveParticipantSet.Load(set, account, character, null, null).Ok);
            Assert.AreEqual(1, p1.Restores);
            Assert.IsTrue(SaveParticipantSet.Capture(set, account, character, SavePassthrough.Never, null));
            Assert.AreEqual("{\"n\":3}", character.Get("p1"));
        }

        [Test]
        public void AccountScopedPartsUseTheAccountDocument()
        {
            var (account, character) = Docs();
            account.Set("r9", "{\"v\":1}");
            var part = new Fake { Key = "r9", Scope = SaveScope.Account, Capture = "{\"v\":2}" };
            var set = new ISaveParticipant[] { part };
            SaveParticipantSet.Load(set, account, character, Pass, null);
            Assert.AreEqual("{\"v\":1}", part.Restored);
            Assert.IsTrue(SaveParticipantSet.Capture(set, account, character, Pass, null));
            Assert.AreEqual("{\"v\":2}", account.Get("r9"));
            Assert.IsNull(character.Get("r9"));
        }
    }

    /// <summary>The default policy and the lint that forces a decision for every save part on the player prefab.</summary>
    public class SavePassthroughTests
    {
        [SetUp]
        public void SetUp() => SavePassthrough.ResetToDefault();

        [TearDown]
        public void TearDown() => SavePassthrough.ResetToDefault();

        [Test]
        public void Default_ThePortedPartsAreReal()
        {
            CollectionAssert.AreEquivalent(new[] { "look", "progression", "inventory", "serums", "wallet", "loadouts", "xpboosts", "skills", "movement", "vault",
                "deepvault", "vendorWeek", "trade", "nova", "weave", "weaves", "autobuff", "crate", "sponsor", "mounts", "autopilot" }, SavePassthrough.RealKeys.ToArray(), "each part goes real with the port that writes it (plan section 0)");
            Assert.IsFalse(SavePassthrough.Is("look"));
            Assert.IsFalse(SavePassthrough.Is("progression"));
            Assert.IsFalse(SavePassthrough.Is("inventory"), "items are used on the server's body: the Pack is real (owner, 2 Nov, item 5)");
            Assert.IsFalse(SavePassthrough.Is("serums"));
            Assert.IsFalse(SavePassthrough.Is("wallet"));
            Assert.IsFalse(SavePassthrough.Is("mounts"), "the mounts part is real (M8 8.4.8)");
            Assert.IsFalse(SavePassthrough.Is("autopilot"), "the autopilot part is real (wave 2, 12 Nov 2026)");
            Assert.IsTrue(SavePassthrough.Is("a key nobody knows"), "unknown keys are passed through: the safe side");
            Assert.IsFalse(SavePassthrough.Never("a key nobody knows"));
        }

        [Test]
        public void MakeReal_FlipsOneKey_AndResetPutsItBack()
        {
            SavePassthrough.MakeReal("a part not ported yet");
            Assert.IsFalse(SavePassthrough.Is("a part not ported yet"));
            SavePassthrough.ResetToDefault();
            Assert.IsTrue(SavePassthrough.Is("a part not ported yet"));
        }

        [Test]
        public void KeyLists_HaveNoDuplicates_AndNoKeyIsBothRealAndPassed()
        {
            Assert.AreEqual(SavePassthrough.PassthroughKeys.Length, SavePassthrough.PassthroughKeys.Distinct().Count());
            Assert.IsEmpty(SavePassthrough.PassthroughKeys.Intersect(SavePassthrough.RealKeys));
        }

        [Test]
        public void Lint_EveryParticipantOnPlayerPrefabIsClassified_AndKeysAreUnique()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gameplay/Player/Prefabs/Player.prefab");
            Assert.IsNotNull(prefab);
            var keys = new List<string>();
            foreach (ISaveParticipant participant in prefab.GetComponentsInChildren<ISaveParticipant>(true))
                keys.Add(participant.SaveKey);
            Assert.IsNotEmpty(keys);
            var unclassified = keys.Where(k => !SavePassthrough.IsClassified(k)).ToList();
            Assert.IsEmpty(unclassified, "Decide real or passthrough in SavePassthrough for: " + string.Join(", ", unclassified));
            var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.IsEmpty(duplicates, "two participants share a key: " + string.Join(", ", duplicates));
        }
    }
}
