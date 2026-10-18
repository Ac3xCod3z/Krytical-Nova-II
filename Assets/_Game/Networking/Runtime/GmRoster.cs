using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>One Game Master the server knows: the name players see, and the account it is bound to.</summary>
    [Serializable]
    public sealed class GmEntry
    {
        public string name = string.Empty;
        /// <summary>The proven account (online phase 2) this GM plays under; empty until bound on the control page.</summary>
        public string account = string.Empty;
        /// <summary>Before phase 2 a GM typed a password in the game; kept only so older settings files still read.</summary>
        public string password = string.Empty;

        public bool IsBound => !string.IsNullOrEmpty(account);
    }

    /// <summary>
    /// Who may act as a Game Master. The host of a game is always GM. On a dedicated server up to
    /// <see cref="MaxGameMasters"/> GMs are set on the control page and saved in server-settings.json. Since online phase 2
    /// a GM is **bound to an account** (owner, 18 Oct 2026): on the control page the owner picks, beside a GM, the online
    /// player who is that GM; the account Unity's sign-in proved for that player (<see cref="ServerAccounts"/>) becomes the
    /// GM, at once and on every later join. No password is typed in the game, and the GM console opens for GMs only.
    /// Runs where the game is decided.
    /// </summary>
    public static class GmRoster
    {
        public const int MaxGameMasters = 3;
        public const int MaxNameLength = 20;

        private static readonly List<GmEntry> Entries = new List<GmEntry>();
        private static readonly Dictionary<ulong, string> Active = new Dictionary<ulong, string>();
        private static bool loaded;

        /// <summary>Raised on the server when a signed-in GM loses GM (removed or unbound), with their connection.</summary>
        public static event Action<ulong> Revoked;

        /// <summary>Raised on the server when a connection becomes GM: its connection and GM name.</summary>
        public static event Action<ulong, string> Granted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Entries.Clear();
            Active.Clear();
            loaded = false;
            Revoked = null;
            Granted = null;
        }

        /// <summary>The GMs this server knows.</summary>
        public static IReadOnlyList<GmEntry> All
        {
            get
            {
                EnsureLoaded();
                return Entries;
            }
        }

        private static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
#if UNITY_SERVER
            GmEntry[] fromSettings = ServerLaunchOptions.LoadForThisServer().gameMasters;
            if (fromSettings != null)
                foreach (GmEntry entry in fromSettings)
                    if (entry != null && ValidateName(entry.name) == null && Entries.Count < MaxGameMasters)
                        Entries.Add(entry);
#endif
        }

        /// <summary>Replaces the list (the control page, tests). Signed-in GMs no longer on it, or no longer bound to their account, lose GM.</summary>
        public static void SetAll(IEnumerable<GmEntry> entries)
        {
            loaded = true;
            Entries.Clear();
            foreach (GmEntry entry in entries)
                if (entry != null && Entries.Count < MaxGameMasters) Entries.Add(entry);
            foreach (ulong client in new List<ulong>(Active.Keys))
            {
                GmEntry still = Find(Active[client]);
                bool sameAccount = still != null && ServerAccounts.TryGet(client, out ServerAccounts.Entry proven) && proven.Account == still.account;
                if (still == null || !still.IsBound || !sameAccount) Sign(client, null);
            }
        }

        /// <summary>The GM of that name, or null.</summary>
        public static GmEntry Find(string name)
        {
            foreach (GmEntry entry in All)
                if (string.Equals(entry.name, name, StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }

        /// <summary>The GM bound to that account, or null.</summary>
        public static GmEntry FindByAccount(string account)
        {
            if (string.IsNullOrEmpty(account)) return null;
            foreach (GmEntry entry in All)
                if (entry.account == account) return entry;
            return null;
        }

        /// <summary>The host's own player is always GM; on a server, a player whose account is bound to a GM.</summary>
        public static bool IsGm(ulong clientId)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsHost && clientId == NetworkManager.ServerClientId) return true;
            return Active.ContainsKey(clientId);
        }

        /// <summary>The GM name a connection plays as, or null.</summary>
        public static string NameOf(ulong clientId) => Active.TryGetValue(clientId, out string name) ? name : null;

        /// <summary>True while a GM of that name is in the game.</summary>
        public static bool IsSignedIn(string name)
        {
            foreach (string active in Active.Values)
                if (string.Equals(active, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// A connection whose proven account is bound to a GM becomes that GM (on joining, or when the owner binds it).
        /// True and the GM's name when it is one.
        /// </summary>
        public static bool SignInBound(ulong clientId, out string name)
        {
            name = null;
            if (!ServerAccounts.TryGet(clientId, out ServerAccounts.Entry proven)) return false;
            GmEntry entry = FindByAccount(proven.Account);
            if (entry == null) return false;
            name = entry.name;
            if (NameOf(clientId) == entry.name) return true;
            Sign(clientId, entry.name);
            Granted?.Invoke(clientId, entry.name);
            return true;
        }

        /// <summary>Forgets a connection that left.</summary>
        public static void Forget(ulong clientId) => Active.Remove(clientId);

        private static void Sign(ulong clientId, string name)
        {
            if (name == null)
            {
                if (Active.Remove(clientId)) Revoked?.Invoke(clientId);
                return;
            }
            Active[clientId] = name;
        }

        /// <summary>Why a GM could not be added (name, a full list), or null when it can.</summary>
        public static string ValidateNew(IReadOnlyList<GmEntry> entries, string name)
        {
            string nameProblem = ValidateName(name);
            if (nameProblem != null) return nameProblem;
            if (entries.Count >= MaxGameMasters) return $"There are already {MaxGameMasters} GMs: remove one first.";
            foreach (GmEntry entry in entries)
                if (string.Equals(entry.name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return $"There is already a GM called {entry.name}.";
            return null;
        }

        /// <summary>Why a GM name will not do, or null.</summary>
        public static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Give the GM a name.";
            string trimmed = name.Trim();
            if (trimmed.Length > MaxNameLength) return $"A GM name is at most {MaxNameLength} characters.";
            foreach (char c in trimmed)
                if (!(char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')) return "A GM name may hold letters, digits, spaces, - and _.";
            return null;
        }
    }
}
