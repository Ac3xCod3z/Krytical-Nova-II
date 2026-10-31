using System;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// A game (client) build is always a Player build. The Editor remembers the standalone subtarget per checkout, and a
    /// killed or failed server build leaves it on Server: client code then compiles with UNITY_SERVER, and map bundles built
    /// meanwhile can carry server-only state (31 Oct 2026: ten of build 13's 29 maps shipped with the sky presenter off).
    /// <see cref="Prepare"/> runs before any client content or player build: it forces Player, asserts it, and clears the
    /// content cache (Library/BuildCache and the Addressables output) when bundles may have been made under other settings.
    /// A cleared cache costs one full rebuild of the maps; the same settings as last time keep the cache.
    /// </summary>
    public static class ClientBuildGuard
    {
        /// <summary>How the maps' content was last built here (Library is per checkout and never committed).</summary>
        public const string StampPath = "Library/KryticalNova-ContentBuild.stamp";
        private const string BuildCachePath = "Library/BuildCache";

        /// <summary>The settings a bundle depends on that its source files do not show.</summary>
        public static string Stamp(StandaloneBuildSubtarget subtarget, string defines, string unityVersion) =>
            $"subtarget={subtarget};unity={unityVersion};defines={defines}";

        /// <summary>No stamp (never built under this guard), another stamp, or a subtarget that had to be corrected: the cache may be tainted.</summary>
        public static bool NeedsPurge(string lastStamp, string currentStamp, bool subtargetWasWrong) =>
            subtargetWasWrong || string.IsNullOrEmpty(lastStamp) || lastStamp != currentStamp;

        /// <summary>Forces and asserts the Player subtarget on Windows, and purges the content cache when needed. Throws when it cannot.</summary>
        public static void Prepare(string what)
        {
            bool wrong = EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Player;
            if (wrong)
            {
                Debug.LogWarning($"[Build] {what}: the standalone subtarget was {EditorUserBuildSettings.standaloneBuildSubtarget} (a server build left it); forcing Player.");
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            }
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                Debug.LogWarning($"[Build] {what}: the active build target was {EditorUserBuildSettings.activeBuildTarget}; switching to StandaloneWindows64.");
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64);
            }
            if (EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Player || EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
                throw new InvalidOperationException($"[Build] {what} needs a Windows Player build, but the Editor is on {EditorUserBuildSettings.activeBuildTarget} / {EditorUserBuildSettings.standaloneBuildSubtarget}.");

            string current = CurrentStamp();
            string last = File.Exists(StampPath) ? File.ReadAllText(StampPath).Trim() : "";
            if (!NeedsPurge(last, current, wrong))
            {
                Debug.Log($"[Build] {what}: Player subtarget confirmed, content cache kept ({current}).");
                return;
            }
            Debug.Log($"[Build] {what}: clearing the content cache (was: {(last.Length == 0 ? "no record" : last)}; now: {current}{(wrong ? "; subtarget had to be corrected" : "")}).");
            // The Scriptable Build Pipeline's cache (what the bundle builder reuses by content hash) is a plain folder.
            if (Directory.Exists(BuildCachePath)) Directory.Delete(BuildCachePath, true);
            AddressableAssetSettings.CleanPlayerContent();
            // The cache is clean, but the stamp is written only when the build succeeds (RecordBuilt).
            if (File.Exists(StampPath)) File.Delete(StampPath);
        }

        /// <summary>Called after a client build succeeded: the next one with the same settings keeps the cache.</summary>
        public static void RecordBuilt() => File.WriteAllText(StampPath, CurrentStamp());

        private static string CurrentStamp() =>
            Stamp(EditorUserBuildSettings.standaloneBuildSubtarget, PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone), Application.unityVersion);
    }
}
