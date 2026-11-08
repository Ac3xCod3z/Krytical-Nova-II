using System.IO;
using System.Linq;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Item 10 (Burst AOT never ran in our player builds since 30 Oct 2026), found 8 Nov 2026 by reading Unity 6000.6's own player build
    /// program (PlayerBuildProgramLibrary.PlayerBuildProgramBase.SetupGenerateNativePluginsForAssembliesArgs): when the build plan is made, Bee
    /// looks at what the LAST build left in <c>Library/Bee/artifacts/&lt;Platform&gt;PlayerBuildProgram/AsyncPluginsFromLinker</c>. When that is
    /// only <c>no_plugins_were_generated.txt</c> (left by a build with Burst switched off), no copy step is planned, so nothing needs the
    /// "Running Burst Compiler" step, so it never runs again and the marker is never replaced: every later build has no Burst, silently.
    /// Clearing that leftover before a Burst-on build makes Bee plan the copy of <c>lib_burst_generated</c>, which runs Burst.
    /// </summary>
    public static class BurstAotMarker
    {
        public const string Marker = "no_plugins_were_generated.txt";

        /// <summary>Removes a "no plugins" leftover of an earlier build for every player platform; returns how many were removed.</summary>
        public static int ClearStaleMarker()
        {
            string artifacts = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Library", "Bee", "artifacts");
            if (!Directory.Exists(artifacts)) return 0;
            int cleared = 0;
            foreach (string program in Directory.GetDirectories(artifacts, "*PlayerBuildProgram"))
            {
                string plugins = Path.Combine(program, "AsyncPluginsFromLinker");
                if (!IsOnlyMarker(plugins)) continue;
                Directory.Delete(plugins, true);
                string symbols = Path.Combine(program, "AsyncPluginsFromLinkerSymbols");
                if (Directory.Exists(symbols)) Directory.Delete(symbols, true);
                cleared++;
                Debug.Log($"[BurstAotMarker] removed the stale \"{Marker}\" of an earlier build in {plugins}: Burst AOT runs in this build");
            }
            return cleared;
        }

        /// <summary>The folder holds the "no plugins" marker and nothing else.</summary>
        public static bool IsOnlyMarker(string folder)
        {
            if (!Directory.Exists(folder)) return false;
            string[] entries = Directory.GetFileSystemEntries(folder);
            return entries.Length == 1 && Path.GetFileName(entries[0]) == Marker;
        }
    }
}
