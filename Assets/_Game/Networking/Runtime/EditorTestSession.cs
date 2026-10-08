using System;
using System.IO;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// A test session for the Editor that touches nothing committed: while a small file sits in the main project's
    /// Library, every Editor of this project (the main one and its Multiplayer Play Mode clones) hosts and joins on
    /// the port it names and keeps its server save in the folder it names. That keeps an Editor test off a build
    /// running on the same machine, which hosts on the default port and writes the default save. Delete the file and
    /// the Editor is back on the defaults. A build never reads it.
    /// <para>The file is two lines, in any order: <c>port=7788</c> and <c>save=C:/some/scratch/folder</c>.</para>
    /// </summary>
    public static class EditorTestSession
    {
        public const string FileName = "KryticalNova.TestSession";

#if UNITY_EDITOR
        private const string CloneFolder = "/Library/VP/";
        private const string PortKey = "port";
        private const string SaveKey = "save";

        /// <summary>Where the file lives: the Library of the project a clone was made from, so all Editors read one file.</summary>
        public static string FilePath
        {
            get
            {
                string data = Application.dataPath.Replace('\\', '/');
                int at = data.IndexOf(CloneFolder, StringComparison.OrdinalIgnoreCase);
                string project = at >= 0 ? data.Substring(0, at) : Path.GetDirectoryName(data)?.Replace('\\', '/');
                return project + "/Library/" + FileName;
            }
        }

        /// <summary>True while the file names a usable port or save folder.</summary>
        public static bool Active => Read(out ushort port, out string save) && (port != 0 || save != null);

        /// <summary>The port every local session uses while a test session is on.</summary>
        public static bool TryGetPort(out ushort port) => Read(out port, out _) && port != 0;

        /// <summary>The folder the server save is kept in while a test session is on, or null.</summary>
        public static string SaveRoot => Read(out _, out string save) ? save : null;

        // Read on every ask: it is asked a few times per session, and the file comes and goes while the Editor runs.
        private static bool Read(out ushort port, out string save)
        {
            port = 0;
            save = null;
            try
            {
                string path = FilePath;
                if (!File.Exists(path)) return false;
                foreach (string raw in File.ReadAllLines(path))
                {
                    int equals = raw.IndexOf('=');
                    if (equals <= 0) continue;
                    string key = raw.Substring(0, equals).Trim();
                    string value = raw.Substring(equals + 1).Trim();
                    if (key.Equals(PortKey, StringComparison.OrdinalIgnoreCase)) ushort.TryParse(value, out port);
                    else if (key.Equals(SaveKey, StringComparison.OrdinalIgnoreCase) && value.Length > 0) save = value;
                }
                return true;
            }
            catch (IOException)
            {
                return false; // being written this instant: the next ask reads it
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void WarnWhenActive()
        {
            if (!Read(out ushort port, out string save) || (port == 0 && save == null)) return;
            Debug.LogWarning($"[EditorTestSession] TEST SESSION ON: local sessions use port {(port != 0 ? port.ToString() : "(default)")}, " +
                             $"the server save is {save ?? "(default)"}. Delete {FilePath} to go back to the defaults.");
        }
#else
        public static bool Active => false;

        public static bool TryGetPort(out ushort port)
        {
            port = 0;
            return false;
        }

        public static string SaveRoot => null;
#endif
    }
}
