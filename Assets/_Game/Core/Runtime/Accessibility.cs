using System;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>A colour-blind correction the screen can be drawn with.</summary>
    public enum ColourBlindMode : byte
    {
        Off,
        /// <summary>Red-weak or red-blind.</summary>
        Protanopia,
        /// <summary>Green-weak or green-blind, the most common.</summary>
        Deuteranopia,
        /// <summary>Blue-weak or blue-blind.</summary>
        Tritanopia,
    }

    /// <summary>
    /// What the player asked for in Settings ▸ Accessibility (owner, 24 Oct 2026), where every layer can read it: how
    /// strong flashing and pulsing effects are (the beat glow, flashes, lightning; 1 as made, 0 none) and the colour-blind
    /// correction of the screen. The settings page keeps the values; this only carries them.
    /// </summary>
    public static class Accessibility
    {
        /// <summary>Every flash, strobe and pulse is multiplied by this, 0 to 1.</summary>
        public static float FlashScale { get; private set; } = 1f;
        public static ColourBlindMode ColourBlind { get; private set; }
        /// <summary>How strongly the colour-blind correction is laid on, 0 to 1.</summary>
        public static float ColourBlindStrength { get; private set; } = 1f;

        /// <summary>
        /// When the photosensitivity warning was agreed to on this PC (UTC ticks; 0: not yet). The player's save agent
        /// records it on the account once signed in (owner, 24 Oct 2026: KN keeps a record of the agreement).
        /// </summary>
        public static long WarningAgreedUtc { get; private set; }

        public static event Action Changed;

        /// <summary>Sets <see cref="WarningAgreedUtc"/>; the settings page calls it when the warning is agreed to and on start.</summary>
        public static void SetWarningAgreed(long utcTicks)
        {
            if (WarningAgreedUtc == utcTicks) return;
            WarningAgreedUtc = utcTicks;
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Changed = null;

        /// <summary>
        /// A colour as the colour-blind correction shows it (the same sums as the screen's correction shader), for
        /// colours drawn over the picture (menus and the HUD) that the screen pass does not reach.
        /// </summary>
        public static Color Correct(Color colour) => Correct(colour, ColourBlind, ColourBlindStrength);

        public static Color Correct(Color colour, ColourBlindMode mode, float strength)
        {
            if (mode == ColourBlindMode.Off || strength <= 0f) return colour;
            Color c = colour.linear;
            float r = c.r, g = c.g, b = c.b, sr, sg, sb;
            switch (mode)
            {
                case ColourBlindMode.Protanopia:
                    sr = 0.152286f * r + 1.052583f * g - 0.204868f * b;
                    sg = 0.114503f * r + 0.786281f * g + 0.099216f * b;
                    sb = -0.003882f * r - 0.048116f * g + 1.051998f * b;
                    break;
                case ColourBlindMode.Deuteranopia:
                    sr = 0.367322f * r + 0.860646f * g - 0.227968f * b;
                    sg = 0.280085f * r + 0.672501f * g + 0.047413f * b;
                    sb = -0.011820f * r + 0.042940f * g + 0.968881f * b;
                    break;
                default:
                    sr = 1.255528f * r - 0.076749f * g - 0.178779f * b;
                    sg = -0.078411f * r + 0.930809f * g + 0.147602f * b;
                    sb = 0.004733f * r + 0.691367f * g + 0.303900f * b;
                    break;
            }
            float lr = r - sr, lg = g - sg, lb = b - sb;
            Vector3 shift = mode == ColourBlindMode.Tritanopia
                ? new Vector3(lr + 0.7f * lb, lg + 0.7f * lb, 0f)
                : new Vector3(0f, 0.7f * lr + lg, 0.7f * lr + lb);
            var corrected = new Color(Mathf.Clamp01(r + shift.x * strength), Mathf.Clamp01(g + shift.y * strength),
                Mathf.Clamp01(b + shift.z * strength), colour.a);
            return corrected.gamma;
        }

        public static void Set(float flashScale, ColourBlindMode colourBlind, float colourBlindStrength)
        {
            FlashScale = Mathf.Clamp01(flashScale);
            ColourBlind = colourBlind;
            ColourBlindStrength = Mathf.Clamp01(colourBlindStrength);
            Changed?.Invoke();
        }
    }
}
