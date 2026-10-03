using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes an icon for every combat, buff and movement skill and for the pills, then assigns it. Shapes come from one
    /// family of Synty interface glyphs (weapon silhouettes for attacks, status and menu symbols for buffs) and are
    /// redrawn in the comic style by <see cref="ComicIconBaker"/>, so they match the rendered gear icons. One colour
    /// per kind of skill, from the Colour Bible, so the hotbar reads at a glance. Re-runnable: icons are re-baked and
    /// re-assigned from the table below; an icon assigned by hand from outside the baked folders is left alone.
    /// </summary>
    public static class SkillIconBuilder
    {
        public const string SkillIconFolder = "Assets/_Game/Gameplay/Progression/Data/Assets/Icons";
        private const string ItemIconFolder = "Assets/_Game/Gameplay/Items/Data/Assets/Icons";
        private const string SkillRoot = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills";
        private const string PillFolder = "Assets/_Game/Gameplay/Items/Data/Assets/Consumables";

        private const string Hud = "Assets/Synty/InterfaceSciFiSoldierHUD/Sprites/";
        private const string Menus = "Assets/Synty/InterfaceSciFiMenus/Sprites/Icons_Menu/";

        private static string Weapon(string name) => $"{Hud}Icons_Weapons/ICON_SM_Wep_{name}_Clean.png";
        private static string Map(string name) => $"{Hud}Icons_Map/ICON_SciFiSoldier_Map_{name}.png";
        private static string Status(string name) => $"{Hud}Icons_Status/ICON_SciFiSoldier_Status_{name}.png";
        private static string Inventory(string name) => $"{Hud}Icons_Inventory/ICON_SciFiSoldier_Inventory_{name}.png";
        private static string Menu(string name) => $"{Menus}ICON_SciFiMenus_Menu_{name}_Clean.png";

        // Colour Bible (Rendering-Bible.md §6b).
        private static readonly Color Coral = new Color32(0xFF, 0x9E, 0x66, 0xFF);   // blade attacks
        private static readonly Color Cyan = new Color32(0x4F, 0xE3, 0xF0, 0xFF);    // blade stances
        private static readonly Color Yellow = new Color32(0xFF, 0xC9, 0x3C, 0xFF);  // heavenly buffs
        private static readonly Color Magenta = new Color32(0xE0, 0x4F, 0xB8, 0xFF); // rage
        private static readonly Color Horizon = new Color32(0xAD, 0xE0, 0xF2, 0xFF); // movement

        /// <summary>Weapon silhouettes are drawn lying flat; this stands them on the diagonal like the gear icons.</summary>
        private const float BladeAngle = 45f;

        private readonly struct Icon
        {
            public readonly string Source;
            public readonly Color Fill;
            public readonly float Rotation;
            public Icon(string source, Color fill, float rotation = 0f) { Source = source; Fill = fill; Rotation = rotation; }
        }

        private static readonly Dictionary<string, Icon> Skills = new Dictionary<string, Icon>
        {
            // Attacks: one blade each, heavier blades for the heavier skills.
            ["FuryStrike"] = new Icon(Weapon("Sword_Double_01_SciFiSpace"), Coral, BladeAngle),
            ["HaeseoStroke"] = new Icon(Weapon("Sword_01_SciFiCyberCity"), Coral, BladeAngle),
            ["GentlemansPen"] = new Icon(Weapon("Dagger_01_SciFiWorlds"), Coral, BladeAngle),
            ["DevilsStrike"] = new Icon(Weapon("Sword_03_SciFiWorlds"), Coral, BladeAngle),
            ["FlashOfRage"] = new Icon(Weapon("Sword_05_SciFiCyberCity"), Coral, BladeAngle),
            ["SunraysStroke"] = new Icon(Weapon("Sword_02_SciFiWorlds"), Coral, BladeAngle),
            ["PhantomThunder"] = new Icon(Weapon("Sword_07_SciFiCyberCity"), Coral, BladeAngle),
            ["ThunderousDragon"] = new Icon(Weapon("Sword_08_SciFiWorlds"), Coral, BladeAngle),

            // Blade stances.
            ["AzureWall"] = new Icon(Inventory("Armor_01"), Cyan),          // elemental defence: armour
            ["LightningFlash"] = new Icon(Map("Lightning_01"), Cyan),        // attack power: the bolt
            ["SpiritualRage"] = new Icon(Map("Fire_01"), Magenta),

            // Heavenly buffs.
            ["HeavenlySerenity"] = new Icon(Status("Bleeding_01"), Yellow),  // chi recovery: a drop of chi
            ["HeavenlyShield"] = new Icon(Status("Armor_01"), Yellow),       // absorb shield
            ["HeavenlyBlow"] = new Icon(Map("Target_01"), Yellow),           // critical rate
            ["GarudasPrayer"] = new Icon(Menu("Star_01"), Yellow),           // luck
            ["HeavenlyRepulsion"] = new Icon(Menu("Refresh_01"), Yellow),    // reflect: the hit turned back
            ["HeavenlyDestruction"] = new Icon(Map("Skull_01"), Yellow),     // shield shatter
            ["ReverseFlow"] = new Icon(Menu("DNA_01"), Yellow),              // stun resistance
        };

        private static readonly Dictionary<MovementSkillId, Icon> Movement = new Dictionary<MovementSkillId, Icon>
        {
            [MovementSkillId.Run] = new Icon(Menu("ArrowUp_02"), Horizon, -90f),
            [MovementSkillId.Jump] = new Icon(Menu("ArrowUp_01"), Horizon),
        };

        /// <summary>Pills by asset name: health reads coral, chi cyan, the dual pill magenta.</summary>
        private static readonly Dictionary<string, Icon> Pills = new Dictionary<string, Icon>
        {
            ["Pill_Vitality"] = new Icon(Inventory("Healing_01"), Coral),
            ["Pill_Chi"] = new Icon(Inventory("Chemistry_01"), Cyan),
            ["Pill_DualHarmony"] = new Icon(Inventory("Herbs_01"), Magenta),
        };

        [MenuItem("Krytical Nova/Progression/Build Skill Icons")]
        public static void Build()
        {
            WeatherVfxBuilder.EnsureFolder(SkillIconFolder);
            int baked = 0, missing = 0;
            try
            {
                foreach (string guid in AssetDatabase.FindAssets("t:SkillDefinitionSO", new[] { SkillRoot }))
                {
                    var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                    EditorUtility.DisplayProgressBar("Baking skill icons", skill.displayName, 0.5f);
                    if (!Skills.TryGetValue(skill.skillId, out Icon icon)) { missing++; Debug.LogWarning($"[Icons] No icon mapped for skill '{skill.skillId}'."); continue; }
                    if (!IsOurs(skill.icon, SkillIconFolder)) continue;
                    skill.icon = Bake(icon, $"{SkillIconFolder}/{skill.skillId}.png");
                    EditorUtility.SetDirty(skill);
                    baked++;
                }

                foreach (string guid in AssetDatabase.FindAssets("t:MovementSkillData", new[] { SkillRoot }))
                {
                    var skill = AssetDatabase.LoadAssetAtPath<MovementSkillData>(AssetDatabase.GUIDToAssetPath(guid));
                    if (!Movement.TryGetValue(skill.skillId, out Icon icon) || !IsOurs(skill.icon, SkillIconFolder)) continue;
                    skill.icon = Bake(icon, $"{SkillIconFolder}/{skill.skillId}.png");
                    EditorUtility.SetDirty(skill);
                    baked++;
                }

                foreach (var pair in Pills)
                {
                    var pill = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{PillFolder}/{pair.Key}.asset");
                    if (pill == null || !IsOurs(pill.icon, ItemIconFolder)) continue;
                    pill.icon = Bake(pair.Value, $"{ItemIconFolder}/{pair.Key}.png");
                    EditorUtility.SetDirty(pill);
                    baked++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Icons] Baked {baked} comic icons into {SkillIconFolder} and {ItemIconFolder}" +
                      (missing > 0 ? $"; {missing} skills have no icon mapped." : "."));
        }

        private static Sprite Bake(Icon icon, string outputPath) =>
            ComicIconBaker.Bake(icon.Source, icon.Fill, outputPath, icon.Rotation);

        /// <summary>True when the slot is empty or holds one of our own baked icons, so a re-bake may replace it.</summary>
        private static bool IsOurs(Sprite current, string folder) =>
            current == null || AssetDatabase.GetAssetPath(current).StartsWith(folder + "/");
    }
}
