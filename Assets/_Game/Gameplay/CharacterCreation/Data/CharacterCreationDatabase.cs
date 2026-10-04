using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Items.Visuals;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// The one asset everything character-related points at: factions, lineages, the part catalog, palettes, the
    /// colour channels and the attachment-joint table. Players, the menus and the server validator all read it.
    /// </summary>
    public sealed class CharacterCreationDatabase : ScriptableObject
    {
        /// <summary>Sidekick's four body blend shapes, in the order its rig-movement table uses.</summary>
        public enum BodyBlend : byte
        {
            Feminine = 0,
            Heavy = 1,
            Skinny = 2,
            Bulk = 3
        }

        /// <summary>
        /// A colour the player picks from swatches. Writes one or more cells of the colour grid, each with its own
        /// shade so a single hair colour can drive hair, brows and stubble.
        /// </summary>
        [Serializable]
        public struct ColorChannel
        {
            [Tooltip("Stable ID stored in recipes. Never renumber.")]
            public byte id;
            public string displayName;
            public ColorTarget[] targets;
            public Color32[] swatches;
        }

        [Serializable]
        public struct ColorTarget
        {
            public byte u;
            public byte v;
            [Tooltip("Brightness multiplier applied to the picked colour for this cell.")]
            public float shade;
        }

        /// <summary>An attachment joint's untouched local pose (from Sidekick's base model).</summary>
        [Serializable]
        public struct JointRest
        {
            public string joint;
            public Vector3 localPosition;
            public Quaternion localRotation;
        }

        /// <summary>How far one body blend moves an attachment joint at full strength (Sidekick's rig-movement table).</summary>
        [Serializable]
        public struct JointOffset
        {
            public string joint;
            public BodyBlend blend;
            public Vector3 maxOffset;
            public Vector3 maxRotationEuler;
        }

        [SerializeField] private FactionCatalog factions;
        [SerializeField] private SidekickPartCatalog parts;
        [SerializeField] private CharacterPaletteLibrary palettes;
        [SerializeField] private List<LineageDefinition> lineages = new List<LineageDefinition>();
        [SerializeField] private List<ColorChannel> channels = new List<ColorChannel>();
        [SerializeField] private List<JointRest> jointRest = new List<JointRest>();
        [SerializeField] private List<JointOffset> jointOffsets = new List<JointOffset>();

        public FactionCatalog Factions => factions;
        public SidekickPartCatalog Parts => parts;
        public CharacterPaletteLibrary Palettes => palettes;
        public IReadOnlyList<LineageDefinition> Lineages => lineages;
        public IReadOnlyList<ColorChannel> Channels => channels;
        public IReadOnlyList<JointRest> JointRests => jointRest;
        public IReadOnlyList<JointOffset> JointOffsets => jointOffsets;

        public LineageDefinition GetLineage(byte id)
        {
            if (id == 0) return null;
            foreach (var lineage in lineages)
                if (lineage != null && lineage.id == id) return lineage;
            return null;
        }

        public void GetLineages(FactionId faction, List<LineageDefinition> results)
        {
            results.Clear();
            foreach (var lineage in lineages)
                if (lineage != null && lineage.faction == faction) results.Add(lineage);
        }

        public bool TryGetChannel(byte id, out ColorChannel channel)
        {
            foreach (var c in channels)
            {
                if (c.id != id) continue;
                channel = c;
                return true;
            }
            channel = default;
            return false;
        }

        /// <summary>Editor bake entry point.</summary>
        public void SetContents(FactionCatalog factionCatalog, SidekickPartCatalog partCatalog, CharacterPaletteLibrary paletteLibrary,
            List<LineageDefinition> lineageList, List<ColorChannel> channelList, List<JointRest> rests, List<JointOffset> offsets)
        {
            factions = factionCatalog;
            parts = partCatalog;
            palettes = paletteLibrary;
            lineages = lineageList;
            channels = channelList;
            jointRest = rests;
            jointOffsets = offsets;
        }
    }
}
