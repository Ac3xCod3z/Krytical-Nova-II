using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KryticalNova.Player;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>Which record a line belongs to, and so which file beside the saves it goes in.</summary>
    public enum RecordKind : byte
    {
        /// <summary><c>gm.log</c>: every GM command, from wherever it was typed or pressed; every kick and ban.</summary>
        Gm,
        /// <summary><c>economy.log</c>: every trade, Kiosk sale, shop sale, Vault move of Creds, Temper, Fusion and Reforge.</summary>
        Economy,
        /// <summary><c>access.log</c>: every join, every game turned away and why, every player who left.</summary>
        Access,
    }

    /// <summary>
    /// The server's records (online phase 2, milestone 4; the "Logging" list of the security model): one line, with the
    /// time and the account, for each thing someone may later ask about. Each line is a server log line (the control
    /// page's live log shows it) and, on a dedicated server, a line in its file beside the saves (<see cref="RecordKind"/>).
    /// Lines are written only where the game is decided, while a session runs; playing alone writes nothing.
    /// </summary>
    public static class ServerRecords
    {
        private const int LongestAnswer = 200;
        private const string NoCharacter = "(no character yet)";
        private static readonly object Gate = new object();
        // Each player in the game as last named with a character, so the line of their leaving still names them after
        // their character has gone. Only players who were let in are here.
        private static readonly Dictionary<ulong, string> InGame = new Dictionary<ulong, string>();

        /// <summary>Where the files are written (the dedicated server's save folder); null: log lines only.</summary>
        public static string LogFolder { get; set; }

        /// <summary>
        /// Months of each record kept (the server's <c>recordMonths</c>, online phase 4: "kick the oldest so old data does
        /// not fill my disk"). Each month starts a new file; the month before is kept as gm-2026-10.log and so on.
        /// </summary>
        public static int MonthsKept { get; set; } = 6;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            LogFolder = null;
            MonthsKept = 6;
            InGame.Clear();
        }

        /// <summary>Access record: a player was let in.</summary>
        public static void Joined(ulong clientId)
        {
            InGame[clientId] = Who(clientId);
            Write(RecordKind.Access, "Joined: " + InGame[clientId]);
        }

        /// <summary>Access record: a player who was in the game left (a game that was turned away was never in: no line).</summary>
        public static void Left(ulong clientId)
        {
            if (!InGame.TryGetValue(clientId, out string last)) return;
            InGame.Remove(clientId);
            string now = Who(clientId);
            Write(RecordKind.Access, "Left: " + (now.StartsWith(NoCharacter, StringComparison.Ordinal) ? last : now));
        }

        /// <summary>True on a host or dedicated server while a session runs: the only place records are kept.</summary>
        public static bool Keeping
        {
            get
            {
                NetworkManager network = NetworkManager.Singleton;
                return network != null && network.IsListening && network.IsServer;
            }
        }

        /// <summary>Writes one line to the record of <paramref name="kind"/>.</summary>
        public static void Write(RecordKind kind, string line)
        {
            if (!Keeping || string.IsNullOrEmpty(line)) return;
            Debug.Log(Prefix(kind) + line);
            WriteFile(LogFolder, kind, line, DateTime.Now);
        }

        /// <summary>The file a record goes in.</summary>
        public static string FileName(RecordKind kind)
        {
            switch (kind)
            {
                case RecordKind.Gm: return "gm.log";
                case RecordKind.Economy: return "economy.log";
                default: return "access.log";
            }
        }

        /// <summary>Appends a line, stamped with <paramref name="time"/>, to the record's file in <paramref name="folder"/>.</summary>
        public static void WriteFile(string folder, RecordKind kind, string line, DateTime time)
        {
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                lock (Gate)
                    File.AppendAllText(CurrentFile(folder, FileName(kind), time, MonthsKept),
                        time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + OneLine(line) + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Server] {FileName(kind)} could not be written: {e.Message}");
            }
        }

        /// <summary>
        /// The file a record line goes in now (<paramref name="name"/>, "gm.log", in <paramref name="folder"/>). When the
        /// file was last written in an earlier month it is first kept as "gm-2026-09.log", and only the newest
        /// <paramref name="monthsKept"/> months stay (this one counts): the oldest go.
        /// </summary>
        public static string CurrentFile(string folder, string name, DateTime now, int monthsKept)
        {
            lock (Gate)
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, name);
                if (!File.Exists(path)) return path;
                DateTime last = File.GetLastWriteTime(path);
                if (last.Year == now.Year && last.Month == now.Month) return path;
                string stem = Path.GetFileNameWithoutExtension(name), extension = Path.GetExtension(name);
                string kept = Path.Combine(folder, $"{stem}-{last:yyyy-MM}{extension}");
                if (File.Exists(kept))
                {
                    File.AppendAllText(kept, File.ReadAllText(path));
                    File.Delete(path);
                }
                else File.Move(path, kept);
                var months = new List<string>(Directory.GetFiles(folder, stem + "-????-??" + extension));
                months.Sort(StringComparer.Ordinal);
                for (int i = 0; i < months.Count - Math.Max(0, monthsKept - 1); i++) File.Delete(months[i]);
                return path;
            }
        }

        /// <summary>"Tolan (account 3f2a…full id…, connection 4)": the character, the whole account id and the connection.</summary>
        public static string Who(Component player)
        {
            if (player == null) return "(nobody)";
            string character = player.TryGetComponent(out PlayerIdentity identity) && !string.IsNullOrEmpty(identity.CharacterName)
                ? identity.CharacterName : NoCharacter;
            string account = identity != null && !string.IsNullOrEmpty(identity.AccountId) ? identity.AccountId : "(none)";
            ulong connection = player.TryGetComponent(out NetworkObject networkObject) && networkObject.IsSpawned ? networkObject.OwnerClientId : 0;
            return $"{character} (account {account}, connection {connection})";
        }

        /// <summary>The player of a connection, as <see cref="Who(Component)"/> writes it; the account alone before a character.</summary>
        public static string Who(ulong clientId)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
            {
                string who = Who(client.PlayerObject);
                if (InGame.ContainsKey(clientId) && !who.StartsWith(NoCharacter, StringComparison.Ordinal)) InGame[clientId] = who;
                return who;
            }
            if (InGame.TryGetValue(clientId, out string last) && !last.StartsWith(NoCharacter, StringComparison.Ordinal)) return last;
            string account = ServerAccounts.TryGet(clientId, out ServerAccounts.Entry entry) ? entry.Account : "(not signed in)";
            return $"{NoCharacter} (account {account}, connection {clientId})";
        }

        /// <summary>An answer cut to its first line and <see cref="LongestAnswer"/> characters, for the record.</summary>
        public static string Short(string answer)
        {
            if (string.IsNullOrEmpty(answer)) return "Done.";
            string first = answer.Trim();
            int end = first.IndexOfAny(new[] { '\r', '\n' });
            if (end >= 0) first = first.Substring(0, end) + " …";
            return first.Length > LongestAnswer ? first.Substring(0, LongestAnswer) + "…" : first;
        }

        private static string Prefix(RecordKind kind)
        {
            switch (kind)
            {
                case RecordKind.Gm: return "[GM log] ";
                case RecordKind.Economy: return "[Economy] ";
                default: return "[Access] ";
            }
        }

        // One record, one line: a reason typed with a line break stays on its line.
        private static string OneLine(string line) => line.Replace("\r", " ").Replace("\n", " ");
    }
}
