using System;
using System.Globalization;
using System.Text;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// A character's look as short ASCII text for the entity netcode (the go-in-game request and the look of observers, M3a-2b).
    /// JSON of a full recipe (48 parts, 15 colours) can pass the 1,440 characters a request carries; this form needs about 550.
    /// <c>R1|version,faction,lineage,bodyType,build,muscle,palette,skinTone,scars,eyeliner,grime,weaponClass,bust,butt,defined,height,eyeGlow|part,part,..|channel.RRGGBBAA,..</c>
    /// A look for observers adds the name in front: <c>escaped-name\n</c> + the recipe text. Every number is range-checked on reading.
    /// </summary>
    public static class RecipeText
    {
        public const string Prefix = "R1";
        const int FieldCount = 17;

        /// <summary>The recipe fields this text carries, in order; a lint test fails when <see cref="CharacterRecipe"/> gains one.</summary>
        public static readonly string[] Fields =
        {
            "version", "faction", "lineage", "bodyType", "build", "muscle", "palette", "skinTone", "scars", "eyeliner", "grime",
            "weaponClass", "bust", "butt", "defined", "height", "eyeGlow", "parts", "colors",
        };

        public static string Encode(in CharacterRecipe r)
        {
            var sb = new StringBuilder(600);
            sb.Append(Prefix).Append('|');
            Num(sb, r.version); Num(sb, (int)r.faction); Num(sb, r.lineage); Num(sb, (int)r.bodyType); Num(sb, r.build); Num(sb, r.muscle);
            Num(sb, r.palette); Num(sb, r.skinTone); Num(sb, r.scars); Num(sb, r.eyeliner); Num(sb, r.grime); Num(sb, (int)r.weaponClass);
            Num(sb, r.bust); Num(sb, r.butt); Num(sb, r.defined); Num(sb, r.height);
            sb.Append(r.eyeGlow ? '1' : '0');
            sb.Append('|');
            for (int i = 0; i < r.PartCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(r.parts[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append('|');
            for (int i = 0; i < r.ColorCount; i++)
            {
                if (i > 0) sb.Append(',');
                ColorOverride c = r.colors[i];
                sb.Append(c.channel.ToString(CultureInfo.InvariantCulture)).Append('.')
                  .Append(c.color.r.ToString("X2")).Append(c.color.g.ToString("X2")).Append(c.color.b.ToString("X2")).Append(c.color.a.ToString("X2"));
            }
            return sb.ToString();
        }

        static void Num(StringBuilder sb, int value) => sb.Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');

        /// <summary>False for anything malformed or out of range (the request is then refused; nothing is guessed).</summary>
        public static bool TryDecode(string text, out CharacterRecipe recipe)
        {
            recipe = default;
            if (string.IsNullOrEmpty(text)) return false;
            string[] sections = text.Split('|');
            if (sections.Length != 4 || sections[0] != Prefix) return false;

            string[] f = sections[1].Split(',');
            if (f.Length != FieldCount) return false;
            var v = new int[FieldCount];
            for (int i = 0; i < FieldCount; i++)
                if (!int.TryParse(f[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out v[i])) return false;
            if (!Byte(v[0]) || !Byte(v[1]) || !Byte(v[2]) || !Byte(v[3]) || !SByte(v[4]) || !SByte(v[5]) || v[6] < 0 || v[6] > ushort.MaxValue
                || !Byte(v[7]) || !Byte(v[8]) || !Byte(v[9]) || !Byte(v[10]) || !Byte(v[11]) || !Byte(v[12]) || !Byte(v[13]) || !Byte(v[14])
                || !Byte(v[15]) || (v[16] != 0 && v[16] != 1))
                return false;

            ushort[] parts = Array.Empty<ushort>();
            if (sections[2].Length > 0)
            {
                string[] p = sections[2].Split(',');
                if (p.Length > CharacterRecipe.MaxParts) return false;
                parts = new ushort[p.Length];
                for (int i = 0; i < p.Length; i++)
                    if (!ushort.TryParse(p[i], NumberStyles.None, CultureInfo.InvariantCulture, out parts[i])) return false;
            }

            ColorOverride[] colors = Array.Empty<ColorOverride>();
            if (sections[3].Length > 0)
            {
                string[] c = sections[3].Split(',');
                if (c.Length > CharacterRecipe.MaxColors) return false;
                colors = new ColorOverride[c.Length];
                for (int i = 0; i < c.Length; i++)
                {
                    int dot = c[i].IndexOf('.');
                    if (dot <= 0 || c[i].Length != dot + 9) return false;
                    if (!byte.TryParse(c[i].Substring(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out byte channel)) return false;
                    if (!uint.TryParse(c[i].Substring(dot + 1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgba)) return false;
                    colors[i] = new ColorOverride(channel, new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba));
                }
            }

            recipe = new CharacterRecipe
            {
                version = (byte)v[0],
                faction = (FactionId)(byte)v[1],
                lineage = (byte)v[2],
                bodyType = (BodyType)(byte)v[3],
                build = (sbyte)v[4],
                muscle = (sbyte)v[5],
                palette = (ushort)v[6],
                skinTone = (byte)v[7],
                scars = (byte)v[8],
                eyeliner = (byte)v[9],
                grime = (byte)v[10],
                weaponClass = (WeaponClassId)(byte)v[11],
                bust = (byte)v[12],
                butt = (byte)v[13],
                defined = (byte)v[14],
                height = (byte)v[15],
                eyeGlow = v[16] == 1,
                parts = parts,
                colors = colors,
            };
            return true;
        }

        static bool Byte(int v) => v >= 0 && v <= byte.MaxValue;
        static bool SByte(int v) => v >= sbyte.MinValue && v <= sbyte.MaxValue;

        // ------------------------------------------------------------------ the look of observers (name + recipe)

        /// <summary>The look other players are shown: the name (escaped to ASCII) on the first line, then the recipe text.</summary>
        public static string EncodeLook(string name, in CharacterRecipe recipe) =>
            Uri.EscapeDataString(name ?? string.Empty) + "\n" + Encode(recipe);

        public static bool TryDecodeLook(string text, out string name, out CharacterRecipe recipe)
        {
            name = null;
            recipe = default;
            if (string.IsNullOrEmpty(text)) return false;
            int line = text.IndexOf('\n');
            if (line < 0) return false;
            try { name = Uri.UnescapeDataString(text.Substring(0, line)); }
            catch (Exception) { return false; }
            return TryDecode(text.Substring(line + 1), out recipe);
        }
    }
}
