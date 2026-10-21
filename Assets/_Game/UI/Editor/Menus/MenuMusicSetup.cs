using KryticalNova.Audio;
using KryticalNova.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Gives each menu scene (Title, FactionSelect, CharacterCreator, CharacterSelect) the menus' music player
    /// (<see cref="MenuMusicPlayer"/>), pointed at <see cref="MusicSetup.MenuMusicPath"/>, which Setup ▸ Music makes from
    /// the music manifest. Every menu scene has one, so Play started from any of them has music; at run time the first
    /// alive keeps playing from scene to scene. Safe to run again.
    /// </summary>
    public static class MenuMusicSetup
    {
        private const string PlayerName = "Menu Music";
        private static readonly System.Reflection.FieldInfo MusicField = typeof(MenuMusicPlayer).GetField("music",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        private static readonly string[] MenuScenes =
        {
            "Assets/_Game/Scenes/MainMenu/Title.unity",
            "Assets/_Game/Scenes/MainMenu/FactionSelect.unity",
            "Assets/_Game/Scenes/MainMenu/CharacterCreator.unity",
            "Assets/_Game/Scenes/MainMenu/CharacterSelect.unity",
        };

        [MenuItem("Krytical Nova/Setup/Menu Music", priority = 14)]
        public static int Build()
        {
            var music = AssetDatabase.LoadAssetAtPath<MenuMusic>(MusicSetup.MenuMusicPath);
            if (music == null)
            {
                Debug.LogWarning($"[Music] No {MusicSetup.MenuMusicPath}: run Krytical Nova > Setup > Music first.");
                return 0;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return 0;
            string back = EditorSceneManager.GetActiveScene().path;

            AudioMixer mixer = AudioSetup.EnsureMixer();
            AudioMixerGroup[] groups = mixer != null ? mixer.FindMatchingGroups("Music") : new AudioMixerGroup[0];
            AudioMixerGroup output = groups.Length > 0 ? groups[0] : null;

            int done = 0;
            foreach (string path in MenuScenes)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
                {
                    Debug.LogWarning($"[Music] No menu scene at {path}.");
                    continue;
                }
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                GameObject host = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (root.name == PlayerName) host = root;
                if (host == null) host = new GameObject(PlayerName);
                if (!host.TryGetComponent(out MenuMusicPlayer player)) player = host.AddComponent<MenuMusicPlayer>();
                var so = new SerializedObject(player);
                so.FindProperty("output").objectReferenceValue = output;
                so.ApplyModifiedPropertiesWithoutUndo();
                // SerializedProperty refused this one reference without a word on 21 Oct 2026 (it stayed empty), so the
                // field is set directly and checked.
                MusicField.SetValue(player, music);
                EditorUtility.SetDirty(player);
                if (MusicField.GetValue(player) as MenuMusic != music)
                    Debug.LogError($"[Music] {path}: the menu music player did not take the MenuMusic asset.");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                done++;
            }

            if (!string.IsNullOrEmpty(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            Debug.Log($"[Music] Menu music in {done} of {MenuScenes.Length} menu scene(s).");
            return done;
        }
    }
}
