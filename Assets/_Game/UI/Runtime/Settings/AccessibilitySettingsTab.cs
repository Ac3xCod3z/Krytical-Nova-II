using System;
using KryticalNova.Core;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Accessibility page of Settings (owner, 24 Oct 2026): how strong every flashing and pulsing effect is, a
    /// Reduce flashing switch, and the colour-blind correction of the screen; and a way to go through the first-time
    /// setup again (the photosensitivity warning, the display, the colours).
    /// </summary>
    public sealed class AccessibilitySettingsTab
    {
        private static readonly string[] ColourBlindNames = { "Off", "Protanopia (red)", "Deuteranopia (green)", "Tritanopia (blue)" };

        /// <summary>Raised when the player asks to see the first-time setup again.</summary>
        public static event Action FirstRunRequested;

        public void Build(VisualElement page)
        {
            page.Clear();
            SettingsRows.Header(page, "FLASHING AND PULSING");
            SettingsRows.Help(page, "Lights that pulse to the beat in a fight, flashes on hits and Cadence, lightning and other bright, quick changes. " +
                                    "Lower it, or switch on Reduce flashing, if flashing lights trouble you.");
            SettingsRows.Slider(page, "Strength of flashing effects", 0f, 1f, () => AccessibilityPreferences.FlashLevel,
                value => AccessibilityPreferences.FlashLevel = value, out _, amount => UnityEngine.Mathf.RoundToInt(amount * 100f) + " %");
            SettingsRows.Toggle(page, "Reduce flashing (a quarter at most)", () => AccessibilityPreferences.ReduceFlashing,
                value => AccessibilityPreferences.ReduceFlashing = value);

            SettingsRows.Header(page, "COLOUR");
            SettingsRows.Choice(page, "Colour-blind correction", ColourBlindNames, () => (int)AccessibilityPreferences.ColourBlind,
                value => AccessibilityPreferences.ColourBlind = (ColourBlindMode)value);
            SettingsRows.Slider(page, "Correction strength", 0f, 1f, () => AccessibilityPreferences.ColourBlindStrength,
                value => AccessibilityPreferences.ColourBlindStrength = value, out _, amount => UnityEngine.Mathf.RoundToInt(amount * 100f) + " %");

            SettingsRows.Header(page, "FIRST-TIME SETUP");
            var again = new Button(() => FirstRunRequested?.Invoke()) { text = "SHOW THE WARNING AND DISPLAY SETUP AGAIN" };
            again.AddToClassList("kn-button");
            again.AddToClassList("settings-action");
            page.Add(again);
        }
    }
}
