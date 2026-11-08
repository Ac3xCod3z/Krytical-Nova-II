using System.Collections.Generic;
using System.Linq;
using KryticalNova.EditorTools;
using KryticalNova.GameFeel;
using MoreMountains.Feedbacks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The sounds of the game feel and the menu feel: every slot the table names has clips, every sound slot on the
    /// two prefabs is filled, plays through its own sources into a mixer group (so a Settings slider governs it),
    /// never at full volume, and a moment that sounds per faction has clips for all three.
    /// </summary>
    public sealed class SoundSlotTests
    {
        private static readonly string[] Prefabs = { GameFeelBuilder.PrefabPath, GameFeelBuilder.MenuFeelPrefabPath };

        private static IEnumerable<(string Prefab, MMF_Player Player, MMF_Sound Sound)> Slots()
        {
            foreach (string path in Prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, path);
                foreach (var player in prefab.GetComponentsInChildren<MMF_Player>(true))
                    foreach (MMF_Sound sound in player.FeedbacksList.OfType<MMF_Sound>())
                        yield return (path, player, sound);
            }
        }

        [Test]
        public void EverySlotInTheTable_HasClips()
        {
            foreach (string slot in GameFeelBuilder.SoundSlotNames)
                Assert.IsNotEmpty(GameFeelBuilder.SoundClips(slot), slot + " has no SFX_ files in its folder");
        }

        [Test]
        public void EverySoundSlot_IsFilled_Routed_AndNeverAtFullVolume()
        {
            var seen = new HashSet<string>();
            foreach ((string prefab, MMF_Player player, MMF_Sound sound) in Slots())
            {
                string where = $"{sound.Label} on {player.name} ({prefab})";
                seen.Add(sound.Label);
                Assert.IsInstanceOf<MMF_VariedSound>(sound, where + " is not a varied sound");
                var varied = (MMF_VariedSound)sound;
                bool perMount = varied.KeyedSfx != null && varied.KeyedSfx.Length > 0; // a mount's own voice: one set per mount
                Assert.IsTrue(perMount
                    ? varied.KeyedSfx.All(set => set.Clips != null && set.Clips.Length > 0 && set.Clips.All(clip => clip != null))
                    : sound.RandomSfx != null && sound.RandomSfx.Length > 0 && sound.RandomSfx.All(clip => clip != null), where + " has no clips");
                Assert.AreEqual(MMF_Sound.PlayMethods.Pool, sound.PlayMethod, where + " needs something else in the scene to be heard");
                Assert.IsNotNull(sound.SfxAudioMixerGroup, where + " plays past the mixer: no slider governs it");
                Assert.LessOrEqual(sound.MaxVolume, GameFeelBuilder.LoudestSlot + 0.001f, where + " is louder than any slot may be");
                Assert.LessOrEqual(sound.MinVolume, sound.MaxVolume, where);
                Assert.IsFalse(sound.Timing.ContributeToTotalDuration, where + " would keep its player counted as playing");
            }
            foreach (string slot in GameFeelBuilder.SoundSlotNames)
                Assert.IsTrue(seen.Contains(slot), slot + " is in the table but on neither prefab");
        }

        [Test]
        public void InterfaceSlots_AnswerToTheInterfaceSlider_TheRestToEffects()
        {
            var menu = AssetDatabase.LoadAssetAtPath<GameObject>(GameFeelBuilder.MenuFeelPrefabPath);
            foreach (var player in menu.GetComponentsInChildren<MMF_Player>(true))
                foreach (MMF_Sound sound in player.FeedbacksList.OfType<MMF_Sound>())
                    Assert.AreEqual("UI", sound.SfxAudioMixerGroup.name, sound.Label);

            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(AudioSetup.MixerPath);
            foreach (string parameter in new[] { "MasterVolume", "MusicVolume", "SfxVolume", "AmbienceVolume", "UiVolume" })
                Assert.IsTrue(mixer.GetFloat(parameter, out _), parameter + " is not exposed: its slider would do nothing");
        }

        /// <summary>
        /// Owner final test (8 Nov 2026): every mount sound (summon, vanish, push-off, saddle, touchdown, roll skid,
        /// shockwave) plays through the Sfx group, so the Settings Effects slider (SfxVolume) governs it.
        /// </summary>
        [Test]
        public void EveryMountSound_AnswersToTheEffectsSlider()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameFeelBuilder.PrefabPath);
            int ride = 0;
            foreach (var player in prefab.GetComponentsInChildren<MMF_Player>(true))
                foreach (MMF_Sound sound in player.FeedbacksList.OfType<MMF_Sound>())
                {
                    if (sound.Label == null || !sound.Label.StartsWith("Ride ")) continue;
                    ride++;
                    Assert.IsNotNull(sound.SfxAudioMixerGroup, sound.Label + " plays past the mixer");
                    Assert.AreEqual("Sfx", sound.SfxAudioMixerGroup.name, sound.Label + " should answer to the Effects slider");
                }
            Assert.GreaterOrEqual(ride, 7, "push-off, saddle land, touchdown, roll skid, shockwave, summon, vanish");
        }

        [Test]
        public void ADeath_SoundsOfEachFaction()
        {
            foreach ((_, MMF_Player player, MMF_Sound sound) in Slots())
            {
                if (sound.Label != "Death" && sound.Label != "Critical Death") continue;
                var varied = (MMF_VariedSound)sound;
                Assert.IsNotEmpty(varied.HegemonySfx, sound.Label + ": Hegemony");
                Assert.IsNotEmpty(varied.NullSfx, sound.Label + ": Null");
                Assert.IsNotEmpty(varied.CovenantSfx, sound.Label + ": Covenant");
            }
        }

        [Test]
        public void EveryWeatherWithSound_HasItsLoop_AndThunderIsThere()
        {
            Assert.IsNotEmpty(WeatherAudioSetup.ThunderClips(), "no SFX_Thunder_ clips in " + WeatherAudioSetup.Folder);
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip AMB_Weather_", new[] { WeatherAudioSetup.Folder }))
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                Assert.AreEqual(AudioClipLoadType.Streaming, importer.defaultSampleSettings.loadType, importer.assetPath + " should stream");
            }
        }

        /// <summary>
        /// Owner, 8 Nov 2026: every mount is summoned and vanishes with its own voice, as data (MountDefinition.soundSet ->
        /// SFX_RideSummon_&lt;set&gt;_nn / SFX_RideVanish_&lt;set&gt;_nn). A mount that names a set has both, and no set holds
        /// another mount's clips.
        /// </summary>
        [Test]
        public void EveryMountThatNamesASoundSet_HasItsOwnSummonAndVanish()
        {
            int named = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:MountDefinition"))
            {
                var mount = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guid));
                string set = mount != null ? new SerializedObject(mount).FindProperty("soundSet")?.stringValue : null;
                if (string.IsNullOrEmpty(set)) continue;
                named++;
                foreach (string slot in new[] { "Ride Summon", "Ride Vanish" })
                {
                    MMF_VariedSound.KeyedSet[] sets = GameFeelBuilder.MountSoundSets(slot);
                    MMF_VariedSound.KeyedSet own = sets.FirstOrDefault(s => s.Key == set);
                    Assert.IsNotNull(own.Clips, $"{mount.name}: no {slot} set '{set}'");
                    Assert.IsNotEmpty(own.Clips, $"{mount.name}: {slot} set '{set}' is empty");
                    Assert.IsTrue(own.Clips.All(c => c.name.Contains("_" + set + "_")), $"{slot} set '{set}' holds another set's clip");
                }
            }
            Assert.GreaterOrEqual(named, 2, "the Brambleback and the Circuit Strider name their sound sets");
        }
    }
}
