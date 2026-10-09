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

        /// <summary>Makes the flattened art of every skin again (after a change to what it is made from), then the sheets.</summary>
        [MenuItem("Krytical Nova/UI/Rebuild Faction Skin Art")]
        public static void RebuildArt()
        {
            foreach (FactionId faction in Factions)
            {
                UiSkin asset = LoadSkin(faction);
                if (asset == null) continue;
                asset.button = asset.menuButton = asset.headerBand = asset.cell = asset.closeButton = null;
                EditorUtility.SetDirty(asset);
            }
            Build();
        }

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
                EnsureInventoryParts(asset);
                EnsureInventoryFrames(asset);
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
            int chromeRules = Chrome(asset, skin.cssClass, chrome) + Design(asset, skin.cssClass, chrome);
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
            foreach (FactionId faction in Factions)
            {
                UiSkin skin = LoadSkin(faction);
                if (skin != null) EnsureCreatorParts(skin);
            }
            AssetDatabase.SaveAssets();
        }

        // ---------------------------------------------------------------- the inventory pages' parts

        // The equipment slots in the order of UiSkin.slotGlyphs (the names of the slots' elements, "equip-<name>").
        private static readonly string[] SlotOrder = { "Weapon", "Armor", "Gloves", "Boots", "Ring", "Amulet", "Cape", "Pet" };

        /// <summary>
        /// Gives a skin asset the parts of its packs' own inventory (the Dark Fantasy HUD's paper doll window, Fantasy
        /// Screens' inventory panel; the Sci-Fi packs have no inventory, so the Hegemony's is made of their slot
        /// pictures and lines, with the pictures they lack borrowed from the Dark Fantasy pack). A part that is set is
        /// left as it is.
        /// </summary>
        private static void EnsureInventoryParts(UiSkin skin)
        {
            if (skin.slotGlyphs != null && skin.slotGlyphs.Length == SlotOrder.Length && skin.dollBarTopLeft != null) return;
            string df = DarkFantasy + "Sprites/Icons_Inventory/ICON_DarkFantasy_Inventory_";
            switch (skin.faction)
            {
                case FactionId.Hegemony:
                {
                    string sf = SciFiHud + "Sprites/Icons_Inventory/ICON_SciFiSoldier_Inventory_";
                    skin.slotGlyphs = new[]
                    {
                        Load<Sprite>(sf + "Weapon_01.png"), Load<Sprite>(sf + "Armor_01.png"), Load<Sprite>(df + "Gauntlets_01_Underlay.png"), Load<Sprite>(df + "Boots_01_Underlay.png"),
                        Load<Sprite>(df + "Rings_02_Underlay.png"), Load<Sprite>(df + "Necklaces_02_Underlay.png"), Load<Sprite>(sf + "Backpack_01.png"), Load<Sprite>(sf + "Science_01.png"),
                    };
                    skin.slotGlyphTint = new Color32(0x5A, 0xC8, 0xE6, 0x59);
                    skin.dollBarTopLeft = skin.dollBarFootLeft = Load<Sprite>(SciFiHud + "Sprites/HUD/SPR_HUD_SciFiSoldier_Line_Horizontal_01.png");
                    skin.dollBarHeight = 10f;
                    skin.dollBarTint = new Color32(0x72, 0xDE, 0xC4, 0xFF);
                    break;
                }
                case FactionId.Null:
                    skin.slotGlyphs = new[]
                    {
                        Load<Sprite>(df + "Swords_01_Underlay.png"), Load<Sprite>(df + "Armor_02_Underlay.png"), Load<Sprite>(df + "Gauntlets_01_Underlay.png"), Load<Sprite>(df + "Boots_01_Underlay.png"),
                        Load<Sprite>(df + "Rings_02_Underlay.png"), Load<Sprite>(df + "Necklaces_02_Underlay.png"), Load<Sprite>(df + "ShoulderPads_01_Underlay.png"), Load<Sprite>(df + "Items_01_Underlay.png"),
                    };
                    skin.slotGlyphTint = new Color32(0x40, 0x6C, 0x6F, 0x8C); // the pack's own tone, a little stronger than it draws it
                    skin.dollBarTopLeft = Load<Sprite>(DarkFantasy + "Sprites/DarkFantasy/SPR_DarkFantasy_Bar_03_TopLeft.png");
                    skin.dollBarTopRight = Load<Sprite>(DarkFantasy + "Sprites/DarkFantasy/SPR_DarkFantasy_Bar_03_TopRight.png");
                    skin.dollBarFootLeft = Load<Sprite>(DarkFantasy + "Sprites/DarkFantasy/SPR_DarkFantasy_Bar_03_BottomLeft.png");
                    skin.dollBarFootRight = Load<Sprite>(DarkFantasy + "Sprites/DarkFantasy/SPR_DarkFantasy_Bar_03_BottomRight.png");
                    skin.dollBarHeight = 20f;
                    skin.dollBarTint = new Color32(0x90, 0x90, 0x90, 0xFF);
                    skin.gridCell = Load<Sprite>(DarkFantasy + "Sprites/HUD/SPR_HUD_DarkFantasy_Box_Small_Parchment_01.png");
                    skin.gridCellTint = new Color32(0x13, 0x13, 0x13, 0xE6);
                    break;
                case FactionId.Covenant:
                {
                    string fs = FantasyScreens + "Sprites/Icons_Inventory/ICON_FantasyScreens_Inventory_";
                    skin.slotGlyphs = new[]
                    {
                        Load<Sprite>(fs + "Swords_01_Underlay.png"), Load<Sprite>(fs + "Armor_02_Underlay.png"), Load<Sprite>(fs + "Gauntlets_01_Underlay.png"), Load<Sprite>(fs + "Boots_01_Underlay.png"),
                        Load<Sprite>(fs + "Rings_02_Underlay.png"), Load<Sprite>(fs + "Necklaces_02_Underlay.png"), Load<Sprite>(fs + "ShoulderPads_01_Underlay.png"), Load<Sprite>(fs + "Items_01_Underlay.png"),
                    };
                    skin.slotGlyphTint = new Color32(0x90, 0x86, 0x5E, 0x80);
                    string bar = FantasyMenus + "Sprites/FantasyMenus/SPR_FantasyMenus_Frame_Bar_01_";
                    skin.dollBarTopLeft = Load<Sprite>(bar + "Top_Left.png");
                    skin.dollBarTopRight = Load<Sprite>(bar + "Top_Right.png");
                    skin.dollBarFootLeft = Load<Sprite>(bar + "Bottom_Left.png");
                    skin.dollBarFootRight = Load<Sprite>(bar + "Bottom_Right.png");
                    skin.dollBarHeight = 20f;
                    skin.dollBarTint = Color.white;
                    skin.gridCell = Load<Sprite>(FantasyWarrior + "Sprites/HUD/SPR_HUD_FantasyWarrior_Box_Medium_Parchment_01.png");
                    skin.gridCellTint = new Color32(0x03, 0x15, 0x22, 0xE6);
                    break;
                }
            }
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssetIfDirty(skin);
        }

        /// <summary>
        /// The ornate frames of the inventory pages, from each pack: the equipment slot's, the item card's and the
        /// one round the card's picture; and the Hegemony's grid cell. A skin that has a slot frame is left as it is.
        /// </summary>
        private static void EnsureInventoryFrames(UiSkin skin)
        {
            if (skin.slotFrame != null) return;
            switch (skin.faction)
            {
                case FactionId.Hegemony:
                {
                    string menus = SciFiMenus + "Sprites/SciFiMenus/SPR_SciFiMenus_";
                    skin.slotFrame = Load<Sprite>(menus + "Frame_Box_Small_10.png"); // the bolted, cut-corner housing
                    skin.slotFrameBorder = 22f; skin.slotFrameOutset = 7f;
                    skin.slotFill = new Color32(10, 34, 48, 230);
                    skin.cardFrame = Load<Sprite>(menus + "Frame_Box_Large_09.png");
                    skin.cardFrameBorder = 46f; skin.cardFrameOutset = 10f;
                    skin.cardBacking = new Color32(6, 30, 40, 245);
                    skin.cardPictureFrame = Load<Sprite>(menus + "Frame_Box_Small_05.png");
                    skin.cardPictureBorder = 16f; skin.cardPictureOutset = 6f;
                    // The Sci-Fi packs have no inventory: their glass slot is the grid's cell.
                    skin.gridCell = skin.itemSlot;
                    skin.gridCellTint = new Color32(20, 42, 60, 235);
                    skin.gridCellBorder = 6f;
                    break;
                }
                case FactionId.Null:
                {
                    string df = DarkFantasy + "Sprites/DarkFantasy/SPR_DarkFantasy_";
                    skin.slotFrame = Load<Sprite>(df + "Frame_Box_Medium_17.png");
                    skin.slotFrameBorder = 15f; skin.slotFrameOutset = 6f;
                    skin.slotFill = new Color32(0x21, 0x39, 0x41, 0xCC); // the pack's own
                    skin.cardFrame = Load<Sprite>(df + "Frame_Box_Large_02.png");
                    skin.cardFrameBorder = 62f; skin.cardFrameOutset = 8f;
                    skin.cardBacking = new Color32(0x16, 0x1A, 0x1A, 0xF4);
                    skin.cardPictureFrame = Load<Sprite>(df + "Frame_Box_Medium_17.png");
                    skin.cardPictureBorder = 18f; skin.cardPictureOutset = 8f;
                    break;
                }
                case FactionId.Covenant:
                {
                    string fw = FantasyWarrior + "Sprites/FantasyWarrior/SPR_FantasyWarrior_";
                    skin.slotFrame = Load<Sprite>(fw + "Frame_Box_04.png"); // steel, with gold corners and a gem in each
                    skin.slotFrameBorder = 18f; skin.slotFrameOutset = 6f;
                    skin.slotFill = new Color32(0x06, 0x2C, 0x48, 0xE6);
                    skin.cardFrame = Load<Sprite>(fw + "Frame_Box_22_Variant_01.png");
                    skin.cardFrameBorder = 28f; skin.cardFrameOutset = 9f;
                    skin.cardBacking = new Color32(0x11, 0x2F, 0x37, 0xF6);
                    skin.cardPictureFrame = Load<Sprite>(fw + "Frame_Box_18_Variant_01.png");
                    skin.cardPictureBorder = 30f; skin.cardPictureOutset = 8f;
                    break;
                }
            }
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssetIfDirty(skin);
        }

        // ---------------------------------------------------------------- the character creator's parts

        private const string FantasyScreens = "Assets/Synty/InterfaceFantasyScreens/";
        public const int CreatorTabCount = 7, TraitCount = 3;

        /// <summary>
        /// Gives a skin asset made before the creator was built per skin the parts of its menu pack that the creator
        /// is made from (the three menu packs are made to one template), and the pictures of its tabs and traits. A
        /// part that is set is left as it is.
        /// </summary>
        public static void EnsureCreatorParts(UiSkin skin)
        {
            string pack, token;
            string[] tabs, traits;
            switch (skin.faction)
            {
                case FactionId.Hegemony:
                    pack = SciFiMenus;
                    token = "SciFiMenus";
                    tabs = new[]
                    {
                        SciFiMenus + "Sprites/Icons_Menu/ICON_SciFiMenus_Menu_Flag_01_Clean.png",
                        SciFiHud + "Sprites/Icons_Inventory/ICON_SciFiSoldier_Inventory_Weapon_01.png",
                        SciFiHud + "Sprites/Icons_Inventory/ICON_SciFiSoldier_Inventory_Armor_01.png",
                        SciFiHud + "Sprites/Icons_Inventory/ICON_SciFiSoldier_Inventory_Helmet_01.png",
                        SciFiMenus + "Sprites/Icons_Menu/ICON_SciFiMenus_Menu_Message_01_Clean.png",
                        SciFiMenus + "Sprites/Icons_Settings/ICON_SciFiMenus_Settings_Color_01_Clean.png",
                        SciFiMenus + "Sprites/Icons_Menu/ICON_SciFiMenus_Menu_Tick_01_Clean.png",
                    };
                    traits = new[]
                    {
                        SciFiHud + "Sprites/Icons_Inventory/ICON_SciFiSoldier_Inventory_Science_01.png",
                        SciFiHud + "Sprites/Icons_Status/ICON_SciFiSoldier_Status_Health_01.png",
                        SciFiHud + "Sprites/Icons_Status/ICON_SciFiSoldier_Status_Armor_01.png",
                    };
                    break;
                case FactionId.Null:
                    pack = DarkFantasyMenus;
                    token = "DarkFantasyMenus";
                    tabs = new[]
                    {
                        DarkFantasyMenus + "Sprites/Icons_DarkFantasyMenus/SPR_DarkFantasyMenus_Icon_Flag_01.png",
                        DarkFantasyMenus + "Sprites/Icons_DarkFantasyMenus/SPR_DarkFantasyMenus_Icon_Swords_01.png",
                        DarkFantasy + "Sprites/Icons_Inventory/ICON_DarkFantasy_Inventory_Armor_01_Clean.png",
                        DarkFantasyMenus + "Sprites/Icons_DarkFantasyMenus/SPR_DarkFantasyMenus_Icon_Helmet_01.png",
                        DarkFantasyMenus + "Sprites/Icons_Menu/ICON_DarkFantasyMenus_Menu_Message_01_Clean.png",
                        DarkFantasyMenus + "Sprites/Icons_Settings/ICON_DarkFantasyMenus_Settings_Color_01_Clean.png",
                        DarkFantasyMenus + "Sprites/Icons_DarkFantasyMenus/SPR_DarkFantasyMenus_Icon_Scroll_01.png",
                    };
                    traits = new[]
                    {
                        DarkFantasy + "Sprites/Icons_Stats/ICON_DarkFantasy_Stat_Mind_01_Clean.png",
                        DarkFantasy + "Sprites/Icons_Stats/ICON_DarkFantasy_Stat_Speed_02_Clean.png",
                        DarkFantasy + "Sprites/Icons_Stats/ICON_DarkFantasy_Stat_Strength_01_Clean.png",
                    };
                    break;
                case FactionId.Covenant:
                    pack = FantasyMenus;
                    token = "FantasyMenus";
                    string character = FantasyScreens + "Sprites/Icons_Character/ICON_FantasyScreens_Character_";
                    tabs = new[]
                    {
                        FantasyMenus + "Sprites/Icons_FantasyMenus/SPR_FantasyMenus_Icon_Flag_01.png",
                        FantasyScreens + "Sprites/Icons_Inventory/ICON_FantasyScreens_Inventory_Swords_01_Underlay.png",
                        character + "Body_01_Underlay.png",
                        character + "Head_01_Underlay.png",
                        FantasyScreens + "Sprites/Icons_Menu/ICON_FantasyScreens_Menu_Message_01_Underlay.png",
                        character + "Markings_01_Underlay.png",
                        FantasyScreens + "Sprites/Icons_Menu/ICON_FantasyScreens_Menu_Done_01_Underlay.png",
                    };
                    traits = new string[0]; // its own creator screen has them
                    break;
                default:
                    return;
            }

            string prefabs = pack + "Prefabs/";
            bool changed = false;
            GameObject Part(GameObject now, string path)
            {
                if (now != null) return now;
                // The Dark Fantasy pack numbers its labels.
                string file = File.Exists($"{prefabs}{path}.prefab") ? $"{prefabs}{path}.prefab" : $"{prefabs}{path}_01.prefab";
                var part = Load<GameObject>(file);
                changed |= part != null;
                return part;
            }
            Sprite[] Pictures(Sprite[] now, string[] paths)
            {
                if (now != null && now.Length == paths.Length && Array.TrueForAll(now, each => each != null)) return now;
                changed = true;
                return Array.ConvertAll(paths, path => Load<Sprite>(path));
            }

            if (skin.faction == FactionId.Covenant && skin.creatorScreen == null)
            {
                skin.creatorScreen = Load<GameObject>(FantasyScreens + "Prefabs/Screens/Screen_FantasyScreens_CharacterCreator_01.prefab");
                changed = true;
            }
            skin.tabStrip = Part(skin.tabStrip, $"Buttons/Menu_{token}_Navigation_Icons_01");
            skin.framedPanel = Part(skin.framedPanel, $"Panels/Panel_{token}_Popup_02");
            skin.iconButton = Part(skin.iconButton, $"Buttons/Menu_{token}_Button_Icon_01");
            skin.sliderRow = Part(skin.sliderRow, $"Settings/Settings_{token}_Item_Slider_01");
            skin.textField = Part(skin.textField, $"Panels/Panel_{token}_Input_01");
            skin.sectionHeader = Part(skin.sectionHeader, $"Settings/Settings_{token}_Section_Header_01");
            skin.headerLabel = Part(skin.headerLabel, $"_CommonComponents/Label_{token}_Header");
            skin.startButton = Part(skin.startButton, $"Buttons/Button_{token}_Start_01");
            skin.footer = Part(skin.footer, $"Buttons/Input_{token}_Footer_01");
            if (skin.faction == FactionId.Null && skin.textFieldFill.a <= 0f)
            {
                skin.textFieldFill = new Color32(0x3A, 0x0E, 0x0C, 0xF8); // owner, 9 Oct 2026: red, not the pack's teal
                changed = true;
            }
            skin.creatorTabIcons = Pictures(skin.creatorTabIcons, tabs);
            skin.traitIcons = Pictures(skin.traitIcons, traits);
            if (changed) EditorUtility.SetDirty(skin);
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
            EnsureDesign(skin);
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
        /// The parts of a window's page that are the pack's own art: the menu row's buttons, the band under a title,
        /// the item cells and the round close button. Each is the pack's frame flattened over its fill in the
        /// faction's dark tone. A part that is there is left exactly as it is.
        /// </summary>
        private static void EnsureDesign(UiSkin skin)
        {
            if (skin.keycap == null)
            {
                // The caps of each pack's own controls page: its cap shape, in the three colours the pack gives it.
                switch (skin.faction)
                {
                    case FactionId.Hegemony:
                        skin.keycap = skin.token;
                        skin.keycapBorder = 9f;
                        skin.keycapTint = new Color32(13, 63, 115, 255);
                        skin.keycapLetter = new Color32(244, 250, 255, 255);
                        skin.keycapHot = new Color32(38, 168, 250, 255);
                        skin.controlsLabel = new Color32(236, 244, 250, 255);
                        break;
                    case FactionId.Null:
                        skin.keycap = Load<Sprite>(DarkFantasy + "Sprites/HUD/SPR_HUD_DarkFantasy_Box_Small_Parchment_01.png");
                        skin.keycapBorder = 9f;
                        skin.keycapTint = new Color32(44, 44, 48, 255);
                        skin.keycapLetter = new Color32(248, 246, 240, 255);
                        skin.keycapHot = new Color32(204, 42, 46, 255);
                        skin.controlsLabel = new Color32(228, 188, 108, 255);
                        break;
                    case FactionId.Covenant:
                        skin.keycap = Load<Sprite>(FantasyWarrior + "Sprites/HUD/SPR_HUD_FantasyWarrior_Box_Small_Parchment_02.png");
                        skin.keycapBorder = 9f;
                        skin.keycapTint = new Color32(42, 58, 76, 255);
                        skin.keycapLetter = new Color32(246, 204, 116, 255);
                        skin.keycapHot = new Color32(120, 150, 186, 255);
                        skin.controlsLabel = new Color32(238, 240, 242, 255);
                        break;
                }
                EditorUtility.SetDirty(skin);
                AssetDatabase.SaveAssetIfDirty(skin);
            }
            if (skin.previewGlow == null)
            {
                switch (skin.faction)
                {
                    case FactionId.Hegemony:
                        skin.previewGlow = Load<Sprite>(SciFiHud + "Sprites/FX/SPR_FX_SciFiSoldier_Glow_01.png");
                        // The Sci-Fi pack has no beams (its diagonal streaks end in a hard edge when stretched): the Dark
                        // Fantasy pack's, which are a white mask, in the Hegemony's accent.
                        skin.previewBeams = Load<Sprite>(DarkFantasy + "Sprites/FX/SPR_FX_DarkFantasy_Beams_01.png");
                        break;
                    case FactionId.Null:
                        skin.previewGlow = Load<Sprite>(DarkFantasy + "Sprites/FX/SPR_FX_DarkFantasy_Glow_01.png");
                        skin.previewBeams = Load<Sprite>(DarkFantasy + "Sprites/FX/SPR_FX_DarkFantasy_Beams_01.png");
                        break;
                    case FactionId.Covenant:
                        skin.previewGlow = Load<Sprite>(FantasyWarrior + "Sprites/FX/SPR_FX_FantasyWarrior_Glow_01.png");
                        skin.previewBeams = Load<Sprite>(FantasyWarrior + "Sprites/FX/SPR_FX_FantasyWarrior_Beams_01.png");
                        break;
                }
                EditorUtility.SetDirty(skin);
                AssetDatabase.SaveAssetIfDirty(skin);
            }
            if (skin.menuButton != null && skin.headerBand != null && skin.cell != null && skin.closeButton != null) return;
            Color.RGBToHSV(skin.panelTone, out float hue, out float saturation, out _);
            Color Tone(float value) => Color.HSVToRGB(hue, Mathf.Clamp01(saturation * 1.2f), value);
            var closeRed = new Color(0.62f, 0.1f, 0.1f);
            string art = $"{FactionHudBuilder.Folder}/{skin.faction}/Art";
            string f = skin.faction.ToString();
            Sprite S(string path) => Load<Sprite>(path);
            switch (skin.faction)
            {
                case FactionId.Hegemony:
                {
                    string menus = SciFiMenus + "Sprites/SciFiMenus/SPR_SciFiMenus_", general = SciFiMenus + "Sprites/General/SPR_SciFiMenus_";
                    skin.menuButton = SpriteRecolourer.Flatten($"MenuButton_{f}", art, S(menus + "Frame_Box_Small_06.png"), S(menus + "Frame_Box_Small_06_Background.png"), Tone(0.2f));
                    skin.menuButtonBorder = 11f;
                    skin.headerBand = SpriteRecolourer.Flatten($"HeaderBand_{f}", art, S(menus + "Button_Frame_04.png"), S(menus + "Button_Frame_04_Background.png"), Tone(0.19f));
                    skin.headerBandBorder = 72f;
                    skin.cell = SpriteRecolourer.Flatten($"Cell_{f}", art, S(menus + "Frame_Box_Small_04.png"), S(menus + "Frame_Box_Small_04_Background.png"), Tone(0.13f));
                    skin.cellBorder = 9f;
                    skin.closeButton = SpriteRecolourer.Flatten($"Close_{f}", art, S(general + "Menu_Box_Small_02.png"), S(general + "Menu_Box_Small_01.png"), closeRed);
                    break;
                }
                case FactionId.Null:
                {
                    string menus = DarkFantasyMenus + "Sprites/DarkFantasyMenus/SPR_DarkFantasyMenus_", hud = DarkFantasy + "Sprites/";
                    skin.menuButton = SpriteRecolourer.Flatten($"MenuButton_{f}", art, S(menus + "Frame_Bar_Medium_07.png"), S(menus + "Frame_Bar_Medium_07_Background.png"), Tone(0.2f));
                    skin.menuButtonBorder = 36f;
                    skin.headerBand = SpriteRecolourer.Flatten($"HeaderBand_{f}", art, S(menus + "Frame_Bar_Medium_06.png"), S(menus + "Frame_Bar_Medium_06_Background.png"), Tone(0.19f));
                    skin.headerBandBorder = 66f;
                    skin.cell = SpriteRecolourer.Flatten($"Cell_{f}", art, S(hud + "DarkFantasy/SPR_DarkFantasy_Frame_Box_Medium_17.png"), S(hud + "HUD/SPR_HUD_DarkFantasy_Box_Small_Parchment_01.png"), Tone(0.13f));
                    skin.cellBorder = 9f;
                    skin.closeButton = SpriteRecolourer.Flatten($"Close_{f}", art, S(hud + "DarkFantasy/SPR_DarkFantasy_Ring_Small_09.png"), S(hud + "DarkFantasy/SPR_DarkFantasy_Ring_Medium_02_Background.png"), closeRed, 0.86f);
                    break;
                }
                case FactionId.Covenant:
                {
                    string menus = FantasyMenus + "Sprites/FantasyMenus/SPR_FantasyMenus_", hud = FantasyWarrior + "Sprites/";
                    skin.menuButton = SpriteRecolourer.Flatten($"MenuButton_{f}", art, S(menus + "Menu_Button_05_Frame.png"), S(menus + "Menu_Button_05_Background.png"), Tone(0.2f));
                    skin.menuButtonBorder = 46f;
                    skin.headerBand = SpriteRecolourer.Flatten($"HeaderBand_{f}", art, S(menus + "Menu_Button_11_Frame.png"), S(menus + "Menu_Button_11_Background.png"), Tone(0.19f));
                    skin.headerBandBorder = 86f;
                    skin.cell = SpriteRecolourer.Flatten($"Cell_{f}", art, S(hud + "FantasyWarrior/SPR_FantasyWarrior_Frame_Box_Small_03.png"), S(hud + "HUD/SPR_HUD_FantasyWarrior_Box_Small_Parchment_02.png"), Tone(0.13f));
                    skin.cellBorder = 5f;
                    skin.closeButton = SpriteRecolourer.Flatten($"Close_{f}", art, S(hud + "FantasyWarrior/SPR_FantasyWarrior_Ring_Small_02_Variant_01.png"), S(hud + "HUD/SPR_HUD_FantasyWarrior_Circle_Medium_01_Clean.png"), closeRed, 0.74f);
                    break;
                }
            }
            EditorUtility.SetDirty(skin);
            AssetDatabase.SaveAssetIfDirty(skin);
        }

        /// <summary>The rules that give a window's page the pack's own parts. They name things through their parents, so they win over the tinted look the converted sheets give them.</summary>
        private static int Design(UiSkin asset, string cssClass, StringBuilder output)
        {
            int count = 0;
            string d = "." + cssClass;
            string Scale(Sprite sprite, float border)
            {
                float widest = Mathf.Max(sprite.border.x, sprite.border.y, sprite.border.z, sprite.border.w);
                return (widest > 0f ? border / widest : 1f).ToString("0.####", CultureInfo.InvariantCulture) + "px";
            }
            void Rule(string selectors, params string[] declarations)
            {
                var parts = selectors.Split(',');
                for (int i = 0; i < parts.Length; i++) parts[i] = $"{d} {parts[i].Trim()}";
                output.AppendLine(string.Join(", ", parts) + " {");
                foreach (string declaration in declarations) output.AppendLine("    " + declaration);
                output.AppendLine("}");
                count++;
            }

            if (asset.menuButton != null)
            {
                // The words stand clear of the bar's end pieces.
                string ends = Mathf.Max(14f, asset.menuButtonBorder * 0.78f).ToString("0.#", CultureInfo.InvariantCulture) + "px";
                Rule("#menu Button", $"background-image: {UrlOf(asset.menuButton)};", "-unity-background-image-tint-color: rgb(222, 222, 222);",
                    $"-unity-slice-scale: {Scale(asset.menuButton, asset.menuButtonBorder)};", "min-width: 112px;", "min-height: 34px;", "height: 34px;", "margin: 0 3px;", $"padding: 0 {ends};");
                Rule("#menu Button:hover", "-unity-background-image-tint-color: rgb(255, 255, 255);");
                Rule("#menu Button:active", "-unity-background-image-tint-color: rgb(170, 170, 170);");
            }
            if (asset.headerBand != null)
            {
                // The title stands on the pack's band, which reaches a little past the page on both sides.
                Rule(".window > .kn-header", $"background-image: {UrlOf(asset.headerBand)};", "-unity-background-image-tint-color: rgb(255, 255, 255);",
                    $"-unity-slice-scale: {Scale(asset.headerBand, asset.headerBandBorder)};", "min-height: 44px;", "align-items: center;",
                    "margin: -4px -8px 10px -8px;", $"padding: 6px 30px 6px {Mathf.Max(30f, asset.headerBandBorder * 0.72f).ToString("0.#", CultureInfo.InvariantCulture)}px;", "border-bottom-width: 0;");
                Rule(".window > .kn-header .heading", "font-size: 19px;");
            }
            if (asset.closeButton != null)
            {
                // A round button on the page's corner, over the frame.
                Rule(".window .close", "position: absolute;", "right: -18px;", "top: -16px;", "width: 34px;", "height: 34px;", "min-width: 34px;", "min-height: 34px;",
                    "margin: 0;", "padding: 0;", "align-items: center;", "justify-content: center;", $"background-image: {UrlOf(asset.closeButton)};",
                    "-unity-background-image-tint-color: rgb(225, 225, 225);", "-unity-slice-scale: 1px;", "-unity-background-scale-mode: scale-to-fit;");
                Rule(".window .close:hover", "-unity-background-image-tint-color: rgb(255, 255, 255);", "scale: 1.1 1.1;");
                Rule(".window .close > .kn-close-glyph", "-unity-background-image-tint-color: rgb(255, 255, 255);", "width: 13px;", "height: 13px;");
            }
            if (asset.keycap != null && asset.keycapTint.a > 0f)
            {
                string Css(Color colour)
                {
                    Color32 c = colour;
                    return $"rgb({c.r}, {c.g}, {c.b})";
                }
                Rule(".key-cap", $"background-image: {UrlOf(asset.keycap)};", $"-unity-background-image-tint-color: {Css(asset.keycapTint)};", $"-unity-slice-scale: {Scale(asset.keycap, asset.keycapBorder)};");
                Rule(".key-cap-text", $"color: {Css(asset.keycapLetter)};");
                Rule(".key-cap-picture", $"-unity-background-image-tint-color: {Css(asset.keycapLetter)};");
                Rule(".settings-key:hover .key-cap, .settings-key.listening .key-cap", $"-unity-background-image-tint-color: {Css(asset.keycapHot)};");
                Rule(".controls-row .settings-row-label", $"color: {Css(asset.controlsLabel)};");
                Rule(".controls-device", $"-unity-background-image-tint-color: {Css(asset.controlsLabel)};");
            }
            if (asset.previewGlow != null) Rule(".preview-glow", $"background-image: {UrlOf(asset.previewGlow)};");
            if (asset.previewBeams != null) Rule(".preview-beams", $"background-image: {UrlOf(asset.previewBeams)};");
            if (asset.cell != null)
            {
                // An equipment slot is the pack's framed slot. A cell of the grid is its plain cell where the skin
                // names one (the packs' grids have no frames), and the framed slot otherwise.
                string cells = asset.gridCell != null ? ".equip-column .equip-slot" : ".inventory-grid .item-cell, .equip-column .equip-slot";
                Rule(cells, $"background-image: {UrlOf(asset.cell)};", "-unity-background-image-tint-color: rgb(214, 214, 214);", $"-unity-slice-scale: {Scale(asset.cell, asset.cellBorder)};");
                Rule(".inventory-grid .item-cell:hover, .equip-column .equip-slot:hover", "-unity-background-image-tint-color: rgb(255, 255, 255);");
                Rule(".inventory-grid .item-cell.drop-valid, .equip-column .equip-slot.drop-valid", "-unity-background-image-tint-color: rgb(170, 255, 220);");
            }
            if (asset.gridCell != null)
            {
                string Tone(Color colour, float lift)
                {
                    Color32 c = Color.Lerp(colour, Color.white, lift);
                    return $"rgba({c.r}, {c.g}, {c.b}, {((Color)c).a.ToString("0.##", CultureInfo.InvariantCulture)})";
                }
                // The empty cell's own colour: a cell with an item in it takes its rarity's ground from the base sheets.
                Rule(".inventory-grid .item-cell", $"background-image: {UrlOf(asset.gridCell)};", $"-unity-slice-scale: {Scale(asset.gridCell, asset.gridCellBorder)};");
                Rule(".inventory-grid .item-cell.item-empty", $"-unity-background-image-tint-color: {Tone(asset.gridCellTint, 0f)};");
                Rule(".inventory-grid .item-cell.item-empty:hover", $"-unity-background-image-tint-color: {Tone(asset.gridCellTint, 0.18f)};");
            }
            string Rgba(Color colour)
            {
                Color32 c = colour;
                return $"rgba({c.r}, {c.g}, {c.b}, {colour.a.ToString("0.##", CultureInfo.InvariantCulture)})";
            }
            if (asset.slotFrame != null)
            {
                // An equipment slot: its fill, and over it the pack's ornate frame reaching out past the slot. The
                // frame takes the rarity of what is worn (the rarity rules tint it) and is grey while the slot is empty.
                const string slot = ".equip-column .equip-slot";
                string reach = (-asset.slotFrameOutset).ToString("0.##", CultureInfo.InvariantCulture) + "px";
                Rule(slot, "background-image: none;", $"background-color: {Rgba(asset.slotFill)};");
                Rule(slot + ":hover", $"background-color: {Rgba(Color.Lerp(asset.slotFill, Color.white, 0.12f))};");
                Rule(slot + " > .item-frame", $"background-image: {UrlOf(asset.slotFrame)};", $"-unity-slice-scale: {Scale(asset.slotFrame, asset.slotFrameBorder)};",
                    $"left: {reach};", $"top: {reach};", $"right: {reach};", $"bottom: {reach};");
                Rule(slot + ".item-empty > .item-frame", "-unity-background-image-tint-color: rgb(132, 132, 132);");
            }
            if (asset.cardPictureFrame != null)
            {
                string reach = (-asset.cardPictureOutset).ToString("0.##", CultureInfo.InvariantCulture) + "px";
                Rule(".card-picture-frame", $"background-image: {UrlOf(asset.cardPictureFrame)};", "-unity-background-image-tint-color: rgb(255, 255, 255);",
                    $"-unity-slice-scale: {Scale(asset.cardPictureFrame, asset.cardPictureBorder)};", $"left: {reach};", $"top: {reach};", $"right: {reach};", $"bottom: {reach};");
            }
            if (asset.cardFrame != null) Rule(".item-tooltip.item-card", $"background-color: {Rgba(asset.cardBacking)};", $"-unity-background-image-tint-color: {Rgba(asset.cardBacking)};");
            if (asset.gridCell != null)
            {
                // In the grid the rarity of an item is the colour of the cell it lies on, as the packs' grids have no
                // outlines: the outline is kept for what is happening to a cell (a pointer over it, a drop).
                const string cell = ".inventory-grid .item-cell";
                // (The grounds themselves are the base sheets' rarity rules, the same in every skin: nothing here names a rarity.)
                Rule($"{cell} > .item-frame", "-unity-background-image-tint-color: rgba(0, 0, 0, 0);");
                Rule($"{cell}:hover > .item-frame", "-unity-background-image-tint-color: rgba(220, 236, 246, 0.6);");
                Rule($"{cell}.drop-valid > .item-frame", "-unity-background-image-tint-color: rgb(140, 255, 220);");
                Rule($"{cell}.drop-hover > .item-frame", "-unity-background-image-tint-color: rgb(255, 214, 92);");
                Rule($"{cell}.reject-flash > .item-frame", "-unity-background-image-tint-color: rgb(230, 70, 80);");
            }
            // The paper doll: what each empty slot is for, and the bars over and under the model.
            for (int i = 0; i < SlotOrder.Length && i < asset.slotGlyphs.Length; i++)
            {
                if (asset.slotGlyphs[i] == null) continue;
                Color32 tint = asset.slotGlyphTint;
                Rule($"#equip-{SlotOrder[i]} > .slot-glyph", $"background-image: {UrlOf(asset.slotGlyphs[i])};",
                    $"-unity-background-image-tint-color: rgba({tint.r}, {tint.g}, {tint.b}, {asset.slotGlyphTint.a.ToString("0.##", CultureInfo.InvariantCulture)});");
            }
            if (asset.dollBarTopLeft != null)
            {
                Color32 bar = asset.dollBarTint;
                string height = asset.dollBarHeight.ToString("0.#", CultureInfo.InvariantCulture) + "px";
                Rule(".doll-bar", $"height: {height};");
                Rule(".doll-bar-half", $"-unity-background-image-tint-color: rgb({bar.r}, {bar.g}, {bar.b});");
                foreach ((string where, Sprite left, Sprite right) in new[] { ("top", asset.dollBarTopLeft, asset.dollBarTopRight), ("foot", asset.dollBarFootLeft, asset.dollBarFootRight) })
                {
                    if (left == null) continue;
                    Rule($".doll-bar--{where} > .doll-bar-left", $"background-image: {UrlOf(left)};");
                    // A pack with one bar for both sides: the right half is the left one seen in a mirror.
                    if (right != null) Rule($".doll-bar--{where} > .doll-bar-right", $"background-image: {UrlOf(right)};");
                    else Rule($".doll-bar--{where} > .doll-bar-right", $"background-image: {UrlOf(left)};", "scale: -1 1;");
                }
            }
            return count;
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
                // The chat's eight tabs are bare words (the one being read has a line under it): framed, they crowd the log.
                output.AppendLine($".{cssClass} .chat-tabs .chat-tab {{");
                output.AppendLine("    background-image: none;");
                output.AppendLine("}");
                count += 5;
            }
            count += FrameRule(output, cssClass, ".panel", asset.panelFrame, asset.panelFrameBorder, asset.panelFrameOutset);
            count += FrameRule(output, cssClass, ".window", asset.windowFrame, asset.windowFrameBorder, asset.windowFrameOutset);
            // The item card wears a frame of its own, not the small panels'.
            count += FrameRule(output, cssClass, ".item-card", asset.cardFrame, asset.cardFrameBorder, asset.cardFrameOutset);
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
