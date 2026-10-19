using System;
using System.Collections.Generic;
using System.IO;
using KryticalNova.Core;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Persistence
{
    /// <summary>
    /// What the server remembers between sessions. Runs only where the game is decided: on the host or dedicated
    /// server, or offline; a joined client never reads or writes a save. Accounts are loaded when one of their
    /// characters enters the world (<see cref="PlayerSaveAgent"/>); world documents (Crews, Kiosks, Contacts) are
    /// loaded on first use and written when they change.
    /// </summary>
    public static class ServerSave
    {
        public const string AccountsPrefix = "accounts/";
        public const string WorldPrefix = "world/";
        private const string FolderName = "ServerSave";
        private const string DefaultProfile = "default";

        private static ISaveStore store;
        // One character of an account is in the world at a time; the agent holding it owns the account document.
        private static readonly Dictionary<string, PlayerSaveAgent> BoundAccounts = new Dictionary<string, PlayerSaveAgent>(StringComparer.Ordinal);
        private static readonly List<PlayerSaveAgent> Agents = new List<PlayerSaveAgent>(16);
        private static readonly Dictionary<string, object> WorldDocuments = new Dictionary<string, object>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> LastWritten = new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool quitHooked;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // The store of the session before (Play in the Editor starts without the statics being made anew).
            (store as IDisposable)?.Dispose();
            store = null;
            BoundAccounts.Clear();
            Agents.Clear();
            WorldDocuments.Clear();
            LastWritten.Clear();
            PartKeysOf.Clear();
            if (!quitHooked)
            {
                quitHooked = true;
                Application.quitting += FlushAll;
                Application.quitting += WaitForDisk; // also when this machine no longer decides the game
            }
        }

        /// <summary>Where saves are kept. Files under the game's data folder unless something else is set (tests, a database).</summary>
        public static ISaveStore Store
        {
            // The files are written by a thread of the store's own, so a save takes none of a frame's time for the disk.
            get => store ??= MakeStore();
            set
            {
                if (!ReferenceEquals(store, value)) (store as IDisposable)?.Dispose();
                store = value;
                WorldDocuments.Clear();
                LastWritten.Clear();
            }
        }

        // Files under the save folder; on a dedicated server whose settings name a save service, the service (online phase 4).
        private static ISaveStore MakeStore()
        {
#if UNITY_SERVER
            KryticalNova.Networking.ServerLaunchOptions options = KryticalNova.Networking.ServerLaunchOptions.LoadForThisServer();
            if (options.UsesSaveService)
            {
                string key = File.Exists(options.SaveServiceKeyFile) ? File.ReadAllText(options.SaveServiceKeyFile).Trim() : string.Empty;
                return new ServiceSaveStore(options.saveService, key, Environment.MachineName + ":" + options.port);
            }
#endif
            return new FileSaveStore(DefaultRoot(), background: true);
        }

        /// <summary>
        /// Each local copy of the game (a Multiplayer Play Mode clone, a build started with -profile) keeps its own saves.
        /// An Editor test session keeps them in the folder it names instead, away from a running build's. A dedicated
        /// server keeps them where its settings say (outside its own folder, so a new build never touches them).
        /// </summary>
        public static string DefaultRoot()
        {
#if UNITY_SERVER
            return KryticalNova.Networking.ServerLaunchOptions.LoadForThisServer().SaveFolderPath;
#else
            string testRoot = KryticalNova.Networking.EditorTestSession.SaveRoot;
            if (!string.IsNullOrEmpty(testRoot)) return testRoot;
            string profile = KryticalNova.Networking.NetworkConnectionManager.LocalProfile();
            return Path.Combine(Application.persistentDataPath, FolderName, string.IsNullOrEmpty(profile) ? DefaultProfile : profile.ToLowerInvariant());
#endif
        }

        /// <summary>True on the machine that decides the game: a server, a host, or a game with no session.</summary>
        public static bool HasAuthority
        {
            get
            {
                var network = NetworkManager.Singleton;
                return network == null || !network.IsListening || network.IsServer;
            }
        }

        /// <summary>The players whose characters are loaded right now.</summary>
        public static IReadOnlyList<PlayerSaveAgent> Online => Agents;

        /// <summary>The agent of a character that is in the world, or null.</summary>
        public static PlayerSaveAgent FindCharacter(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            for (int i = 0; i < Agents.Count; i++)
                if (Agents[i] != null && Agents[i].CharacterId == characterId) return Agents[i];
            return null;
        }

        // ------------------------------------------------------------------ accounts

        /// <summary>The account's document, or a new empty one. Never null.</summary>
        // ---- How an account is kept (online phase 4, milestone 2): byte-sized saves.
        // accounts/<id>                      the header: version 2, its account-wide part names, each character's name,
        //                                    faction, level, map and place, and part names. No game state.
        // accounts/<id>/parts/<part>         one account-wide part (the Deep Vault, the trade allowance, ...)
        // characters/<character>/parts/<part> one part of one character (the Pack, the wallet, the skills, ...)
        // A save writes only the parts whose text changed, and the header, as one change where the store can (the save
        // service); a part a system no longer saves is deleted. An account saved before this (version 1, one document
        // holding everything) is read as it is, kept once as accounts/<id>-v1, and written in the new layout by its next
        // save.
        public const string CharactersPrefix = "characters/";
        private const string PartsFolder = "/parts/";
        private const string OldCopySuffix = "-v1";

        // The part keys each loaded account has in the store now, to know which ones a save must delete.
        private static readonly Dictionary<string, HashSet<string>> PartKeysOf = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        [Serializable] private sealed class VersionOnly { public int version; }

        [Serializable]
        private sealed class AccountHeader
        {
            public int version = AccountSave.CurrentVersion;
            public string accountId;
            public List<string> partKeys = new List<string>();
            public List<CharacterHeader> characters = new List<CharacterHeader>();
        }

        [Serializable]
        private sealed class CharacterHeader
        {
            public string characterId;
            public string name;
            public int faction;
            public int level = 1;
            public long savedUtc;
            public string map;
            public float x, y, z;
            public List<string> partKeys = new List<string>();
        }

        /// <summary>Where one account-wide part is kept.</summary>
        public static string AccountPartKey(string accountId, string part) => AccountsPrefix + accountId + PartsFolder + PartName(part);

        /// <summary>Where one part of one character is kept.</summary>
        public static string CharacterPartKey(string characterId, string part) => CharactersPrefix + characterId + PartsFolder + PartName(part);

        // A part's name as a save key allows it (lower case: "vendorWeek" is kept as "vendorweek").
        private static string PartName(string part) => (part ?? string.Empty).ToLowerInvariant();

        /// <summary>The account's document, or a new empty one. Never null.</summary>
        public static AccountSave LoadAccount(string accountId)
        {
            PartKeysOf[accountId] = new HashSet<string>(StringComparer.Ordinal);
            return ReadAccount(Store, accountId, live: true);
        }

        /// <summary>
        /// An account as kept in <paramref name="from"/>, in either layout, changing nothing there (the copy tool reads
        /// another server's saves this way). Null when the store has none.
        /// </summary>
        public static AccountSave ReadAccountFrom(ISaveStore from, string accountId) =>
            from != null && from.TryRead(AccountsPrefix + accountId, out _) ? ReadAccount(from, accountId, live: false) : null;

        // live: this server's own store (remember what was read, keep an old one-piece account once beside it).
        private static AccountSave ReadAccount(ISaveStore from, string accountId, bool live)
        {
            string key = AccountsPrefix + accountId;
            AccountSave account = null;
            if (from.TryRead(key, out string json))
            {
                try
                {
                    int version = JsonUtility.FromJson<VersionOnly>(json)?.version ?? 1;
                    account = version >= 2 ? ReadSplit(from, accountId, json, live) : ReadWhole(from, accountId, json, live);
                }
                catch (Exception e)
                {
                    if (!live) throw;
                    // Keep the unreadable file: a later save would otherwise replace it with an empty account.
                    Debug.LogError($"[Save] Account {accountId} could not be read and was set aside: {e.Message}");
                    from.Write(key + "-unreadable-" + DateTime.UtcNow.Ticks, json);
                    account = null;
                }
            }
            account ??= new AccountSave();
            account.accountId = accountId;
            account.parts ??= new List<SavePart>();
            account.characters ??= new List<CharacterSave>();
            return account;
        }

        // An account saved before the split (version 1): one document with everything. It is kept once, as it was, beside
        // the new layout; the next save writes the new layout (nothing of it is in LastWritten, so every part goes).
        private static AccountSave ReadWhole(ISaveStore from, string accountId, string json, bool live)
        {
            AccountSave account = JsonUtility.FromJson<AccountSave>(json);
            string copy = AccountsPrefix + accountId + OldCopySuffix;
            if (live && !from.TryRead(copy, out _))
            {
                from.Write(copy, json);
                Debug.Log($"[Save] Account {accountId} is in the old one-piece layout: kept as {copy}; it is saved in parts from now on.");
            }
            return account;
        }

        // The header, then every part it names, in one trip where the store can.
        private static AccountSave ReadSplit(ISaveStore from, string accountId, string headerJson, bool live)
        {
            AccountHeader header = JsonUtility.FromJson<AccountHeader>(headerJson);
            var account = new AccountSave { version = AccountSave.CurrentVersion, accountId = accountId };
            var wanted = new List<string>();
            foreach (string part in header.partKeys ?? new List<string>()) wanted.Add(AccountPartKey(accountId, part));
            foreach (CharacterHeader c in header.characters ?? new List<CharacterHeader>())
                foreach (string part in c.partKeys ?? new List<string>()) wanted.Add(CharacterPartKey(c.characterId, part));

            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            if (from is IBatchSaveStore batch) batch.TryReadMany(wanted, found);
            else
                foreach (string key in wanted)
                    if (from.TryRead(key, out string text)) found[key] = text;

            HashSet<string> present = live ? PartKeysOf[accountId] : new HashSet<string>(StringComparer.Ordinal);
            foreach (string part in header.partKeys ?? new List<string>())
                if (found.TryGetValue(AccountPartKey(accountId, part), out string text)) { account.parts.Add(new SavePart { key = part, json = text }); Remember(AccountPartKey(accountId, part), text, present); }
            foreach (CharacterHeader c in header.characters ?? new List<CharacterHeader>())
            {
                var character = new CharacterSave
                {
                    characterId = c.characterId, name = c.name, faction = c.faction, level = c.level, savedUtc = c.savedUtc,
                    map = c.map, x = c.x, y = c.y, z = c.z,
                };
                foreach (string part in c.partKeys ?? new List<string>())
                    if (found.TryGetValue(CharacterPartKey(c.characterId, part), out string text)) { character.parts.Add(new SavePart { key = part, json = text }); Remember(CharacterPartKey(c.characterId, part), text, present); }
                account.characters.Add(character);
            }
            int missing = wanted.Count - found.Count;
            if (missing > 0) Debug.LogWarning($"[Save] Account {accountId}: {missing} of its {wanted.Count} part(s) were not found; those systems start empty.");
            if (live)
            {
                LastWritten[AccountsPrefix + accountId] = headerJson;
                foreach (string key in present) LastWritten[key] = found[key];
            }
            return account;
        }

        private static void Remember(string key, string text, HashSet<string> present) => present.Add(key);

        /// <summary>
        /// Every document of an account in the parts layout (each part, then the header), whatever was written before: for
        /// copying an account into another store (the copy tool).
        /// </summary>
        public static List<KeyValuePair<string, string>> SplitDocuments(AccountSave account)
        {
            var all = new List<KeyValuePair<string, string>>();
            string header = BuildSplit(account, (key, json) => all.Add(new KeyValuePair<string, string>(key, json)), new HashSet<string>(StringComparer.Ordinal));
            all.Add(new KeyValuePair<string, string>(AccountsPrefix + account.accountId, header));
            return all;
        }

        /// <summary>
        /// Writes the parts of the account that changed since they were last written, and its header, as one change where
        /// the store can. Returns true when anything was written.
        /// </summary>
        public static bool WriteAccount(AccountSave account)
        {
            if (account == null || !SaveKeys.IsId(account.accountId)) return false;
            string id = account.accountId;
            var writes = new List<KeyValuePair<string, string>>();
            var keep = new HashSet<string>(StringComparer.Ordinal);
            string headerJson = BuildSplit(account, (key, json) => Changed(key, json, writes, null), keep);

            // Parts the account no longer has: deleted with the same change.
            if (!PartKeysOf.TryGetValue(id, out HashSet<string> present)) PartKeysOf[id] = present = new HashSet<string>(StringComparer.Ordinal);
            var deletes = new List<string>();
            foreach (string key in present)
                if (!keep.Contains(key)) deletes.Add(key);

            Changed(AccountsPrefix + id, headerJson, writes, null); // the header last: files read it last
            if (writes.Count == 0 && deletes.Count == 0) return false;

            if (Store is IBatchSaveStore batch) batch.WriteBatch(writes, deletes);
            else
            {
                foreach (KeyValuePair<string, string> write in writes) Store.Write(write.Key, write.Value);
                foreach (string key in deletes) Store.Delete(key);
            }
            foreach (KeyValuePair<string, string> write in writes) LastWritten[write.Key] = write.Value;
            foreach (string key in deletes) LastWritten.Remove(key);
            PartKeysOf[id] = keep;
            return true;
        }

        // Hands every part's key and text to part, puts each part key in keys, and returns the header's text.
        private static string BuildSplit(AccountSave account, Action<string, string> part, HashSet<string> keys)
        {
            string id = account.accountId;
            var header = new AccountHeader { accountId = id };
            foreach (SavePart p in account.parts ?? new List<SavePart>())
            {
                if (p == null || string.IsNullOrEmpty(p.json)) continue;
                header.partKeys.Add(p.key);
                keys.Add(AccountPartKey(id, p.key));
                part(AccountPartKey(id, p.key), p.json);
            }
            foreach (CharacterSave character in account.characters ?? new List<CharacterSave>())
            {
                if (character == null || !SaveKeys.IsId(character.characterId)) continue;
                var entry = new CharacterHeader
                {
                    characterId = character.characterId, name = character.name, faction = character.faction, level = character.level,
                    savedUtc = character.savedUtc, map = character.map, x = character.x, y = character.y, z = character.z,
                };
                foreach (SavePart p in character.parts ?? new List<SavePart>())
                {
                    if (p == null || string.IsNullOrEmpty(p.json)) continue;
                    entry.partKeys.Add(p.key);
                    keys.Add(CharacterPartKey(character.characterId, p.key));
                    part(CharacterPartKey(character.characterId, p.key), p.json);
                }
                header.characters.Add(entry);
            }
            return JsonUtility.ToJson(header);
        }

        // Adds the key to the writes when its text differs from what was last written.
        private static void Changed(string key, string json, List<KeyValuePair<string, string>> writes, HashSet<string> keep)
        {
            keep?.Add(key);
            if (LastWritten.TryGetValue(key, out string last) && last == json) return;
            writes.Add(new KeyValuePair<string, string>(key, json));
        }

        /// <summary>
        /// Reads an account whose owner is not in the world, changes it and writes it back (an offline Kiosk owner's
        /// earnings, for one). Refused while the account is loaded: change the live player instead.
        /// </summary>
        public static bool EditOfflineAccount(string accountId, Action<AccountSave> edit)
        {
            if (!HasAuthority || edit == null || !SaveKeys.IsId(accountId) || BoundAccounts.ContainsKey(accountId)) return false;
            AccountSave account = LoadAccount(accountId);
            edit(account);
            WriteAccount(account);
            return true;
        }

        public static bool IsAccountOnline(string accountId) => !string.IsNullOrEmpty(accountId) && BoundAccounts.ContainsKey(accountId);

        /// <summary>The ids of every saved account (not their parts or kept copies).</summary>
        public static List<string> AccountIds()
        {
            var ids = new List<string>();
            foreach (string key in Store.Keys(AccountsPrefix))
            {
                string id = key.Substring(AccountsPrefix.Length);
                if (SaveKeys.IsId(id)) ids.Add(id);
            }
            return ids;
        }

        /// <summary>
        /// The saved accounts holding a character of that name (whole name, any case), for a GM naming a player who is not
        /// on (gm.ban). Accounts carried over to a signed-in one are left out: the carried copy is the one in use. Reads
        /// every account, so it is for rare commands only.
        /// </summary>
        public static List<string> AccountsWithCharacter(string characterName)
        {
            var found = new List<string>();
            if (!HasAuthority || string.IsNullOrWhiteSpace(characterName)) return found;
            characterName = characterName.Trim();
            List<string> carried = World<CarriedAccounts>(CarriedDocument).old;
            foreach (string key in Store.Keys(AccountsPrefix))
            {
                string id = key.Substring(AccountsPrefix.Length);
                if (!SaveKeys.IsId(id) || carried.Contains(id) || !Store.TryRead(key, out string json)) continue;
                AccountSave account;
                try { account = JsonUtility.FromJson<AccountSave>(json); }
                catch (Exception) { continue; }
                if (account?.characters == null) continue;
                foreach (CharacterSave character in account.characters)
                    if (character != null && string.Equals(character.name, characterName, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(id);
                        break;
                    }
            }
            return found;
        }

        // ------------------------------------------------------------------ carrying old accounts over (online phase 2)

        /// <summary>The server's settings say whether old accounts may still be carried over (switched off before any public test).</summary>
        public static bool CarryOverEnabled { get; set; } = true;

        /// <summary>Raised when an account was carried over (old id, new id): world documents that name owners follow it.</summary>
        public static event Action<string, string> AccountCarried;

        private const string CarriedDocument = "carried-accounts";

        [Serializable]
        private sealed class CarriedAccounts
        {
            public List<string> old = new List<string>();
        }

        /// <summary>
        /// Moves the account a game played under before Unity sign-in (<paramref name="oldId"/>, made up by the game) to the
        /// account its sign-in proves (<paramref name="newId"/>), once (owner, 18 Oct 2026: carry the test saves over).
        /// Only when the proven account has no saves of its own, the old one is not in the world, and no one has carried
        /// it over before; the old document stays as it was, as a backup. True when it moved.
        /// </summary>
        public static bool CarryOverOnce(string oldId, string newId)
        {
            if (!HasAuthority || !CarryOverEnabled || !SaveKeys.IsId(oldId) || !SaveKeys.IsId(newId) || oldId == newId) return false;
            if (BoundAccounts.ContainsKey(oldId) || BoundAccounts.ContainsKey(newId)) return false;
            CarriedAccounts carried = World<CarriedAccounts>(CarriedDocument);
            if (carried.old.Contains(oldId)) return false;
            if (Store.TryRead(AccountsPrefix + newId, out _)) return false; // the proven account has saves of its own
            if (!Store.TryRead(AccountsPrefix + oldId, out _)) return false; // nothing to carry
            AccountSave account = LoadAccount(oldId);
            account.accountId = newId;
            if (!WriteAccount(account)) return false;
            carried.old.Add(oldId);
            SaveWorld(CarriedDocument);
            Debug.Log($"[Save] Carried account {oldId} over to the signed-in account {newId} ({account.characters.Count} character(s)).");
            AccountCarried?.Invoke(oldId, newId);
            return true;
        }

        // ------------------------------------------------------------------ world documents

        /// <summary>A world-wide document ("crews", "kiosks"), loaded once and kept. Never null.</summary>
        public static T World<T>(string name) where T : class, new()
        {
            string key = WorldPrefix + name;
            if (WorldDocuments.TryGetValue(key, out object cached) && cached is T typed) return typed;

            T document = null;
            if (Store.TryRead(key, out string json))
            {
                try
                {
                    document = JsonUtility.FromJson<T>(json);
                    LastWritten[key] = json;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Save] World document '{name}' could not be read and was set aside: {e.Message}");
                    Store.Write(key + "-unreadable-" + DateTime.UtcNow.Ticks, json);
                }
            }
            document ??= new T();
            WorldDocuments[key] = document;
            return document;
        }

        /// <summary>Writes a world document now, if it changed.</summary>
        public static void SaveWorld(string name)
        {
            string key = WorldPrefix + name;
            if (HasAuthority && WorldDocuments.TryGetValue(key, out object document)) WriteIfChanged(key, JsonUtility.ToJson(document));
        }

        // ------------------------------------------------------------------ everything

        /// <summary>Saves every loaded character and every world document. Called on quit; safe to call any time.</summary>
        public static void FlushAll()
        {
            if (!HasAuthority) return;
            using var timed = KryticalNova.Diagnostics.PerformanceLog.Time("save.all");
            for (int i = Agents.Count - 1; i >= 0; i--)
                if (Agents[i] != null) Agents[i].Save();
            foreach (var pair in WorldDocuments) WriteIfChanged(pair.Key, JsonUtility.ToJson(pair.Value));
            WaitForDisk();
        }

        /// <summary>Waits until every save handed to the store is on disk. The game does on quitting; a test may at any time.</summary>
        public static void WaitForDisk()
        {
            (store as FileSaveStore)?.Flush();
            (store as ServiceSaveStore)?.Flush();
        }

        /// <summary>GM: deletes every save in the store. Characters in the world are released first and keep playing unsaved.</summary>
        public static int WipeAll()
        {
            if (!HasAuthority) return 0;
            for (int i = Agents.Count - 1; i >= 0; i--)
                if (Agents[i] != null) Agents[i].ReleaseWithoutSaving();
            int deleted = 0;
            foreach (string key in Store.Keys(string.Empty))
            {
                Store.Delete(key);
                deleted++;
            }
            WorldDocuments.Clear();
            LastWritten.Clear();
            PartKeysOf.Clear();
            return deleted;
        }

        private static bool WriteIfChanged(string key, string json)
        {
            if (LastWritten.TryGetValue(key, out string last) && last == json) return false;
            Store.Write(key, json);
            LastWritten[key] = json;
            return true;
        }

        // ------------------------------------------------------------------ who is loaded

        /// <summary>
        /// Claims an account for an agent. An offline avatar gives way to the networked player that replaces it;
        /// a second player on an account that is already in the world is refused.
        /// </summary>
        internal static bool TryBind(PlayerSaveAgent agent, string accountId, out string refusal)
        {
            refusal = null;
            if (BoundAccounts.TryGetValue(accountId, out PlayerSaveAgent holder) && holder != null && holder != agent)
            {
                if (holder.IsSpawned)
                {
                    refusal = "This account already has a character in the world.";
                    return false;
                }
                holder.SaveAndRelease();
            }
            // With the save service, one server at a time holds an account (online phase 4): another server's player is refused.
            if (store is ServiceSaveStore service && !BoundAccounts.ContainsKey(accountId) && !service.TryLock(AccountsPrefix + accountId, out string heldBy))
            {
                refusal = "This account is in the world on another server.";
                Debug.Log($"[Save] Account {accountId} is held by {heldBy}: the character was not loaded here.");
                return false;
            }
            BoundAccounts[accountId] = agent;
            if (!Agents.Contains(agent)) Agents.Add(agent);
            return true;
        }

        internal static void Release(PlayerSaveAgent agent, string accountId)
        {
            Agents.Remove(agent);
            if (!string.IsNullOrEmpty(accountId) && BoundAccounts.TryGetValue(accountId, out PlayerSaveAgent holder) && holder == agent)
            {
                BoundAccounts.Remove(accountId);
                (store as ServiceSaveStore)?.Unlock(AccountsPrefix + accountId);
            }
        }
    }
}
