using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// "Play DOTS here (test saves)" (owner, 5 Nov 2026): only the test save service on loopback 7802 answering as krytical_nova_test is
    /// ever used, the test account can never be a Unity player's account, and a refused session keeps nothing.
    /// </summary>
    public sealed class PlayHereTestSavesTests
    {
        [Test]
        public void TheStopSave_RunsOnlyWhenLeavingPlayInTestSaves_AndNotAfterLogOut()
        {
            Assert.IsTrue(PlayHereLeave.SavesOnStop(exitingPlay: true, testSaves: true, loggedOut: false), "test saves, Stop: saved (owner's r7)");
            Assert.IsFalse(PlayHereLeave.SavesOnStop(true, false, false), "plain Play DOTS here (memory saves) and every other Play: no effect");
            Assert.IsFalse(PlayHereLeave.SavesOnStop(true, true, true), "after Log Out the bodies are saved and gone: no second save");
            Assert.IsFalse(PlayHereLeave.SavesOnStop(false, true, false), "any other Play-mode change: nothing");
        }

        [Test]
        public void TheStopSave_WaitsAtMostItsSeconds_ThenGivesUpAndSaysSo_AndStillGoesOn()
        {
            float askedSeconds = -1f;
            bool stopped = false;
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
                new System.Text.RegularExpressions.Regex(@"did not take every save within 5 s; gave up waiting"));
            bool sent = PlayHereLeave.LeaveAll(new UnityEngine.GameObject[0], s => { askedSeconds = s; return false; }, () => stopped = true,
                PlayHereLeave.StopSeconds);
            Assert.IsFalse(sent);
            Assert.AreEqual(5f, askedSeconds, "the store is asked to wait 5 s at most (Stop never hangs the Editor)");
            Assert.IsTrue(stopped, "giving up never blocks what follows");

            bool sentInTime = PlayHereLeave.LeaveAll(new UnityEngine.GameObject[0], s => true, null, PlayHereLeave.StopSeconds);
            Assert.IsTrue(sentInTime, "in time: no warning (an unexpected log fails the test)");
        }

        [Test]
        public void OnlyTheTestServicesAddress_IsAccepted()
        {
            Assert.IsNull(PlayHereTestSaves.UrlProblem("http://127.0.0.1:7802"));
            Assert.IsNull(PlayHereTestSaves.UrlProblem("http://127.0.0.1:7802/"));
            Assert.IsNull(PlayHereTestSaves.UrlProblem(PlayHereTestSaves.Url));
            foreach (string refused in new[]
                     {
                         "http://127.0.0.1:7801", "http://127.0.0.1:7777", "http://127.0.0.1:7778", "http://127.0.0.1:8090",
                         "http://localhost:7802", "http://192.168.1.5:7802", "https://127.0.0.1:7802", "http://127.0.0.1:7803",
                         "http://127.0.0.1:7802/doc", "http://127.0.0.1:7802/?db=krytical_nova", "http://u:p@127.0.0.1:7802",
                         "127.0.0.1:7802", "", "   ", null, "not a url",
                     })
                Assert.IsNotNull(PlayHereTestSaves.UrlProblem(refused), "refused: " + (refused ?? "null"));
        }

        [Test]
        public void OnlyTheTestDatabase_IsAccepted()
        {
            Assert.IsNull(PlayHereTestSaves.HealthProblem("{\"ok\":true,\"documents\":3,\"bytes\":9,\"test\":true,\"database\":\"krytical_nova_test\"}"));
            foreach (string refused in new[]
                     {
                         "{\"ok\":true,\"test\":false,\"database\":\"krytical_nova\"}",
                         "{\"ok\":true,\"test\":true,\"database\":\"krytical_nova\"}",
                         "{\"ok\":true,\"test\":false,\"database\":\"krytical_nova_test\"}",
                         "{\"ok\":true,\"documents\":3}",   // an older service that does not say which database: refused
                         "{\"ok\":false,\"test\":true,\"database\":\"krytical_nova_test\"}",
                         "{\"ok\":true,\"test\":true,\"database\":\"krytical_nova_drill\"}",
                         "", null, "not json",
                     })
                Assert.IsNotNull(PlayHereTestSaves.HealthProblem(refused), "refused: " + (refused ?? "null"));
        }

        [Test]
        public void TheTestAccount_IsAnId_FromItsOwnHashDomain()
        {
            string account = PlayHereTestSaves.Account;
            Assert.IsTrue(SaveKeys.IsId(account), "saved like every account");
            Assert.AreEqual(account, PlayHereTestSaves.Account, "always the same");
            foreach (string unityPlayer in new[] { "tester", "Tester", "playhere|tester", "kn-playhere-test|tester", "", "devbot|1" })
                Assert.AreNotEqual(AccountIds.FromUnityPlayer(unityPlayer), account, "never a Unity player's account: " + unityPlayer);
        }

        /// <summary>
        /// Against the live test service only (run by hand with <c>SaveService --test</c> on 7802; never part of a batch run): the
        /// Tester is made once in krytical_nova_test and found again by the next session, as a relog finds it.
        /// </summary>
        [Test, Explicit("needs SaveService --test running on 127.0.0.1:7802")]
        public void OnTheTestService_TheTesterIsMadeOnce_AndFoundAgain()
        {
            // a filtered batch run names this test too: with no test service up it is skipped, never failed (the live run starts one)
            if (!PlayHereTestSaves.TryCheck(out string missing)) Assert.Ignore(missing);
            Assert.IsTrue(PlayHereTestSaves.TryOpen(out ServiceSaveStore store, out string problem), problem);
            CharacterCreationDatabase oldDatabase = ServerRoster.Database;
            try
            {
                ServerSave.Store = store;
                // the roster checks the look against the player's character rules, as the Play server installs them (PlayerWorldsSetup)
                var player = PlayerWorldsSetup.PlayerPrefab();
                var identity = player != null ? player.GetComponent<KryticalNova.Player.PlayerIdentity>() : null;
                Assert.IsNotNull(identity, "the player prefab's identity");
                ServerRoster.Database = identity.Database;
                Assert.IsTrue(KnPlayHereTestRecipe(out CharacterRecipe recipe), "a look");
                string first = PlayHereTestSaves.EnsureTester(recipe, out problem);
                Assert.IsTrue(SaveKeys.IsId(first), problem);
                store.Flush();
                Assert.AreEqual(first, PlayHereTestSaves.EnsureTester(recipe, out problem), "the same character again, never a second");
                AccountSave saved = ServerSave.ReadAccountFrom(store, PlayHereTestSaves.Account);
                Assert.IsNotNull(saved?.Find(first), "it is in the test database's account");
            }
            finally
            {
                ServerSave.Store = null;   // the service store is closed; the next use makes the usual one
                ServerRoster.Database = oldDatabase;
            }
        }

        [Test, Explicit("needs SaveService --test running on 127.0.0.1:7802")]
        public void OnTheTestService_TheLobbyListsTheTester_ReadOnly()
        {
            // A2 live: character select's "here" list in a test-saves session, read from the test service itself
            if (!PlayHereTestSaves.TryCheck(out string missing)) Assert.Ignore(missing);
            Assert.IsTrue(PlayHereTestSaves.TryOpen(out ServiceSaveStore store, out string problem), problem);
            CharacterCreationDatabase oldDatabase = ServerRoster.Database;
            try
            {
                ServerSave.Store = store;
                var identity = PlayerWorldsSetup.PlayerPrefab()?.GetComponent<KryticalNova.Player.PlayerIdentity>();
                Assert.IsNotNull(identity, "the player prefab's identity");
                ServerRoster.Database = identity.Database;
                Assert.IsTrue(KnPlayHereTestRecipe(out CharacterRecipe recipe), "a look");
                string tester = PlayHereTestSaves.EnsureTester(recipe, out problem);
                Assert.IsTrue(SaveKeys.IsId(tester), problem);
                store.Flush();
                string before = store.TryRead(ServerSave.AccountsPrefix + PlayHereTestSaves.Account, out string j) ? j : null;

                var lobby = new PlayHereTestRosterStore(store, PlayHereTestSaves.Account);
                CharacterRoster roster = lobby.Load();
                bool listed = false;
                for (int i = 0; i < CharacterRoster.SlotCount; i++)
                    if (roster.IsUsed(i) && roster.Get(i).id == tester) listed = roster.Get(i).name == PlayHereTestSaves.TesterName;
                Assert.IsTrue(listed, "the Tester is listed by its id and name");
                UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("read-only"));
                lobby.Save(new CharacterRoster());
                store.Flush();
                Assert.IsTrue(store.TryRead(ServerSave.AccountsPrefix + PlayHereTestSaves.Account, out string after));
                Assert.AreEqual(before, after, "the lobby wrote nothing to the test service");
            }
            finally
            {
                ServerSave.Store = null;
                ServerRoster.Database = oldDatabase;
            }
        }

        static bool KnPlayHereTestRecipe(out CharacterRecipe recipe)
        {
            recipe = default;
            if (!KnPlayHere.TryTesterLook(out string text, out _)) return false;
            return RecipeText.TryDecode(text, out recipe);
        }

        [Test]
        public void ARefusedSession_KeepsNothing()
        {
            var store = new RefusedSaveStore();
            store.Write("accounts/" + PlayHereTestSaves.Account, "{}");
            Assert.IsFalse(store.TryRead("accounts/" + PlayHereTestSaves.Account, out string json));
            Assert.IsNull(json);
            Assert.AreEqual(0, store.Keys("").Count);
        }

        // ---- A2: Log Out -> character select -> Enter in one Play (test saves only) ----

        [Test]
        public void TheRelogHooks_AreNullOutsideTheTestSavesMode()
        {
            // nothing sets them but KnPlayHere's test-saves start (cleared again on leaving Play): Log Out and the lobby are unchanged
            Assert.IsNull(EntityNetcodeSession.StopWorlds, "Log Out leaves as before");
            Assert.IsNull(CharacterSession.HereStore, "character select lists this PC's own characters as before");
        }

        [Test]
        public void TheTestRoster_ListsTheTestAccountsCharacters_ReadOnly()
        {
            ISaveStore before = ServerSave.Store;
            var memory = new MemorySaveStore();
            ServerSave.Store = memory;
            try
            {
                string account = PlayHereTestSaves.Account;
                var saved = new AccountSave { accountId = account };
                saved.characters.Add(new CharacterSave { characterId = "0123456789abcdef0123456789abcdef", name = "Tester", slot = 0, level = 12, savedUtc = 5 });
                saved.characters.Add(new CharacterSave { characterId = "fedcba9876543210fedcba9876543210", name = "Second", slot = 2, savedUtc = 9 });
                Assert.IsTrue(ServerSave.WriteAccount(saved));
                ServerSave.WaitForDisk();
                var keysBefore = new System.Collections.Generic.List<string>(memory.Keys(""));
                string accountJson = memory.TryRead(ServerSave.AccountsPrefix + account, out string j) ? j : null;

                var store = new PlayHereTestRosterStore(memory, account);
                CharacterRoster roster = store.Load();
                Assert.AreEqual("Tester", roster.Get(0)?.name);
                Assert.AreEqual("0123456789abcdef0123456789abcdef", roster.Get(0).id);
                Assert.AreEqual(12, roster.Get(0).level);
                Assert.AreEqual("Second", roster.Get(2)?.name);
                Assert.AreEqual(2, roster.lastPlayedSlot, "the newest save is the one chosen first");

                store.Save(new CharacterRoster());
                store.DeleteAll();
                CollectionAssert.AreEquivalent(keysBefore, memory.Keys(""), "read-only: nothing written or deleted");
                Assert.IsTrue(memory.TryRead(ServerSave.AccountsPrefix + account, out string after));
                Assert.AreEqual(accountJson, after, "the account is byte for byte as it was");
                Assert.AreEqual("Tester", store.Load().Get(0)?.name, "a save from the lobby changes nothing");
            }
            finally { ServerSave.Store = before; }
        }

        [Test]
        public void TheTestRoster_ListsNothing_WhenTheStoreCannotBeRead()
        {
            Assert.AreEqual(0, CountUsed(new PlayHereTestRosterStore(new RefusedSaveStore(), PlayHereTestSaves.Account).Load()), "refused store");
            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("could not be read"));
            Assert.AreEqual(0, CountUsed(new PlayHereTestRosterStore(new ThrowingStore(), PlayHereTestSaves.Account).Load()), "a store that throws");
            Assert.AreEqual(0, CountUsed(new PlayHereTestRosterStore(new MemorySaveStore(), PlayHereTestSaves.Account).Load()), "no account");
        }

        static int CountUsed(CharacterRoster roster)
        {
            int n = 0;
            for (int i = 0; i < CharacterRoster.SlotCount; i++) if (roster.IsUsed(i)) n++;
            return n;
        }

        sealed class ThrowingStore : ISaveStore
        {
            public bool TryRead(string key, out string json) => throw new System.IO.IOException("down");
            public void Write(string key, string json) => throw new System.IO.IOException("down");
            public void Delete(string key) => throw new System.IO.IOException("down");
            public System.Collections.Generic.IReadOnlyList<string> Keys(string prefix) => throw new System.IO.IOException("down");
        }
    }
}
