using System;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// The name of this running copy of the game on its machine (a build's "-profile", a Multiplayer Play Mode clone's
    /// own), which keeps its sign-in, characters and settings apart from other copies. In Core so that settings kept per
    /// profile (key bindings, HUD, graphics) need not know the network code.
    /// </summary>
    public static class LocalProfile
    {
        private const string ProfileArgument = "-profile";
        private const int MaxProfileLength = 30;

        /// <summary>
        /// The name that keeps this running copy of the game apart from others on the same machine: its sign-in,
        /// its saved characters and its interface settings. A build started with "-profile &lt;name&gt;" uses that
        /// name; a Multiplayer Play Mode clone uses its own (<see cref="EditorClone"/>). Null otherwise,
        /// which is the machine's default save.
        /// </summary>
        public static string Name()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], ProfileArgument, StringComparison.OrdinalIgnoreCase)) continue;
                string named = Clean(args[i + 1]);
                if (named != null) return named;
            }
            return EditorClone();
        }

        /// <summary>
        /// Editor instances cloned for multiplayer testing (Multiplayer Play Mode virtual players) share this
        /// machine's sign-in cache. Each gets its own authentication profile, so they are different players.
        /// Also used to keep each clone's saved characters apart. Null in the main Editor and in builds.
        /// </summary>
        public static string EditorClone()
        {
#if UNITY_EDITOR
            const string cloneFolder = "/Library/VP/";
            string path = Application.dataPath.Replace('\\', '/');
            int at = path.IndexOf(cloneFolder, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;

            string id = path.Substring(at + cloneFolder.Length);
            int end = id.IndexOf('/');
            if (end >= 0) id = id.Substring(0, end);

            return Clean(id);
#else
            return null;
#endif
        }

        // Authentication profiles accept letters, digits, '-' and '_', up to 30 characters.
        private static string Clean(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            var profile = new System.Text.StringBuilder(MaxProfileLength);
            foreach (char c in raw)
            {
                if (profile.Length == MaxProfileLength) break;
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') profile.Append(c);
            }
            return profile.Length > 0 ? profile.ToString() : null;
        }
    }
}
