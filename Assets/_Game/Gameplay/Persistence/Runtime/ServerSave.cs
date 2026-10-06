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
            store = null;
            BoundAccounts.Clear();
            Agents.Clear();
            WorldDocuments.Clear();
            LastWritten.Clear();
            if (!quitHooked)
            {
                quitHooked = true;
                Application.quitting += FlushAll;
            }
        }

        /// <summary>Where saves are kept. Files under the game's data folder unless something else is set (tests, a database).</summary>
        public static ISaveStore Store
        {
            get => store ??= new FileSaveStore(DefaultRoot());
            set
            {
                store = value;
                WorldDocuments.Clear();
                LastWritten.Clear();
            }
        }

        /// <summary>Each local copy of the game (a Multiplayer Play Mode clone, a build started with -profile) keeps its own saves.</summary>
        public static string DefaultRoot()
        {
            string profile = KryticalNova.Networking.NetworkConnectionManager.LocalProfile();
            return Path.Combine(Application.persistentDataPath, FolderName, string.IsNullOrEmpty(profile) ? DefaultProfile : profile.ToLowerInvariant());
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
        public static AccountSave LoadAccount(string accountId)
        {
            string key = AccountsPrefix + accountId;
            AccountSave account = null;
            if (Store.TryRead(key, out string json))
            {
                try
                {
                    account = JsonUtility.FromJson<AccountSave>(json);
                    LastWritten[key] = json;
                }
                catch (Exception e)
                {
                    // Keep the unreadable file: a later save would otherwise replace it with an empty account.
                    Debug.LogError($"[Save] Account {accountId} could not be read and was set aside: {e.Message}");
                    Store.Write(key + "-unreadable-" + DateTime.UtcNow.Ticks, json);
                }
            }
            account ??= new AccountSave();
            account.accountId = accountId;
            account.parts ??= new List<SavePart>();
            account.characters ??= new List<CharacterSave>();
            return account;
        }

        /// <summary>Writes the account if it differs from what is on disk. Returns true when it wrote.</summary>
        public static bool WriteAccount(AccountSave account)
        {
            if (account == null || !SaveKeys.IsId(account.accountId)) return false;
            return WriteIfChanged(AccountsPrefix + account.accountId, JsonUtility.ToJson(account));
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
            for (int i = Agents.Count - 1; i >= 0; i--)
                if (Agents[i] != null) Agents[i].Save();
            foreach (var pair in WorldDocuments) WriteIfChanged(pair.Key, JsonUtility.ToJson(pair.Value));
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
            BoundAccounts[accountId] = agent;
            if (!Agents.Contains(agent)) Agents.Add(agent);
            return true;
        }

        internal static void Release(PlayerSaveAgent agent, string accountId)
        {
            Agents.Remove(agent);
            if (!string.IsNullOrEmpty(accountId) && BoundAccounts.TryGetValue(accountId, out PlayerSaveAgent holder) && holder == agent)
                BoundAccounts.Remove(accountId);
        }
    }
}
