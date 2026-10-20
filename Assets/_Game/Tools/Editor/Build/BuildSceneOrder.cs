using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The scenes that go into a build, decided in one place. The game ships the front end only (Title is the boot
    /// scene): every map is an Addressable of its own (owner, 20 Oct 2026; <see cref="MapAddressables"/>, which this
    /// keeps current) and is loaded through <c>MapScenes</c>. A dedicated server runs one map and never changes it, so
    /// its build lists the maps after the front end (<see cref="ShippedForServer"/>) and boots straight into its own
    /// (DedicatedServerBoot). Development scenes are in the Editor's list only and never ship. Scenes that do not exist
    /// yet are skipped, so the scene builders can call this in any order.
    /// </summary>
    public static class BuildSceneOrder
    {
        public const string MenuSceneFolder = "Assets/_Game/Scenes/MainMenu";
        public const string WorldScenePath = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";

        // Scene names match KryticalNova.UI.Menus.FrontEnd. The maps are not here: they are Addressables.
        private static readonly string[] Scenes =
        {
            MenuSceneFolder + "/Title.unity",
            MenuSceneFolder + "/FactionSelect.unity",
            MenuSceneFolder + "/CharacterCreator.unity",
            MenuSceneFolder + "/CharacterSelect.unity",
            MenuSceneFolder + "/Loading.unity"
        };

        // Scenes the Editor must know so that a second Editor can follow the host into them, and that never ship:
        // Shipped() leaves them out, and it is what a build takes.
        private static readonly string[] DevelopmentScenes =
        {
            "Assets/_Game/Scenes/Development/Test_Cadence.unity"
        };

        /// <summary>The scenes the game's build is made of, in order: the Editor's list without the development scenes.</summary>
        public static string[] Shipped()
        {
            Apply();
            var shipped = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled && Array.IndexOf(DevelopmentScenes, scene.path) < 0) shipped.Add(scene.path);
            return shipped.ToArray();
        }

        /// <summary>A dedicated server's scenes: the game's, then every map (Concord first), which it loads from its build list.</summary>
        public static string[] ShippedForServer()
        {
            var scenes = new List<string>(Shipped());
            foreach (string map in MapAddressables.MapScenePaths())
                if (!scenes.Contains(map)) scenes.Add(map);
            return scenes.ToArray();
        }

        public static void Apply()
        {
            var current = EditorBuildSettings.scenes;
            var scenes = new List<EditorBuildSettingsScene>();
            // The front end, then the development scenes. The maps are Addressables, kept current here.
            var paths = new List<string>(Scenes);
            paths.AddRange(DevelopmentScenes);
            foreach (string path in paths)
            {
                if (!File.Exists(path)) continue;
                var existing = Array.Find(current, s => s.path == path);
                scenes.Add(existing ?? new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
            MapAddressables.Apply();
        }
    }
}
