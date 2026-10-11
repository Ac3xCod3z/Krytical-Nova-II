using System.IO;
using KryticalNova.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The Editor's switch for the performance log. A built game always writes one; Play in the Editor writes one
    /// only while this is ticked (into <c>Library/PerformanceLogs</c>), because the Editor's own slow frames are in
    /// it and would be taken for the game's. Takes effect the next time Play is entered.
    /// </summary>
    public static class PerformanceLogMenu
    {
        private const string RecordPath = "Krytical Nova/Build/Record Performance Log In Play";
        private const string FolderPath = "Krytical Nova/Build/Show Performance Logs";

        [MenuItem(RecordPath, priority = 60)]
        private static void Toggle()
        {
            bool on = !EditorPrefs.GetBool(PerformanceLog.EditorPrefKey, false);
            EditorPrefs.SetBool(PerformanceLog.EditorPrefKey, on);
            Debug.Log(on
                ? $"[PerformanceLog] Play in the Editor will be recorded to {PerformanceLogRecorder.Folder()} (from the next time Play is entered)."
                : "[PerformanceLog] Play in the Editor is no longer recorded.");
        }

        [MenuItem(RecordPath, true)]
        private static bool ToggleShown()
        {
            Menu.SetChecked(RecordPath, EditorPrefs.GetBool(PerformanceLog.EditorPrefKey, false));
            return true;
        }

        /// <summary>The build's logs when there are any (they are the ones that count), else the Editor's.</summary>
        [MenuItem(FolderPath, priority = 61)]
        private static void ShowFolder()
        {
            string build = Path.GetFullPath(TestBuildBuilder.OutputFolder + "/" + TestBuildBuilder.LogsFolder);
            string editor = PerformanceLogRecorder.Folder();
            string folder = Directory.Exists(build) ? build : editor;
            if (!Directory.Exists(folder))
            {
                Debug.Log($"[PerformanceLog] No log has been written yet. A build writes to {build}, the Editor to {editor}.");
                return;
            }
            EditorUtility.RevealInFinder(folder);
        }
    }
}
