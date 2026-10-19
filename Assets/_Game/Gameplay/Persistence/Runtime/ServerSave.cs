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
            PendingLedger.Clear();
            WorldEntryKeys.Clear();
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
                WorldEntryKeys.Clear();
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
        // accounts/<id>/<character>/<part>   one part of one of its characters (the Pack, the wallet, the skills, ...)
        // A character's parts live inside its account: a character id comes from the player's game, so a part reached
        // by character id alone could be another account's (and an account carried over keeps its characters' ids).
        // A save writes only the parts whose text changed, and the header, as one change where the store can (the save
        // service); a part a system no longer saves is deleted. An account saved before this (version 1, one document
        // holding everything) is read as it is, kept once as accounts/<id>-v1, and written in the new layout by its next
        // save.
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

        /// <summary>Where one part of one character of an account is kept.</summary>
        public static string CharacterPartKey(string accountId, string characterId, string part) =>
            AccountsPrefix + accountId + "/" + characterId + "/" + PartName(part);

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
                foreach (string part in c.partKeys ?? new List<string>()) wanted.Add(CharacterPartKey(accountId, c.characterId, part));

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
                    if (found.TryGetValue(CharacterPartKey(accountId, c.characterId, part), out string text)) { character.parts.Add(new SavePart { key = part, json = text }); Remember(CharacterPartKey(accountId, c.characterId, part), text, present); }
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
        /// Writes the parts of the account that changed since they were last written, its header, and the ledger lines
        /// waiting for it, as one change. Returns true when anything was written.
        /// </summary>
        public static bool WriteAccount(AccountSave account)
        {
            var batch = new SaveBatch();
            Action written = Collect(account, batch);
            if (written == null || batch.IsEmpty) return false;
            Send(batch);
            written();
            return true;
        }

        // Puts what changed in the account (and its waiting ledger lines) into the batch; returns what to remember once the
        // batch is handed to the store, or null for an account that cannot be saved.
        private static Action Collect(AccountSave account, SaveBatch batch)
        {
            if (account == null || !SaveKeys.IsId(account.accountId)) return null;
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
            batch.Writes.AddRange(writes);
            batch.Deletes.AddRange(deletes);
            if (PendingLedger.TryGetValue(id, out List<LedgerEntry> lines))
            {
                batch.Ledger.AddRange(lines);
                PendingLedger.Remove(id);
            }
            return () =>
            {
                foreach (KeyValuePair<string, string> write in writes) LastWritten[write.Key] = write.Value;
                foreach (string key in deletes) LastWritten.Remove(key);
                PartKeysOf[id] = keep;
            };
        }

        // One change to the store: whole where it can (the save service, files through their journal).
        private static void Send(SaveBatch batch)
        {
            if (Store is IBatchSaveStore whole)
            {
                whole.WriteBatch(batch);
                return;
            }
            foreach (KeyValuePair<string, string> write in batch.Writes) Store.Write(write.Key, write.Value);
            foreach (string key in batch.Deletes) Store.Delete(key);
            if (batch.Ledger.Count > 0) Debug.LogWarning($"[Save] This store keeps no ledger: {batch.Ledger.Count} line(s) were not kept.");
        }

        // ------------------------------------------------------------------ changes saved together, and the ledger (online phase 4, milestone 3)

        // Ledger lines waiting for the next save of their account: they are saved with the change they describe, never
        // apart from it, so a crash loses both or neither and the ledger always agrees with the saves.
        private static readonly Dictionary<string, List<LedgerEntry>> PendingLedger = new Dictionary<string, List<LedgerEntry>>(StringComparer.Ordinal);

        /// <summary>
        /// Records a ledger line (what an account gained or lost). It is saved with the next save of its account: call
        /// <see cref="PlayerSaveAgent.Save"/> or <see cref="SaveTogether"/> after the change it describes. A line for an
        /// account that is not loaded here is saved at once.
        /// </summary>
        public static void Ledger(LedgerEntry line)
        {
            if (!HasAuthority || line == null || !SaveKeys.IsId(line.account)) return;
            if (!BoundAccounts.ContainsKey(line.account) && !PartKeysOf.ContainsKey(line.account))
            {
                var batch = new SaveBatch();
                batch.Ledger.Add(line);
                Send(batch);
                return;
            }
            if (!PendingLedger.TryGetValue(line.account, out List<LedgerEntry> lines)) PendingLedger[line.account] = lines = new List<LedgerEntry>();
            lines.Add(line);
        }

        /// <summary>
        /// Adds to the waiting line of the same kind and character, or starts one: many small movements (every Cred a
        /// wallet earns, every pickup) become one line per save, not one each.
        /// </summary>
        public static void Tally(LedgerEntry line, long creds, int itemId, int count, string itemName)
        {
            if (!HasAuthority || line == null || !SaveKeys.IsId(line.account)) return;
            if (PendingLedger.TryGetValue(line.account, out List<LedgerEntry> lines))
                foreach (LedgerEntry waiting in lines)
                {
                    if (waiting.kind != line.kind || waiting.character != line.character || waiting.note != TallyNote) continue;
                    waiting.creds += creds;
                    waiting.utc = line.utc;
                    AddItem(waiting, itemId, count, itemName);
                    return;
                }
            line.creds = creds;
            line.note = TallyNote;
            AddItem(line, itemId, count, itemName);
            Ledger(line);
        }

        // A tally line says so, so a GM reading it knows it sums a while (up to a save) rather than one moment.
        private const string TallyNote = "since the last save";

        private static void AddItem(LedgerEntry line, int itemId, int count, string itemName)
        {
            if (itemId == 0 || count == 0) return;
            for (int i = 0; i < line.items.Count; i++)
                if (line.items[i].item == itemId)
                {
                    LedgerItem item = line.items[i];
                    item.count += count;
                    line.items[i] = item;
                    return;
                }
            line.items.Add(new LedgerItem { item = itemId, count = count, name = itemName });
        }

        /// <summary>True when ledger lines wait for the account's next save.</summary>
        public static bool HasPendingLedger(string accountId) => !string.IsNullOrEmpty(accountId) && PendingLedger.ContainsKey(accountId);

        /// <summary>
        /// Saves several players and world documents as one change (online phase 4, milestone 3): a trade (both players), a
        /// Kiosk sale (the buyer and the Kiosks), a Crew founding (the founder and the Crews). On the save service it is
        /// one transaction; in files, one journaled change. A crash loses all of it or none: an item can never be on both
        /// sides of a trade, nor on neither. Their waiting ledger lines go in the same change.
        /// <para>Any economy action that changes more than one player or world document saves this way (owner, 18 Oct 2026:
        /// the economy will be reworked; the rework keeps this).</para>
        /// </summary>
        public static void SaveTogether(IReadOnlyList<PlayerSaveAgent> players, params string[] worlds)
        {
            if (!HasAuthority) return;
            var accounts = new List<AccountSave>();
            if (players != null)
                foreach (PlayerSaveAgent agent in players)
                {
                    if (agent == null || !agent.IsLoaded) continue;
                    agent.CaptureForSave();
                    accounts.Add(agent.Account);
                }
            WriteTogether(accounts, worlds);
        }

        /// <summary>
        /// The accounts' changes, their waiting ledger lines and the world documents that changed, as one change: what
        /// <see cref="SaveTogether"/> writes once the players' state is gathered. True when anything was written.
        /// </summary>
        public static bool WriteTogether(IReadOnlyList<AccountSave> accountsToWrite, params string[] worlds)
        {
            if (!HasAuthority) return false;
            var batch = new SaveBatch();
            var written = new List<Action>();
            var accounts = new HashSet<string>(StringComparer.Ordinal);
            if (accountsToWrite != null)
                foreach (AccountSave account in accountsToWrite)
                {
                    if (account == null || !accounts.Add(account.accountId)) continue;
                    Action done = Collect(account, batch);
                    if (done != null) written.Add(done);
                }
            foreach (string name in worlds ?? Array.Empty<string>())
            {
                Action done = CollectWorld(name, batch);
                if (done != null) written.Add(done);
            }
            if (batch.IsEmpty) return false;
            Send(batch);
            foreach (Action done in written) done();
            return true;
        }

        /// <summary>The same for one player and world documents (a Kiosk sale: the buyer and the Kiosks).</summary>
        public static void SaveTogether(PlayerSaveAgent player, params string[] worlds) =>
            SaveTogether(player != null ? new[] { player } : Array.Empty<PlayerSaveAgent>(), worlds);

        /// <summary>The ledger lines of an account (null: every account) since <paramref name="sinceUtc"/>, newest first.</summary>
        public static List<LedgerEntry> ReadLedger(string accountId, long sinceUtc, int limit)
        {
            // Lines still waiting for their account's save are not in the store yet; they are shown too.
            var lines = new List<LedgerEntry>();
            foreach (KeyValuePair<string, List<LedgerEntry>> waiting in PendingLedger)
                if (accountId == null || waiting.Key == accountId)
                    foreach (LedgerEntry line in waiting.Value)
                        if (line.utc >= sinceUtc) lines.Add(line);
            if (Store is ILedgerStore ledger) lines.AddRange(ledger.ReadLedger(accountId, sinceUtc, limit));
            lines.Sort((x, y) => y.utc.CompareTo(x.utc));
            if (lines.Count > limit) lines.RemoveRange(limit, lines.Count - limit);
            return lines;
        }

        /// <summary>Drops ledger lines older than <paramref name="days"/> days. Returns how many went (-1: the store could not be asked).</summary>
        public static int PruneLedger(int days) =>
            Store is ILedgerStore ledger ? ledger.PruneLedger(DateTime.UtcNow.AddDays(-Math.Max(1, days)).Ticks) : 0;

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
                    keys.Add(CharacterPartKey(id, character.characterId, p.key));
                    part(CharacterPartKey(id, character.characterId, p.key), p.json);
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

        // ---- How a world document of entries is kept (online phase 4, milestone 4): one entry at a time.
        // world/<name>          the header: version 2 and the entries' keys, in order. No game state.
        // world/<name>/<entry>  one entry (a Crew, a character's Contacts, a Kiosk, a Sponsor link).
        // A save writes only the entries whose text changed, deletes the ones that went, and rewrites the header only when
        // the list of entries changed, as one change. So a change to one Crew writes that Crew alone, and two servers
        // changing two different Crews never write over each other. A document that is not a list of entries
        // (IWorldEntries) is kept whole as before (the bans, the carried accounts). One saved whole before this is read
        // as it is, kept once as world/<name>-v1, and written in entries by its next save.

        [Serializable]
        private sealed class WorldHeader
        {
            public int version = 2;
            public List<string> keys = new List<string>();
        }

        // The entry keys each world document of entries has in the store now, to know which ones a save must delete.
        private static readonly Dictionary<string, HashSet<string>> WorldEntryKeys = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>Where one entry of a world document of entries is kept.</summary>
        public static string WorldEntryKey(string name, string entry) => WorldPrefix + name + "/" + entry;

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
                    int version = JsonUtility.FromJson<VersionOnly>(json)?.version ?? 1;
                    document = version >= 2 && typeof(IWorldEntries).IsAssignableFrom(typeof(T))
                        ? ReadWorldEntries<T>(name, json)
                        : JsonUtility.FromJson<T>(json);
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

        // The header, then every entry it names, many in one trip where the store can.
        private static T ReadWorldEntries<T>(string name, string headerJson) where T : class, new()
        {
            WorldHeader header = JsonUtility.FromJson<WorldHeader>(headerJson);
            var wanted = new List<string>();
            foreach (string entry in header.keys ?? new List<string>()) wanted.Add(WorldEntryKey(name, entry));
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int start = 0; start < wanted.Count; start += 400)
            {
                List<string> chunk = wanted.GetRange(start, Math.Min(400, wanted.Count - start));
                if (Store is IBatchSaveStore batch) batch.TryReadMany(chunk, found);
                else
                    foreach (string key in chunk)
                        if (Store.TryRead(key, out string text)) found[key] = text;
            }
            var texts = new List<string>(wanted.Count);
            var present = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in wanted)
            {
                if (!found.TryGetValue(key, out string text)) continue;
                texts.Add(text);
                present.Add(key);
                LastWritten[key] = text;
            }
            if (texts.Count < wanted.Count)
                Debug.LogWarning($"[Save] World document '{name}': {wanted.Count - texts.Count} of its {wanted.Count} entries were not found.");
            var document = new T();
            ((IWorldEntries)document).RestoreEntries(texts);
            WorldEntryKeys[WorldPrefix + name] = present;
            return document;
        }

        /// <summary>Writes a world document now, if it changed: only the entries that changed, as one change.</summary>
        public static void SaveWorld(string name)
        {
            if (!HasAuthority) return;
            var batch = new SaveBatch();
            Action written = CollectWorld(name, batch);
            if (written == null || batch.IsEmpty) return;
            Send(batch);
            written();
        }

        // Puts what changed in a world document into the batch; returns what to remember once the batch is handed to the
        // store, or null when the document is not loaded.
        private static Action CollectWorld(string name, SaveBatch batch)
        {
            string key = WorldPrefix + name;
            if (!WorldDocuments.TryGetValue(key, out object document)) return null;
            if (!(document is IWorldEntries entries))
            {
                string json = JsonUtility.ToJson(document);
                if (LastWritten.TryGetValue(key, out string last) && last == json) return () => { };
                batch.Write(key, json);
                return () => LastWritten[key] = json;
            }

            var listed = new List<KeyValuePair<string, string>>();
            entries.CollectEntries(listed);
            var header = new WorldHeader();
            var keep = new HashSet<string>(StringComparer.Ordinal);
            var writes = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> entry in listed)
            {
                string entryName = EntryName(entry.Key, keep, name);
                header.keys.Add(entryName);
                string entryKey = WorldEntryKey(name, entryName);
                keep.Add(entryKey);
                if (LastWritten.TryGetValue(entryKey, out string last) && last == entry.Value) continue;
                writes.Add(new KeyValuePair<string, string>(entryKey, entry.Value ?? string.Empty));
            }
            if (!WorldEntryKeys.TryGetValue(key, out HashSet<string> present)) present = new HashSet<string>(StringComparer.Ordinal);
            var deletes = new List<string>();
            foreach (string old in present)
                if (!keep.Contains(old)) deletes.Add(old);

            string headerJson = JsonUtility.ToJson(header);
            LastWritten.TryGetValue(key, out string lastHeader);
            // Saved whole before (milestone 3 and earlier): kept once, as it was, beside the entries.
            if (lastHeader != null && (JsonUtility.FromJson<VersionOnly>(lastHeader)?.version ?? 1) < 2 && !Store.TryRead(key + OldCopySuffix, out _))
            {
                batch.Write(key + OldCopySuffix, lastHeader);
                Debug.Log($"[Save] World document '{name}' was kept whole: kept as {key + OldCopySuffix}; it is saved one entry at a time from now on.");
            }
            batch.Writes.AddRange(writes);
            batch.Deletes.AddRange(deletes);
            bool headerChanged = lastHeader != headerJson;
            if (headerChanged) batch.Write(key, headerJson); // the header last: files read it last
            return () =>
            {
                foreach (KeyValuePair<string, string> write in writes) LastWritten[write.Key] = write.Value;
                foreach (string gone in deletes) LastWritten.Remove(gone);
                if (headerChanged) LastWritten[key] = headerJson;
                WorldEntryKeys[key] = keep;
            };
        }

        // An entry's key as a save key allows it, and never the same as another entry's of the document.
        private static string EntryName(string raw, HashSet<string> taken, string world)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (raw ?? string.Empty).ToLowerInvariant())
                sb.Append((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_' ? c : '_');
            string name = sb.Length > 0 ? sb.ToString() : "entry";
            if (name.Length > 60) name = name.Substring(0, 60);
            string candidate = name;
            for (int n = 2; taken.Contains(WorldEntryKey(world, candidate)); n++) candidate = name + "-" + n;
            return candidate;
        }

        // ------------------------------------------------------------------ everything

        /// <summary>Saves every loaded character and every world document. Called on quit; safe to call any time.</summary>
        public static void FlushAll()
        {
            if (!HasAuthority) return;
            using var timed = KryticalNova.Diagnostics.PerformanceLog.Time("save.all");
            for (int i = Agents.Count - 1; i >= 0; i--)
                if (Agents[i] != null) Agents[i].Save();
            var worlds = new SaveBatch();
            var worldsWritten = new List<Action>();
            foreach (string key in new List<string>(WorldDocuments.Keys))
            {
                Action done = CollectWorld(key.Substring(WorldPrefix.Length), worlds);
                if (done != null) worldsWritten.Add(done);
            }
            if (!worlds.IsEmpty)
            {
                Send(worlds);
                foreach (Action done in worldsWritten) done();
            }
            // Ledger lines of accounts no longer in the world (one that left before its line's save): kept, never dropped.
            if (PendingLedger.Count > 0)
            {
                var left = new SaveBatch();
                foreach (List<LedgerEntry> lines in PendingLedger.Values) left.Ledger.AddRange(lines);
                PendingLedger.Clear();
                Send(left);
            }
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
            PendingLedger.Clear();
            WorldEntryKeys.Clear();
            return deleted;
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
