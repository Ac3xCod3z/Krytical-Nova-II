using System;
using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// The edits the character creator makes to a <see cref="CharacterRecipe"/>: start from a lineage's default
    /// look, step a head region through its options, pick colours, randomise. Kept apart from the UI so the rules
    /// (left and right parts change together, optional regions may be empty) live in one place and are unit-tested.
    /// </summary>
    public static class RecipeEdit
    {
        /// <summary>The look a new character of this lineage and body type starts with: its first ready-made face.</summary>
        public static CharacterRecipe Default(LineageDefinition lineage, BodyType bodyType)
        {
            var setup = lineage.SetupFor(bodyType);
            var recipe = setup.presets.Count > 0 ? setup.presets[0].recipe.Clone() : Bare(lineage, setup);
            Stamp(ref recipe, lineage, bodyType);
            return recipe;
        }

        /// <summary>A ready-made face of the lineage, keeping the body shape and sliders of <paramref name="current"/>.</summary>
        public static CharacterRecipe FromPreset(LineageDefinition lineage, BodyType bodyType, int preset, in CharacterRecipe current)
        {
            var setup = lineage.SetupFor(bodyType);
            if (preset < 0 || preset >= setup.presets.Count) return current.Clone();
            var recipe = setup.presets[preset].recipe.Clone();
            Stamp(ref recipe, lineage, bodyType);
            recipe.CopyBodyFrom(current);
            return recipe;
        }

        /// <summary>Which ready-made face <paramref name="recipe"/> still matches (same parts and colours), or -1.</summary>
        public static int MatchingPreset(LineageDefinition.Setup setup, in CharacterRecipe recipe)
        {
            for (int i = 0; i < setup.presets.Count; i++)
            {
                var candidate = setup.presets[i].recipe;
                if (candidate.palette == recipe.palette && candidate.skinTone == recipe.skinTone
                    && SameSet(candidate.parts, recipe.parts) && SameColors(candidate.colors, recipe.colors))
                    return i;
            }
            return -1;
        }

        // ---------------- Parts ----------------

        /// <summary>The part the recipe wears in <paramref name="region"/>, or 0.</summary>
        public static ushort GetPart(in CharacterRecipe recipe, SidekickPartCatalog catalog, BodyRegion region)
        {
            for (int i = 0; i < recipe.PartCount; i++)
            {
                ushort id = recipe.parts[i];
                if (id != 0 && catalog.TryGet(id, out var entry) && entry.region == region) return id;
            }
            return 0;
        }

        /// <summary>Puts <paramref name="id"/> in <paramref name="region"/>, replacing what was there. 0 empties the region.</summary>
        public static void SetPart(ref CharacterRecipe recipe, SidekickPartCatalog catalog, BodyRegion region, ushort id)
        {
            var kept = new List<ushort>(recipe.PartCount + 1);
            for (int i = 0; i < recipe.PartCount; i++)
            {
                ushort existing = recipe.parts[i];
                if (existing == 0) continue;
                if (catalog.TryGet(existing, out var entry) && entry.region == region) continue;
                kept.Add(existing);
            }
            if (id != 0) kept.Add(id);
            recipe.parts = kept.ToArray();
        }

        /// <summary>The right-hand region that always changes with a left-hand one (brows, ears), or None.</summary>
        public static BodyRegion Twin(BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.EyebrowLeft: return BodyRegion.EyebrowRight;
                case BodyRegion.EarLeft: return BodyRegion.EarRight;
                default: return BodyRegion.None;
            }
        }

        /// <summary>True for a region that only follows its twin and is never offered on its own.</summary>
        public static bool IsFollower(BodyRegion region) => region == BodyRegion.EyebrowRight || region == BodyRegion.EarRight;

        /// <summary>A region is worth a control when the player really has a choice in it.</summary>
        public static bool HasChoice(in LineageDefinition.RegionOptions options) =>
            options.partIds != null && !IsFollower(options.region)
            && (options.partIds.Length > 1 || (options.optional && options.partIds.Length > 0));

        /// <summary>Index of the worn option in <paramref name="options"/>, or -1 when the region is empty.</summary>
        public static int IndexOf(in CharacterRecipe recipe, SidekickPartCatalog catalog, in LineageDefinition.RegionOptions options)
        {
            ushort current = GetPart(recipe, catalog, options.region);
            return current == 0 ? -1 : Array.IndexOf(options.partIds, current);
        }

        /// <summary>
        /// Wears option <paramref name="index"/> of a region; -1 empties it when the region is optional. A region's
        /// twin (the right brow, the right ear) takes the matching part.
        /// </summary>
        public static void Select(ref CharacterRecipe recipe, SidekickPartCatalog catalog, LineageDefinition.Setup setup,
            in LineageDefinition.RegionOptions options, int index)
        {
            bool empty = index < 0 || index >= options.partIds.Length;
            if (empty && !options.optional) return;
            ushort id = empty ? (ushort)0 : options.partIds[index];
            SetPart(ref recipe, catalog, options.region, id);

            var twin = Twin(options.region);
            if (twin == BodyRegion.None || !setup.TryGetRegion(twin, out var twinOptions)) return;
            SetPart(ref recipe, catalog, twin, empty ? (ushort)0 : MatchTwin(catalog, id, twinOptions, index));
        }

        /// <summary>Moves a region to its next (+1) or previous (-1) option, wrapping round. Returns the new index.</summary>
        public static int Step(ref CharacterRecipe recipe, SidekickPartCatalog catalog, LineageDefinition.Setup setup,
            in LineageDefinition.RegionOptions options, int direction)
        {
            int count = options.partIds != null ? options.partIds.Length : 0;
            if (count == 0) return -1;
            int first = options.optional ? -1 : 0; // an optional region has one more stop: empty
            int stops = count - first;
            int stop = Mathf.Max(IndexOf(recipe, catalog, options), first) - first;
            stop = ((stop + direction) % stops + stops) % stops;
            int index = stop + first;
            Select(ref recipe, catalog, setup, options, index);
            return index;
        }

        // The twin is the part of the same pack and variant on the other side; packs list both sides in the same order,
        // so the same index is the fallback.
        private static ushort MatchTwin(SidekickPartCatalog catalog, ushort id, in LineageDefinition.RegionOptions twinOptions, int index)
        {
            if (twinOptions.partIds == null || twinOptions.partIds.Length == 0) return 0;
            if (catalog.TryGet(id, out var source))
            {
                foreach (ushort candidate in twinOptions.partIds)
                    if (catalog.TryGet(candidate, out var entry) && entry.pack == source.pack && entry.variant == source.variant)
                        return candidate;
            }
            return twinOptions.partIds[Mathf.Clamp(index, 0, twinOptions.partIds.Length - 1)];
        }

        // ---------------- Colours ----------------

        public static bool TryGetColor(in CharacterRecipe recipe, byte channel, out Color32 color)
        {
            for (int i = 0; i < recipe.ColorCount; i++)
            {
                if (recipe.colors[i].channel != channel) continue;
                color = recipe.colors[i].color;
                return true;
            }
            color = default;
            return false;
        }

        /// <summary>Sets the colour of a channel (hair, eyes, glow...), replacing an earlier pick.</summary>
        public static void SetColor(ref CharacterRecipe recipe, byte channel, Color32 color)
        {
            var colors = new List<ColorOverride>(recipe.ColorCount + 1);
            for (int i = 0; i < recipe.ColorCount; i++)
                if (recipe.colors[i].channel != channel) colors.Add(recipe.colors[i]);
            colors.Add(new ColorOverride(channel, color));
            recipe.colors = colors.ToArray();
        }

        /// <summary>True when the right eye has a colour of its own (heterochromia).</summary>
        public static bool HasHeterochromia(in CharacterRecipe recipe) => TryGetColor(recipe, CharacterColorChannels.EyeRight, out _);

        /// <summary>
        /// Heterochromia on: the right eye starts as the eye colour (<paramref name="eyeColour"/> when the look has none
        /// picked yet), ready to be changed. Off: the right eye's own colour goes and Eyes colours both again.
        /// </summary>
        public static void SetHeterochromia(ref CharacterRecipe recipe, bool on, Color32 eyeColour)
        {
            if (on == HasHeterochromia(recipe)) return;
            if (on)
            {
                SetColor(ref recipe, CharacterColorChannels.EyeRight, TryGetColor(recipe, CharacterColorChannels.Eyes, out var eyes) ? eyes : eyeColour);
                return;
            }
            var colors = new List<ColorOverride>(recipe.ColorCount);
            for (int i = 0; i < recipe.ColorCount; i++)
                if (recipe.colors[i].channel != CharacterColorChannels.EyeRight) colors.Add(recipe.colors[i]);
            recipe.colors = colors.ToArray();
        }

        /// <summary>Index of the swatch the channel currently uses, or -1 (the palette's own colour).</summary>
        public static int SwatchIndex(in CharacterRecipe recipe, in CharacterCreationDatabase.ColorChannel channel)
        {
            if (!TryGetColor(recipe, channel.id, out var color) || channel.swatches == null) return -1;
            for (int i = 0; i < channel.swatches.Length; i++)
            {
                var s = channel.swatches[i];
                if (s.r == color.r && s.g == color.g && s.b == color.b) return i;
            }
            return -1;
        }

        // ---------------- Randomise ----------------

        /// <summary>
        /// A random, valid look for a lineage and body type: a ready-made face as the base (so the palette is
        /// coherent), then random head parts, skin, colours and build.
        /// </summary>
        public static CharacterRecipe Randomize(LineageDefinition lineage, BodyType bodyType, CharacterCreationDatabase database,
            System.Random random)
        {
            var setup = lineage.SetupFor(bodyType);
            var recipe = setup.presets.Count > 0
                ? setup.presets[random.Next(setup.presets.Count)].recipe.Clone()
                : Bare(lineage, setup);
            Stamp(ref recipe, lineage, bodyType);

            var catalog = database.Parts;
            foreach (var options in setup.regions)
            {
                if (!HasChoice(options)) continue;
                // Bald and beardless are choices too, but not as likely as any one style.
                bool leaveEmpty = options.optional && random.NextDouble() < (options.region == BodyRegion.FacialHair ? 0.4 : 0.08);
                Select(ref recipe, catalog, setup, options, leaveEmpty ? -1 : random.Next(options.partIds.Length));
            }

            if (setup.skinTones.Count > 0) recipe.skinTone = (byte)(1 + random.Next(setup.skinTones.Count));
            foreach (byte id in setup.channels)
            {
                if (!database.TryGetChannel(id, out var channel) || channel.swatches == null || channel.swatches.Length == 0) continue;
                SetColor(ref recipe, id, channel.swatches[random.Next(channel.swatches.Length)]);
            }

            if (setup.builds.Count > 0)
            {
                var build = setup.builds[random.Next(setup.builds.Count)];
                recipe.build = build.build;
                recipe.muscle = build.muscle;
            }
            return recipe;
        }

        // ---------------- Helpers ----------------

        // The first option of every region that must be filled: enough to be valid when a lineage has no presets.
        private static CharacterRecipe Bare(LineageDefinition lineage, LineageDefinition.Setup setup)
        {
            var parts = new List<ushort>();
            foreach (var options in setup.regions)
                if (!options.optional && options.partIds != null && options.partIds.Length > 0) parts.Add(options.partIds[0]);
            return new CharacterRecipe { palette = lineage.defaultPalette, parts = parts.ToArray(), colors = Array.Empty<ColorOverride>() };
        }

        private static void Stamp(ref CharacterRecipe recipe, LineageDefinition lineage, BodyType bodyType)
        {
            recipe.Upgrade();
            recipe.faction = lineage.faction;
            recipe.lineage = lineage.id;
            recipe.bodyType = bodyType;
            if (recipe.parts == null) recipe.parts = Array.Empty<ushort>();
            if (recipe.colors == null) recipe.colors = Array.Empty<ColorOverride>();
        }

        private static bool SameSet(ushort[] a, ushort[] b)
        {
            int countA = a != null ? a.Length : 0, countB = b != null ? b.Length : 0;
            if (countA != countB) return false;
            for (int i = 0; i < countA; i++)
                if (Array.IndexOf(b, a[i]) < 0) return false;
            return true;
        }

        private static bool SameColors(ColorOverride[] a, ColorOverride[] b)
        {
            int countA = a != null ? a.Length : 0, countB = b != null ? b.Length : 0;
            if (countA != countB) return false;
            for (int i = 0; i < countA; i++)
                if (Array.IndexOf(b, a[i]) < 0) return false;
            return true;
        }
    }
}
