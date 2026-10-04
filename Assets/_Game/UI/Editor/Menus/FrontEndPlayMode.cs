using System;
using System.IO;
using KryticalNova.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Editor switch for where Play starts. On: Play always begins at the Title screen, like a build, whatever scene
    /// is open. Off (the default): Play runs the open scene, so pressing Play in the zone still drops straight into
    /// it with the last-played character. Multiplayer Play Mode clones follow the main Editor's switch, so a second
    /// client goes through the same menus as the first. The switch reaches a clone through a small file in the main
    /// project's Library: each Editor process keeps its own copy of the preferences, so a clone never sees a
    /// preference the main Editor changed after the clone started.
    /// </summary>
    [InitializeOnLoad]
    public static class FrontEndPlayMode
    {
        private const string MenuPath = "Krytical Nova/Front End/Play From Title Screen";
        private const string CloneFolder = "/Library/VP/";
        private const double ClonePollSeconds = 1.0;

        // Editor preferences are shared by every project on the machine; keep this one per project. A clone lives in
        // <project>/Library/VP/<id>, so it is keyed by the project it was cloned from and reads the same value.
        private static readonly string PrefKey = "KryticalNova.FrontEnd.PlayFromTitle." + MainDataPath().GetHashCode();
        private static readonly bool IsClone = Application.dataPath.Replace('\\', '/').IndexOf(CloneFolder, StringComparison.OrdinalIgnoreCase) >= 0;
        private static readonly string SharedFile = MainDataPath().Replace('\\', '/').Replace("/Assets", "/Library/KryticalNova.PlayFromTitle");

        private static bool applied;
        private static bool appliedValue;
        private static double nextPoll;

        static FrontEndPlayMode()
        {
            if (!IsClone) WriteShared(EditorPrefs.GetBool(PrefKey, false));
            EditorApplication.delayCall += Apply;
            // The switch is flipped in the main Editor; a clone only learns of it by reading the preference again.
            if (IsClone) EditorApplication.update += PollInClone;
        }

        public static bool Enabled
        {
            get => IsClone ? ReadShared() : EditorPrefs.GetBool(PrefKey, false);
            set
            {
                EditorPrefs.SetBool(PrefKey, value);
                WriteShared(value);
                Apply();
            }
        }

        [MenuItem(MenuPath, priority = 40)]
        private static void Toggle() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static string MainDataPath()
        {
            string path = Application.dataPath;
            int at = path.Replace('\\', '/').IndexOf(CloneFolder, StringComparison.OrdinalIgnoreCase);
            return at < 0 ? path : path.Substring(0, at) + "/Assets";
        }

        private static void PollInClone()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + ClonePollSeconds;
            // Also when something else changed the start scene behind this switch's back.
            bool on = Enabled;
            if (!applied || appliedValue != on || (EditorSceneManager.playModeStartScene != null) != on) Apply();
        }

        private static bool ReadShared()
        {
            try { return File.Exists(SharedFile) ? File.ReadAllText(SharedFile).Trim() == "1" : EditorPrefs.GetBool(PrefKey, false); }
            catch (IOException) { return appliedValue; } // the main Editor is writing it: keep what is applied
        }

        private static void WriteShared(bool on)
        {
            try { File.WriteAllText(SharedFile, on ? "1" : "0"); }
            catch (IOException e) { Debug.LogWarning($"[FrontEndPlayMode] Could not share the switch with Play Mode clones: {e.Message}"); }
        }

        private static void Apply()
        {
            bool on = Enabled;
            applied = true;
            appliedValue = on;
            EditorSceneManager.playModeStartScene = on
                ? AssetDatabase.LoadAssetAtPath<SceneAsset>(FrontEndBuilder.ScenePath(FrontEnd.TitleScene))
                : null;
        }
    }
}
