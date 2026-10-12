using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>What a painted part of a weapon takes from a colourway. Baked into prefabs: append only.</summary>
    public enum WeaponPaintRole : byte
    {
        /// <summary>Left as the model has it (a paint), or left to the colourway's metal (a metal).</summary>
        Keep = 0,
        Primary = 1,
        Secondary = 2,
        Accent = 3,
        Trim = 4,
        Leather = 5,
        Glow = 6,
        Under = 7
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
    /// One metal of a weapon model: a grey of the pack's palette, told from the model's other greys by how bright
    /// it is, and what a dye puts there. The blade or barrel keeps the colourway's metal (<see cref="WeaponPaintRole.Keep"/>);
    /// a guard, a pommel or a housing takes one of the colourway's colours.
    /// </summary>
    [Serializable]
    public struct WeaponPaintShade
    {
        [Tooltip("How bright the metal is on the model, 0 to 1.")]
        [Range(0f, 1f)] public float value;
        [Tooltip("How far a grey's brightness may lie from it and still be this metal.")]
        [Range(0.01f, 0.5f)] public float width;
        [Tooltip("Keep = the colourway's metal (the blade, the barrel). Anything else = that colour of the colourway.")]
        public WeaponPaintRole role;
    }

    /// <summary>
    /// How a weapon is dyed, free of textures and scenes so it can be tested. A weapon's model takes its colours
    /// from its pack's shared palette texture, a handful of them, and each is one material of the weapon. The
    /// materials of a model are found once. Its paints (<see cref="Bands"/>): colours of one hue are one paint, the
    /// paint that covers the most becomes the colourway's primary colour, the next its secondary, and so on;
    /// browns are leather, gold and brass are the trim, and what the model lights up is the glow. Its metals
    /// (<see cref="Shades"/>): the grey that covers the most is the blade or the barrel and stays metal, the other
    /// greys (a guard, a housing) take the colourway's colours no paint has taken. A dye then repaints every colour
    /// of the palette by what it belongs to (<see cref="Recolour"/>): a part keeps its own shading, a dark grip
    /// takes a dark shade of the colourway, and true black is left alone.
    /// </summary>
    public static class WeaponPaintRules
    {
        /// <summary>Below this saturation a colour is metal, not paint.</summary>
        public const float GreySaturation = 0.18f;
        /// <summary>Below this brightness a colour is a dark part: grips, barrels, the insides of things.</summary>
        public const float DarkValue = 0.1f;
        /// <summary>Below this brightness a colour is black, and is left alone: shadows, seams, muzzles.</summary>
        public const float BlackValue = 0.035f;
        /// <summary>How far apart two hues may be and still be one paint (about 22 degrees).</summary>
        public const float BandWidth = 0.06f;
        public const int MaxBands = 8;

        /// <summary>How far apart two greys may be in brightness and still be one metal.</summary>
        public const float ShadeWidth = 0.08f;
        public const int MaxShades = 6;
        /// <summary>A second metal takes a colour only when it covers at least this share of the model: smaller ones are rivets and edges.</summary>
        public const float MinShadeShare = 0.08f;

        /// <summary>Gold and brass: yellow to orange, brighter and more vivid than leather.</summary>
        public const float GoldHueMin = 0.07f, GoldHueMax = 0.17f, GoldSaturation = 0.35f, GoldValue = 0.5f;
        /// <summary>Gold that covers more than this share of the whole model is the weapon's own colour, not its trim.</summary>
        public const float GoldMaxShare = 0.5f;

        /// <summary>A dark part is this many times brighter under a dye, and never brighter than <see cref="DarkCeiling"/>: it must show its colour and stay dark.</summary>
        public const float DarkLift = 1.6f, DarkCeiling = 0.16f;
        /// <summary>A colour as dull as this has no hue to give a dark part: the colourway's main colour is used instead.</summary>
        public const float DullSaturation = 0.15f;
        /// <summary>Below this brightness a pixel of an emission map is not a light.</summary>
        public const float LightFloor = 0.04f;

        /// <summary>One colour a model uses, and how much of its surface wears it.</summary>
        public struct Sample
        {
            public Color32 colour;
            public float weight;
            /// <summary>The model lights this colour up (its emission map is lit there).</summary>
            public bool lit;
            public Sample(Color32 colour, float weight, bool lit = false) { this.colour = colour; this.weight = weight; this.lit = lit; }
        }

        private struct Cluster
        {
            public float x, y;          // the hue as a point on the wheel, weighted
            public float value, saturation, weight, lit;
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
            float painted = 0f, whole = 0f;
            if (samples != null)
            {
                // Heaviest first, so a paint's hue is set by its largest part.
                var order = new List<Sample>(samples);
                order.Sort((a, b) => b.weight.CompareTo(a.weight));
                foreach (Sample sample in order)
                {
                    if (sample.weight <= 0f) continue;
                    whole += sample.weight;
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
                    if (sample.lit) c.lit += sample.weight;
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
                if (c.lit * 2f > c.weight) role = WeaponPaintRole.Glow; // the model lights it up: no guessing
                else if (IsLeather(hue, saturation, value)) role = WeaponPaintRole.Leather;
                else if (value >= 0.9f && saturation >= 0.6f && share < 0.12f) role = WeaponPaintRole.Glow;
                // Fittings, not the weapon's colour: they take the trim and no place among the main colours.
                else if (IsGold(hue, saturation, value) && c.weight / whole < GoldMaxShare) role = WeaponPaintRole.Trim;
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

        /// <summary>
        /// The metals of a model, the one that covers the most first: that one is the blade or the barrel and keeps
        /// the colourway's metal. Each other grey that covers enough of the model takes the first of the
        /// colourway's main colours that none of the model's paints wears, so a weapon that is all metal shows the
        /// colourway too. Empty for a model with one metal: there is nothing to tell apart.
        /// </summary>
        public static WeaponPaintShade[] Shades(IReadOnlyList<Sample> samples, IReadOnlyList<WeaponPaintBand> bands)
        {
            var clusters = new List<Cluster>();
            float whole = 0f;
            if (samples != null)
            {
                var order = new List<Sample>(samples);
                order.Sort((a, b) => b.weight.CompareTo(a.weight));
                foreach (Sample sample in order)
                {
                    if (sample.weight <= 0f) continue;
                    whole += sample.weight;
                    Color.RGBToHSV(sample.colour, out _, out float s, out float v);
                    if (s >= GreySaturation || v < DarkValue) continue;
                    int found = -1;
                    for (int i = 0; i < clusters.Count && found < 0; i++)
                        if (Mathf.Abs(clusters[i].value / clusters[i].weight - v) <= ShadeWidth) found = i;
                    Cluster c = found >= 0 ? clusters[found] : default;
                    c.value += v * sample.weight;
                    c.weight += sample.weight;
                    if (sample.lit) c.lit += sample.weight;
                    if (found >= 0) clusters[found] = c; else clusters.Add(c);
                }
            }
            if (clusters.Count < 2 || whole <= 0f) return Array.Empty<WeaponPaintShade>();

            clusters.Sort((a, b) => b.weight.CompareTo(a.weight));
            if (clusters.Count > MaxShades) clusters.RemoveRange(MaxShades, clusters.Count - MaxShades);

            var free = new List<WeaponPaintRole> { WeaponPaintRole.Primary, WeaponPaintRole.Secondary };
            if (bands != null)
                foreach (WeaponPaintBand band in bands) free.Remove(band.role);

            var shades = new WeaponPaintShade[clusters.Count];
            bool any = false;
            for (int i = 0; i < clusters.Count; i++)
            {
                Cluster c = clusters[i];
                WeaponPaintRole role = WeaponPaintRole.Keep;
                if (c.lit * 2f > c.weight) role = WeaponPaintRole.Glow; // a white light
                else if (i > 0 && free.Count > 0 && c.weight / whole >= MinShadeShare)
                {
                    role = free[0];
                    free.RemoveAt(0);
                }
                any |= role != WeaponPaintRole.Keep;
                shades[i] = new WeaponPaintShade { value = Mathf.Clamp01(c.value / c.weight), width = ShadeWidth, role = role };
            }
            return any ? shades : Array.Empty<WeaponPaintShade>();
        }

        /// <summary>Brown, and neither bright nor vivid: a strap, a wrapped grip, a wooden haft.</summary>
        public static bool IsLeather(float hue, float saturation, float value) =>
            hue >= 0.03f && hue <= 0.13f && saturation < 0.65f && value < 0.6f;

        /// <summary>Gold or brass: a guard, a ring, an inlay. Leather is asked first; this is what is brighter or more vivid.</summary>
        public static bool IsGold(float hue, float saturation, float value) =>
            hue >= GoldHueMin && hue <= GoldHueMax && saturation >= GoldSaturation && value >= GoldValue && !IsLeather(hue, saturation, value);

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
                case WeaponPaintRole.Under: return theme.under;
                default: return default;
            }
        }

        /// <summary>
        /// A colour of the palette as a dyed weapon shows it, by its paints alone: every grey is one metal and dark
        /// parts are left alone.
        /// </summary>
        /// <param name="metal">Greys take the colourway's metal.</param>
        public static Color32 Recolour(Color32 source, IReadOnlyList<WeaponPaintBand> bands, in GearThemeLibrary.Theme theme, bool metal) =>
            Recolour(source, bands, null, theme, metal, false);

        /// <summary>
        /// A colour of the palette as a dyed weapon shows it. A colour that belongs to none of the weapon's paints
        /// comes back as it went in.
        /// </summary>
        /// <param name="shades">The model's metals: a grey that is one of them takes what that metal takes.</param>
        /// <param name="metal">Greys that are no metal of their own take the colourway's metal.</param>
        /// <param name="dark">Dark parts take a dark shade of the colourway. Black is left alone either way.</param>
        public static Color32 Recolour(Color32 source, IReadOnlyList<WeaponPaintBand> bands, IReadOnlyList<WeaponPaintShade> shades,
            in GearThemeLibrary.Theme theme, bool metal, bool dark)
        {
            Color.RGBToHSV(source, out float h, out float s, out float v);
            if (v < DarkValue) return dark && v >= BlackValue ? Dark(theme, v, source.a) : source;

            if (s < GreySaturation)
            {
                int shade = -1;
                float nearest = float.MaxValue;
                for (int i = 0; shades != null && i < shades.Count; i++)
                {
                    float distance = Mathf.Abs(shades[i].value - v);
                    if (distance > shades[i].width || distance >= nearest) continue;
                    shade = i;
                    nearest = distance;
                }
                if (shade >= 0 && shades[shade].role != WeaponPaintRole.Keep)
                    return Scale(ColourOf(shades[shade].role, theme), Mathf.Clamp(v / Mathf.Max(shades[shade].value, 0.05f), 0.55f, 1.5f), source.a);

                if (!metal || theme.metal == null || theme.metal.Length == 0) return source;
                // The colourway's metal runs from lightest to darkest: a bright part takes a light shade.
                int step = Mathf.Clamp((int)((1f - v) * theme.metal.Length), 0, theme.metal.Length - 1);
                Color32 plate = theme.metal[step];
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

        /// <summary>
        /// A dark part under a dye: the hue of the colourway's under colour (its main colour when that one is too
        /// dull to have a hue), a little brighter than the part was so that the colour can be seen, and still dark.
        /// </summary>
        private static Color32 Dark(in GearThemeLibrary.Theme theme, float value, byte alpha)
        {
            Color.RGBToHSV(theme.under, out float h, out float s, out _);
            if (s < DullSaturation) Color.RGBToHSV(theme.primary, out h, out s, out _);
            Color32 dyed = Color.HSVToRGB(h, Mathf.Min(s, 0.75f), Mathf.Clamp(value * DarkLift, BlackValue, DarkCeiling));
            dyed.a = alpha;
            return dyed;
        }

        /// <summary>
        /// A pixel of a model's emission map as a dyed weapon shows it: a light takes the colourway's glow colour at
        /// the brightness it had, whatever colour the pack gave it. What is not lit stays unlit.
        /// </summary>
        public static Color32 RecolourLight(Color32 source, in GearThemeLibrary.Theme theme)
        {
            float v = Mathf.Max(source.r, Mathf.Max(source.g, source.b)) / 255f;
            if (v < LightFloor) return source;
            Color.RGBToHSV(theme.glow, out float h, out float s, out float glowValue);
            if (glowValue < LightFloor) return source; // a colourway with no glow of its own
            Color32 dyed = Color.HSVToRGB(h, s, v);
            dyed.a = source.a;
            return dyed;
        }

        private static Color32 Scale(Color32 colour, float factor, byte alpha) => new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.r * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.g * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.b * factor), 0, 255), alpha);
    }
}
