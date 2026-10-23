using System;
using KryticalNova.Factions;
using KryticalNova.Items.Visuals;
using KryticalNova.Progression;

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
        /// <summary>2: the body sliders (bust, butt, definition, height; 22 Oct 2026). Older recipes read as the body they had.</summary>
        public const byte CurrentVersion = 2;
        public const byte BodySlidersVersion = 2;
        /// <summary>The height a recipe from before the body sliders has: 50 = 1.00, the body as modelled.</summary>
        public const byte DefaultHeight = 50;
        public const float MinHeightScale = 0.90f;
        public const float MaxHeightScale = 1.10f;
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
        /// <summary>
        /// Discipline: the weapon class the character starts with (and uses while unarmed). One of its faction's
        /// three. None on characters saved before classes existed; they read as their faction's offense class.
        /// </summary>
        public WeaponClassId weaponClass;
        /// <summary>Body sliders, 0 to 100 (version 2). Bust and butt are worn by a feminine body only, definition by a masculine one.</summary>
        public byte bust;
        public byte butt;
        public byte defined;
        /// <summary>0 to 100: the whole character scaled from the floor, 0 = 0.90, 50 = 1.00, 100 = 1.10.</summary>
        public byte height;

        public bool IsEmpty => lineage == 0;
        public int PartCount => parts != null ? parts.Length : 0;
        public int ColorCount => colors != null ? colors.Length : 0;

        private bool HasSliders => version >= BodySlidersVersion;

        /// <summary>The height slider as it reads: a recipe from before the sliders stands at 50.</summary>
        public byte Height => HasSliders ? (byte)Math.Min((int)height, 100) : DefaultHeight;

        /// <summary>The character's uniform scale from the floor, 0.90 to 1.10.</summary>
        public float HeightScale => MinHeightScale + (MaxHeightScale - MinHeightScale) * Height * 0.01f;

        /// <summary>
        /// Same mapping as Sidekick's tool: body type is all or nothing, size splits into heavy or skinny. The body
        /// sliders are copied straight (0 is the body as modelled), each only on the body type that wears it.
        /// </summary>
        public BodyShapeWeights ToShape() => new BodyShapeWeights
        {
            masculineFeminine = bodyType == BodyType.Feminine ? 100f : 0f,
            heavy = build > 0 ? build : 0f,
            skinny = build < 0 ? -build : 0f,
            buff = (muscle + 100f) * 0.5f,
            bust = HasSliders && bodyType == BodyType.Feminine ? Math.Min((int)bust, 100) : 0f,
            butt = HasSliders && bodyType == BodyType.Feminine ? Math.Min((int)butt, 100) : 0f,
            defined = HasSliders && bodyType == BodyType.Masculine ? Math.Min((int)defined, 100) : 0f
        };

        /// <summary>
        /// Brings a recipe from an older version up to this one without changing how it looks: before the body
        /// sliders it gets no bust, butt or definition and the middle height. Call before editing the sliders.
        /// </summary>
        public void Upgrade()
        {
            if (version >= CurrentVersion) return;
            if (version < BodySlidersVersion)
            {
                bust = butt = defined = 0;
                height = DefaultHeight;
            }
            version = CurrentVersion;
        }

        /// <summary>
        /// Takes the body of <paramref name="from"/> (build, muscle, sliders, height), keeping only the sliders this
        /// recipe's body type wears. Call on an up-to-date recipe (<see cref="Upgrade"/>).
        /// </summary>
        public void CopyBodyFrom(in CharacterRecipe from)
        {
            build = from.build;
            muscle = from.muscle;
            BodyShapeWeights shape = from.ToShape();
            bool feminine = bodyType == BodyType.Feminine;
            bust = feminine ? (byte)shape.bust : (byte)0;
            butt = feminine ? (byte)shape.butt : (byte)0;
            defined = feminine ? (byte)0 : (byte)shape.defined;
            height = from.Height;
        }

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
                || scars != other.scars || eyeliner != other.eyeliner || grime != other.grime || weaponClass != other.weaponClass
                || bust != other.bust || butt != other.butt || defined != other.defined || height != other.height
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
            hash = HashCode.Combine(hash, bust, butt, defined, height);
            for (int i = 0; i < PartCount; i++) hash = HashCode.Combine(hash, parts[i]);
            return hash;
        }
    }
}
