using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the colour themes of the gear sets (GearThemes.asset) and works out which colour cells each outfit
    /// piece paints. A Sidekick mesh takes every colour from one cell of a 16x16 grid, found through its UVs, so a
    /// piece's cells are read off its own meshes: a theme is then laid on exactly what the piece covers and never on
    /// skin or bone.
    ///
    /// A theme is a few role colours. The grid is baked from them: within each part of the body the cells are ranked
    /// by how much surface they paint, and the largest takes the leading colour, the next the second, and so on down
    /// to a small accent. Two or three colours plus a dark and an accent is what keeps a set from clashing with itself.
    /// Colours already in the asset are kept, so a theme can be tuned in the Inspector and rebuilt.
    /// </summary>
    public static class GearThemeBuilder
    {
        public const string LibraryPath = SidekickGearGenerator.ItemsRoot + "/GearThemes.asset";

        private static readonly EquipSlot[] OutfitSlots = { EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Cape };

        // ---------------------------------------------------------------- the starting colours of each set

        private struct Recipe
        {
            public string tag;
            public int band;
            public GearThemeStyle style;
            public string primary, secondary, under, accent, trim, leather, glow;
            public string[] metal;
        }

        private static Recipe Set(string tag, int band, GearThemeStyle style, string primary, string secondary, string under,
            string accent, string metal, string trim, string leather, string glow) => new Recipe
        {
            tag = tag, band = band, style = style, primary = primary, secondary = secondary, under = under, accent = accent,
            metal = metal.Split(' '), trim = trim, leather = leather, glow = glow
        };

        private const GearThemeStyle Two = GearThemeStyle.TwoPiece, Full = GearThemeStyle.FullSuit;

        // Seeds only: a theme that is already in the asset keeps the colours it has there.
        private static readonly Recipe[] Recipes =
        {
            // Hegemony soldiers
            Set("SOLD", 1, Two, "4A6FA5", "B9C3CF", "262C38", "E8ECF0", "A9B4C0 7C8794 566070 343B46", "C0C7D0", "3A3F4A", "5AC8FF"), // Sentinel: slate blue and steel
            Set("SOLD", 2, Two, "C2A16B", "C8662B", "3A322B", "F2E2B8", "9A917F 746C5D 524B40 322D27", "B58E4A", "4A3626", "FFA53C"), // Striker: desert tan and burnt orange
            Set("SOLD", 3, Two, "4B5058", "8E1F2B", "1C1E22", "D9383F", "8A9099 62676F 44484F 2A2D32", "9AA0A8", "2B2325", "FF3B3B"), // Bastion: gunmetal and crimson
            Set("SOLD", 4, Full, "E6E8EC", "C9A13B", "1E2740", "2F6BFF", "F0F2F5 CDD2D9 8E96A3 39415A", "C9A13B", "2A3350", "3D8BFF"), // Commander: white ceramic and gold
            // Hegemony civilians
            Set("CIVL", 1, Two, "3C4046", "E5C420", "23262B", "F4F1E6", "9EA3A8 787D82 55595E 303337", "B8BCC0", "2C2A28", "E8F000"), // Courier: charcoal and hi-vis yellow
            Set("CIVL", 2, Two, "27406B", "2D9C9A", "1A2232", "D8E4EC", "B4BDC6 8892A0 5D6674 363D48", "C5CCD4", "222B3A", "40E0D0"), // Analyst: navy and teal
            Set("CIVL", 3, Two, "6E1E2E", "E3D6B8", "2A1A1E", "B8914A", "C2A46B 9A8050 6C5A3A 3E3424", "C9A65A", "3A2420", "FFC66E"), // Director: burgundy and cream
            Set("CIVL", 4, Full, "1D1D21", "CBA135", "2E2E34", "1FA36B", "E3C56A CBA135 8F7226 3A3020", "E3C56A", "26221C", "2CE08A"), // Magnate: black and gold
            // Null machines: the plating is the metal, and its second shade covers the most
            Set("ROBO", 1, Two, "C8642A", "A7AEB5", "2E3136", "E6B91E", "D3D8DD C8642A 8A9097 3A3D42", "E6B91E", "3A322B", "FFB02E"), // Welder: oxide orange and bare steel
            Set("ROBO", 2, Two, "5E6638", "9A4124", "26281F", "8DFF3A", "9A4124 5E6638 3F4528 23261A", "8A8F6A", "2E2A20", "8DFF3A"), // Ravager: drab olive and rust
            Set("ROBO", 3, Two, "DDD8CC", "1E1E22", "1E1E22", "8A4DFF", "F0ECE2 DDD8CC 8E8A80 232327", "B9B4A8", "2A282C", "A35CFF"), // Overseer: bone white and black
            Set("ROBO", 4, Full, "1B1B22", "C9CED6", "101015", "FF2E9A", "E3E7ED 2B2B38 B9BEC8 16161C", "E0E4EA", "1A1A20", "FF2E9A"), // Prime: obsidian and chrome
            // Null skeletons
            Set("SKTN", 1, Two, "55663C", "8C6A3A", "2B2A22", "D8C89A", "B08A4E 8C6A3A 5E4828 34281A", "A8843F", "3D3324", "9BE85A"), // Cryptwarden: moss and tarnished bronze
            Set("SKTN", 2, Two, "8A1C1C", "34363A", "1B1A1C", "D7D2C4", "6A6E74 44474C 2E3034 1B1C1F", "8C8F94", "2A1E1C", "FF4A3A"), // Deathsworn: blood red and blackened iron
            Set("SKTN", 3, Two, "7C6BB5", "9DB4C8", "232438", "E6F4FF", "C3D6E6 9DB4C8 677E94 384656", "BFD0DE", "2E2C44", "5CF2FF"), // Lichbound: pale violet and frost steel
            Set("SKTN", 4, Full, "26231F", "E2DAC6", "1A1816", "4DFF9A", "E2DAC6 2B2826 A88838 161412", "C8A64A", "2A2420", "4DFF9A"), // Revenant: ebony, ivory and old gold
            // Covenant vikings
            Set("VIKG", 1, Two, "7A5632", "BFAF90", "3A2E22", "9C3A2A", "A7A9AB 808286 5A5C60 35363A", "9FA2A6", "5C4128", "FFC27A"), // Huscarl: brown leather and undyed wool
            Set("VIKG", 2, Two, "2A2725", "A32222", "1A1817", "D8D0C0", "8E8E8C 666664 474746 2A2A29", "7E7E7C", "3A2A22", "FF5A3A"), // Berserker: black fur and war-paint red
            Set("VIKG", 3, Two, "23447E", "B8C0C8", "1C2333", "F0F0EA", "D0D6DC A8B0B8 747C86 3C424A", "C8CED4", "3A3028", "7CC4FF"), // Jarl: deep blue and silver
            Set("VIKG", 4, Full, "1E1C1C", "A31A24", "2E2A28", "D8A93A", "E0B84C B88F2E 7A5E1E 3A2E12", "E0B84C", "2C2220", "FFB13A"), // Warlord: black, crimson and gold
            // Covenant elves
            Set("ELVN", 1, Two, "3E6B3A", "6B4A2E", "2A2F22", "D9C690", "C08552 9A6A40 6E4B2D 3F2B1A", "C08552", "5A3F27", "B6FF6A"), // Wildrunner: forest green and bark
            Set("ELVN", 2, Two, "2A8C8C", "C3CCD2", "1E3238", "F2F6F8", "E1E7EB BCC5CC 86919A 444C54", "D6DEE4", "2C4046", "7CF5E6"), // Bladesinger: teal and silver
            Set("ELVN", 3, Two, "9A4B22", "D69A2D", "33241A", "F0DDB0", "B88A45 8E6A34 624822 382A14", "C9993F", "4A2F1C", "FFB84A"), // Highguard: russet and amber
            Set("ELVN", 4, Full, "EAE2CF", "E0B13A", "5A4326", "FF9A2E", "F2D06B E0B13A A67E22 5A4416", "F2D06B", "6B4E2A", "FFC24A"), // Sunlord: ivory and sun gold
        };

        // ---------------------------------------------------------------- Sidekick's colour cells

        private sealed class CellRow
        {
            public string Name { get; set; }
            public int Grp { get; set; }
            public int U { get; set; }
            public int V { get; set; }
        }

        private struct Cell
        {
            public string name;  // Sidekick's colour property ("Torso Outfit 02", "Metal 01"); null where the grid has none
            public int group;    // 1 species, 2 outfit, 3 attachments, 4 materials, 5 elements
        }

        private static Cell[] cellTable;

        private static Cell[] Cells()
        {
            if (cellTable != null) return cellTable;
            var table = new Cell[GearThemeLibrary.CellCount];
            using (var db = SidekickDatabase.OpenReadOnly())
            {
                if (db == null) return table; // not cached: try again once the database is there
                foreach (var row in db.Query<CellRow>("select name as Name, color_group as Grp, u as U, v as V from sk_color_property"))
                {
                    if (row.U < 0 || row.V < 0 || row.U >= GearThemeLibrary.Grid || row.V >= GearThemeLibrary.Grid) continue;
                    table[row.V * GearThemeLibrary.Grid + row.U] = new Cell { name = row.Name, group = row.Grp };
                }
            }
            return cellTable = table;
        }

        /// <summary>Gear may recolour outfit, attachment and material cells. Skin, hair, eyes and bone are the character's.</summary>
        private static bool Paintable(Cell cell) =>
            cell.name != null && (cell.group == 2 || cell.group == 3 || (cell.group == 4 && !cell.name.StartsWith("Bone", StringComparison.Ordinal)));

        /// <summary>Surface area a look's meshes paint from each cell of the colour grid (cells gear may not recolour are left at zero).</summary>
        public static void AddPaintedAreas(ItemDefinition look, float[] areas)
        {
            if (look == null || look.partCatalog == null || look.appearancePartIds == null || look.appearancePartIds.Length == 0) return;
            var table = Cells();
            var parts = new List<SidekickPartRef>();
            PartLibrary.LoadAll(look.partCatalog, look.appearancePartIds, parts);
            foreach (var part in parts)
            {
                var mesh = part.mesh;
                if (mesh == null || !mesh.isReadable) continue;
                var vertices = mesh.vertices;
                var uv = mesh.uv;
                var triangles = mesh.triangles;
                if (uv == null || uv.Length != vertices.Length) continue;
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    Vector2 centre = (uv[a] + uv[b] + uv[c]) / 3f;
                    int u = Mathf.Clamp((int)(centre.x * GearThemeLibrary.Grid), 0, GearThemeLibrary.Grid - 1);
                    int v = Mathf.Clamp((int)(centre.y * GearThemeLibrary.Grid), 0, GearThemeLibrary.Grid - 1);
                    int cell = v * GearThemeLibrary.Grid + u;
                    if (!Paintable(table[cell])) continue;
                    areas[cell] += Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).magnitude * 0.5f;
                }
            }
        }

        /// <summary>The cells a look paints that a theme may recolour, lowest index first.</summary>
        public static byte[] PaintedCells(ItemDefinition look)
        {
            var areas = new float[GearThemeLibrary.CellCount];
            AddPaintedAreas(look, areas);
            var cells = new List<byte>();
            for (int i = 0; i < areas.Length; i++)
                if (areas[i] > 0f) cells.Add((byte)i);
            return cells.ToArray();
        }

        // ---------------------------------------------------------------- build

        /// <summary>
        /// Names the theme of every outfit line above Common (once: a theme picked by hand is kept), adds the themes
        /// the library lacks with their starting colours, and bakes every theme's grid from its colours and the pieces
        /// that wear it. Run by "Krytical Nova/Items/1. Generate Gear Looks From Synty", before the ladder is baked.
        /// </summary>
        public static GearThemeLibrary Build()
        {
            var dictionary = AssetDatabase.LoadAssetAtPath<GearDictionary>(SidekickGearGenerator.DictionaryPath);
            if (dictionary == null) return null;
            var library = AssetDatabase.LoadAssetAtPath<GearThemeLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<GearThemeLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var lines = dictionary.Lines.ToList();
            int named = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (!IsThemed(line) || !string.IsNullOrEmpty(line.theme) || string.IsNullOrEmpty(line.set)) continue;
                line.theme = $"{line.tag}_{line.set}";
                lines[i] = line;
                named++;
            }
            if (named > 0)
            {
                dictionary.SetLines(dictionary.Parts, lines);
                EditorUtility.SetDirty(dictionary);
            }

            int added = 0;
            foreach (var line in lines)
            {
                if (!IsThemed(line) || string.IsNullOrEmpty(line.theme) || library.IdOf(line.theme) != 0) continue;
                library.Add(StartingTheme(line));
                added++;
            }

            for (int index = 0; index < library.Themes.Count; index++)
            {
                var theme = library.Themes[index];
                var areas = new float[GearThemeLibrary.CellCount];
                foreach (var line in lines)
                    if (IsThemed(line) && line.theme == theme.name) AddPaintedAreas(line.look, areas);
                theme.cells = BakeCells(theme, areas);
                library.Set(index, theme);
            }
            EditorUtility.SetDirty(library);
            Debug.Log($"[Items] Gear themes: {library.Themes.Count} baked ({added} new), {named} lines given their set's theme.");
            return library;
        }

        private static bool IsThemed(GearDictionary.Line line) =>
            line.IsValid && line.band != SidekickGearGenerator.BandCommon && Array.IndexOf(OutfitSlots, line.slot) >= 0;

        private static GearThemeLibrary.Theme StartingTheme(GearDictionary.Line line)
        {
            foreach (var recipe in Recipes)
            {
                if (recipe.tag != line.tag || recipe.band != line.band) continue;
                return new GearThemeLibrary.Theme
                {
                    name = line.theme, style = recipe.style,
                    primary = Hex(recipe.primary), secondary = Hex(recipe.secondary), under = Hex(recipe.under), accent = Hex(recipe.accent),
                    metal = recipe.metal.Select(Hex).ToArray(), trim = Hex(recipe.trim), leather = Hex(recipe.leather), glow = Hex(recipe.glow)
                };
            }
            // A set this table has never heard of: plain steel, so it shows up as something to give colours to.
            Debug.LogWarning($"[Items] No starting colours for the theme '{line.theme}': it starts as plain steel. Edit it in {LibraryPath}.");
            return new GearThemeLibrary.Theme
            {
                name = line.theme, style = GearThemeStyle.TwoPiece,
                primary = Hex("7C8794"), secondary = Hex("A9B4C0"), under = Hex("2A2D32"), accent = Hex("E8ECF0"),
                metal = new[] { Hex("A9B4C0"), Hex("7C8794"), Hex("566070"), Hex("343B46") },
                trim = Hex("C0C7D0"), leather = Hex("3A3F4A"), glow = Hex("5AC8FF")
            };
        }

        // ---------------------------------------------------------------- colours into cells

        private enum Role { Primary, Secondary, Under, Accent, PrimaryDeep, SecondaryDeep, UnderLift }

        private enum BodyPart { Upper, Lower, Extremity, Plate, Back, Head }

        private static readonly Regex Dedicated = new Regex(@"^(?<part>.+) (?:Outfit|Attachment) (?<n>\d\d)$");

        /// <summary>The colours a part of the body takes, largest painted area first.</summary>
        private static Role[] Sequence(GearThemeStyle style, BodyPart part)
        {
            if (style == GearThemeStyle.FullSuit)
            {
                switch (part)
                {
                    case BodyPart.Lower:
                    case BodyPart.Extremity: return new[] { Role.Primary, Role.Under, Role.Secondary, Role.Accent, Role.PrimaryDeep };
                    case BodyPart.Plate:
                    case BodyPart.Back: return new[] { Role.Secondary, Role.Primary, Role.Under, Role.Accent, Role.SecondaryDeep };
                    default: return new[] { Role.Primary, Role.Secondary, Role.Under, Role.Accent, Role.PrimaryDeep };
                }
            }
            switch (part)
            {
                case BodyPart.Upper: return new[] { Role.Primary, Role.Under, Role.Secondary, Role.Accent, Role.PrimaryDeep };
                case BodyPart.Lower:
                case BodyPart.Extremity: return new[] { Role.Under, Role.Primary, Role.Secondary, Role.Accent, Role.UnderLift };
                default: return new[] { Role.Primary, Role.Secondary, Role.Under, Role.Accent, Role.PrimaryDeep };
            }
        }

        private static BodyPart PartOf(string family)
        {
            switch (family)
            {
                case "Torso": case "Arm": return BodyPart.Upper;
                case "Hips": case "Leg": return BodyPart.Lower;
                case "Hand": case "Foot": return BodyPart.Extremity;
                case "Back": return BodyPart.Back;
                case "Head": case "Face": return BodyPart.Head;
                default: return BodyPart.Plate; // shoulders, elbows, knees and the hip plates
            }
        }

        private static Color32 Colour(GearThemeLibrary.Theme theme, Role role)
        {
            switch (role)
            {
                case Role.Primary: return theme.primary;
                case Role.Secondary: return theme.secondary;
                case Role.Under: return theme.under;
                case Role.Accent: return theme.accent;
                case Role.PrimaryDeep: return Shade(theme.primary, 0.78f);
                case Role.SecondaryDeep: return Shade(theme.secondary, 0.78f);
                default: return Mix(theme.under, theme.primary, 0.3f);
            }
        }

        private sealed class Swatch
        {
            public int number;                 // the "01".."05" of the cell's name
            public float area;
            public readonly List<int> cells = new List<int>(2); // left and right share a swatch
        }

        private static Color32[] BakeCells(GearThemeLibrary.Theme theme, float[] areas)
        {
            var table = Cells();
            var cells = new Color32[GearThemeLibrary.CellCount];
            var families = new Dictionary<string, List<Swatch>>(StringComparer.Ordinal);
            for (int cell = 0; cell < cells.Length; cell++)
            {
                cells[cell] = new Color32(255, 255, 255, 255);
                string name = table[cell].name;
                if (name == null || !Paintable(table[cell])) continue;

                var match = Dedicated.Match(name);
                if (!match.Success)
                {
                    cells[cell] = Material(theme, name);
                    continue;
                }
                // Left and right of a limb are one swatch, so a set is never lopsided.
                string family = match.Groups["part"].Value.Replace(" Left", string.Empty).Replace(" Right", string.Empty);
                int number = int.Parse(match.Groups["n"].Value);
                if (!families.TryGetValue(family, out var swatches)) families[family] = swatches = new List<Swatch>();
                var swatch = swatches.Find(s => s.number == number);
                if (swatch == null) swatches.Add(swatch = new Swatch { number = number });
                swatch.cells.Add(cell);
                swatch.area += areas[cell];
            }

            foreach (var family in families)
            {
                var sequence = Sequence(theme.style, PartOf(family.Key));
                // Most surface first; swatches nothing wears yet follow in their own order.
                var ranked = family.Value.OrderByDescending(s => s.area).ThenBy(s => s.number).ToList();
                for (int rank = 0; rank < ranked.Count; rank++)
                {
                    Color32 colour = Colour(theme, sequence[Mathf.Min(rank, sequence.Length - 1)]);
                    foreach (int cell in ranked[rank].cells) cells[cell] = colour;
                }
            }
            return cells;
        }

        /// <summary>The colour of a material cell: metal, straps, lights... shared by every piece a character wears.</summary>
        private static Color32 Material(GearThemeLibrary.Theme theme, string name)
        {
            Color32 Metal(int shade) => theme.metal != null && shade < theme.metal.Length ? theme.metal[shade] : theme.trim;
            switch (name)
            {
                case "Metal 01": return Metal(0);
                case "Metal 02": return Metal(1);
                case "Metal 03": return Metal(2);
                case "Metal 04": return Metal(3);
                case "Jewellery Metal 01": return theme.trim;
                case "Jewellery Metal 02": return Shade(theme.trim, 0.8f);
                case "Jewellery Gem 01": return theme.glow;
                case "Jewellery Gem 02": return theme.accent;
                case "Glow 01":
                case "Glow 02":
                case "Transparency FX 01": return theme.glow;
                case "Transparency FX 02": return Shade(theme.glow, 0.75f);
                case "Digital Screen 01": return Shade(theme.glow, 0.85f);
                case "Digital Screen 02": return Shade(theme.glow, 0.6f);
                case "Glass 01": return Mix(theme.glow, new Color32(255, 255, 255, 255), 0.35f);
                case "Glass 02": return Shade(theme.glow, 0.6f);
                case "Rubber 01": return Shade(theme.under, 0.6f);
                case "Rubber 02": return Shade(theme.under, 0.42f);
                case "Plastic 01": return Mix(theme.under, Metal(1), 0.35f);
                case "Plastic 02": return Shade(theme.under, 0.85f);
                case "Fur 01": return Shade(theme.leather, 1.3f);
                case "Fur 02": return Shade(theme.leather, 1.05f);
                case "Rope 01": return Shade(theme.leather, 1.2f);
                case "Rope 02": return Shade(theme.leather, 0.95f);
                case "Strap 01": return theme.leather;
                case "Strap 02": return Shade(theme.leather, 0.78f);
                case "Stitch 01": return Shade(theme.leather, 1.6f);
                case "Stitch 02": return Shade(theme.leather, 1.3f);
                case "Bandage 01": return Mix(theme.secondary, new Color32(236, 230, 214, 255), 0.6f);
                case "Bandage 02": return Mix(theme.secondary, new Color32(190, 182, 164, 255), 0.6f);
                case "Paper 01": return Mix(theme.secondary, new Color32(232, 222, 196, 255), 0.7f);
                case "Paper 02": return Mix(theme.secondary, new Color32(186, 174, 148, 255), 0.7f);
                case "Wood 01": return Shade(theme.leather, 0.95f);
                case "Wood 02": return Shade(theme.leather, 0.7f);
                case "Feathers 01": return theme.accent;
                case "Feathers 02": return theme.primary;
                case "Concrete 01": return Metal(2);
                case "Concrete 02": return Metal(3);
                default: return theme.under; // hair and teeth accessories: nothing gear wears
            }
        }

        private static Color32 Shade(Color32 colour, float factor) => new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.r * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.g * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(colour.b * factor), 0, 255), 255);

        private static Color32 Mix(Color32 from, Color32 to, float t) => Color32.Lerp(from, to, t);

        private static Color32 Hex(string hex) =>
            ColorUtility.TryParseHtmlString("#" + hex, out var colour) ? (Color32)colour : new Color32(255, 0, 255, 255);
    }
}
