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
        private const string Tag = "KN1";

        /// <summary>What a joining game sends: its build, and whether it is the Editor.</summary>
        public static byte[] Payload(string build, bool editor) => Encoding.UTF8.GetBytes(Tag + "|" + (build ?? string.Empty) + "|" + (editor ? "1" : "0"));

        public static bool TryRead(byte[] payload, out string build, out bool editor)
        {
            build = null;
            editor = false;
            if (payload == null || payload.Length == 0 || payload.Length > 256) return false;
            string[] parts = Encoding.UTF8.GetString(payload).Split('|');
            if (parts.Length != 3 || parts[0] != Tag) return false;
            build = parts[1];
            editor = parts[2] == "1";
            return true;
        }

        /// <summary>
        /// Why a game may not join, in the words it will show, or null when it may. <paramref name="online"/> counts the
        /// players already in (not this one).
        /// </summary>
        public static string Refusal(string serverBuild, bool serverIsEditor, byte[] payload, int online, int maxPlayers)
        {
            if (!TryRead(payload, out string build, out bool editor))
                return "Your game is older than this server. Open the Krytical Nova launcher to update it.";
            if (!string.Equals(build, serverBuild, StringComparison.Ordinal) && !editor && !serverIsEditor)
                return $"This server runs {GameBuild.Describe(serverBuild)} and your game is {GameBuild.Describe(build)}. " +
                       "Open the Krytical Nova launcher to update.";
            if (maxPlayers > 0 && online >= maxPlayers)
                return "The server is full. Try again in a little while.";
            return null;
        }
    }
}
