using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Krytical Nova ▸ Online ▸ Entities ▸ Soak ▸ 3 Run Baseline Soak / 4 Stop Everything (wave 4, 12 Nov 2026): start
    /// <c>Tools/Editor/Build/Scripts~/soak_run.py</c> in its own console window, outside Unity (the soak runs with no Editor busy; the
    /// script has the gates, the job object and the result page). Nothing here touches the real server.
    /// </summary>
    public static class SoakMenu
    {
        const string Menu = "Krytical Nova/Online/Entities/Soak/";

        static string Script => Path.GetFullPath(Path.Combine(Application.dataPath, "_Game", "Tools", "Editor", "Build", "Scripts~", "soak_run.py"));

        [MenuItem(Menu + "3 Run Baseline Soak (S0)", priority = 3)]
        static void RunBaseline() => Start("S0");

        [MenuItem(Menu + "4 Stop Everything", priority = 4)]
        static void StopEverything() => Start("stop");

        static void Start(string what)
        {
            if (!File.Exists(Script)) { Debug.LogError("[Soak] " + Script + " is missing."); return; }
            // cmd /k keeps the window open so the lines stay readable; the script itself stops only what it started
            Process.Start(new ProcessStartInfo("cmd.exe", $"/k python \"{Script}\" {what}") { UseShellExecute = true });
            Debug.Log($"[Soak] soak_run.py {what} started in its own window.");
        }
    }
}
