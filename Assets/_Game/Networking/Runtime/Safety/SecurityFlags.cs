using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using KryticalNova.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.Networking
{
    /// <summary>
    /// What a game did that a real game would not (online phase 2, milestone 3): a request over its speed limit, junk or
    /// oversized data, an impossible claim. Each flag says who (character and account), where (map and position), what
    /// was asked, why it was refused and what the server did. The control page's Flags section shows them live
    /// (<see cref="JsonAfter"/>); each is also a server log line and a line in <c>flags.log</c> beside the saves. The
    /// same thing again from the same game within <see cref="RepeatSeconds"/> adds to the flag's count instead of a new
    /// line, so a flood does not bury everything else.
    /// </summary>
    public static class SecurityFlags
    {
        public const float RepeatSeconds = 10f;
        private const int Kept = 300;

        public sealed class Flag
        {
            public long Id;
            public DateTime Time;
            public ulong ClientId;
            public string Character;
            public string Account;
            public string Map;
            public Vector3 Position;
            public string Call;
            public string What;
            public string Why;
            public string Action;
            public int Count;
            public float LastSeen;
        }

        private static readonly LinkedList<Flag> Flags = new LinkedList<Flag>();
        private static readonly object Gate = new object();
        private static long nextId;

        /// <summary>Where <c>flags.log</c> is written (the server's save folder); null: no file.</summary>
        public static string LogFolder { get; set; }

        /// <summary>Raised on the game's thread with each new or counted-again flag.</summary>
        public static event Action<Flag> Raised;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            lock (Gate) Flags.Clear();
            nextId = 0;
            LogFolder = null;
        }

        /// <summary>
        /// Server: flags what client <paramref name="clientId"/> did. <paramref name="call"/> is the request's name,
        /// <paramref name="what"/> what it asked in plain words, <paramref name="why"/> why it was refused,
        /// <paramref name="action"/> what the server did ("Ignored", "Disconnected").
        /// </summary>
        public static void Raise(ulong clientId, string call, string what, string why, string action = "Ignored")
        {
            float now = Time.realtimeSinceStartup;
            Flag flag;
            bool repeat = false;
            lock (Gate)
            {
                flag = null;
                for (LinkedListNode<Flag> node = Flags.Last; node != null; node = node.Previous)
                {
                    Flag f = node.Value;
                    if (now - f.LastSeen > RepeatSeconds) break;
                    if (f.ClientId == clientId && f.Call == call && f.Why == why && f.Action == action)
                    {
                        flag = f;
                        Flags.Remove(node);
                        break;
                    }
                }
                if (flag != null)
                {
                    repeat = true;
                    flag.Count++;
                }
                else
                {
                    flag = new Flag { ClientId = clientId, Call = call, What = what, Why = why, Action = action, Count = 1 };
                    Describe(flag);
                }
                flag.Id = ++nextId;          // moved to the end: the page shows it again with its new count
                flag.Time = DateTime.Now;
                flag.LastSeen = now;
                Flags.AddLast(flag);
                while (Flags.Count > Kept) Flags.RemoveFirst();
            }

            string line = Line(flag);
            // A repeat is counted, not logged again each time: the line goes out on the 1st, 10th, 100th...
            if (!repeat || IsRound(flag.Count))
            {
                Debug.LogWarning("[Flag] " + line);
                WriteFile(line);
            }
            Raised?.Invoke(flag);
        }

        /// <summary>Who and where: the character, account, map and position of the client's player right now.</summary>
        private static void Describe(Flag flag)
        {
            flag.Character = "(no character yet)";
            flag.Account = ServerAccounts.TryGet(flag.ClientId, out ServerAccounts.Entry entry) ? entry.Account : "(not signed in)";
            flag.Map = SceneManager.GetActiveScene().name;
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.ConnectedClients.TryGetValue(flag.ClientId, out NetworkClient client) && client.PlayerObject != null)
            {
                Transform body = client.PlayerObject.transform;
                flag.Position = body.position;
                flag.Map = body.gameObject.scene.name;
                if (body.TryGetComponent(out PlayerIdentity identity) && !string.IsNullOrEmpty(identity.CharacterName))
                    flag.Character = identity.CharacterName;
            }
        }

        private static bool IsRound(int count) => count == 10 || count == 100 || count == 1000 || count % 10000 == 0;

        private static string Line(Flag f) =>
            $"{f.Character} (account {Short(f.Account)}, connection {f.ClientId}) in {f.Map} at ({f.Position.x:0}, {f.Position.y:0}, {f.Position.z:0}): " +
            $"{f.What} [{f.Call}]. {f.Why}. {f.Action}." + (f.Count > 1 ? $" x{f.Count}" : string.Empty);

        private static string Short(string account) => account != null && account.Length > 8 ? account.Substring(0, 8) + "…" : account;

        private static void WriteFile(string line)
        {
            if (string.IsNullOrEmpty(LogFolder)) return;
            try
            {
                File.AppendAllText(ServerRecords.CurrentFile(LogFolder, "flags.log", DateTime.Now, ServerRecords.MonthsKept),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + line + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Flag] flags.log could not be written: " + e.Message);
            }
        }

        /// <summary>The flags after <paramref name="after"/> (an id the page last saw), as the control page's JSON.</summary>
        public static string JsonAfter(long after)
        {
            var sb = new StringBuilder("{\"flags\":[");
            long last = after;
            lock (Gate)
            {
                bool first = true;
                foreach (Flag f in Flags)
                {
                    if (f.Id <= after) continue;
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":").Append(f.Id)
                      .Append(",\"time\":").Append(Json(f.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture)))
                      .Append(",\"character\":").Append(Json(f.Character))
                      .Append(",\"account\":").Append(Json(f.Account))
                      .Append(",\"connection\":").Append(f.ClientId)
                      .Append(",\"map\":").Append(Json(f.Map))
                      .Append(",\"where\":").Append(Json($"{f.Position.x:0}, {f.Position.y:0}, {f.Position.z:0}"))
                      .Append(",\"call\":").Append(Json(f.Call))
                      .Append(",\"what\":").Append(Json(f.What))
                      .Append(",\"why\":").Append(Json(f.Why))
                      .Append(",\"action\":").Append(Json(f.Action))
                      .Append(",\"count\":").Append(f.Count).Append('}');
                    last = f.Id;
                }
            }
            return sb.Append("],\"last\":").Append(last).Append('}').ToString();
        }

        private static string Json(string text)
        {
            if (text == null) return "\"\"";
            var sb = new StringBuilder("\"");
            foreach (char c in text)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
