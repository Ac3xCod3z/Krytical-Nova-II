using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Generates the faction skins of the in-game UI. The layout belongs to the Hegemony sheets (the base): this tool
    /// reads them and writes, for each other faction, a sheet of overrides that only swaps what a skin is made of:
    /// sprites (same role, the faction's own Synty HUD pack), fonts and colours. Every override selector is prefixed
    /// with the faction class, so the base sheets are never edited and the Hegemony UI cannot change.
    /// Re-run after editing a base sheet. A generated sheet may be hand-tuned, but a re-run replaces it.
    /// </summary>
    public static class FactionSkinBuilder
    {
        private const string OutputFolder = "Assets/_Game/UI/Common";
        private const string SciFiHud = "/Assets/Synty/InterfaceSciFiSoldierHUD/";
        private const string DarkFantasy = "/Assets/Synty/InterfaceDarkFantasyHUD/";
        private const string FantasyWarrior = "/Assets/Synty/InterfaceFantasyWarriorHUD/";

        private static readonly string[] BaseSheets =
        {
            "Assets/_Game/UI/Common/KryticalTheme.uss",
            "Assets/_Game/UI/HUD/ProgressionHUD.uss",
            "Assets/_Game/UI/Settings/Settings.uss",
            "Assets/_Game/UI/Inventory/Items.uss",
            "Assets/_Game/UI/Inventory/Forge.uss",
            "Assets/_Game/UI/HUD/Herald/Herald.uss",
            "Assets/_Game/UI/HUD/Minimap/Minimap.uss",
            "Assets/_Game/UI/HUD/Map/WorldMap.uss",
            "Assets/_Game/UI/HUD/Chat/ChatWindow.uss",
            "Assets/_Game/UI/HUD/Duel/DuelPanels.uss",
            "Assets/_Game/UI/HUD/Party/PartyPanels.uss",
            "Assets/_Game/UI/HUD/Pet/PetPanels.uss",
            "Assets/_Game/UI/Social/SocialPanels.uss",
            "Assets/_Game/UI/Social/EmoteWheel.uss",
            "Assets/_Game/UI/Economy/EconomyWindows.uss",
            "Assets/_Game/UI/NPC/NpcInteraction.uss"
        };

        private sealed class Skin
        {
            public string name, cssClass, pack;
            public string bodyFont, titleFont;
            /// <summary>Base sprite file name (no extension) to this skin's sprite path, or null to remove the image.</summary>
            public Dictionary<string, string> sprites;
            /// <summary>Hue the base accent (teal) becomes, the hue of the dark panel tones, and the hue of small gold labels.</summary>
            public float accentHue, neutralHue, labelHue;
            public float accentSaturation = 1.45f, neutralSaturation = 0.85f;
            /// <summary>The pack's fonts are smaller on the line than Exo 2 at the same size; text is scaled up to match.</summary>
            public float fontScale = 1f;
        }

        // Roles: panel fill, inset card / button, token, item slot, outlines, bars, pips, close glyph, grab handle,
        // soft backing. Rings and map shapes keep the base sprites (they must match the round minimap) and only recolour.
        private static Skin Null() => new Skin
        {
            name = "Null", cssClass = "kn-faction-null", pack = "Synty INTERFACE - Dark Fantasy HUD",
            bodyFont = DarkFantasy + "Fonts/Markazi/MarkaziText-SemiBold.ttf",
            titleFont = DarkFantasy + "Fonts/Pirata One/PirataOne-Regular.ttf",
            accentHue = 357f, neutralHue = 285f, labelHue = 262f, fontScale = 1.28f,
            sprites = HudSprites(DarkFantasy + "Sprites/HUD/SPR_HUD_DarkFantasy_", new Dictionary<string, string>
            {
                { "Box_02", "Box_Medium_Parchment_01" }, { "Box_04", "Box_Small_Parchment_01" },
                { "Box_03", "Box_Small_Parchment_02" }, { "Box_Glass_04", "Box_Small_Parchment_03" },
                { "Frame_02", "Frame_Box_01" }, { "Frame_04", "Frame_Box_03" },
                { "Bar_Angled_01", "Bar_01_Clean" },
                { "Pip_Hexagon_01_Active", "Circle_01_Med_Clean" }, { "Pip_Hexagon_01_Inactive", "Ring_Medium_01_Clean" },
                { "Icon_InactiveItem_01", "Close_01_Clean" }, { "Triangle_02", "Arrow_Small_01_Clean" },
                { "Gradient_Outwards_Smooth_01", "Gradient_Outwards_01" }, { "Background_Hexagons_01", null }
            })
        };

        private static Skin Covenant() => new Skin
        {
            name = "Covenant", cssClass = "kn-faction-covenant", pack = "Synty INTERFACE - Fantasy Warrior HUD",
            bodyFont = FantasyWarrior + "Fonts/Grenze/Grenze-SemiBold.ttf",
            titleFont = FantasyWarrior + "Fonts/Texturina/Texturina_18pt-SemiBold.ttf",
            accentHue = 39f, neutralHue = 32f, labelHue = 140f, fontScale = 1.12f,
            sprites = HudSprites(FantasyWarrior + "Sprites/HUD/SPR_HUD_FantasyWarrior_", new Dictionary<string, string>
            {
                { "Box_02", "Box_Medium_Parchment_01" }, { "Box_04", "Box_Small_Parchment_01" },
                { "Box_03", "Box_Small_Parchment_02" }, { "Box_Glass_04", "Box_Small_Parchment_03" },
                { "Frame_02", "Frame_Box_Medium_01" }, { "Frame_04", "Frame_Box_Small_01" },
                { "Bar_Angled_01", "Bar_Diamond_01" },
                { "Pip_Hexagon_01_Active", "Circle_Small_01_Clean" }, { "Pip_Hexagon_01_Inactive", "Ring_Small_01_Clean" },
                { "Icon_InactiveItem_01", "Icon_InactiveItem_01" }, { "Triangle_02", "Triangle_Small_01_Clean" },
                { "Gradient_Outwards_Smooth_01", "Gradient_Outwards_Smooth_01" }, { "Background_Hexagons_01", null }
            })
        };

        // Keys are the short role names above; the base files are SPR_HUD_SciFiSoldier_<key>.png. The Sci-Fi Menus
        // patterns behind the world map (hexagons, crosses, scan lines) have no fantasy counterpart and are removed.
        private static Dictionary<string, string> HudSprites(string prefix, Dictionary<string, string> roles)
        {
            var map = new Dictionary<string, string>();
            foreach (var role in roles)
                map["SPR_HUD_SciFiSoldier_" + role.Key] = role.Value != null ? prefix + role.Value + ".png" : null;
            map["SPR_SciFiMenus_Background_Hexagons_01"] = null;
            map["SPR_SciFiMenus_Background_Crosses_01"] = null;
            map["SPR_SciFiMenus_Background_ScanLines_01"] = null;
            return map;
        }

        [MenuItem("Krytical Nova/UI/Build Faction Skins")]
        public static void Build()
        {
            foreach (var skin in new[] { Null(), Covenant() })
            {
                var output = new StringBuilder();
                output.AppendLine("/*");
                output.AppendLine($" * {skin.name} skin of the in-game UI ({skin.pack}).");
                output.AppendLine(" * GENERATED by Krytical Nova > UI > Build Faction Skins from the Hegemony sheets: same layout, other sprites,");
                output.AppendLine(" * fonts and colours. It applies under the ." + skin.cssClass + " class, which FactionSkin puts on the UI when the");
                output.AppendLine(" * local player belongs to this faction. Re-run the tool after changing a base sheet.");
                output.AppendLine(" */");
                int rules = 0;
                foreach (string path in BaseSheets)
                {
                    if (!File.Exists(path))
                    {
                        Debug.LogWarning($"[UI] Base sheet missing: {path}");
                        continue;
                    }
                    output.AppendLine();
                    output.AppendLine($"/* ---- {Path.GetFileName(path)} ---- */");
                    rules += Convert(File.ReadAllText(path), skin, output);
                }
                string target = $"{OutputFolder}/KryticalSkin_{skin.name}.uss";
                File.WriteAllText(target, output.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"));
                Debug.Log($"[UI] {skin.name} skin: {rules} override rules in {target}.");
            }
            AssetDatabase.Refresh();
        }

        // ---------------------------------------------------------------- conversion

        private static readonly Regex Comment = new Regex(@"/\*.*?\*/", RegexOptions.Singleline);
        private static readonly Regex Rule = new Regex(@"([^{}]+)\{([^{}]*)\}");
        private static readonly Regex Url = new Regex("url\\(\\s*\"?([^\")]+)\"?\\s*\\)");
        private static readonly Regex Rgb = new Regex(@"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*(?:,\s*([\d.]+)\s*)?\)");
        private static readonly Regex Number = new Regex(@"-?\d*\.?\d+");

        private static int Convert(string uss, Skin skin, StringBuilder output)
        {
            int count = 0;
            foreach (Match rule in Rule.Matches(Comment.Replace(uss, string.Empty)))
            {
                string selectors = rule.Groups[1].Value.Trim();
                if (selectors.Length == 0 || selectors.StartsWith("@")) continue;

                // Rarity colours mean the same thing in every faction, so they are never moved into a skin's palette.
                bool keepColours = selectors.Contains(".rarity-");
                var changed = new List<string>();
                float sliceRatio = 1f; // how much larger the new sprite's border is: the slice scale shrinks by it
                string sliceDeclaration = null;
                foreach (string raw in rule.Groups[2].Value.Split(';'))
                {
                    int colon = raw.IndexOf(':');
                    if (colon < 0) continue;
                    string property = raw.Substring(0, colon).Trim();
                    string value = raw.Substring(colon + 1).Trim();
                    if (property == "-unity-slice-scale")
                    {
                        sliceDeclaration = value;
                        continue;
                    }
                    if (property == "font-size")
                    {
                        if (!Mathf.Approximately(skin.fontScale, 1f)) changed.Add($"font-size: {ScaleNumber(value, skin.fontScale)};");
                        continue;
                    }
                    string converted = ConvertValue(property, value, skin, ref sliceRatio, keepColours);
                    if (converted != value) changed.Add($"{property}: {converted};");
                }
                if (sliceDeclaration != null && !Mathf.Approximately(sliceRatio, 1f))
                    changed.Add($"-unity-slice-scale: {ScaleNumber(sliceDeclaration, 1f / sliceRatio)};");
                if (changed.Count == 0) continue;

                output.AppendLine(Prefix(selectors, skin.cssClass) + " {");
                foreach (string declaration in changed) output.AppendLine("    " + declaration);
                output.AppendLine("}");
                count++;
            }
            return count;
        }

        private static string Prefix(string selectors, string cssClass)
        {
            var parts = selectors.Split(',');
            for (int i = 0; i < parts.Length; i++) parts[i] = $".{cssClass} {parts[i].Trim()}";
            return string.Join(", ", parts);
        }

        private static string ConvertValue(string property, string value, Skin skin, ref float sliceRatio, bool keepColours)
        {
            var url = Url.Match(value);
            if (url.Success)
            {
                string path = url.Groups[1].Value;
                string file = path;
                int cut = file.IndexOfAny(new[] { '#', '?' });
                if (cut >= 0) file = file.Substring(0, cut);

                if (property == "-unity-font-definition" || property == "-unity-font")
                {
                    // The base uses a display face (Orbitron, Zen Dots) for titles and Exo 2 for everything else.
                    bool title = file.Contains("Orbitron") || file.Contains("ZenDots");
                    return $"url(\"{(title ? skin.titleFont : skin.bodyFont)}\")";
                }

                string key = Path.GetFileNameWithoutExtension(file);
                if (!skin.sprites.TryGetValue(key, out string replacement)) return value;
                if (replacement == null) return "none";
                sliceRatio = BorderRatio(file, replacement);
                return $"url(\"{replacement}#{Path.GetFileNameWithoutExtension(replacement)}\")";
            }
            return keepColours ? value : Rgb.Replace(value, match => Recolour(match, skin));
        }

        /// <summary>New border size over old border size, so the corners keep their on-screen size.</summary>
        private static float BorderRatio(string basePath, string skinPath)
        {
            var from = AssetDatabase.LoadAssetAtPath<Sprite>(basePath.TrimStart('/'));
            var to = AssetDatabase.LoadAssetAtPath<Sprite>(skinPath.TrimStart('/'));
            if (to == null) Debug.LogWarning($"[UI] Skin sprite not found: {skinPath}");
            if (from == null || to == null) return 1f;
            float fromBorder = Mathf.Max(from.border.x, from.border.y, from.border.z, from.border.w);
            float toBorder = Mathf.Max(to.border.x, to.border.y, to.border.z, to.border.w);
            return fromBorder > 0f && toBorder > 0f ? toBorder / fromBorder : 1f;
        }

        private static string ScaleNumber(string value, float factor)
        {
            return Number.Replace(value, m =>
            {
                float number = float.Parse(m.Value, CultureInfo.InvariantCulture) * factor;
                return number.ToString("0.##", CultureInfo.InvariantCulture);
            }, 1);
        }

        /// <summary>
        /// Moves a base colour into the skin's palette by hue family: the teal accent family takes the faction accent,
        /// the blue-grey panel tones take the faction's dark tone, the small gold labels take its second colour.
        /// Greys, whites and signal colours (danger red, waypoint yellow, portal violet) stay as they are.
        /// </summary>
        private static string Recolour(Match match, Skin skin)
        {
            var color = new Color(int.Parse(match.Groups[1].Value) / 255f, int.Parse(match.Groups[2].Value) / 255f,
                int.Parse(match.Groups[3].Value) / 255f);
            Color.RGBToHSV(color, out float h, out float s, out float v);
            float hue = h * 360f;

            if (s >= 0.12f && hue >= 140f && hue < 200f)
                color = Color.HSVToRGB(skin.accentHue / 360f, Mathf.Clamp01(s * skin.accentSaturation), v);
            // Only the dark or washed-out blues are panel tones; a strong blue is a signal (chi bar, rare items, allies).
            else if (s >= 0.08f && hue >= 200f && hue <= 260f && (v <= 0.5f || s <= 0.3f))
                color = Color.HSVToRGB(skin.neutralHue / 360f, Mathf.Clamp01(s * skin.neutralSaturation), v);
            else if (s >= 0.35f && s <= 0.6f && hue >= 30f && hue <= 55f)
                color = Color.HSVToRGB(skin.labelHue / 360f, s, v);
            else
                return match.Value;

            int r = Mathf.RoundToInt(color.r * 255f), g = Mathf.RoundToInt(color.g * 255f), b = Mathf.RoundToInt(color.b * 255f);
            return match.Groups[4].Success ? $"rgba({r}, {g}, {b}, {match.Groups[4].Value})" : $"rgb({r}, {g}, {b})";
        }
    }
}
