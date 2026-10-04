using System;
using KryticalNova.Factions;
using KryticalNova.Items.Visuals;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Everything that defines how a character looks: lineage, body, chosen parts and colours. This is what the
    /// creator edits, what is saved in a character slot (JSON) and, as <see cref="CharacterNetState"/>, what is
    /// replicated. Part and palette values are catalog IDs, never asset references.
    /// </summary>
    [Serializable]
    public struct CharacterRecipe : IEquatable<CharacterRecipe>
    {
        public const byte CurrentVersion = 1;
        public const int MaxParts = 48;
        public const int MaxColors = 15;

        public byte version;
        public FactionId faction;
        public byte lineage;
        public BodyType bodyType;
        /// <summary>-100 very slim to +100 very heavy.</summary>
        public sbyte build;
        /// <summary>-100 lean to +100 muscular.</summary>
        public sbyte muscle;
        /// <summary>Chosen part IDs, one per body region.</summary>
        public ushort[] parts;
        /// <summary>Base colour palette ID.</summary>
        public ushort palette;
        /// <summary>0 keeps the palette's own skin; otherwise a 1-based index into the lineage setup's skin tones.</summary>
        public byte skinTone;
        public ColorOverride[] colors;
        /// <summary>Shader detail amounts, 0 to 100.</summary>
        public byte scars;
        public byte eyeliner;
        public byte grime;

        public bool IsEmpty => lineage == 0;
        public int PartCount => parts != null ? parts.Length : 0;
        public int ColorCount => colors != null ? colors.Length : 0;

        /// <summary>Same mapping as Sidekick's tool: body type is all or nothing, size splits into heavy or skinny.</summary>
        public BodyShapeWeights ToShape() => new BodyShapeWeights
        {
            masculineFeminine = bodyType == BodyType.Feminine ? 100f : 0f,
            heavy = build > 0 ? build : 0f,
            skinny = build < 0 ? -build : 0f,
            buff = (muscle + 100f) * 0.5f
        };

        /// <summary>A copy whose arrays are not shared with this one (undo history, presets).</summary>
        public CharacterRecipe Clone()
        {
            var copy = this;
            copy.parts = parts != null ? (ushort[])parts.Clone() : Array.Empty<ushort>();
            copy.colors = colors != null ? (ColorOverride[])colors.Clone() : Array.Empty<ColorOverride>();
            return copy;
        }

        public bool Equals(CharacterRecipe other)
        {
            if (version != other.version || faction != other.faction || lineage != other.lineage || bodyType != other.bodyType
                || build != other.build || muscle != other.muscle || palette != other.palette || skinTone != other.skinTone
                || scars != other.scars || eyeliner != other.eyeliner || grime != other.grime
                || PartCount != other.PartCount || ColorCount != other.ColorCount)
                return false;
            for (int i = 0; i < PartCount; i++)
                if (parts[i] != other.parts[i]) return false;
            for (int i = 0; i < ColorCount; i++)
                if (!colors[i].Equals(other.colors[i])) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is CharacterRecipe other && Equals(other);

        public override int GetHashCode()
        {
            int hash = HashCode.Combine(version, (byte)faction, lineage, (byte)bodyType, build, muscle, palette, skinTone);
            for (int i = 0; i < PartCount; i++) hash = HashCode.Combine(hash, parts[i]);
            return hash;
        }
    }
}
