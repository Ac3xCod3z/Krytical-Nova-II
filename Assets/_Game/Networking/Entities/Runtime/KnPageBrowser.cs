#if UNITY_SERVER || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// NGO's browser control page on the entity netcode's page port (8 Nov 2026 port of ServerControlPage.cs): the files of the
    /// <c>ControlPage</c> folder beside the server, the password login (cookie <c>kn_page</c>, 5 wrong passwords lock an address for 5 minutes:
    /// Core's <see cref="ControlPageRules.Lockout"/>), the home-network and Host-header checks, and for every button press the same-origin
    /// check plus the session's <c>X-KN-Check</c> token (ServerControlPage:279-337). The page's calls (the NGO JS, <c>ControlPage~/app.js</c>)
    /// are mapped onto the entity handlers (<see cref="KnPageRules.Handle"/> with <c>authorized</c>, <see cref="KnPageDesk"/>), so the owner's
    /// JSON API (loopback + <c>X-KN-Key</c>) is unchanged: a request that carries that header never comes here (<see cref="Wants"/>).
    /// A player is named by the PlayerKey of the player list (NGO's page named a connection id: the same field, <c>id</c>, holds the key here).
    /// Pure routing, no socket: <see cref="Route"/> is tested directly.
    /// </summary>
    public static class KnPageBrowser
    {
        /// <summary>What only the server's player side knows (filled by <c>KnPageBrowserHost</c> in Persistence.Entities; empty, the browser page is off).</summary>
        public interface IHost
        {
            /// <summary>The page password (made and written into server-settings.json when there was none, as NGO's EnsurePagePassword); empty = no browser page.</summary>
            string Password();
            /// <summary>The <c>ControlPage</c> folder beside the server.</summary>
            string Folder();
            /// <summary>Game thread. The members of NGO's <c>/api/status</c> that are not the player list or the countdown, without braces:
            /// <c>"port":..,"mapServer":..,"addresses":[..],"gms":[..],"bans":[..],"serverList":[..],"mode":..,...</c>.</summary>
            string StatusExtras();
            /// <summary>Game thread. The GM name a player signed in as, or empty.</summary>
            string GmNameOf(ulong playerKey);
            /// <summary>Game thread. NGO /api/backup.</summary>
            string Backup();
            /// <summary>Game thread. NGO /api/mode: saves the mode and restarts into it.</summary>
            string SwitchMode(string mode, float seconds);
            string ListAdd(string name, string address, int port);
            string ListRemove(string id);
            /// <summary>The request's own thread (it may take minutes): NGO /api/list/publish.</summary>
            string ListPublish();
        }

        public const string CookieName = "kn_page";
        const string SecurityHeaders = "X-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nReferrer-Policy: no-referrer\r\n" +
            "Content-Security-Policy: default-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'\r\n";

        public static IHost Host;
        public static readonly State Shared = new State();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Host = null;
            Shared.Clear();
        }

        static readonly Dictionary<string, string> Types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" }, { ".css", "text/css; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" },
            { ".ttf", "font/ttf" }, { ".otf", "font/otf" }, { ".png", "image/png" }, { ".svg", "image/svg+xml" }, { ".ico", "image/x-icon" },
        };

        /// <summary>Login state: wrong tries per address and the sessions (token to check token and end).</summary>
        public sealed class State
        {
            public readonly ControlPageRules.Lockout Lockout = new ControlPageRules.Lockout();
            readonly Dictionary<string, (string Check, DateTime Expires)> m_Sessions = new Dictionary<string, (string, DateTime)>();
            /// <summary>Slows guessing a little more after a wrong password (NGO: 400 ms). Tests set 0.</summary>
            public int WrongPasswordDelayMs = 400;

            public void Clear()
            {
                lock (m_Sessions) m_Sessions.Clear();
            }

            /// <summary>Opens a session: its cookie token (the check token is in <paramref name="check"/>).</summary>
            public string Open(DateTime now, out string check)
            {
                string token = ControlPageRules.NewToken();
                check = ControlPageRules.NewToken(16);
                lock (m_Sessions)
                {
                    var old = new List<string>();
                    foreach (var pair in m_Sessions) if (pair.Value.Expires <= now) old.Add(pair.Key);
                    foreach (string key in old) m_Sessions.Remove(key);
                    m_Sessions[token] = (check, now + ControlPageRules.SessionTime);
                }
                return token;
            }

            /// <summary>The session's check token, or null when the session is unknown or old.</summary>
            public string CheckOf(string token, DateTime now)
            {
                if (string.IsNullOrEmpty(token)) return null;
                lock (m_Sessions)
                {
                    if (!m_Sessions.TryGetValue(token, out var session)) return null;
                    if (session.Expires > now) return session.Check;
                    m_Sessions.Remove(token);
                    return null;
                }
            }

            public void Close(string token)
            {
                if (token == null) return;
                lock (m_Sessions) m_Sessions.Remove(token);
            }
        }

        [Serializable]
        sealed class PageBody
        {
            public string password = string.Empty, text = string.Empty, reason = string.Empty, line = string.Empty, id = string.Empty,
                name = string.Empty, address = string.Empty;
            public int port;
            public float seconds;
            public bool restart;
        }

        /// <summary>True when the browser page answers this request: a file of the page, or any <c>/api/</c> call that has no owner key header.</summary>
        public static bool Wants(ControlPageRules.Request request)
        {
            if (Host == null || request == null || request.Path == null) return false;
            if (!string.IsNullOrEmpty(request.Header(StopEndpointRules.KeyHeader))) return false;   // the owner's JSON API
            string path = request.Path;
            if (path.StartsWith("/api/", StringComparison.Ordinal)) return true;
            if (path == "/") return (request.Header("Accept") ?? string.Empty).IndexOf("text/html", StringComparison.OrdinalIgnoreCase) >= 0;
            return Types.ContainsKey(Path.GetExtension(path));
        }

        public static KnPageRules.Reply Route(State state, IHost host, ControlPageRules.Request request, IPAddress from, KnStatusSnapshot status,
            StopEndpointRules stop, bool stopping, DateTime? nowUtc = null)
        {
            DateTime now = nowUtc ?? DateTime.UtcNow;
            if (host == null) return Text(404, "Not found.");
            // only the home network, and a request that names this machine (not a web site): ServerControlPage:279-283
            if (!ControlPageRules.IsHomeNetwork(from)) return Text(403, "The control page answers the home network only.");
            if (!ControlPageRules.IsAcceptableHost(request.Header("Host"))) return Text(403, "Open the page by this PC's address.");
            string path = request.Path;
            if (!path.StartsWith("/api/", StringComparison.Ordinal))
            {
                if (request.Method != "GET" && request.Method != "HEAD") return Text(405, "No.");
                return ServeFile(host.Folder(), path == "/" ? "/index.html" : path);
            }
            string addressText = from.ToString();
            if (path == "/api/login" && request.Method == "POST") return Login(state, host, request, addressText, now);

            string token = request.Cookie(CookieName);
            string check = state.CheckOf(token, now);
            if (check == null) return Json(401, "{\"error\":\"Log in first.\"}");

            if (request.Method == "GET" || request.Method == "HEAD")
            {
                switch (path)
                {
                    case "/api/session": return Json(200, "{\"check\":" + ControlPageRules.Json(check) + "}");
                    case "/api/status": return StatusReply(host, status, stopping);
                    case "/api/log":
                    case "/api/flags":
                    case "/api/bans":
                        return Forward(request.Method, path, request.Query, null, status, stop, stopping);
                }
                return Json(404, "{\"error\":\"No such call.\"}");
            }

            // a button press: only from the page itself, which alone holds the session's check (ServerControlPage:332-337)
            if (request.Method != "POST" || !ControlPageRules.IsSameOrigin(request) || !ControlPageRules.SecretEquals(request.Header("X-KN-Check"), check))
                return Json(403, "{\"error\":\"That did not come from the control page.\"}");
            PageBody b = ParseBody(request.Body);
            switch (path)
            {
                case "/api/logout":
                    state.Close(token);
                    return With(Json(200, "{\"answer\":\"Logged out.\"}"), "Set-Cookie: " + CookieName + "=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0\r\n");
                case "/api/save":
                    return Forward("POST", path, null, "{}", status, stop, stopping);
                case "/api/announce":
                    return Forward("POST", path, null, Fields(("text", b.text)), status, stop, stopping);
                case "/api/command":
                    return Forward("POST", path, null, Fields(("line", b.line)), status, stop, stopping);
                case "/api/ban":
                {
                    // the page's "id" is a player of the list (their key) or a typed character name or account id; "text" is the length
                    bool listed = TryKey(b.id, out ulong key) && status != null && status.HasPlayer(key);
                    return Forward("POST", path, null, Fields(("player", listed ? key.ToString(CultureInfo.InvariantCulture) : string.Empty),
                        ("name", listed ? string.Empty : b.id), ("length", b.text), ("reason", b.reason)), status, stop, stopping);
                }
                case "/api/unban":
                    return Forward("POST", path, null, Fields(("name", b.id)), status, stop, stopping);
                case "/api/gm/add":
                case "/api/gm/unbind":
                case "/api/gm/remove":
                    return Recorded(Forward("POST", path, null, Fields(("name", b.name)), status, stop, stopping), Pressed(path, b));
                case "/api/gm/bind":
                    return Recorded(Forward("POST", path, null, Fields(("name", b.name), ("player", b.id)), status, stop, stopping), Pressed(path, b));
                case "/api/kick":
                    return Kick(b, status);
                case "/api/backup":
                    return Press(path, b, host.Backup);
                case "/api/stop":
                    return Press(path, b, () => KnShutdown.Schedule(b.seconds, b.reason, b.restart));
                case "/api/cancel":
                    return Press(path, b, KnShutdown.Cancel);
                case "/api/mode":
                    return Press(path, b, () => host.SwitchMode(b.text, b.seconds));
                case "/api/list/add":
                    return Press(path, b, () => host.ListAdd(b.name, b.address, b.port));
                case "/api/list/remove":
                    return Press(path, b, () => host.ListRemove(b.id));
                case "/api/list/publish":
                {
                    string answer = host.ListPublish();   // on this request's thread, not the game's
                    KnPageDesk.Desk?.Record(Pressed(path, b), answer);
                    return With(KnPageRules.Answer(answer), null);
                }
            }
            return Json(404, "{\"error\":\"No such call.\"}");
        }

        // ------------------------------------------------------------------ login

        static KnPageRules.Reply Login(State state, IHost host, ControlPageRules.Request request, string from, DateTime now)
        {
            if (state.Lockout.IsLocked(from, now, out TimeSpan left))
                return Json(429, "{\"error\":" + ControlPageRules.Json($"Too many wrong passwords. Try again in {Math.Ceiling(left.TotalMinutes)} minute(s).") + "}");
            if (!ControlPageRules.IsSameOrigin(request)) return Json(403, "{\"error\":\"That did not come from the control page.\"}");
            string password = host.Password();
            if (string.IsNullOrEmpty(password)) return Json(503, "{\"error\":\"The control page has no password yet.\"}");
            PageBody b = ParseBody(request.Body);
            if (!ControlPageRules.SecretEquals(b.password, password))
            {
                if (state.WrongPasswordDelayMs > 0) System.Threading.Thread.Sleep(state.WrongPasswordDelayMs);
                int tries = state.Lockout.Wrong(from, now);
                Debug.LogWarning($"[Server] Wrong control page password from {from}.");
                string error = tries > 0 ? $"Wrong password. {tries} tr{(tries == 1 ? "y" : "ies")} left before a 5-minute lock." : "Wrong password. Locked for 5 minutes.";
                return Json(401, "{\"error\":" + ControlPageRules.Json(error) + "}");
            }
            state.Lockout.Right(from);
            string token = state.Open(now, out string check);
            Debug.Log($"[Server] Control page opened from {from}.");
            return With(Json(200, "{\"check\":" + ControlPageRules.Json(check) + "}"),
                $"Set-Cookie: {CookieName}={token}; HttpOnly; SameSite=Strict; Path=/; Max-Age={(int)ControlPageRules.SessionTime.TotalSeconds}\r\n");
        }

        // ------------------------------------------------------------------ status

        /// <summary>NGO's <c>/api/status</c> (ServerControlPage:171-233) from the entity server's snapshot and the host's extras.</summary>
        static KnPageRules.Reply StatusReply(IHost host, KnStatusSnapshot s, bool stopping) =>
            new KnPageRules.Reply(200, "application/json; charset=utf-8", string.Empty, false, 0, () => StatusJson(host, s, stopping), true, null, SecurityHeaders);

        public static string StatusJson(IHost host, KnStatusSnapshot s, bool stopping)
        {
            s = s ?? new KnStatusSnapshot();
            KnShutdown.Countdown count = KnShutdown.Current;
            double now = KnShutdown.Now;
            var sb = new StringBuilder(1024);
            sb.Append("{\"name\":").Append(ControlPageRules.Json(s.Name))
              .Append(",\"listening\":true")
              .Append(",\"map\":").Append(ControlPageRules.Json(s.Map))
              .Append(",\"maxPlayers\":").Append(s.MaxPlayers.ToString(CultureInfo.InvariantCulture))
              .Append(",\"uptime\":").Append(((int)s.UptimeSeconds).ToString(CultureInfo.InvariantCulture))
              .Append(",\"shutdownIn\":").Append(count.SecondsLeft(now).ToString(CultureInfo.InvariantCulture))
              .Append(",\"restarting\":").Append(count.Active && count.Restarts ? "true" : "false")
              .Append(",\"stopping\":").Append(stopping || s.Stopping ? "true" : "false")
              .Append(",\"version\":").Append(ControlPageRules.Json(s.Build))
              .Append(",\"players\":[");
            for (int i = 0; i < s.PlayerRows.Length; i++)
            {
                KnPlayerRow r = s.PlayerRows[i];
                if (i > 0) sb.Append(',');
                // level and faction as NGO's page showed them (ServerControlPage:183-187), read from the server body into the row
                sb.Append("{\"id\":").Append(ControlPageRules.Json(r.Key.ToString(CultureInfo.InvariantCulture)))
                  .Append(",\"name\":").Append(ControlPageRules.Json(r.Name))
                  .Append(",\"level\":").Append(r.Level.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"faction\":").Append(ControlPageRules.Json(r.Faction ?? string.Empty))
                  .Append(",\"map\":").Append(ControlPageRules.Json(r.Map))
                  .Append(",\"gm\":").Append(ControlPageRules.Json(host.GmNameOf(r.Key) ?? string.Empty))
                  .Append(",\"ping\":").Append(r.PingMs.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"lateHits\":0,\"goneTargets\":0}");
            }
            sb.Append(']');
            string extras = host.StatusExtras();
            if (!string.IsNullOrEmpty(extras)) sb.Append(',').Append(extras);
            return sb.Append('}').ToString();
        }

        // ------------------------------------------------------------------ the calls that need their own words

        /// <summary>NGO /api/kick {id, reason} (ServerControlPage:496-506): the player's key, the reason in the words the player reads.</summary>
        static KnPageRules.Reply Kick(PageBody b, KnStatusSnapshot status)
        {
            if (!TryKey(b.id, out ulong key) || status == null || !status.HasPlayer(key))
                return With(KnPageRules.Answer("That player is no longer here."), null);
            string reason = (b.reason ?? string.Empty).Trim();
            string told = reason.Length == 0 ? "A GM removed you from the server." : "A GM removed you from the server: " + reason;
            string who = "player " + key;
            foreach (KnPlayerRow row in status.PlayerRows) if (row.Key == key && !string.IsNullOrEmpty(row.Name)) who = row.Name;
            return Work(() =>
            {
                // the main thread only: the refusal reaches the player, then the connection closes (as ServerCommands.KickEntity)
                string answer = KnPlayerRefusals.Enqueue(new PlayerKey(key), told) ? $"Removed {who}." : "That player is no longer here.";
                KnPageDesk.Desk?.Record(Pressed("/api/kick", b), answer);
                return answer;
            });
        }

        static KnPageRules.Reply Press(string path, PageBody b, Func<string> action) =>
            Work(() =>
            {
                string answer = action();
                KnPageDesk.Desk?.Record(Pressed(path, b), answer);
                return answer;
            });

        static KnPageRules.Reply Work(Func<string> work) =>
            new KnPageRules.Reply(200, "application/json; charset=utf-8", string.Empty, false, 0, work, false, null, SecurityHeaders);

        /// <summary>The same press through the entity handlers, the owner's check already done by the password and the session.</summary>
        static KnPageRules.Reply Forward(string method, string path, string query, string body, KnStatusSnapshot status, StopEndpointRules stop, bool stopping) =>
            With(KnPageRules.Handle(method, path, true, null, null, status, stop, stopping, query, body, authorized: true), null);

        /// <summary>Writes the press into the GM record once its work has run (NGO wrote every press except the command box).</summary>
        static KnPageRules.Reply Recorded(KnPageRules.Reply reply, string pressed)
        {
            if (reply.Work == null) return reply;
            Func<string> inner = reply.Work;
            return new KnPageRules.Reply(reply.Status, reply.ContentType, reply.Body, false, 0, () =>
            {
                string answer = inner();
                KnPageDesk.Desk?.Record(pressed, answer);
                return answer;
            }, reply.WorkIsJson, null, SecurityHeaders);
        }

        static KnPageRules.Reply With(KnPageRules.Reply reply, string extra) =>
            new KnPageRules.Reply(reply.Status, reply.ContentType ?? "application/json; charset=utf-8", reply.Body, reply.BeginStop, reply.KickKey, reply.Work,
                reply.WorkIsJson, reply.Bytes, (reply.ExtraHeaders ?? SecurityHeaders) + extra);

        // ------------------------------------------------------------------ files

        /// <summary>A file of the page's folder: plain names only (no "..", no hidden files, known types), never cached (a new build's page never meets an old script).</summary>
        public static KnPageRules.Reply ServeFile(string folder, string urlPath)
        {
            string relative;
            try { relative = Uri.UnescapeDataString(urlPath ?? string.Empty).TrimStart('/').Replace('\\', '/'); }
            catch (UriFormatException) { return Text(404, "Not found."); }
            if (string.IsNullOrEmpty(folder) || relative.Length == 0 || relative.Contains("..") || relative.Contains(":")
                || relative.StartsWith(".", StringComparison.Ordinal) || !Types.TryGetValue(Path.GetExtension(relative), out string type))
                return Text(404, "Not found.");
            string root = Path.GetFullPath(folder);
            string full = Path.GetFullPath(Path.Combine(root, relative));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return Text(404, "Not found.");
            return new KnPageRules.Reply(200, type, string.Empty, false, 0, null, false, File.ReadAllBytes(full), SecurityHeaders);
        }

        // ------------------------------------------------------------------ small helpers

        static KnPageRules.Reply Text(int status, string text) =>
            new KnPageRules.Reply(status, "text/plain; charset=utf-8", text, false, 0, null, false, null, SecurityHeaders);

        static KnPageRules.Reply Json(int status, string json) =>
            new KnPageRules.Reply(status, "application/json; charset=utf-8", json, false, 0, null, false, null, SecurityHeaders);

        static PageBody ParseBody(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? new PageBody() : JsonUtility.FromJson<PageBody>(json) ?? new PageBody(); }
            catch (Exception) { return new PageBody(); }
        }

        static bool TryKey(string text, out ulong key) =>
            ulong.TryParse(text ?? string.Empty, NumberStyles.None, CultureInfo.InvariantCulture, out key) && key != 0;

        /// <summary>A JSON object of the given text fields (the entity handlers' bodies).</summary>
        static string Fields(params (string Name, string Value)[] fields)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < fields.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(fields[i].Name).Append("\":").Append(ControlPageRules.Json(fields[i].Value ?? string.Empty));
            }
            return sb.Append('}').ToString();
        }

        /// <summary>"kick id=4 reason=spam": the button's call and what it was given, for the GM record. Never the password.</summary>
        static string Pressed(string path, PageBody b)
        {
            var sb = new StringBuilder(path.Substring("/api/".Length));
            void Add(string field, string value)
            {
                if (!string.IsNullOrWhiteSpace(value)) sb.Append(' ').Append(field).Append("=\"").Append(value.Trim()).Append('"');
            }
            Add("id", b.id);
            Add("name", b.name);
            Add("text", b.text);
            Add("reason", b.reason);
            Add("address", b.address);
            if (b.port != 0) sb.Append(" port=").Append(b.port);
            if (b.seconds != 0f) sb.Append(" seconds=").Append(Mathf.RoundToInt(b.seconds));
            if (b.restart) sb.Append(" restart");
            return sb.ToString();
        }
    }
}
#endif
