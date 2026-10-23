using System;
using KryticalNova.Audio;
using KryticalNova.Networking;
using UnityEngine;
using UnityEngine.Audio;

namespace KryticalNova.UI
{
    /// <summary>The volume sliders of the Settings window, in the order they are shown.</summary>
    public enum AudioChannel { Master, Music, Effects, Ambience, Interface }

    /// <summary>
    /// The player's volume options, persisted in PlayerPrefs like <see cref="HudPreferences"/>, and the one place
    /// that writes them to the game's AudioMixer. A slider runs 0-1; the mixer takes decibels. A channel the mixer
    /// has no group for (see <see cref="Has"/>) is simply not driven.
    /// </summary>
    public static class AudioPreferences
    {
        // The mixer's exposed volume parameters, by channel.
        private static readonly string[] Parameters = { "MasterVolume", "MusicVolume", "SfxVolume", "AmbienceVolume", "UiVolume" };
        private static readonly string[] Keys = { "master", "music", "effects", "ambience", "interface" };
        private const float Silent = -80f;
        /// <summary>The furthest the beat can be moved either way, in milliseconds.</summary>
        public const float BeatOffsetLimit = 150f;
        /// <summary>
        /// Where every volume slider starts (owner, 21 Oct 2026): half, so nobody's first minute in the game is too
        /// loud. The player moves them from there and the game keeps what they choose.
        /// </summary>
        public const float DefaultVolume = 0.5f;
        // Bumped when the starting volumes change for players who already have some saved: on the first start after
        // it, their saved volumes are put back to the new start once (1 = everything at half, 21 Oct 2026).
        private const int VolumeDefaultsVersion = 1;

        private static readonly string Prefix = BuildPrefix();
        private static AudioMixer mixer;

        private static string BuildPrefix()
        {
            string profile = NetworkConnectionManager.LocalProfile();
            return string.IsNullOrEmpty(profile) ? "kn.audio." : "kn.audio." + profile + ".";
        }

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            mixer = null;
            UpgradeSavedVolumes();
            Application.focusChanged -= OnFocusChanged;
            Application.focusChanged += OnFocusChanged;
            AudioListener.volume = 1f;
            CombatBeat.OutputDelaySeconds = BeatOffset / 1000.0;
        }

        public static float Get(AudioChannel channel) => Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + Keys[(int)channel], DefaultVolume));

        /// <summary>
        /// Volumes saved before the starting volumes last changed go back to the new start, once per profile; after
        /// that the player's own choice is kept. Runs before anything reads a volume.
        /// </summary>
        private static void UpgradeSavedVolumes()
        {
            string key = Prefix + "volumeDefaults";
            if (PlayerPrefs.GetInt(key, 0) >= VolumeDefaultsVersion) return;
            foreach (string channel in Keys) PlayerPrefs.DeleteKey(Prefix + channel);
            PlayerPrefs.SetInt(key, VolumeDefaultsVersion);
            PlayerPrefs.Save();
        }

        public static void Set(AudioChannel channel, float value01)
        {
            value01 = Mathf.Clamp01(value01);
            if (Mathf.Approximately(Get(channel), value01)) return;
            PlayerPrefs.SetFloat(Prefix + Keys[(int)channel], value01);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        /// <summary>
        /// How much later this player hears the fight music than the game plays it, in milliseconds (speakers, a
        /// wireless headset). Everything that keeps time to the beat is moved by it; a negative value moves it earlier.
        /// </summary>
        public static float BeatOffset
        {
            get => Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "beatOffset", 0f), -BeatOffsetLimit, BeatOffsetLimit);
            set
            {
                value = Mathf.Round(Mathf.Clamp(value, -BeatOffsetLimit, BeatOffsetLimit));
                if (Mathf.Approximately(BeatOffset, value)) return;
                PlayerPrefs.SetFloat(Prefix + "beatOffset", value);
                PlayerPrefs.Save();
                CombatBeat.OutputDelaySeconds = value / 1000.0;
                Changed?.Invoke();
            }
        }

        /// <summary>Every volume back to its start (<see cref="DefaultVolume"/>), the beat where the game plays it, and
        /// silence in the background on.</summary>
        public static void ResetToDefaults()
        {
            foreach (string key in Keys) PlayerPrefs.DeleteKey(Prefix + key);
            PlayerPrefs.DeleteKey(Prefix + "muteBackground");
            PlayerPrefs.DeleteKey(Prefix + "beatOffset");
            CombatBeat.OutputDelaySeconds = 0.0;
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        /// <summary>The Beat Ring round the pet in a fight, which shows the fight music's beat (on unless switched off).</summary>
        public static bool BeatRing
        {
            get => PlayerPrefs.GetInt(Prefix + "beatRing", 1) != 0;
            set
            {
                if (BeatRing == value) return;
                PlayerPrefs.SetInt(Prefix + "beatRing", value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>The COMBO word of the Cadence combo on the screen in a fight (on unless switched off).</summary>
        public static bool ComboMeter
        {
            get => PlayerPrefs.GetInt(Prefix + "comboMeter", 1) != 0;
            set
            {
                if (ComboMeter == value) return;
                PlayerPrefs.SetInt(Prefix + "comboMeter", value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>No sound while the game window is not the one in front.</summary>
        public static bool MuteInBackground
        {
            get => PlayerPrefs.GetInt(Prefix + "muteBackground", 1) != 0;
            set
            {
                if (MuteInBackground == value) return;
                PlayerPrefs.SetInt(Prefix + "muteBackground", value ? 1 : 0);
                PlayerPrefs.Save();
                OnFocusChanged(Application.isFocused);
                Changed?.Invoke();
            }
        }

        /// <summary>Hands over the mixer the sliders drive (the zone scene's HUD does) and applies the saved volumes.</summary>
        public static void Bind(AudioMixer target)
        {
            mixer = target;
            Apply();
        }

        /// <summary>True once a mixer is bound, so the saved volumes are applied.</summary>
        public static bool IsBound => mixer != null;

        /// <summary>True when the bound mixer has a group for the channel.</summary>
        public static bool Has(AudioChannel channel) => mixer != null && mixer.GetFloat(Parameters[(int)channel], out _);

        private static void Apply()
        {
            if (mixer == null) return;
            for (int i = 0; i < Parameters.Length; i++)
                mixer.SetFloat(Parameters[i], ToDecibels(Get((AudioChannel)i)));
        }

        private static float ToDecibels(float value01) => value01 <= 0.0001f ? Silent : Mathf.Log10(value01) * 20f;

        private static void OnFocusChanged(bool focused) => AudioListener.volume = focused || !MuteInBackground ? 1f : 0f;
    }
}
