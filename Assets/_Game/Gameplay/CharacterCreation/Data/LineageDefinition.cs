using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Items;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// One lineage of a faction (a Sidekick pack: Civilian, Soldier, Robot, Skeleton, Viking, Elf). The body is fixed:
    /// under its gear it is the bare body (small clothes, bare metal or bones). What a player chooses is
    /// the head, so each body type (masculine, feminine) has its own setup: the head parts on offer, the body
    /// builds, ready-made faces and the colour options. Baked by the editor tool, then safe to hand-tune.
    /// </summary>
    public sealed class LineageDefinition : ScriptableObject
    {
        [Tooltip("Stable ID. Saved in characters and sent over the network: never renumber.")]
        public byte id;
        public FactionId faction;
        public string displayName = "Lineage";
        [TextArea(2, 6)] public string description;
        [Tooltip("Three short flavour traits shown as chips in the creator.")]
        public string[] traits = Array.Empty<string>();
        [Tooltip("Species letters of this lineage's own rig suffix: HU, EV, SN or RO.")]
        public string speciesCode = "HU";
        [Tooltip("Which gear fits this lineage's body (the gear dictionary's body column).")]
        public BodyFit body = BodyFit.Humanoid;
        [Tooltip("The lineage's own outfit pack code, e.g. SCFI_CIVL.")]
        public string ownPack;
        [Tooltip("What this lineage's level-one gear looks like: variant 01 of the lineage's own pack. Menu dolls that " +
                 "know no gear stand in it. Not chosen by the player.")]
        public ushort[] basicOutfit = Array.Empty<ushort>();
        [Tooltip("The body under the gear, shown where a gear slot is empty: small clothes for humans and elves, bones " +
                 "for skeletons, the bare chassis for robots. Empty = the basic outfit is shown instead.")]
        public ushort[] bareBody = Array.Empty<ushort>();
        [Tooltip("Base colour palette for this lineage (skin, hair and eye picks are applied over it).")]
        public ushort defaultPalette;
        [Tooltip("The species' chest wrap, worn by feminine bodies under bare torsos (0 = species has none).")]
        public ushort wrapPartId;

        public Setup masculine = new Setup();
        public Setup feminine = new Setup();

        public Setup SetupFor(BodyType bodyType) => bodyType == BodyType.Feminine ? feminine : masculine;

        /// <summary>The parts shown where no gear is worn: the bare body, or the basic outfit for a doll that is shown dressed.</summary>
        public ushort[] BodyParts(bool dressed) => dressed || bareBody == null || bareBody.Length == 0 ? basicOutfit : bareBody;

        [Serializable]
        public sealed class Setup
        {
            public List<RegionOptions> regions = new List<RegionOptions>();
            public List<BuildPreset> builds = new List<BuildPreset>();
            [Tooltip("Ready-made looks. The first one is the default for a new character.")]
            public List<Preset> presets = new List<Preset>();
            [Tooltip("Whole-skin colour sets (empty for species without skin).")]
            public List<SkinTone> skinTones = new List<SkinTone>();
            [Tooltip("Colour channels this setup offers (ids from the database).")]
            public byte[] channels = Array.Empty<byte>();

            [NonSerialized] private Dictionary<BodyRegion, HashSet<ushort>> allowed;

            public bool TryGetRegion(BodyRegion region, out RegionOptions options)
            {
                foreach (var r in regions)
                {
                    if (r.region != region) continue;
                    options = r;
                    return true;
                }
                options = default;
                return false;
            }

            public bool Allows(BodyRegion region, ushort partId)
            {
                if (allowed == null)
                {
                    allowed = new Dictionary<BodyRegion, HashSet<ushort>>(regions.Count);
                    foreach (var r in regions)
                        allowed[r.region] = new HashSet<ushort>(r.partIds ?? Array.Empty<ushort>());
                }
                return allowed.TryGetValue(region, out var set) && set.Contains(partId);
            }

            public bool OffersChannel(byte channel) => Array.IndexOf(channels, channel) >= 0;

            public void InvalidateCache() => allowed = null;
        }

        /// <summary>The head parts a character of this setup may choose for one region (head, hair, brows, ears...).</summary>
        [Serializable]
        public struct RegionOptions
        {
            public BodyRegion region;
            [Tooltip("The region may be left empty (hair, brows, facial hair).")]
            public bool optional;
            public ushort[] partIds;
        }

        [Serializable]
        public struct BuildPreset
        {
            public string name;
            [Tooltip("-100 very slim to +100 very heavy.")]
            public sbyte build;
            [Tooltip("-100 lean to +100 muscular.")]
            public sbyte muscle;
        }

        [Serializable]
        public struct Preset
        {
            public string name;
            public CharacterRecipe recipe;
        }

        [Serializable]
        public struct SkinTone
        {
            public string name;
            public Color32 swatch;
            public CellColor[] cells;
        }

        /// <summary>One cell of the 16x16 Sidekick colour grid.</summary>
        [Serializable]
        public struct CellColor
        {
            public byte u;
            public byte v;
            public Color32 color;
        }

        private void OnValidate()
        {
            masculine?.InvalidateCache();
            feminine?.InvalidateCache();
        }
    }
}
