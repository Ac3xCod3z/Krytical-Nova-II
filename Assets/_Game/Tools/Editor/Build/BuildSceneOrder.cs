using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The scenes that go into a build, decided in one place: the front end first (Title is the boot scene), then the
    /// world. Nothing else ships: development scenes stay out of the list. Scenes that do not exist yet are skipped,
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

        public static void Apply()
        {
            var current = EditorBuildSettings.scenes;
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (string path in Scenes)
            {
                if (!File.Exists(path)) continue;
                var existing = Array.Find(current, s => s.path == path);
                scenes.Add(existing ?? new EditorBuildSettingsScene(path, true));
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
