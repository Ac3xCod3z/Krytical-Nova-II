#if UNITY_EDITOR
using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Networking.Entities;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// "Play DOTS here (test saves)" (owner, 5 Nov 2026: relog tests in Play DOTS here): the session saves to the TEST save service only,
    /// as one fixed test account with a real "Tester" character. Safe by construction:
    /// - the only address ever used is <see cref="Url"/> (loopback 7802, where the service listens only when started with --test); any
    ///   other text is refused (<see cref="UrlProblem"/>);
    /// - before anything is saved the service must answer as the test service, database <see cref="TestDatabase"/>
    ///   (<see cref="HealthProblem"/>); anything else refuses the session, and a refused session saves nothing anywhere and stops Play;
    /// - the service key is read from the owner's secrets file into memory for the request header only, never logged or copied;
    /// - the account (<see cref="Account"/>) is a hash in its own domain ("kn-playhere-test|"), so it can never equal an account made from a
    ///   Unity player id (<see cref="AccountIds.FromUnityPlayer"/> hashes "unity|..."), and it only ever lives in the test database.
    /// Editor only.
    /// </summary>
    public static class PlayHereTestSaves
    {
        public const string Url = "http://127.0.0.1:7802";
        public const string TestDatabase = "krytical_nova_test";
        public const string TesterName = "Tester";
        const string Holder = "playhere-test";

        /// <summary>The fixed test account: 32 hex like every account id (SaveKeys.IsId), from its own hash domain.</summary>
        public static string Account => HashId("kn-playhere-test|tester");

        /// <summary>Why <paramref name="url"/> is not exactly the test lane's address, or null when it is.</summary>
        public static string UrlProblem(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "No save service address.";
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri uri)) return "The save service address does not read: " + url;
            if (uri.Scheme != Uri.UriSchemeHttp || uri.Host != "127.0.0.1" || uri.Port != 7802 || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.UserInfo))
                return "Only the test save service at " + Url + " may be used here, not " + url + ".";
            return null;
        }

        [Serializable]
        sealed class HealthAnswer
        {
            public bool ok;
            public bool test;
            public string database;
        }

        /// <summary>Why the service's /health answer is not the test service on <see cref="TestDatabase"/>, or null when it is.</summary>
        public static string HealthProblem(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "The save service gave no answer.";
            HealthAnswer answer;
            try { answer = JsonUtility.FromJson<HealthAnswer>(json); }
            catch (Exception) { return "The save service's answer does not read."; }
            if (answer == null || !answer.ok) return "The save service says its database is down.";
            if (!answer.test || answer.database != TestDatabase)
                return "The service on " + Url + " is not the test save service (database " + (answer.database ?? "not named") + "): nothing is saved.";
            return null;
        }

        /// <summary>
        /// Opens the session's store: the address checked, the key read, the service asked who it is. False with the reason on any
        /// problem (then nothing may be saved this session).
        /// </summary>
        public static bool TryOpen(out ServiceSaveStore store, out string problem)
        {
            store = null;
            if (!TryCheck(out string key, out problem)) return false;
            store = new ServiceSaveStore(Url, key, Holder);
            return true;
        }

        /// <summary>The checks of <see cref="TryOpen"/> without opening a store (the menu asks before Play starts).</summary>
        public static bool TryCheck(out string problem) => TryCheck(out _, out problem);

        static bool TryCheck(out string key, out string problem)
        {
            key = null;
            problem = UrlProblem(Url);
            if (problem != null) return false;
            string keyFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KryticalNova-Secrets", "save-service-key.txt");
            if (!File.Exists(keyFile)) { problem = "There is no save service key in the secrets folder: start the save service once."; return false; }
            key = File.ReadAllText(keyFile).Trim();
            string health;
            try
            {
                using var http = new HttpClient { BaseAddress = new Uri(Url), Timeout = TimeSpan.FromSeconds(5) };
                http.DefaultRequestHeaders.Add("X-KN-Key", key);
                HttpResponseMessage response = Task.Run(() => http.GetAsync("/health")).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    problem = "The test save service refused (" + (int)response.StatusCode + "): start SaveService --test.";
                    return false;
                }
                health = Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                // the reason, never the key (an HttpRequestException carries the address and the socket's words only)
                Exception inner = e is AggregateException a && a.InnerException != null ? a.InnerException : e;
                problem = "No test save service answers on " + Url + " (" + inner.GetType().Name + ": " + inner.Message + "): start SaveService --test.";
                return false;
            }
            problem = HealthProblem(health);
            return problem == null;
        }

        /// <summary>
        /// The Tester's character id in the test store, made on its first session through the roster (as a player's own Create). Null with
        /// <paramref name="problem"/> when the roster would not make it.
        /// </summary>
        public static string EnsureTester(CharacterRecipe recipe, out string problem)
        {
            problem = null;
            string account = Account;
            AccountSave saved = ServerSave.ReadAccountFrom(ServerSave.Store, account);
            if (saved?.characters != null)
                foreach (CharacterSave c in saved.characters)
                    if (c != null && string.Equals(c.name, TesterName, StringComparison.OrdinalIgnoreCase)) return c.characterId;
            RosterReply made = ServerRoster.Handle(account, null, new RosterRequest { op = RosterRequest.Create, name = TesterName, recipe = recipe });
            if (made == null || !made.ok || string.IsNullOrEmpty(made.characterId))
            {
                problem = "The test save service could not make the Tester: " + (made?.message ?? "no answer");
                return null;
            }
            return made.characterId;
        }

        static string HashId(string text)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            var id = new StringBuilder(32);
            for (int i = 0; i < 16; i++) id.Append(hash[i].ToString("x2"));
            return id.ToString();
        }
    }

    /// <summary>
    /// A2 (Log Out in a test-saves session) and r7 (Stop in one): every server body leaves the normal way first
    /// (<see cref="ServerLeaving.Notify"/>: its save is written and its account let go, as a disconnect does), the store sends everything it
    /// holds (waiting at most the given seconds), and only then <c>stopWorlds</c> runs. A second leave of the same body finds nothing to
    /// save (PlayerSaveAgent.SaveAndRelease returns once the character is let go).
    /// </summary>
    public static class PlayHereLeave
    {
        /// <summary>The longest a Stop waits for the test save service: Stop never hangs the Editor (Nova's review, 6 Nov 2026).</summary>
        public const float StopSeconds = 5f;

        /// <summary>r7: the stop-time save runs only when Play is being left in a test-saves session that has not logged out already.</summary>
        public static bool SavesOnStop(bool exitingPlay, bool testSaves, bool loggedOut) => exitingPlay && testSaves && !loggedOut;

        /// <summary>True when the store took every save in time.</summary>
        public static bool LeaveAll(System.Collections.Generic.IEnumerable<GameObject> serverBodies, ISaveStore store, Action stopWorlds,
            float seconds = 15f)
        {
            Func<float, bool> sendAll = store is ServiceSaveStore service
                ? service.Flush
                : (Func<float, bool>)(_ => { ServerSave.WaitForDisk(); return true; });
            return LeaveAll(serverBodies, sendAll, stopWorlds, seconds);
        }

        /// <summary><see cref="LeaveAll(System.Collections.Generic.IEnumerable{GameObject}, ISaveStore, Action, float)"/> with the store's send-all as a function (tests).</summary>
        public static bool LeaveAll(System.Collections.Generic.IEnumerable<GameObject> serverBodies, Func<float, bool> sendAll, Action stopWorlds,
            float seconds)
        {
            foreach (GameObject body in serverBodies)
            {
                if (body == null) continue;
                try { ServerLeaving.Notify(body); }
                catch (Exception e) { Debug.LogError("[KnPlayHere] a body's leaving rules threw while leaving: " + e); }
            }
            bool sent = sendAll == null || sendAll(seconds);
            if (!sent)
                Debug.LogWarning($"[KnPlayHere] Test saves: the test save service did not take every save within {seconds:0.#} s; gave up waiting " +
                                 "(what is left is sent again while the Editor runs, and may be lost if it closes).");
            stopWorlds?.Invoke();
            return sent;
        }
    }

    /// <summary>
    /// Character select's "here" list in a test-saves session (A2): the test account's characters as <paramref name="store"/> holds them,
    /// read-only. Nothing is ever written (a create, delete or rename here is refused with one warning); a store that cannot be read lists
    /// nothing (never this PC's own characters, never another service).
    /// </summary>
    public sealed class PlayHereTestRosterStore : ICharacterStore
    {
        readonly ISaveStore m_Store;
        readonly string m_Account;
        bool m_Warned;

        public PlayHereTestRosterStore(ISaveStore store, string account)
        {
            m_Store = store;
            m_Account = account;
        }

        public CharacterRoster Load()
        {
            var roster = new CharacterRoster();
            AccountSave saved;
            try { saved = ServerSave.ReadAccountFrom(m_Store, m_Account); }
            catch (Exception e) { Debug.LogError("[KnPlayHere] Test saves: the test account's characters could not be read: " + e.Message); return roster; }
            if (saved?.characters == null) return roster;
            ServerRoster.AssignSlots(saved);
            long newest = long.MinValue;
            foreach (CharacterSave character in saved.characters)
            {
                if (character == null || character.slot < 0 || character.slot >= CharacterRoster.SlotCount || string.IsNullOrEmpty(character.characterId)) continue;
                KryticalNova.Player.PlayerIdentity.TryReadLook(character.Get(KryticalNova.Player.PlayerIdentity.LookSaveKey), out CharacterRecipe recipe);
                roster.Set(character.slot, new CharacterProfile
                {
                    id = character.characterId, name = character.name, recipe = recipe, level = Mathf.Max(1, character.level),
                    created = character.created, lastPlayed = character.savedUtc, worn = Array.Empty<WornPiece>(), mustRename = character.rename,
                });
                if (character.savedUtc > newest) { newest = character.savedUtc; roster.lastPlayedSlot = character.slot; }
            }
            return roster;
        }

        public void Save(CharacterRoster roster) => Refuse();
        public void DeleteAll() => Refuse();

        void Refuse()
        {
            if (m_Warned) return;
            m_Warned = true;
            Debug.LogWarning("[KnPlayHere] Test saves: this list is the test account's, read-only (the Tester is made by the menu; create, " +
                             "delete and rename are not kept here).");
        }
    }

    /// <summary>The store of a refused test-saves session (Play stops at once): it reads nothing and keeps nothing, anywhere.</summary>
    public sealed class RefusedSaveStore : ISaveStore
    {
        public bool TryRead(string key, out string json) { json = null; return false; }
        public void Write(string key, string json) { }
        public void Delete(string key) { }
        public System.Collections.Generic.IReadOnlyList<string> Keys(string prefix) => Array.Empty<string>();
    }

    /// <summary>The join check of a test-saves session: the one local client joins as the fixed test account (never proven).</summary>
    public sealed class PlayHereTestJoinCheck : IKnJoinCheck
    {
        public KnJoinVerdict Poll(Entity connection, string payload) => KnJoinVerdict.AcceptAs(PlayHereTestSaves.Account, string.Empty, false);
        public void Forget(Entity connection) { }
    }
}
#endif
