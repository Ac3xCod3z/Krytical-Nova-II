using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>What a painted part of a weapon takes from a colourway. Baked into prefabs: append only.</summary>
    public enum WeaponPaintRole : byte
    {
        /// <summary>Left as the model has it.</summary>
        Keep = 0,
        Primary = 1,
        Secondary = 2,
        Accent = 3,
        Trim = 4,
        Leather = 5,
        Glow = 6
    }

    /// <summary>One paint of a weapon model: the hue its parts have in the pack's palette, and what a dye puts there.</summary>
    [Serializable]
    public struct WeaponPaintBand
    {
        [Tooltip("The paint's hue, 0 to 1 round the colour wheel.")]
        [Range(0f, 1f)] public float hue;
        [Tooltip("How far a colour's hue may lie from it and still be this paint.")]
        [Range(0.005f, 0.25f)] public float width;
        public WeaponPaintRole role;
        [Tooltip("How bright the paint is on the model: a darker or lighter part of it keeps its shading under a dye.")]
        [Range(0.01f, 1f)] public float value;
    }

    /// <summary>
    /// How a weapon is dyed, free of textures and scenes so it can be tested. A weapon's model takes its colours
    /// from its pack's shared palette texture, a handful of them. The paints of a model are found once
    /// (<see cref="Bands"/>): colours of one hue are one paint, the paint that covers the most becomes the
    /// colourway's primary colour, the next its secondary, and so on; browns are leather, small bright lights are
    /// the glow. A dye then repaints every colour of the palette by the paint it belongs to
    /// (<see cref="Recolour"/>): greys take the colourway's metal, near-black is left alone, and a part keeps its
    /// own shading.
    /// </summary>
    public static class WeaponPaintRules
    {
        /// <summary>Below this saturation a colour is metal, not paint.</summary>
        public const float GreySaturation = 0.18f;
        /// <summary>Below this brightness a colour is left alone: grips, shadows, barrels.</summary>
        public const float DarkValue = 0.1f;
        /// <summary>How far apart two hues may be and still be one paint (about 22 degrees).</summary>
        public const float BandWidth = 0.06f;
        public const int MaxBands = 8;

        /// <summary>One colour a model uses, and how much of its surface wears it.</summary>
        public struct Sample
        {
            public Color32 colour;
            public float weight;
            public Sample(Color32 colour, float weight) { this.colour = colour; this.weight = weight; }
        }

        private struct Cluster
        {
            public float x, y;          // the hue as a point on the wheel, weighted
            public float value, saturation, weight;
            public float Hue => Mathf.Repeat(Mathf.Atan2(y, x) / (2f * Mathf.PI), 1f);
        }

        /// <summary>Distance between two hues round the wheel, 0 to 0.5.</summary>
        public static float HueDistance(float a, float b)
        {
            float d = Mathf.Abs(Mathf.Repeat(a, 1f) - Mathf.Repeat(b, 1f));
            return d > 0.5f ? 1f - d : d;
        }

        /// <summary>The paints of a model, the one that covers the most first within its kind.</summary>
        public static WeaponPaintBand[] Bands(IReadOnlyList<Sample> samples)
        {
            var clusters = new List<Cluster>();
            float painted = 0f;
            if (samples != null)
            {
                // Heaviest first, so a paint's hue is set by its largest part.
                var order = new List<Sample>(samples);
                order.Sort((a, b) => b.weight.CompareTo(a.weight));
                foreach (Sample sample in order)
                {
                    if (sample.weight <= 0f) continue;
                    Color.RGBToHSV(sample.colour, out float h, out float s, out float v);
                    if (s < GreySaturation || v < DarkValue) continue;
                    painted += sample.weight;
                    int found = -1;
                    for (int i = 0; i < clusters.Count && found < 0; i++)
                        if (HueDistance(clusters[i].Hue, h) <= BandWidth) found = i;
                    Cluster c = found >= 0 ? clusters[found] : default;
                    c.x += Mathf.Cos(h * 2f * Mathf.PI) * sample.weight;
                    c.y += Mathf.Sin(h * 2f * Mathf.PI) * sample.weight;
                    c.value += v * sample.weight;
                    c.saturation += s * sample.weight;
                    c.weight += sample.weight;
                    if (found >= 0) clusters[found] = c; else clusters.Add(c);
                }
            }
            if (clusters.Count == 0 || painted <= 0f) return Array.Empty<WeaponPaintBand>();

            clusters.Sort((a, b) => b.weight.CompareTo(a.weight));
            if (clusters.Count > MaxBands) clusters.RemoveRange(MaxBands, clusters.Count - MaxBands);

            var bands = new WeaponPaintBand[clusters.Count];
            int rank = 0;
            for (int i = 0; i < clusters.Count; i++)
            {
                Cluster c = clusters[i];
                float hue = c.Hue, value = c.value / c.weight, saturation = c.saturation / c.weight, share = c.weight / painted;
                WeaponPaintRole role;
                if (IsLeather(hue, saturation, value)) role = WeaponPaintRole.Leather;
                else if (value >= 0.9f && saturation >= 0.6f && share < 0.12f) role = WeaponPaintRole.Glow;
                else
                {
                    role = rank == 0 ? WeaponPaintRole.Primary : rank == 1 ? WeaponPaintRole.Secondary
                        : rank == 2 ? WeaponPaintRole.Accent : WeaponPaintRole.Trim;
                    rank++;
                }
                bands[i] = new WeaponPaintBand { hue = hue, width = BandWidth, role = role, value = Mathf.Clamp(value, 0.01f, 1f) };
            }
            return bands;
        }

        /// <summary>Brown, and neither bright nor vivid: a strap, a wrapped grip, a wooden haft.</summary>
        public static bool IsLeather(float hue, float saturation, float value) =>
            hue >= 0.03f && hue <= 0.13f && saturation < 0.65f && value < 0.6f;

        /// <summary>The colour a colourway gives a paint.</summary>
        public static Color32 ColourOf(WeaponPaintRole role, in GearThemeLibrary.Theme theme)
        {
            switch (role)
            {
                case WeaponPaintRole.Primary: return theme.primary;
                case WeaponPaintRole.Secondary: return theme.secondary;
                case WeaponPaintRole.Accent: return theme.accent;
                case WeaponPaintRole.Trim: return theme.trim;
                case WeaponPaintRole.Leather: return theme.leather;
                case WeaponPaintRole.Glow: return theme.glow;
                default: return default;
            }
        }

        /// <summary>
        /// A colour of the palette as a dyed weapon shows it. A colour that belongs to none of the weapon's paints
        /// comes back as it went in.
        /// </summary>
        /// <param name="metal">Greys take the colourway's metal.</param>
        public static Color32 Recolour(Color32 source, IReadOnlyList<WeaponPaintBand> bands, in GearThemeLibrary.Theme theme, bool metal)
        {
            Color.RGBToHSV(source, out float h, out float s, out float v);
            if (v < DarkValue) return source;

            if (s < GreySaturation)
            {
                if (!metal || theme.metal == null || theme.metal.Length == 0) return source;
                // The colourway's metal runs from lightest to darkest: a bright part takes a light shade.
                int shade = Mathf.Clamp((int)((1f - v) * theme.metal.Length), 0, theme.metal.Length - 1);
                Color32 plate = theme.metal[shade];
                Color.RGBToHSV(plate, out _, out _, out float plateValue);
                return Scale(plate, Mathf.Clamp(v / Mathf.Max(plateValue, 0.05f), 0.75f, 1.25f), source.a);
            }

            if (bands == null) return source;
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < bands.Count; i++)
            {
                float distance = HueDistance(bands[i].hue, h);
                if (distance > bands[i].width || distance >= bestDistance) continue;
                best = i;
                bestDistance = distance;
            }
            if (best < 0 || bands[best].role == WeaponPaintRole.Keep) return source;
            // The part keeps its own shading: darker or lighter than the paint's middle, as it was.
            float factor = Mathf.Clamp(v / Mathf.Max(bands[best].value, 0.05f), 0.55f, 1.5f);
            return Scale(ColourOf(bands[best].role, theme), factor, source.a);
        }

        private static Color32 Scale(Color32 colour, float factor, byte alpha) => new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.r * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.g * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.b * factor), 0, 255), alpha);
    }
}
