using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using KryticalNova.GameFeel;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The sounds of the game feel and of the menu feel: for every sound slot, by its name, which folder its clips
    /// are in, which volume slider it answers to, how loud it plays and how far its pitch may wander. A slot plays
    /// every clip named <c>SFX_&lt;Slot&gt;_&lt;number&gt;</c> in its folder, never the same one twice running, so a
    /// further variation is a file dropped in beside the others and <b>Krytical Nova ▸ Setup ▸ Sounds</b> run again.
    /// The numbers here are where a slot starts; from then on they are tuned on the prefab like the rest of the feel,
    /// until the prefab is built again.
    /// </summary>
    public static partial class GameFeelBuilder
    {
        public const string SfxFolder = "Assets/_Game/Audio/SFX";
        private const string UnassignedSuffix = " (assign clip)";
        /// <summary>No slot plays louder than this: the mix keeps its headroom when several sound at once.</summary>
        public const float LoudestSlot = 0.7f;

        private enum SoundChannel { Effects, Interface }

        private readonly struct SoundSpec
        {
            public readonly string Folder;
            public readonly SoundChannel Channel;
            public readonly float Volume;
            /// <summary>The pitch range, in semitones below and above the clip as recorded.</summary>
            public readonly float Lowest, Highest;
            /// <summary>How many of it can sound at once.</summary>
            public readonly int Voices;
            /// <summary>Cut off when its player is stopped (a charge let go early).</summary>
            public readonly bool StopsWithPlayer;

            public SoundSpec(string folder, SoundChannel channel, float volume, float lowest, float highest, int voices, bool stopsWithPlayer = false)
            {
                Folder = folder;
                Channel = channel;
                Volume = volume;
                Lowest = lowest;
                Highest = highest;
                Voices = voices;
                StopsWithPlayer = stopsWithPlayer;
            }
        }

        private static SoundSpec Combat(float volume, float lowest, float highest, int voices, bool stopsWithPlayer = false) =>
            new SoundSpec("Combat", SoundChannel.Effects, volume, lowest, highest, voices, stopsWithPlayer);
        private static SoundSpec Movement(float volume, float lowest, float highest, int voices) =>
            new SoundSpec("Movement", SoundChannel.Effects, volume, lowest, highest, voices);
        private static SoundSpec Moment(float volume, float lowest = -0.5f, float highest = 0.5f, int voices = 1) =>
            new SoundSpec("Moments", SoundChannel.Effects, volume, lowest, highest, voices);
        private static SoundSpec Interface(float volume, float lowest, float highest, int voices = 2) =>
            new SoundSpec("Interface", SoundChannel.Interface, volume, lowest, highest, voices);

        private static readonly Dictionary<string, SoundSpec> SoundSpecs = new Dictionary<string, SoundSpec>
        {
            // ---- fighting: heard many times a minute, so several clips each and a wide pitch range
            ["Swing Whoosh"] = Combat(0.40f, -2f, 2f, 4),
            ["Hit"] = Combat(0.45f, -2f, 2f, 5),
            ["Heavy Hit"] = Combat(0.60f, -2f, 1f, 3),
            ["Hurt"] = Combat(0.50f, -3f, 0f, 3),
            ["PvP Critical"] = Combat(0.70f, -3f, 0f, 2),
            ["Skill Slash"] = Combat(0.50f, -1.5f, 1.5f, 3),
            ["Buff Cast"] = Combat(0.45f, -1f, 1f, 2),
            ["Charge Build-up"] = Combat(0.40f, -1f, 1f, 1, stopsWithPlayer: true),
            ["Kill Confirm"] = Combat(0.50f, -2f, 2f, 2),

            // ---- moving
            ["Footstep Burst"] = Movement(0.25f, -2f, 2f, 2),
            ["Jump"] = Movement(0.35f, 0f, 3f, 2),
            ["Land Thud"] = Movement(0.45f, -2f, 1f, 2),
            ["Body Thud"] = Movement(0.50f, -4f, -1f, 2),

            // ---- the player's own moments: rare, one clip each, close to the pitch they were made at
            ["Death"] = Moment(0.60f),
            ["Critical Death"] = Moment(0.65f),
            ["Respawn"] = Moment(0.50f),
            ["Pet Evolved"] = Moment(0.55f),
            ["Level Up"] = Moment(0.60f),
            ["Rare Find"] = Moment(0.50f, -1f, 1f, 2),
            ["Forge Success"] = Moment(0.55f, -1.5f, 1.5f, 2),
            ["Forge Fail"] = Moment(0.50f, -3f, 0f, 2),
            ["Forge Break"] = Moment(0.60f),
            ["Ascension Rise"] = Moment(0.55f),
            ["Nova Tier"] = Moment(0.65f),
            ["Ascension Hold"] = Moment(0.40f),
            ["Ascension Slip"] = Moment(0.50f),
            ["Nova Held"] = Moment(0.50f),
            ["Standing Rank"] = Moment(0.50f),

            // ---- the interface: quiet, and the ones under the pointer all the time the quietest
            ["Click Tick"] = Interface(0.25f, -3f, 3f, 3),
            ["Select"] = Interface(0.35f, -1f, 1f),
            ["Hover"] = Interface(0.20f, -2f, 2f, 3),
            ["Press"] = Interface(0.40f, -1.5f, 1.5f),
            ["Confirm"] = Interface(0.45f, -1f, 1f),
            ["Back"] = Interface(0.40f, -2f, 0f),
            ["Error"] = Interface(0.45f, -0.5f, 0.5f),
            ["Toggle"] = Interface(0.40f, -2f, 2f),
            ["Slide"] = Interface(0.20f, -4f, 4f, 3),
            ["Open"] = Interface(0.40f, -1.5f, 1.5f),
            ["Tab"] = Interface(0.35f, -2f, 2f),
            ["Page Forward"] = Interface(0.30f, 0f, 2f),
            ["Page Back"] = Interface(0.30f, -2f, 0f),
        };

        /// <summary>Every slot the table knows, for the checks.</summary>
        public static IEnumerable<string> SoundSlotNames => SoundSpecs.Keys;

        /// <summary>The clips of a slot: every <c>SFX_&lt;Slot&gt;_&lt;number&gt;</c> in its folder, in order.</summary>
        public static AudioClip[] SoundClips(string slot)
        {
            if (!SoundSpecs.TryGetValue(slot, out SoundSpec spec)) return System.Array.Empty<AudioClip>();
            string folder = SfxFolder + "/" + spec.Folder;
            if (!AssetDatabase.IsValidFolder(folder)) return System.Array.Empty<AudioClip>();
            var name = new Regex("^SFX_" + Regex.Escape(SoundFileKey(slot)) + @"_\d+$");
            return AssetDatabase.FindAssets("t:AudioClip", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => name.IsMatch(System.IO.Path.GetFileNameWithoutExtension(path)))
                .OrderBy(path => path, System.StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
                .Where(clip => clip != null)
                .ToArray();
        }

        private static string SoundFileKey(string slot) => slot.Replace(" ", string.Empty).Replace("-", string.Empty);

        /// <summary>
        /// Gives every sound slot of the game feel and the menu feel its clips, volume, pitch range and mixer group
        /// from the table, leaving everything else on the two prefabs as it was tuned. Returns how many slots sound.
        /// </summary>
        public static int ApplySounds()
        {
            AudioMixer mixer = AudioSetup.EnsureMixer();
            int sounding = 0;
            foreach (string path in new[] { PrefabPath, MenuFeelPrefabPath })
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                using (var scope = new PrefabUtility.EditPrefabContentsScope(path))
                {
                    foreach (var player in scope.prefabContentsRoot.GetComponentsInChildren<MMF_Player>(true))
                    {
                        // The menu feel inside the game feel prefab is set on its own prefab, not overridden here.
                        if (PrefabUtility.IsPartOfPrefabInstance(player)) continue;
                        for (int i = 0; i < player.FeedbacksList.Count; i++)
                        {
                            if (!(player.FeedbacksList[i] is MMF_Sound old)) continue;
                            string slot = SlotOf(old.Label);
                            if (!SoundSpecs.ContainsKey(slot)) continue;

                            var sound = old as MMF_VariedSound;
                            if (sound == null)
                            {
                                sound = player.AddFeedback(typeof(MMF_VariedSound), false) as MMF_VariedSound;
                                player.FeedbacksList[i] = sound;
                            }
                            if (ConfigureSound(sound, slot, mixer)) sounding++;
                        }
                        EditorUtility.SetDirty(player);
                    }
                }
            }
            return sounding;
        }

        private static string SlotOf(string label) =>
            label != null && label.EndsWith(UnassignedSuffix) ? label.Substring(0, label.Length - UnassignedSuffix.Length) : label ?? string.Empty;

        /// <summary>Sets a slot from the table. False when the slot has no clips yet (it is left labelled as unassigned).</summary>
        private static bool ConfigureSound(MMF_VariedSound sound, string slot, AudioMixer mixer)
        {
            SoundSpec spec = SoundSpecs[slot];
            AudioClip[] clips = SoundClips(slot);
            sound.Label = clips.Length > 0 ? slot : slot + UnassignedSuffix;
            sound.Sfx = null;
            sound.RandomSfx = clips;
            // Its own audio sources, on the feel prefab: nothing else in the scene is needed for it to be heard.
            sound.PlayMethod = MMF_Sound.PlayMethods.Pool;
            sound.PoolSize = spec.Voices;
            float volume = Mathf.Min(spec.Volume, LoudestSlot);
            sound.MinVolume = volume * 0.9f;
            sound.MaxVolume = volume;
            sound.MinPitch = Mathf.Pow(2f, spec.Lowest / 12f);
            sound.MaxPitch = Mathf.Pow(2f, spec.Highest / 12f);
            sound.SpatialBlend = 0f;
            sound.StopSoundOnFeedbackStop = spec.StopsWithPlayer;
            // A long clip must not make its player count as still playing: the charge is stopped by that.
            sound.Timing.ContributeToTotalDuration = false;
            if (mixer != null)
            {
                AudioMixerGroup[] groups = mixer.FindMatchingGroups(spec.Channel == SoundChannel.Interface ? "UI" : "Sfx");
                if (groups.Length > 0) sound.SfxAudioMixerGroup = groups[0];
            }
            return clips.Length > 0;
        }

        private static void Sound(MMF_Player player, string label)
        {
            var sound = player.AddFeedback(typeof(MMF_VariedSound)) as MMF_VariedSound;
            if (SoundSpecs.ContainsKey(label)) ConfigureSound(sound, label, AudioSetup.EnsureMixer());
            else sound.Label = label + UnassignedSuffix;
        }
    }
}
