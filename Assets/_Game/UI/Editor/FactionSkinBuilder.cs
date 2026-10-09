using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using KryticalNova.Factions;
using KryticalNova.UI;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Generates the faction skins of the in-game UI from the factions' <see cref="UiSkin"/> assets (UI/Skins). The
    /// layout belongs to the base sheets: this tool reads them and writes, for each faction, a sheet of overrides
    /// that only swaps what a skin is made of: sprites (the same part, from the skin asset), fonts and colours. Every
    /// override selector is prefixed with the faction's class, so the base sheets are never edited and a skin can
    /// never change a layout. A skin that names the base sheets' own sprites, fonts and colours (the Hegemony's, as
    /// shipped) comes out as an empty sheet.
    /// Runs by itself when a skin asset is saved (<see cref="UiSkinWatcher"/>); re-run it after editing a base sheet.
    /// A generated sheet is never tuned by hand: change the asset.
    /// </summary>
    public static class FactionSkinBuilder
    {
        public const string SkinFolder = "Assets/_Game/UI/Skins";
        private const string OutputFolder = "Assets/_Game/UI/Common";
        private const string SciFiHud = "Assets/Synty/InterfaceSciFiSoldierHUD/";
        private const string DarkFantasy = "Assets/Synty/InterfaceDarkFantasyHUD/";
        private const string FantasyWarrior = "Assets/Synty/InterfaceFantasyWarriorHUD/";
        private const string SciFiMenus = "Assets/Synty/InterfaceSciFiMenus/";
        private const string DarkFantasyMenus = "Assets/Synty/InterfaceDarkFantasyMenus/";
        private const string FantasyMenus = "Assets/Synty/InterfaceFantasyMenus/";
        // The base sheets' own sprites and fonts: a skin that names the same ones overrides nothing.
        private const string BaseSpritePrefix = "SPR_HUD_SciFiSoldier_";
        private const string BaseBodyFont = SciFiHud + "Fonts/exo-2-0/Exo2.0-SemiBold.otf";
        private const string BaseTitleFont = SciFiHud + "Fonts/Orbitron/Orbitron-ExtraBold.ttf";

        public static readonly FactionId[] Factions = { FactionId.Hegemony, FactionId.Null, FactionId.Covenant };

        private static readonly string[] BaseSheets =
        {
            "Assets/_Game/UI/Common/KryticalTheme.uss",
            "Assets/_Game/UI/HUD/ProgressionHUD.uss",
            "Assets/_Game/UI/Settings/Settings.uss",
            "Assets/_Game/UI/Inventory/Items.uss",
            "Assets/_Game/UI/Inventory/Forge.uss",
            "Assets/_Game/UI/HUD/Herald/Herald.uss",
            "Assets/_Game/UI/HUD/GameMenu/GameMenu.uss",
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

        // Each part of a skin, and the base sprite (SPR_HUD_SciFiSoldier_<key>) that stands for it in the base sheets.
        // Rings and map shapes are not parts of a skin: they must match the round minimap, and only take its colours.
        private static readonly (string key, Func<UiSkin, Sprite> get, Action<UiSkin, Sprite> set)[] Parts =
        {
            ("Box_02", s => s.panel, (s, v) => s.panel = v),
            ("Box_04", s => s.card, (s, v) => s.card = v),
            ("Box_03", s => s.token, (s, v) => s.token = v),
            ("Box_Glass_04", s => s.itemSlot, (s, v) => s.itemSlot = v),
            ("Frame_02", s => s.frame, (s, v) => s.frame = v),
            ("Frame_04", s => s.frameSmall, (s, v) => s.frameSmall = v),
            ("Bar_Angled_01", s => s.bar, (s, v) => s.bar = v),
            ("Pip_Hexagon_01_Active", s => s.pipOn, (s, v) => s.pipOn = v),
            ("Pip_Hexagon_01_Inactive", s => s.pipOff, (s, v) => s.pipOff = v),
            ("Icon_InactiveItem_01", s => s.closeIcon, (s, v) => s.closeIcon = v),
            ("Triangle_02", s => s.arrowIcon, (s, v) => s.arrowIcon = v),
            ("Gradient_Outwards_Smooth_01", s => s.softBacking, (s, v) => s.softBacking = v),
        };

        // The sci-fi packs' patterns behind the world map and the larger panels: a skin has them or has none.
        private static readonly string[] TechPatterns =
        {
            "SPR_HUD_SciFiSoldier_Background_Hexagons_01", "SPR_SciFiMenus_Background_Hexagons_01",
            "SPR_SciFiMenus_Background_Crosses_01", "SPR_SciFiMenus_Background_ScanLines_01"
        };

        /// <summary>A skin asset, resolved to what the conversion works with.</summary>
        private sealed class Skin
        {
            public string name, cssClass, pack;
            /// <summary>Null: the base sheets' own font stays.</summary>
            public string bodyFont, titleFont;
            /// <summary>Base sprite file name (no extension) to this skin's sprite, or null to remove the image.</summary>
            public Dictionary<string, Sprite> sprites;
            public bool recolour;
            /// <summary>Hue the base accent (teal) becomes, the hue of the dark panel tones, and the hue of small gold labels.</summary>
            public float accentHue, neutralHue, labelHue;
            public float accentSaturation, neutralSaturation;
            public float fontScale;
        }

        public static string SkinPath(FactionId faction) => $"{SkinFolder}/UiSkin_{faction}.asset";
        public static string SheetPath(FactionId faction) => $"{OutputFolder}/KryticalSkin_{faction}.uss";

        public static UiSkin LoadSkin(FactionId faction) => AssetDatabase.LoadAssetAtPath<UiSkin>(SkinPath(faction));

        [MenuItem("Krytical Nova/UI/Build Faction Skins")]
        public static void Build()
        {
            EnsureSkins();
            bool wrote = false;
            foreach (FactionId faction in Factions)
            {
                UiSkin asset = LoadSkin(faction);
                if (asset == null) continue;
                EnsureChrome(asset);
                string text = Generate(asset, out int rules);
                string target = SheetPath(faction);
                // An unchanged sheet is left alone: writing it would re-import it and rebuild every open UI.
                if (File.Exists(target) && File.ReadAllText(target) == text) continue;
                File.WriteAllText(target, text);
                wrote = true;
                Debug.Log($"[UI] {faction} skin: {rules} override rules in {target}.");
            }
            if (wrote) AssetDatabase.Refresh();
        }

        /// <summary>The whole skin sheet of one faction, as it is written to disk.</summary>
        public static string Generate(UiSkin asset, out int rules)
        {
            Skin skin = Resolve(asset);
            var output = new StringBuilder();
            output.AppendLine("/*");
            output.AppendLine($" * {skin.name} skin of the in-game UI ({skin.pack}).");
            output.AppendLine($" * GENERATED by Krytical Nova > UI > Build Faction Skins from {SkinPath(asset.faction)} and the base sheets:");
            output.AppendLine(" * same layout, the skin's sprites, fonts and colours. It applies under the ." + skin.cssClass + " class, which FactionSkin");
            output.AppendLine(" * puts on the UI when the local player belongs to this faction. Do not edit: change the skin asset.");
            output.AppendLine(" */");
            rules = 0;
            foreach (string path in BaseSheets)
            {
                if (!File.Exists(path))
                {
                    Debug.LogWarning($"[UI] Base sheet missing: {path}");
                    continue;
                }
                var sheet = new StringBuilder();
                int count = Convert(File.ReadAllText(path), skin, sheet);
                if (count == 0) continue;
                output.AppendLine();
                output.AppendLine($"/* ---- {Path.GetFileName(path)} ---- */");
                output.Append(sheet);
                rules += count;
            }
            var chrome = new StringBuilder();
            int chromeRules = Chrome(asset, skin.cssClass, chrome);
            if (chromeRules > 0)
            {
                output.AppendLine();
                output.AppendLine("/* ---- window chrome: the pack's own frames and ornaments ---- */");
                output.Append(chrome);
                rules += chromeRules;
            }
            return output.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        private static Skin Resolve(UiSkin asset)
        {
            var skin = new Skin
            {
                name = asset.faction.ToString(), cssClass = UiSkin.ClassOf(asset.faction), pack = asset.hudPack,
                recolour = asset.recolour, accentSaturation = asset.accentSaturation, neutralSaturation = asset.panelSaturation,
                fontScale = asset.fontScale, sprites = new Dictionary<string, Sprite>(),
            };
            Color.RGBToHSV(asset.accent, out float accent, out _, out _);
            Color.RGBToHSV(asset.panelTone, out float neutral, out _, out _);
            Color.RGBToHSV(asset.label, out float label, out _, out _);
            skin.accentHue = accent * 360f;
            skin.neutralHue = neutral * 360f;
            skin.labelHue = label * 360f;

            string body = asset.bodyFont != null ? AssetDatabase.GetAssetPath(asset.bodyFont) : null;
            string title = asset.titleFont != null ? AssetDatabase.GetAssetPath(asset.titleFont) : null;
            skin.bodyFont = string.IsNullOrEmpty(body) || body == BaseBodyFont ? null : "/" + body;
            skin.titleFont = string.IsNullOrEmpty(title) || title == BaseTitleFont ? null : "/" + title;

            foreach (var part in Parts)
            {
                Sprite sprite = part.get(asset);
                if (sprite == null)
                {
                    Debug.LogWarning($"[UI] {SkinPath(asset.faction)} has no sprite for '{part.key}': the base one stays.");
                    continue;
                }
                skin.sprites[BaseSpritePrefix + part.key] = sprite;
            }
            if (!asset.techPatterns)
                foreach (string pattern in TechPatterns) skin.sprites[pattern] = null;
            return skin;
        }

        // ---------------------------------------------------------------- the skin assets

        /// <summary>
        /// Makes the three skin assets where one is missing, with the parts each faction's packs have for them. An
        /// asset that is there is left exactly as it is: it is the place a skin is tuned.
        /// </summary>
        public static void EnsureSkins()
        {
            WeatherVfxBuilder.EnsureFolder(SkinFolder);
            Ensure(FactionId.Hegemony, skin =>
            {
                skin.hudPack = "Synty INTERFACE - Sci-Fi Soldier HUD";
                skin.menuPack = "Synty INTERFACE - Sci-Fi Menus";
                skin.bodyFont = Load<Font>(BaseBodyFont);
                skin.titleFont = Load<Font>(BaseTitleFont);
                skin.fontScale = 1f;
                skin.recolour = false; // the base sheets' colours are the Hegemony's own
                skin.accent = Color.HSVToRGB(170f / 360f, 0.72f, 0.85f);
                skin.panelTone = Color.HSVToRGB(225f / 360f, 0.35f, 0.3f);
                skin.label = Color.HSVToRGB(42f / 360f, 0.5f, 0.85f);
                skin.techPatterns = true;
                foreach (var part in Parts)
                    part.set(skin, Load<Sprite>($"{SciFiHud}Sprites/HUD/{BaseSpritePrefix}{part.key}.png"));
                Menus(skin, SciFiMenus, "SciFiMenus", new Color32(0x4F, 0xE3, 0xF0, 0xFF));
            });
            Ensure(FactionId.Null, skin =>
            {
                skin.hudPack = "Synty INTERFACE - Dark Fantasy HUD";
                skin.menuPack = "Synty INTERFACE - Dark Fantasy Menus";
                skin.bodyFont = Load<Font>(DarkFantasy + "Fonts/Markazi/MarkaziText-SemiBold.ttf");
                skin.titleFont = Load<Font>(DarkFantasy + "Fonts/Pirata One/PirataOne-Regular.ttf");
                skin.fontScale = 1.28f;
                skin.recolour = true;
                skin.accent = Color.HSVToRGB(357f / 360f, 0.8f, 0.85f);
                skin.panelTone = Color.HSVToRGB(285f / 360f, 0.35f, 0.3f);
                skin.label = Color.HSVToRGB(262f / 360f, 0.5f, 0.85f);
                skin.accentSaturation = 1.45f;
                skin.panelSaturation = 0.85f;
                skin.techPatterns = false;
                HudParts(skin, DarkFantasy + "Sprites/HUD/SPR_HUD_DarkFantasy_", new[]
                {
                    "Box_Medium_Parchment_01", "Box_Small_Parchment_01", "Box_Small_Parchment_02", "Box_Small_Parchment_03",
                    "Frame_Box_01", "Frame_Box_03", "Bar_01_Clean", "Circle_01_Med_Clean", "Ring_Medium_01_Clean",
                    "Close_01_Clean", "Arrow_Small_01_Clean", "Gradient_Outwards_01"
                });
                Menus(skin, DarkFantasyMenus, "DarkFantasyMenus", new Color32(0xD8, 0x34, 0x3C, 0xFF));
            });
            Ensure(FactionId.Covenant, skin =>
            {
                skin.hudPack = "Synty INTERFACE - Fantasy Warrior HUD";
                skin.menuPack = "Synty INTERFACE - Fantasy Menus";
                skin.bodyFont = Load<Font>(FantasyWarrior + "Fonts/Grenze/Grenze-SemiBold.ttf");
                skin.titleFont = Load<Font>(FantasyWarrior + "Fonts/Texturina/Texturina_18pt-SemiBold.ttf");
                skin.fontScale = 1.12f;
                skin.recolour = true;
                skin.accent = Color.HSVToRGB(39f / 360f, 0.8f, 0.85f);
                skin.panelTone = Color.HSVToRGB(32f / 360f, 0.35f, 0.3f);
                skin.label = Color.HSVToRGB(140f / 360f, 0.5f, 0.85f);
                skin.accentSaturation = 1.45f;
                skin.panelSaturation = 0.85f;
                skin.techPatterns = false;
                HudParts(skin, FantasyWarrior + "Sprites/HUD/SPR_HUD_FantasyWarrior_", new[]
                {
                    "Box_Medium_Parchment_01", "Box_Small_Parchment_01", "Box_Small_Parchment_02", "Box_Small_Parchment_03",
                    "Frame_Box_Medium_01", "Frame_Box_Small_01", "Bar_Diamond_01", "Circle_Small_01_Clean", "Ring_Small_01_Clean",
                    "Icon_InactiveItem_01", "Triangle_Small_01_Clean", "Gradient_Outwards_Smooth_01"
                });
                Menus(skin, FantasyMenus, "FantasyMenus", new Color32(0xE8, 0xB0, 0x4A, 0xFF));
            });
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- window chrome

        // What is read rather than glanced at: these get the skin's nearly solid backing. The always-on panels (chat,
        // the Squad frames, prompts) keep the layout's see-through fill.
        private static readonly string[] SolidPanels =
        {
            ".window", ".item-tooltip", ".item-menu", ".item-confirm", ".amount-dialog", ".game-menu", ".player-menu",
        };

        /// <summary>
        /// Gives a skin asset made before window chrome existed the frames and ornaments of its pack. An asset that
        /// has a window frame is left exactly as it is.
        /// </summary>
        private static void EnsureChrome(UiSkin skin)
        {
            if (skin.windowBacking.a <= 0f)
            {
                // The faction's dark tone, darker still.
                Color.RGBToHSV(skin.panelTone, out float hue, out float saturation, out _);
                Color backing = Color.HSVToRGB(hue, Mathf.Clamp01(saturation * 1.1f), 0.085f);
                backing.a = 0.97f;
                skin.windowBacking = backing;
                EditorUtility.SetDirty(skin);
                AssetDatabase.SaveAssetIfDirty(skin);
            }
            if (skin.button == null)
            {
                // The pack's small box frame over its fill, in the faction's dark tone.
                Color.RGBToHSV(skin.panelTone, out float hue, out float saturation, out _);
                Color fillTint = Color.HSVToRGB(hue, Mathf.Clamp01(saturation), 0.2f);
                string art = $"{FactionHudBuilder.Folder}/{skin.faction}/Art";
                string name = $"Button_{skin.faction}";
                switch (skin.faction)
                {
                    case FactionId.Hegemony:
                        skin.button = SpriteRecolourer.Flatten(name, art, Load<Sprite>(SciFiMenus + "Sprites/SciFiMenus/SPR_SciFiMenus_Frame_Box_Small_07.png"),
                            Load<Sprite>(SciFiMenus + "Sprites/SciFiMenus/SPR_SciFiMenus_Frame_Box_Small_07_Background.png"), fillTint);
                        skin.buttonBorder = 7f;
                        break;
                    case FactionId.Null:
                        skin.button = SpriteRecolourer.Flatten(name, art, Load<Sprite>(DarkFantasy + "Sprites/DarkFantasy/SPR_DarkFantasy_Frame_Box_Medium_01_Variant_01.png"),
                            Load<Sprite>(DarkFantasy + "Sprites/DarkFantasy/SPR_DarkFantasy_Frame_Box_Medium_01_Background.png"), fillTint);
                        skin.buttonBorder = 8f;
                        break;
                    case FactionId.Covenant:
                        skin.button = SpriteRecolourer.Flatten(name, art, Load<Sprite>(FantasyWarrior + "Sprites/FantasyWarrior/SPR_FantasyWarrior_Frame_Box_22_Variant_01.png"), null, fillTint);
                        skin.buttonBorder = 7f;
                        break;
                }
                EditorUtility.SetDirty(skin);
                AssetDatabase.SaveAssetIfDirty(skin);
            }
            if (skin.windowFrame != null) return;
            switch (skin.faction)
            {
                case FactionId.Hegemony:
                    skin.windowFrame = Load<Sprite>(SciFiHud + "Sprites/SciFi/SPR_SciFiSoldier_Frame_TechyDark_01.png");
                    skin.windowFrameBorder = 30f;
                    skin.windowFrameOutset = 5f;
                    skin.panelFrame = null; // the pack's small panels are its tinted line frames: the layout's own
                    skin.crown = Load<Sprite>(SciFiHud + "Sprites/SciFi/SPR_SciFiSoldier_Greeble_BoxTop_02.png");
                    skin.crownSize = new Vector2(52f, 38f);
                    skin.crownRise = 33f;
                    skin.crownBack = null;
                    break;
                case FactionId.Null:
                    skin.windowFrame = Load<Sprite>(DarkFantasy + "Sprites/DarkFantasy/SPR_DarkFantasy_Frame_Box_Large_08.png");
                    skin.windowFrameBorder = 26f;
                    skin.windowFrameOutset = 8f;
                    skin.panelFrame = Load<Sprite>(DarkFantasy + "Sprites/DarkFantasy/SPR_DarkFantasy_Frame_Box_Large_07.png");
                    skin.panelFrameBorder = 8f;
                    skin.panelFrameOutset = 1f;
                    skin.crown = Load<Sprite>(DarkFantasy + "Sprites/DarkFantasy/SPR_DarkFantasy_Greeble_Skull_01.png");
                    skin.crownSize = new Vector2(46f, 46f);
                    skin.crownRise = 30f;
                    skin.crownBack = Load<Sprite>(DarkFantasy + "Sprites/DarkFantasy/SPR_DarkFantasy_Greeble_Horns_05.png");
                    skin.crownBackSize = new Vector2(92f, 46f);
                    skin.crownBackRise = 30f;
                    break;
                case FactionId.Covenant:
                    skin.windowFrame = Load<Sprite>(FantasyWarrior + "Sprites/FantasyWarrior/SPR_FantasyWarrior_Frame_Box_06.png");
                    skin.windowFrameBorder = 20f;
                    skin.windowFrameOutset = 6f;
                    skin.panelFrame = Load<Sprite>(FantasyWarrior + "Sprites/FantasyWarrior/SPR_FantasyWarrior_Frame_Box_12.png");
                    skin.panelFrameBorder = 12f;
                    skin.panelFrameOutset = 1f;
                    skin.crown = Load<Sprite>(FantasyWarrior + "Sprites/FantasyWarrior/SPR_FantasyWarrior_Greeble_Curlicue_24.png");
                    skin.crownSize = new Vector2(96f, 48f);
                    skin.crownRise = 30f;
                    skin.crownBack = null;
                    break;
                default:
                    return;
            }
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssetIfDirty(skin);
        }

        /// <summary>
        /// The rules that put a skin's own frames and ornaments on windows and panels. They come after the converted
        /// sheets and name the frame through its parent, so they win over the tinted outline those sheets give it.
        /// </summary>
        private static int Chrome(UiSkin asset, string cssClass, StringBuilder output)
        {
            int count = 0;
            if (asset.windowBacking.a > 0f)
            {
                Color32 backing = asset.windowBacking;
                string alpha = asset.windowBacking.a.ToString("0.##", CultureInfo.InvariantCulture);
                var selectors = new List<string>();
                foreach (string solid in SolidPanels) selectors.Add($".{cssClass} {solid}");
                output.AppendLine(string.Join(", ", selectors) + " {");
                output.AppendLine($"    background-color: rgba({backing.r}, {backing.g}, {backing.b}, {alpha});");
                output.AppendLine("}");
                count++;
            }
            if (asset.button != null)
            {
                Sprite art = asset.button;
                float widest = Mathf.Max(art.border.x, art.border.y, art.border.z, art.border.w);
                string scale = widest > 0f ? (asset.buttonBorder / widest).ToString("0.####", CultureInfo.InvariantCulture) : "1";
                // The art keeps its colours: a state only lightens or darkens it.
                output.AppendLine($".{cssClass} Button {{");
                output.AppendLine($"    background-image: {UrlOf(art)};");
                output.AppendLine("    -unity-background-image-tint-color: rgb(212, 212, 212);");
                output.AppendLine($"    -unity-slice-scale: {scale}px;");
                output.AppendLine("}");
                output.AppendLine($".{cssClass} Button:hover {{");
                output.AppendLine("    -unity-background-image-tint-color: rgb(255, 255, 255);");
                output.AppendLine("}");
                output.AppendLine($".{cssClass} Button:active {{");
                output.AppendLine("    -unity-background-image-tint-color: rgb(165, 165, 165);");
                output.AppendLine("}");
                output.AppendLine($".{cssClass} Button:disabled {{");
                output.AppendLine("    -unity-background-image-tint-color: rgba(120, 120, 120, 0.85);");
                output.AppendLine("}");
                count += 4;
            }
            count += FrameRule(output, cssClass, ".panel", asset.panelFrame, asset.panelFrameBorder, asset.panelFrameOutset);
            count += FrameRule(output, cssClass, ".window", asset.windowFrame, asset.windowFrameBorder, asset.windowFrameOutset);
            count += CrownRule(output, cssClass, SyntySkin.CrownBackClass, asset.crownBack, asset.crownBackSize, asset.crownBackRise);
            count += CrownRule(output, cssClass, SyntySkin.CrownClass, asset.crown, asset.crownSize, asset.crownRise);
            return count;
        }

        private static int FrameRule(StringBuilder output, string cssClass, string parent, Sprite frame, float border, float outset)
        {
            if (frame == null) return 0;
            float widest = Mathf.Max(frame.border.x, frame.border.y, frame.border.z, frame.border.w);
            string scale = widest > 0f ? (border / widest).ToString("0.####", CultureInfo.InvariantCulture) : "1";
            string reach = (-outset).ToString("0.##", CultureInfo.InvariantCulture) + "px";
            output.AppendLine($".{cssClass} {parent} > .{SyntySkin.FrameClass} {{");
            output.AppendLine($"    background-image: {UrlOf(frame)};");
            output.AppendLine("    -unity-background-image-tint-color: rgb(255, 255, 255);");
            output.AppendLine($"    -unity-slice-scale: {scale}px;");
            output.AppendLine($"    left: {reach};");
            output.AppendLine($"    top: {reach};");
            output.AppendLine($"    right: {reach};");
            output.AppendLine($"    bottom: {reach};");
            output.AppendLine("}");
            // The corner pip belongs to the tinted outline, not to a frame with corners of its own.
            output.AppendLine($".{cssClass} {parent} > .{SyntySkin.PipClass} {{");
            output.AppendLine("    display: none;");
            output.AppendLine("}");
            return 2;
        }

        private static int CrownRule(StringBuilder output, string cssClass, string layer, Sprite ornament, Vector2 size, float rise)
        {
            if (ornament == null) return 0;
            output.AppendLine($".{cssClass} .window > .{layer} {{");
            output.AppendLine("    display: flex;");
            output.AppendLine($"    background-image: {UrlOf(ornament)};");
            output.AppendLine($"    width: {size.x.ToString("0.##", CultureInfo.InvariantCulture)}px;");
            output.AppendLine($"    height: {size.y.ToString("0.##", CultureInfo.InvariantCulture)}px;");
            output.AppendLine($"    top: {(-rise).ToString("0.##", CultureInfo.InvariantCulture)}px;");
            output.AppendLine("}");
            return 1;
        }

        private static string UrlOf(Sprite sprite) => $"url(\"/{AssetDatabase.GetAssetPath(sprite)}#{sprite.name}\")";

        private static void Ensure(FactionId faction, Action<UiSkin> defaults)
        {
            if (LoadSkin(faction) != null) return;
            var skin = ScriptableObject.CreateInstance<UiSkin>();
            skin.faction = faction;
            defaults(skin);
            AssetDatabase.CreateAsset(skin, SkinPath(faction));
        }

        /// <summary>The sprites of a pack for the parts, in the order of <see cref="Parts"/>.</summary>
        private static void HudParts(UiSkin skin, string prefix, string[] names)
        {
            for (int i = 0; i < Parts.Length && i < names.Length; i++)
                Parts[i].set(skin, Load<Sprite>(prefix + names[i] + ".png"));
        }

        /// <summary>The three menu packs are made to one template: the same screens under the pack's own name.</summary>
        private static void Menus(UiSkin skin, string pack, string token, Color accent)
        {
            string prefabs = pack + "Prefabs/";
            skin.titleScreen = Load<GameObject>($"{prefabs}Screens/Screen_{token}_Title_01.prefab");
            skin.choiceScreen = Load<GameObject>($"{prefabs}Screens/Screen_{token}_SelectDifficulty_01.prefab");
            skin.listScreen = Load<GameObject>($"{prefabs}Screens/Screen_{token}_Load_Game_01.prefab");
            skin.loadingScreen = Load<GameObject>($"{prefabs}Screens/Screen_{token}_Loading_01.prefab");
            skin.confirmPopup = Load<GameObject>($"{prefabs}Popups/Popup_{token}_Save_Confirmation_01.prefab");
            skin.optionRow = Load<GameObject>($"{prefabs}Settings/Settings_{token}_Item_Array_01.prefab");
            // The Dark Fantasy pack numbers its labels.
            string label = $"{prefabs}_CommonComponents/Label_{token}_Body.prefab";
            skin.bodyLabel = Load<GameObject>(File.Exists(label) ? label : $"{prefabs}_CommonComponents/Label_{token}_Body_01.prefab");
            skin.menuAccent = accent;
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogWarning($"[UI] Skin part not found: {path}");
            return asset;
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

                // Rarity colours mean the same thing in every faction, and a speech bubble is comic ink on paper in every
                // one of them: neither is ever moved into a skin's palette.
                bool keepColours = !skin.recolour || selectors.Contains(".rarity-") || selectors.Contains("-bubble");
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
                    string font = title ? skin.titleFont : skin.bodyFont;
                    return font != null ? $"url(\"{font}\")" : value;
                }

                string key = Path.GetFileNameWithoutExtension(file);
                if (!skin.sprites.TryGetValue(key, out Sprite replacement)) return value;
                if (replacement == null) return "none";
                string replacementPath = AssetDatabase.GetAssetPath(replacement);
                if (replacementPath == file.TrimStart('/')) return value; // the base sprite itself
                sliceRatio = BorderRatio(file, replacement);
                return $"url(\"/{replacementPath}#{replacement.name}\")";
            }
            return keepColours ? value : Rgb.Replace(value, match => Recolour(match, skin));
        }

        /// <summary>New border size over old border size, so the corners keep their on-screen size.</summary>
        private static float BorderRatio(string basePath, Sprite to)
        {
            var from = AssetDatabase.LoadAssetAtPath<Sprite>(basePath.TrimStart('/'));
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
