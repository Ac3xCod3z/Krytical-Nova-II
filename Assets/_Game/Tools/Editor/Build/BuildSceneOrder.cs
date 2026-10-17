using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The scenes that go into a build, decided in one place: the front end first (Title is the boot scene), then the
    /// world: Zone01, then the World Atlas maps. Nothing else ships: development scenes are in the Editor's list only
    /// where a second Editor has to follow the host into one, and are left out of every build. Scenes that do not exist yet are skipped,
    /// so the scene builders can call this in any order. A dedicated server boots the same list and skips to the
    /// world (DedicatedServerBoot).
    /// </summary>
    public static class BuildSceneOrder
    {
        public const string MenuSceneFolder = "Assets/_Game/Scenes/MainMenu";
        public const string WorldScenePath = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";

        // Scene names match KryticalNova.UI.Menus.FrontEnd.
        private static readonly string[] Scenes =
        {
            MenuSceneFolder + "/Title.unity",
            MenuSceneFolder + "/FactionSelect.unity",
            MenuSceneFolder + "/CharacterCreator.unity",
            MenuSceneFolder + "/CharacterSelect.unity",
            MenuSceneFolder + "/Loading.unity",
            WorldScenePath
        };

        // Scenes the Editor must know so that a second Editor can follow the host into them, and that never ship:
        // Shipped() leaves them out, and it is what a build takes.
        private static readonly string[] DevelopmentScenes =
        {
            "Assets/_Game/Scenes/Development/Test_Cadence.unity"
        };

        /// <summary>The scenes a build is made of, in order: the Editor's list without the development scenes.</summary>
        public static string[] Shipped()
        {
            Apply();
            var shipped = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled && Array.IndexOf(DevelopmentScenes, scene.path) < 0) shipped.Add(scene.path);
            return shipped.ToArray();
        }

        public static void Apply()
        {
            var current = EditorBuildSettings.scenes;
            var scenes = new List<EditorBuildSettingsScene>();
            // The front end and Zone01, then the World Atlas maps (WorldScenesBuilder), then the development scenes.
            var paths = new List<string>(Scenes);
            paths.AddRange(WorldScenesBuilder.ScenePaths());
            paths.AddRange(DevelopmentScenes);
            foreach (string path in paths)
            {
                if (!File.Exists(path)) continue;
                var existing = Array.Find(current, s => s.path == path);
                scenes.Add(existing ?? new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
