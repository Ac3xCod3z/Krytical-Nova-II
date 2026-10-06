using System;
using System.Reflection;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The game's AudioMixer and who plays into which group. The mixer starts as a copy of FEEL's sound-manager mixer
    /// (Master with Music, Sfx and UI under it, each volume exposed) and gains an Ambience group. The zone scene step
    /// then routes what plays sound today: weather loops and thunder to Ambience, the game feel sound slots to Sfx.
    /// Safe to run again; a mixer that already exists is kept as it is.
    /// </summary>
    public static class AudioSetup
    {
        public const string MixerPath = "Assets/_Game/Audio/Mixers/KN_Main.mixer";
        private const string SourceMixer = "Assets/_ThirdParty/Feel/MMTools/Core/MMAudio/MMSoundManager/Settings/MMSoundManagerAudioMixer.mixer";
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

        /// <summary>Routes the open scene's sound sources, and the game feel prefab's sound slots, into the mixer.</summary>
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

            int slots = RouteGameFeel(effects);
            Debug.Log($"[Audio] {routed} scene source(s) routed to {ambience.name}; {slots} game feel sound slot(s) routed to {effects.name}.");
        }

        private static int RouteGameFeel(AudioMixerGroup effects)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GameFeelBuilder.PrefabPath) == null) return 0;
            int slots = 0;
            using (var scope = new PrefabUtility.EditPrefabContentsScope(GameFeelBuilder.PrefabPath))
            {
                foreach (var player in scope.prefabContentsRoot.GetComponentsInChildren<MMF_Player>(true))
                {
                    foreach (MMF_Feedback feedback in player.FeedbacksList)
                    {
                        if (!(feedback is MoreMountains.Feedbacks.MMF_Sound sound) || sound.SfxAudioMixerGroup == effects) continue;
                        sound.SfxAudioMixerGroup = effects;
                        slots++;
                    }
                    if (slots > 0) EditorUtility.SetDirty(player);
                }
            }
            return slots;
        }

        private static AudioMixerGroup First(AudioMixer mixer, string name)
        {
            AudioMixerGroup[] groups = mixer.FindMatchingGroups(name);
            return groups.Length > 0 ? groups[0] : null;
        }
    }
}
