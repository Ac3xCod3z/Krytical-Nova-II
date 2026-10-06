using System;
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
            Application.focusChanged -= OnFocusChanged;
            Application.focusChanged += OnFocusChanged;
            AudioListener.volume = 1f;
        }

        public static float Get(AudioChannel channel) => Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + Keys[(int)channel], 1f));

        public static void Set(AudioChannel channel, float value01)
        {
            value01 = Mathf.Clamp01(value01);
            if (Mathf.Approximately(Get(channel), value01)) return;
            PlayerPrefs.SetFloat(Prefix + Keys[(int)channel], value01);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
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
