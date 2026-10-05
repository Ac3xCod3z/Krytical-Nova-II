using System;
using KryticalNova.Factions;
using Unity.Collections;
using Unity.Netcode;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// A character's name and recipe in network form: an unmanaged struct (fixed-size lists instead of arrays) so it
    /// can live in a NetworkVariable and travel in an RPC. Only the used list elements are written.
    /// </summary>
    public struct CharacterNetState : INetworkSerializable, IEquatable<CharacterNetState>
    {
        public FixedString32Bytes name;
        public byte version;
        public FactionId faction;
        public byte lineage;
        public BodyType bodyType;
        public sbyte build;
        public sbyte muscle;
        public ushort palette;
        public byte skinTone;
        public byte scars;
        public byte eyeliner;
        public byte grime;
        public KryticalNova.Progression.WeaponClassId weaponClass;
        public FixedList128Bytes<ushort> parts;       // room for 63, recipes cap at CharacterRecipe.MaxParts
        public FixedList128Bytes<ColorOverride> colors; // room for 15

        /// <summary>No identity yet: the prefab's own look is shown.</summary>
        public bool IsEmpty => lineage == 0;

        public static CharacterNetState From(string characterName, in CharacterRecipe recipe)
        {
            var state = new CharacterNetState
            {
                version = recipe.version,
                faction = recipe.faction,
                lineage = recipe.lineage,
                bodyType = recipe.bodyType,
                build = recipe.build,
                muscle = recipe.muscle,
                palette = recipe.palette,
                skinTone = recipe.skinTone,
                scars = recipe.scars,
                eyeliner = recipe.eyeliner,
                grime = recipe.grime,
                weaponClass = recipe.weaponClass
            };
            // Truncates instead of throwing if a name ever exceeds the buffer; rules cap names well below it.
            state.name.CopyFromTruncated(characterName ?? string.Empty);
            int partCount = Math.Min(recipe.PartCount, CharacterRecipe.MaxParts);
            for (int i = 0; i < partCount; i++) state.parts.Add(recipe.parts[i]);
            int colorCount = Math.Min(recipe.ColorCount, CharacterRecipe.MaxColors);
            for (int i = 0; i < colorCount; i++) state.colors.Add(recipe.colors[i]);
            return state;
        }

        public CharacterRecipe ToRecipe()
        {
            var recipe = new CharacterRecipe
            {
                version = version,
                faction = faction,
                lineage = lineage,
                bodyType = bodyType,
                build = build,
                muscle = muscle,
                palette = palette,
                skinTone = skinTone,
                scars = scars,
                eyeliner = eyeliner,
                grime = grime,
                weaponClass = weaponClass,
                parts = new ushort[parts.Length],
                colors = new ColorOverride[colors.Length]
            };
            for (int i = 0; i < parts.Length; i++) recipe.parts[i] = parts[i];
            for (int i = 0; i < colors.Length; i++) recipe.colors[i] = colors[i];
            return recipe;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref name);
            serializer.SerializeValue(ref version);
            serializer.SerializeValue(ref faction);
            serializer.SerializeValue(ref lineage);
            serializer.SerializeValue(ref bodyType);
            serializer.SerializeValue(ref build);
            serializer.SerializeValue(ref muscle);
            serializer.SerializeValue(ref palette);
            serializer.SerializeValue(ref skinTone);
            serializer.SerializeValue(ref scars);
            serializer.SerializeValue(ref eyeliner);
            serializer.SerializeValue(ref grime);
            serializer.SerializeValue(ref weaponClass);

            byte partCount = (byte)parts.Length;
            serializer.SerializeValue(ref partCount);
            if (serializer.IsReader)
            {
                parts.Clear();
                int kept = Math.Min(partCount, (byte)CharacterRecipe.MaxParts);
                for (int i = 0; i < partCount; i++)
                {
                    ushort id = 0;
                    serializer.SerializeValue(ref id);
                    if (i < kept) parts.Add(id);
                }
            }
            else
            {
                for (int i = 0; i < partCount; i++)
                {
                    ushort id = parts[i];
                    serializer.SerializeValue(ref id);
                }
            }

            byte colorCount = (byte)colors.Length;
            serializer.SerializeValue(ref colorCount);
            if (serializer.IsReader)
            {
                colors.Clear();
                int kept = Math.Min(colorCount, (byte)CharacterRecipe.MaxColors);
                for (int i = 0; i < colorCount; i++)
                {
                    var entry = default(ColorOverride);
                    serializer.SerializeValue(ref entry.channel);
                    serializer.SerializeValue(ref entry.color);
                    if (i < kept) colors.Add(entry);
                }
            }
            else
            {
                for (int i = 0; i < colorCount; i++)
                {
                    var entry = colors[i];
                    serializer.SerializeValue(ref entry.channel);
                    serializer.SerializeValue(ref entry.color);
                }
            }
        }

        public bool Equals(CharacterNetState other)
        {
            if (!name.Equals(other.name) || version != other.version || faction != other.faction || lineage != other.lineage
                || bodyType != other.bodyType || build != other.build || muscle != other.muscle || palette != other.palette
                || skinTone != other.skinTone || scars != other.scars || eyeliner != other.eyeliner || grime != other.grime
                || weaponClass != other.weaponClass
                || parts.Length != other.parts.Length || colors.Length != other.colors.Length)
                return false;
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != other.parts[i]) return false;
            for (int i = 0; i < colors.Length; i++)
                if (!colors[i].Equals(other.colors[i])) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is CharacterNetState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(name.GetHashCode(), (byte)faction, lineage, (byte)bodyType, palette, parts.Length);
    }
}
