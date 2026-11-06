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
    }
}
