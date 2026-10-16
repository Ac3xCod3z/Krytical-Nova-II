using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>One Game Master the server knows: a name players see and the password that unlocks GM in the game.</summary>
    [Serializable]
    public sealed class GmEntry
    {
        public string name = string.Empty;
        public string password = string.Empty;
    }

    /// <summary>
    /// Who may act as a Game Master (owner, 16 Oct 2026; a stand-in until Unity sign-in proves who a player is). The host
    /// of a game is always GM. On a dedicated server up to <see cref="MaxGameMasters"/> GMs are set on the control page
    /// (saved in server-settings.json), each with a password of their own that they type once per session in the GM
    /// console (F2). An account id is not trusted for this: a game sends its own, so it could be faked. Five wrong
    /// passwords send that player away. Runs where the game is decided.
    /// </summary>
    public static class GmRoster
    {
        public const int MaxGameMasters = 3;
        public const int MinPasswordLength = 8;
        public const int MaxNameLength = 20;
        public const int WrongTriesBeforeKick = 5;

        private static readonly List<GmEntry> Entries = new List<GmEntry>();
        private static readonly Dictionary<ulong, string> Active = new Dictionary<ulong, string>();
        private static readonly Dictionary<ulong, int> Wrong = new Dictionary<ulong, int>();
        private static bool loaded;

        /// <summary>Raised on the server when a GM is removed while signed in, with their connection.</summary>
        public static event Action<ulong> Revoked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Entries.Clear();
            Active.Clear();
            Wrong.Clear();
            loaded = false;
        }

        /// <summary>The GMs this server knows (names only are ever shown).</summary>
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
                    if (entry != null && ValidateName(entry.name) == null && !string.IsNullOrEmpty(entry.password) && Entries.Count < MaxGameMasters)
                        Entries.Add(entry);
#endif
        }

        /// <summary>Replaces the list (the control page, tests). Signed-in GMs no longer on it lose GM.</summary>
        public static void SetAll(IEnumerable<GmEntry> entries)
        {
            loaded = true;
            Entries.Clear();
            foreach (GmEntry entry in entries)
                if (entry != null && Entries.Count < MaxGameMasters) Entries.Add(entry);
            foreach (ulong client in new List<ulong>(Active.Keys))
                if (Find(Active[client]) == null) Sign(client, null);
        }

        /// <summary>The GM of that name, or null.</summary>
        public static GmEntry Find(string name)
        {
            foreach (GmEntry entry in All)
                if (string.Equals(entry.name, name, StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }

        /// <summary>The host's own player is always GM; on a server, a player who typed a GM password this session.</summary>
        public static bool IsGm(ulong clientId)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsHost && clientId == NetworkManager.ServerClientId) return true;
            return Active.ContainsKey(clientId);
        }

        /// <summary>The GM name a connection signed in as, or null.</summary>
        public static string NameOf(ulong clientId) => Active.TryGetValue(clientId, out string name) ? name : null;

        /// <summary>True while a GM of that name is signed in.</summary>
        public static bool IsSignedIn(string name)
        {
            foreach (string active in Active.Values)
                if (string.Equals(active, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Checks a GM password for a connection. True and the GM's name when it is one; otherwise a message, and
        /// <paramref name="kick"/> once the connection has used up its tries.
        /// </summary>
        public static bool TryLogin(ulong clientId, string password, out string name, out string message, out bool kick)
        {
            kick = false;
            name = Match(All, password);
            if (name != null)
            {
                Wrong.Remove(clientId);
                Sign(clientId, name);
                message = $"You are GM ({name}). GM commands now run on the server; gm.help lists them.";
                return true;
            }
            Wrong.TryGetValue(clientId, out int wrong);
            Wrong[clientId] = ++wrong;
            int left = WrongTriesBeforeKick - wrong;
            kick = left <= 0;
            message = kick ? "Wrong GM password. Too many tries: you are sent away from the server."
                           : $"Wrong GM password. {left} tr{(left == 1 ? "y" : "ies")} left.";
            return false;
        }

        /// <summary>The name of the entry whose password this is, or null. Every entry is compared, in full.</summary>
        public static string Match(IReadOnlyList<GmEntry> entries, string password)
        {
            if (string.IsNullOrEmpty(password) || entries == null) return null;
            string found = null;
            foreach (GmEntry entry in entries)
                if (entry != null && SecretEquals(entry.password, password) && found == null) found = entry.name;
            return found;
        }

        /// <summary>Forgets a connection that left.</summary>
        public static void Forget(ulong clientId)
        {
            Active.Remove(clientId);
            Wrong.Remove(clientId);
        }

        private static void Sign(ulong clientId, string name)
        {
            if (name == null)
            {
                if (Active.Remove(clientId)) Revoked?.Invoke(clientId);
                return;
            }
            Active[clientId] = name;
        }

        /// <summary>Why a GM could not be added (name, password, a full list), or null when it can.</summary>
        public static string ValidateNew(IReadOnlyList<GmEntry> entries, string name, string password)
        {
            string nameProblem = ValidateName(name);
            if (nameProblem != null) return nameProblem;
            if (entries.Count >= MaxGameMasters) return $"There are already {MaxGameMasters} GMs: remove one first.";
            foreach (GmEntry entry in entries)
            {
                if (string.Equals(entry.name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return $"There is already a GM called {entry.name}.";
                if (SecretEquals(entry.password, password)) return "Another GM has that password: each GM needs their own.";
            }
            if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength) return $"A GM password needs at least {MinPasswordLength} characters.";
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

        private static bool SecretEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            int diff = a.Length ^ b.Length;
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
                diff |= (i < a.Length ? a[i] : 0) ^ (i < b.Length ? b[i] : 0);
            return diff == 0;
        }
    }
}
