using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Social;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Makes the Crew symbol library from the table below: for each faction, the pictures of its own Synty interface
    /// pack that a Crew's symbol number stands for (menu Krytical Nova ▸ UI ▸ Build Crew Symbols). The pictures are
    /// read where the packs keep them; nothing under Assets/Synty is changed. Every faction has the same number of
    /// them, and that number is written to Crew Settings, which is what the server holds a Crew's choice to.
    /// <para>Which pictures: the owner chooses from the option sheets (11 Oct 2026); until then these are a first
    /// pick. To change one, change its line and run the menu again. A faction's own emblem
    /// (FactionAssetBuilder) is never one of them: a Crew cannot wear its faction's mark as its own.</para>
    /// </summary>
    public static class CrewSymbolBuilder
    {
        public const string LibraryPath = "Assets/_Game/Gameplay/Social/Data/Assets/CrewSymbolLibrary.asset";
        private const string SettingsPath = "Assets/_Game/Gameplay/Social/Data/Assets/CrewSettings.asset";

        // Every picture is white, so a Crew's Leader can give it a colour (owner, 11 Oct 2026). A faction's own
        // emblem is never in its list (the Hegemony's helmet Humans_01, the Covenant's lion head).
        private const string SciFiFactions = "Assets/Synty/InterfaceSciFiMenus/Sprites/Icons_Factions/ICON_SciFiMenus_Factions_";
        private const string SciFiSoldier = "Assets/Synty/InterfaceSciFiSoldierHUD/Sprites/";
        private const string DarkFlat = "Assets/Synty/InterfaceDarkFantasyMenus/Sprites/Icons_DarkFantasyMenus_Flat/ICON_DarkFantasyMenus_";
        private const string DarkGeneral = "Assets/Synty/InterfaceDarkFantasyMenus/Sprites/General/SPR_DarkFantasyMenus_";
        private const string Warrior = "Assets/Synty/InterfaceFantasyWarriorHUD/Sprites/";

        private static readonly (FactionId faction, string[] pictures)[] Sets =
        {
            (FactionId.Hegemony, new[]
            {
                SciFiFactions + "Humans_02_Clean.png", SciFiFactions + "Humans_03_Clean.png", SciFiFactions + "Humans_04_Clean.png",
                SciFiFactions + "Aliens_01_Clean.png", SciFiFactions + "Aliens_05_Clean.png", SciFiFactions + "Aliens_06_Clean.png",
                SciFiFactions + "Robots_05_Clean.png", SciFiSoldier + "Icons_Map/ICON_SciFiSoldier_Map_Ship_01.png",
                SciFiSoldier + "Icons_Map/ICON_SciFiSoldier_Map_Biohazard_01.png", SciFiSoldier + "Icons_Map/ICON_SciFiSoldier_Map_Target_01.png",
                SciFiSoldier + "Icons_Map/ICON_SciFiSoldier_Map_Radiation_01.png", SciFiSoldier + "Icons_Inventory/ICON_SciFiSoldier_Inventory_Science_01.png",
            }),
            (FactionId.Null, new[]
            {
                DarkFlat + "Currency_Rune_01_Clean.png", DarkFlat + "Eye_01_Clean.png", DarkFlat + "Eye_02_Clean.png", DarkFlat + "Hand_01_Clean.png",
                DarkFlat + "Sigil_01_Clean.png", DarkFlat + "Sigil_03_Clean.png", DarkFlat + "Skull_02_Clean.png", DarkFlat + "Skull_03_Clean.png",
                DarkFlat + "Skull_04_Clean.png", DarkFlat + "Spell_01_Clean.png", DarkFlat + "Sigil_05_Clean.png", DarkGeneral + "Sigil_Triangle_Medium_01.png",
            }),
            (FactionId.Covenant, new[]
            {
                Warrior + "HUD/SPR_HUD_FantasyWarrior_Symbol_FleurDeLis_01.png", Warrior + "Icons_Map/ICON_FantasyWarrior_Map_Dragon_01_Clean.png",
                Warrior + "Icons_Map/ICON_FantasyWarrior_Map_Horse_01_Clean.png", Warrior + "Icons_Map/ICON_FantasyWarrior_Map_Home_01_Clean.png",
                Warrior + "Icons_Inventory/ICON_FantasyWarrior_Inventory_Helmets_01_Clean.png", Warrior + "Icons_Stats/ICON_FantasyWarrior_Stat_Speed_02_Clean.png",
                Warrior + "Icons_Inventory/ICON_FantasyWarrior_Inventory_Swords_01_Clean.png", Warrior + "Icons_Inventory/ICON_FantasyWarrior_Inventory_Axes_01_Clean.png",
                Warrior + "Icons_Stats/ICON_FantasyWarrior_Stat_Strength_02_Clean.png", Warrior + "Icons_Elements/ICON_FantasyWarrior_Element_Air_01_Clean.png",
                Warrior + "Icons_Map/ICON_FantasyWarrior_Map_Monster_01_Clean.png", Warrior + "Icons_Elements/ICON_FantasyWarrior_Element_Air_02_Clean.png",
            }),
        };

        [MenuItem("Krytical Nova/UI/Build Crew Symbols")]
        public static void Build()
        {
            var library = AssetDatabase.LoadAssetAtPath<CrewSymbolLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<CrewSymbolLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            int count = int.MaxValue;
            var sets = new List<CrewSymbolLibrary.FactionSymbols>();
            foreach ((FactionId faction, string[] pictures) in Sets)
            {
                var symbols = new List<Sprite>();
                foreach (string path in pictures)
                {
                    var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    if (sprite == null) Debug.LogError($"[Crew] {faction}: no sprite at {path}. That symbol is left out.");
                    else symbols.Add(sprite);
                }
                sets.Add(new CrewSymbolLibrary.FactionSymbols { faction = faction, symbols = symbols.ToArray() });
                count = Mathf.Min(count, symbols.Count);
            }
            library.sets = sets.ToArray();
            EditorUtility.SetDirty(library);

            var settings = AssetDatabase.LoadAssetAtPath<CrewSettings>(SettingsPath);
            if (settings != null)
            {
                settings.symbols = library;
                // What every faction can show: a Crew's number never points past a faction's last picture.
                if (count > 0 && count != int.MaxValue) settings.symbolCount = count;
                EditorUtility.SetDirty(settings);
            }
            else Debug.LogError($"[Crew] No Crew Settings at {SettingsPath}: the library was made but nothing points to it.");

            AssetDatabase.SaveAssets();
            Debug.Log($"[Crew] Crew symbols: {sets.Count} factions, {count} symbols each, in {LibraryPath}.");
        }
    }
}
