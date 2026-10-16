using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The game's AudioMixer and who plays into which group. The mixer starts as a copy of FEEL's sound-manager mixer
    /// (Master with Music, Sfx and UI under it, each volume exposed) and gains an Ambience group. The zone scene step
    /// then routes what plays sound: weather loops and thunder to Ambience, the game feel's sound slots to Sfx and
    /// the menu feel's (with the world's click and select) to UI, so every sound answers to one of the Settings
    /// sliders. <see cref="Sounds"/> is the same without the scene: how the clips are imported, the slots' clips
    /// (GameFeelBuilder.ApplySounds) and the weather's (<see cref="WeatherAudioSetup"/>).
    /// Safe to run again; a mixer that already exists is kept as it is.
    /// </summary>
    public static class AudioSetup
    {
        public const string MixerPath = "Assets/_Game/Audio/Mixers/KN_Main.mixer";
        private const string SourceMixer = "Assets/_ThirdParty/Feel/MMTools/Core/MMAudio/MMSoundManager/Settings/MMSoundManagerAudioMixer.mixer";
        public const string AudioFolder = "Assets/_Game/Audio";
        /// <summary>A clip shorter than this is unpacked when it loads and plays with no decoding cost.</summary>
        private const float ShortClipSeconds = 1.5f;
        private const string AmbienceGroup = "Ambience";
        private const string AmbienceParameter = "AmbienceVolume";
        // The volumes FEEL's mixer exposes already (and ours, once it is there).
        private static readonly string[] KnownParameters = { "MasterVolume", "MusicVolume", "SfxVolume", "UiVolume", AmbienceParameter };

        public static AudioMixer EnsureMixer()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null)
            {
                WeatherVfxBuilder.EnsureFolder("Assets/_Game/Audio/Mixers");
                if (!AssetDatabase.CopyAsset(SourceMixer, MixerPath))
                {
                    Debug.LogError($"[Audio] Could not copy {SourceMixer} to {MixerPath}.");
                    return null;
                }
                AssetDatabase.ImportAsset(MixerPath);
                mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            }
            if (mixer != null && mixer.FindMatchingGroups(AmbienceGroup).Length == 0 && !TryAddAmbienceGroup(mixer))
                Debug.LogWarning($"[Audio] {MixerPath} has no '{AmbienceGroup}' group and one could not be added from code. " +
                                 $"Add it under Master in the Audio Mixer window and expose its volume as '{AmbienceParameter}'; " +
                                 "until then weather sound follows the Effects slider.");
            return mixer;
        }

        /// <summary>
        /// Adds the Ambience group and exposes its volume. Mixer authoring has no public API, so this goes through
        /// the editor's own controller by reflection and reports failure instead of throwing.
        /// </summary>
        private static bool TryAddAmbienceGroup(AudioMixer mixer)
        {
            try
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                Type controllerType = mixer.GetType(); // UnityEditor.Audio.AudioMixerController
                object master = controllerType.GetProperty("masterGroup", flags)?.GetValue(mixer);
                MethodInfo create = controllerType.GetMethod("CreateNewGroup", flags);
                MethodInfo addChild = controllerType.GetMethod("AddChildToParent", flags);
                if (master == null || create == null || addChild == null) return false;

                object group = create.Invoke(mixer, new object[] { AmbienceGroup, false });
                addChild.Invoke(mixer, new[] { group, master });

                // Expose the new group's volume under the name the Settings slider drives.
                Type groupType = group.GetType();
                object volumeGuid = groupType.GetMethod("GetGUIDForVolume", flags)?.Invoke(group, null);
                Type pathType = controllerType.Assembly.GetType("UnityEditor.Audio.AudioGroupParameterPath");
                MethodInfo expose = controllerType.GetMethod("AddExposedParameter", flags);
                if (volumeGuid == null || pathType == null || expose == null) return false;
                object path = Activator.CreateInstance(pathType, flags, null, new[] { group, volumeGuid }, null);
                expose.Invoke(mixer, new[] { path });

                // The parameter is created with a generated name, anywhere in the list: it is the one nobody named.
                var so = new SerializedObject(mixer);
                SerializedProperty exposed = so.FindProperty("m_ExposedParameters");
                if (exposed == null) return false;
                bool named = false;
                for (int i = 0; i < exposed.arraySize; i++)
                {
                    SerializedProperty name = exposed.GetArrayElementAtIndex(i).FindPropertyRelative("name");
                    if (System.Array.IndexOf(KnownParameters, name.stringValue) >= 0) continue;
                    name.stringValue = AmbienceParameter;
                    named = true;
                }
                if (!named) return false;
                so.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(mixer);
                AssetDatabase.SaveAssets();
                return mixer.FindMatchingGroups(AmbienceGroup).Length > 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Audio] Adding the Ambience group failed: {e.GetBaseException().Message}");
                return false;
            }
        }

        /// <summary>
        /// Sets up everything that sounds, outside any scene: the import settings of the game's clips, the clips of
        /// the game feel and menu feel sound slots, and the weather's loops (with the thunder of the open scene).
        /// </summary>
        [MenuItem("Krytical Nova/Setup/Sounds")]
        public static void Sounds()
        {
            int imported = ApplyImportSettings();
            int slots = (int?)PresentationTools.Call("GameFeelBuilder", "ApplySounds") ?? 0;
            int slotCount = (PresentationTools.Get("GameFeelBuilder", "SoundSlotNames") as System.Collections.Generic.IEnumerable<string>)?.Count() ?? 0;
            int loops = WeatherAudioSetup.Apply();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Audio] {slots} sound slot(s) of {slotCount} have clips; {loops} weather profile(s) have a loop; " +
                      $"{WeatherAudioSetup.ThunderClips().Length} thunder clip(s); {imported} clip(s) had their import settings changed.");
        }

        /// <summary>
        /// How the game's clips are imported, by what they are: a weather loop streams from disk; thunder and the
        /// long one-shots stay packed in memory; a short effect is unpacked on load. Returns how many changed.
        /// </summary>
        public static int ApplyImportSettings()
        {
            if (!AssetDatabase.IsValidFolder(AudioFolder)) return 0;
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { AudioFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (importer == null || clip == null) continue;

                bool loop = System.IO.Path.GetFileName(path).StartsWith("AMB_");
                bool brief = !loop && clip.length < ShortClipSeconds;
                AudioImporterSampleSettings want = importer.defaultSampleSettings;
                want.loadType = loop ? AudioClipLoadType.Streaming : brief ? AudioClipLoadType.DecompressOnLoad : AudioClipLoadType.CompressedInMemory;
                want.compressionFormat = brief ? AudioCompressionFormat.ADPCM : AudioCompressionFormat.Vorbis;
                want.quality = loop ? 0.5f : 0.7f;
                want.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
                want.preloadAudioData = !loop;

                AudioImporterSampleSettings have = importer.defaultSampleSettings;
                bool same = have.loadType == want.loadType && have.compressionFormat == want.compressionFormat &&
                            Mathf.Approximately(have.quality, want.quality) && have.sampleRateSetting == want.sampleRateSetting &&
                            have.preloadAudioData == want.preloadAudioData && !importer.forceToMono && importer.loadInBackground == !brief;
                if (same) continue;
                importer.defaultSampleSettings = want;
                importer.forceToMono = false; // the files are already mono where one channel is enough
                importer.loadInBackground = !brief;
                importer.SaveAndReimport();
                changed++;
            }
            return changed;
        }

        /// <summary>Routes the open scene's sound sources into the mixer, then sets up the sounds themselves.</summary>
        public static void RouteScene()
        {
            AudioMixer mixer = EnsureMixer();
            if (mixer == null) return;
            AudioMixerGroup effects = First(mixer, "Sfx");
            AudioMixerGroup ambience = First(mixer, AmbienceGroup) ?? effects;

            int routed = 0;
            foreach (var source in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (source.outputAudioMixerGroup != null) continue;
                // Everything in a zone that owns an AudioSource today is weather: loops and thunder.
                Undo.RecordObject(source, "Route Audio Source");
                source.outputAudioMixerGroup = ambience;
                EditorUtility.SetDirty(source);
                routed++;
            }
            if (routed > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Debug.Log($"[Audio] {routed} scene source(s) routed to {ambience.name}.");
            Sounds();
        }

        private static AudioMixerGroup First(AudioMixer mixer, string name)
        {
            AudioMixerGroup[] groups = mixer.FindMatchingGroups(name);
            return groups.Length > 0 ? groups[0] : null;
        }
    }
}
