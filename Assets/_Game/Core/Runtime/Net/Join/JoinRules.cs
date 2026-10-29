using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Which build this is. A release build (Krytical Nova ▸ Release ▸ Publish to Test) and the server built for it carry
    /// "0.1.0 build N" as their version; any other build (the Editor, a test build) has no number and is a development
    /// build.
    /// </summary>
    public static class GameBuild
    {
        private static readonly Regex BuildNumber = new Regex(@"build (\d+)$");

        public static string Version => Application.version;

        /// <summary>The release's build number, or 0 for a development build.</summary>
        public static int Number => NumberIn(Application.version);

        /// <summary>A player's release build (not the Editor, not a test build).</summary>
        public static bool IsRelease => Number > 0 && !Application.isEditor;

        public static int NumberIn(string version)
        {
            Match match = BuildNumber.Match(version ?? string.Empty);
            return match.Success && int.TryParse(match.Groups[1].Value, out int number) ? number : 0;
        }

        /// <summary>"build 12", or "a development build", for messages.</summary>
        public static string Describe(string version)
        {
            int number = NumberIn(version);
            return number > 0 ? "build " + number : "a development build";
        }
    }

    /// <summary>
    /// Who may join (online step 4, owner's pick: the same build only). A joining game sends its build with its
    /// connection request; the server answers before the player is in, so a refused player reads why: a different
    /// build (open the launcher), or a full server. The Editor may join, or host, any build: it is the owner's own.
    /// </summary>
    public static class JoinRules
    {
        // KN2 (online phase 2): the Unity sign-in token that proves who the player is, and the account id the game made up
        // before sign-in existed (its characters are carried over once to the proven account).
        private const string Tag = "KN2";
        /// <summary>Longest join request read: a sign-in token is one to two thousand characters, a packed roster request
        /// at most <see cref="RosterWire.MaxRequestChars"/>.</summary>
        public const int MaxPayloadBytes = 5800; // under the transport's 6144 (NetworkManager prefab)

        /// <summary>
        /// What a joining game sends: its build, whether it is the Editor, its sign-in token and its old account id. A
        /// game that only asks about its characters (<see cref="RosterLink"/>, online phase 4) adds its request as a sixth
        /// part, packed (<see cref="RosterWire"/>); the server answers it and never lets that connection in.
        /// </summary>
        public static byte[] Payload(string build, bool editor, string token = "", string oldAccount = "", string request = "", string ticket = "") =>
            Encoding.UTF8.GetBytes(PayloadText(build, editor, token, oldAccount, request, ticket));

        /// <summary>
        /// The same payload as text (the Netcode for Entities join sends text, in chunks). The seventh part is the travel
        /// ticket a world service hands out when a player moves between map servers; a game without one sends none, and the
        /// older reader (six parts at most) never sees one because only the Entities server asks for it.
        /// </summary>
        public static string PayloadText(string build, bool editor, string token = "", string oldAccount = "", string request = "", string ticket = "") =>
            Tag + "|" + Clean(build) + "|" + (editor ? "1" : "0") + "|" + Clean(token) + "|" + Clean(oldAccount)
            + (string.IsNullOrEmpty(request) && string.IsNullOrEmpty(ticket) ? string.Empty : "|" + Clean(request))
            + (string.IsNullOrEmpty(ticket) ? string.Empty : "|" + Clean(ticket));

        private static string Clean(string part) => (part ?? string.Empty).Replace("|", string.Empty);

        public static bool TryRead(byte[] payload, out string build, out bool editor) => TryRead(payload, out build, out editor, out _, out _);

        public static bool TryRead(byte[] payload, out string build, out bool editor, out string token, out string oldAccount) =>
            TryRead(payload, out build, out editor, out token, out oldAccount, out _);

        /// <summary>The same, and the roster request of a game that only asks about its characters (empty for a join).</summary>
        public static bool TryRead(byte[] payload, out string build, out bool editor, out string token, out string oldAccount, out string request)
        {
            request = string.Empty;
            build = null;
            editor = false;
            token = string.Empty;
            oldAccount = string.Empty;
            if (payload == null || payload.Length == 0 || payload.Length > MaxPayloadBytes) return false;
            string[] parts = Encoding.UTF8.GetString(payload).Split('|');
            if ((parts.Length != 5 && parts.Length != 6) || parts[0] != Tag) return false; // the NGO server takes no ticket
            build = parts[1];
            editor = parts[2] == "1";
            token = parts[3];
            oldAccount = parts[4];
            if (parts.Length == 6) request = parts[5];
            return true;
        }

        /// <summary>The Entities server's reader: the same text, with the travel ticket (empty when the game sent none).</summary>
        public static bool TryReadText(string payload, out string build, out bool editor, out string token, out string oldAccount,
            out string request, out string ticket)
        {
            ticket = string.Empty;
            build = null;
            editor = false;
            token = oldAccount = request = string.Empty;
            if (string.IsNullOrEmpty(payload) || payload.Length > MaxPayloadBytes) return false;
            string[] parts = payload.Split('|');
            if (parts.Length < 5 || parts.Length > 7 || parts[0] != Tag) return false;
            build = parts[1];
            editor = parts[2] == "1";
            token = parts[3];
            oldAccount = parts[4];
            if (parts.Length >= 6) request = parts[5];
            if (parts.Length == 7) ticket = parts[6];
            return true;
        }

        /// <summary>Why a join is turned away before anything else is asked (<see cref="None"/>: it may go on).</summary>
        public enum Verdict { None, Unreadable, WrongBuild, Full }

        /// <summary>
        /// The build and capacity rules on their own, so a server can answer with a reason code as well as the words.
        /// <paramref name="build"/> and <paramref name="editor"/> come from a payload that was read.
        /// </summary>
        public static Verdict Judge(string serverBuild, bool serverIsEditor, string build, bool editor, int online, int maxPlayers)
        {
            if (!string.Equals(build, serverBuild, StringComparison.Ordinal) && !editor && !serverIsEditor) return Verdict.WrongBuild;
            if (maxPlayers > 0 && online >= maxPlayers) return Verdict.Full;
            return Verdict.None;
        }

        /// <summary>The words a player reads for a <see cref="Verdict"/> (null for <see cref="Verdict.None"/>).</summary>
        public static string Words(Verdict verdict, string serverBuild, string build)
        {
            switch (verdict)
            {
                case Verdict.Unreadable:
                    return "Your game is older than this server. Open the Krytical Nova launcher to update it.";
                case Verdict.WrongBuild:
                    return $"This server runs {GameBuild.Describe(serverBuild)} and your game is {GameBuild.Describe(build)}. " +
                           "Open the Krytical Nova launcher to update.";
                case Verdict.Full:
                    return "The server is full. Try again in a little while.";
                default:
                    return null;
            }
        }

        /// <summary>
        /// Why a game may not join, in the words it will show, or null when it may. <paramref name="online"/> counts the
        /// players already in (not this one).
        /// </summary>
        public static string Refusal(string serverBuild, bool serverIsEditor, byte[] payload, int online, int maxPlayers)
        {
            if (!TryRead(payload, out string build, out bool editor)) return Words(Verdict.Unreadable, serverBuild, null);
            return Words(Judge(serverBuild, serverIsEditor, build, editor, online, maxPlayers), serverBuild, build);
        }
    }
}
