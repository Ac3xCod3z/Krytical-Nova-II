#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// The rest of NGO's control page on the entity netcode's page (8 Nov 2026 port of ServerControlPage:319-416): save everyone, announce,
    /// ban / unban by a player from the list (or a character name or an account id), the bans in force, the live log and the security flags.
    /// The owner's only, as the GM tools are (this machine and the save service key, checked by <see cref="KnPageRules.Handle"/>); each button
    /// runs on the game's thread and goes in the GM record with its answer, as NGO's did. NGO's password, cookie and home-network login stay
    /// NGO's: this page is reached from the Server app's machine.
    /// </summary>
    public static class KnPageDesk
    {
        /// <summary>The server's side of the buttons (filled by the entity netcode's player side; empty, the page answers 503).</summary>
        public interface IDesk
        {
            /// <summary>NGO /api/save: every player and world document now.</summary>
            string Save();
            /// <summary>NGO /api/announce: news for every player on the server.</summary>
            string Announce(string text);
            /// <summary>NGO /api/ban: <paramref name="playerKey"/> from the player list, or else <paramref name="who"/> (a character or an account id).</summary>
            string Ban(string who, ulong playerKey, string length, string reason, string from);
            /// <summary>NGO /api/unban: by account id or the character it was put on.</summary>
            string Unban(string who);
            /// <summary>The bans in force: <c>[{"account":..,"character":..,"by":..,"reason":..,"untilUtc":..}]</c> (0 = for good).</summary>
            string BansJson();
            /// <summary>NGO /api/flags: the security flags after an id.</summary>
            string FlagsJson(long after);
            /// <summary>The GM record line of a button press and its answer (NGO ServerRecords.Write(Gm, ...)).</summary>
            void Record(string pressed, string answer);
        }

        public static IDesk Desk;

        public const string SavePath = "/api/save", AnnouncePath = "/api/announce", BanPath = "/api/ban", UnbanPath = "/api/unban",
            BansPath = "/api/bans", LogPath = "/api/log", FlagsPath = "/api/flags";
        public const string From = "Control page (this machine)";

        public static bool Handles(string path) =>
            path == SavePath || path == AnnouncePath || path == BanPath || path == UnbanPath || path == BansPath || path == LogPath || path == FlagsPath;

        [Serializable]
        private sealed class Body
        {
            public string text = string.Empty;
            public string name = string.Empty;
            public string player = string.Empty;
            public string length = string.Empty;
            public string reason = string.Empty;
        }

        /// <summary>The call, after the owner's check.</summary>
        public static KnPageRules.Reply Handle(string method, string path, string query, string body)
        {
            bool get = method == "GET" || method == "HEAD";
            if (path == LogPath)
                return get ? new KnPageRules.Reply(200, "application/json", PageLog.After(After(query)), false) : new KnPageRules.Reply(405, null, string.Empty, false);
            IDesk desk = Desk;
            if (desk == null) return new KnPageRules.Reply(503, "application/json", "{\"error\":\"The server's tools are not ready yet.\"}", false);
            if (path == BansPath)
                return get ? new KnPageRules.Reply(200, "application/json", string.Empty, false, 0, desk.BansJson, true) : new KnPageRules.Reply(405, null, string.Empty, false);
            if (path == FlagsPath)
            {
                long after = After(query);
                return get ? new KnPageRules.Reply(200, "application/json", string.Empty, false, 0, () => desk.FlagsJson(after), true)
                    : new KnPageRules.Reply(405, null, string.Empty, false);
            }
            if (method != "POST") return new KnPageRules.Reply(405, null, string.Empty, false);
            Body b;
            try { b = string.IsNullOrWhiteSpace(body) ? new Body() : JsonUtility.FromJson<Body>(body) ?? new Body(); }
            catch (Exception) { return new KnPageRules.Reply(400, "application/json", "{\"error\":\"The request is not JSON.\"}", false); }
            string text = (b.text ?? string.Empty).Trim(), name = (b.name ?? string.Empty).Trim();
            switch (path)
            {
                case SavePath:
                    return Work(() => Pressed(desk, "save", desk.Save()));
                case AnnouncePath:
                    if (text.Length == 0) return KnPageRules.Answer("Type the announcement first.");
                    if (text.Length > 500) text = text.Substring(0, 500);
                    return Work(() => Pressed(desk, "announce " + text, desk.Announce(text)));
                case BanPath:
                    ulong.TryParse(b.player ?? string.Empty, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong key);
                    if (key == 0 && name.Length == 0) return KnPageRules.Answer("Pick the player from the player list, or name a character or an account id.");
                    string length = (b.length ?? string.Empty).Trim(), reason = (b.reason ?? string.Empty).Trim();
                    return Work(() => Pressed(desk, $"ban {(key != 0 ? "player " + key : name)} {length} {reason}".TrimEnd(), desk.Ban(name, key, length, reason, From)));
                case UnbanPath:
                    if (name.Length == 0) return KnPageRules.Answer("Name the ban: the character name or the account id.");
                    return Work(() => Pressed(desk, "unban " + name, desk.Unban(name)));
            }
            return new KnPageRules.Reply(404, "application/json", "{\"error\":\"No such call.\"}", false);
        }

        static KnPageRules.Reply Work(Func<string> work) => new KnPageRules.Reply(200, "application/json", string.Empty, false, 0, work);

        static string Pressed(IDesk desk, string pressed, string answer)
        {
            desk.Record(pressed, answer);
            return answer;
        }

        static long After(string query)
        {
            if (string.IsNullOrEmpty(query)) return 0;
            foreach (string pair in query.TrimStart('?').Split('&'))
                if (pair.StartsWith("after=", StringComparison.Ordinal) && long.TryParse(pair.Substring(6), out long after)) return after;
            return 0;
        }

        /// <summary>The server's log for the page: NGO's last 500 lines, errors marked, each with an id the page asks after.</summary>
        public static class PageLog
        {
            public const int Kept = 500;
            static readonly object Gate = new object();
            static readonly LinkedList<(long Id, string Text)> Lines = new LinkedList<(long, string)>();
            static long s_Next = 1;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
            static void Hook()
            {
                Application.logMessageReceivedThreaded -= OnLog;
                Application.logMessageReceivedThreaded += OnLog;
            }

            static void OnLog(string message, string stack, LogType type) => Add(message, type == LogType.Error || type == LogType.Exception);

            public static void Add(string message, bool error = false)
            {
                if (string.IsNullOrEmpty(message)) return;
                if (message.Length > 2000) message = message.Substring(0, 2000) + "...";
                if (error) message = "(error) " + message;
                lock (Gate)
                {
                    Lines.AddLast((s_Next++, message));
                    while (Lines.Count > Kept) Lines.RemoveFirst();
                }
            }

            public static string After(long after)
            {
                var sb = new StringBuilder("{\"lines\":[");
                long last = after;
                lock (Gate)
                {
                    bool first = true;
                    foreach (var (id, text) in Lines)
                    {
                        if (id <= after) continue;
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"id\":").Append(id).Append(",\"text\":").Append(ControlPageRules.Json(text)).Append('}');
                        last = id;
                    }
                }
                return sb.Append("],\"last\":").Append(last).Append('}').ToString();
            }

            public static void Reset()
            {
                lock (Gate) { Lines.Clear(); s_Next = 1; }
            }
        }
    }
}
#endif
