using System;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// Settings ▸ Accessibility, kept on this PC (owner, 24 Oct 2026: a photosensitivity warning and settings for every
    /// flashing effect, as most games have now, and a colour-blind setting). The values are handed to
    /// <see cref="Accessibility"/>, where every part of the game reads them. Whether the first-time setup (the warning,
    /// the display and the colours) has been done is kept here too.
    /// </summary>
    public static class AccessibilityPreferences
    {
        private const string Prefix = "kn.access.";
        /// <summary>The most flashing is allowed to be while Reduce flashing is on.</summary>
        public const float ReducedCap = 0.25f;

        public static event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Load()
        {
            Changed = null;
            Apply();
        }

        /// <summary>Flashing and pulsing effects, 0 to 1 (1: as made).</summary>
        public static float FlashLevel
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "flash", 1f));
            set => SetFloat("flash", Mathf.Clamp01(value));
        }

        /// <summary>Reduce flashing: every flash, strobe and pulse at a quarter at most, whatever the slider says.</summary>
        public static bool ReduceFlashing
        {
            get => PlayerPrefs.GetInt(Prefix + "reduce", 0) != 0;
            set => SetInt("reduce", value ? 1 : 0);
        }

        public static ColourBlindMode ColourBlind
        {
            get => (ColourBlindMode)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "colourBlind", 0), 0, 3);
            set => SetInt("colourBlind", (int)value);
        }

        /// <summary>How strongly the colour-blind correction is laid on, 0 to 1.</summary>
        public static float ColourBlindStrength
        {
            get => Mathf.Clamp01(PlayerPrefs.GetFloat(Prefix + "colourBlindStrength", 1f));
            set => SetFloat("colourBlindStrength", Mathf.Clamp01(value));
        }

        /// <summary>True once the first-time setup (warning, display, colours) has been through on this PC.</summary>
        public static bool FirstRunDone
        {
            get => PlayerPrefs.GetInt(Prefix + "firstRun", 0) != 0;
            set { PlayerPrefs.SetInt(Prefix + "firstRun", value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>
        /// The photosensitivity warning was agreed to on this PC (owner, 24 Oct 2026: an agreement page whose answer is kept,
        /// so it does not come back on every start), and when (UTC, round-trip format). Empty while it has not been.
        /// </summary>
        public static string WarningAgreedOn
        {
            get => PlayerPrefs.GetString(Prefix + "warningAgreed", string.Empty);
            set
            {
                PlayerPrefs.SetString(Prefix + "warningAgreed", value ?? string.Empty);
                PlayerPrefs.Save();
                Accessibility.SetWarningAgreed(WarningAgreedTicks);
            }
        }

        private static long WarningAgreedTicks =>
            DateTime.TryParse(WarningAgreedOn, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime at) ? at.ToUniversalTime().Ticks : 0L;

        public static bool WarningAgreed => !string.IsNullOrEmpty(WarningAgreedOn);

        /// <summary>What every flash is multiplied by now.</summary>
        public static float EffectiveFlash => ReduceFlashing ? Mathf.Min(FlashLevel, ReducedCap) : FlashLevel;

        public static void ResetToDefaults()
        {
            foreach (string key in new[] { "flash", "reduce", "colourBlind", "colourBlindStrength" }) PlayerPrefs.DeleteKey(Prefix + key);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        private static void SetFloat(string key, float value)
        {
            if (Mathf.Approximately(PlayerPrefs.GetFloat(Prefix + key, float.NaN), value)) return;
            PlayerPrefs.SetFloat(Prefix + key, value);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        private static void SetInt(string key, int value)
        {
            if (PlayerPrefs.HasKey(Prefix + key) && PlayerPrefs.GetInt(Prefix + key) == value) return;
            PlayerPrefs.SetInt(Prefix + key, value);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        private static void Apply()
        {
            Accessibility.Set(EffectiveFlash, ColourBlind, ColourBlindStrength);
            Accessibility.SetWarningAgreed(WarningAgreedTicks);
        }
    }
}
