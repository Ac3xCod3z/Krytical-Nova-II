using System.Linq;
using KryticalNova.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The sound of the weather: which loop each weather profile plays and how loud, and the thunder the open
    /// scene's lightning picks from. Loops are <c>AMB_Weather_&lt;Name&gt;</c> and thunder is every
    /// <c>SFX_Thunder_&lt;number&gt;</c> in the weather folder. Clear skies are silent on purpose.
    /// </summary>
    public static class WeatherAudioSetup
    {
        public const string Folder = "Assets/_Game/Audio/Ambience/Weather";
        private const string ProfileFolder = "Assets/_Game/World/Data/Assets/Weather";
        private const float ThunderVolume = 0.8f;

        // Profile, loop, volume. The loops are levelled alike, so the volume is how present the weather is.
        private static readonly (string Profile, string Loop, float Volume)[] Loops =
        {
            ("WP_Clear", null, 0f),
            ("WP_FewClouds", null, 0f),
            ("WP_PartlyCloudy", "Wind", 0.12f),
            ("WP_MostlyCloudy", "Wind", 0.20f),
            ("WP_Overcast", "Wind", 0.30f),
            ("WP_Fog", "FogWind", 0.30f),
            ("WP_LightRain", "LightRain", 0.90f), // a quiet recording: drops over near silence
            ("WP_Rain", "Rain", 0.55f),
            ("WP_Thunderstorm", "Storm", 0.70f),
            ("WP_Snow", "Snow", 0.50f),
            ("WP_Blizzard", "Blizzard", 0.65f),
        };

        public static AudioClip[] ThunderClips() =>
            AssetDatabase.FindAssets("t:AudioClip SFX_Thunder_", new[] { Folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
                .Where(clip => clip != null)
                .ToArray();

        /// <summary>Sets the profiles' loops and the open scene's thunder. Returns how many profiles have a loop.</summary>
        public static int Apply()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) return 0;
            int looping = 0;
            foreach ((string name, string loop, float volume) in Loops)
            {
                var profile = AssetDatabase.LoadAssetAtPath<WeatherProfile>($"{ProfileFolder}/{name}.asset");
                if (profile == null) continue;
                AudioClip clip = loop == null ? null : AssetDatabase.LoadAssetAtPath<AudioClip>($"{Folder}/AMB_Weather_{loop}.wav");
                if (clip != null) looping++;
                if (profile.ambienceLoop == clip && (clip == null || Mathf.Approximately(profile.ambienceVolume, volume))) continue;
                profile.ambienceLoop = clip;
                if (clip != null) profile.ambienceVolume = volume;
                EditorUtility.SetDirty(profile);
            }

            AudioClip[] thunder = ThunderClips();
            foreach (var lightning in Object.FindObjectsByType<LightningController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var so = new SerializedObject(lightning);
                SerializedProperty clips = so.FindProperty("thunderClips");
                bool same = clips.arraySize == thunder.Length;
                for (int i = 0; same && i < thunder.Length; i++) same = clips.GetArrayElementAtIndex(i).objectReferenceValue == thunder[i];
                var source = so.FindProperty("thunderSource").objectReferenceValue as AudioSource;
                if (source != null && !Mathf.Approximately(source.volume, ThunderVolume))
                {
                    Undo.RecordObject(source, "Thunder Volume");
                    source.volume = ThunderVolume;
                    EditorUtility.SetDirty(source);
                    EditorSceneManager.MarkSceneDirty(lightning.gameObject.scene);
                }
                if (same) continue;
                clips.arraySize = thunder.Length;
                for (int i = 0; i < thunder.Length; i++) clips.GetArrayElementAtIndex(i).objectReferenceValue = thunder[i];
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(lightning.gameObject.scene);
            }
            return looping;
        }
    }
}
