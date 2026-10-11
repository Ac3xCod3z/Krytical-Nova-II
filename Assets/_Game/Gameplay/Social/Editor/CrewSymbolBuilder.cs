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

        private const string SciFiCrests = "Assets/Synty/InterfaceSciFiMenus/Sprites/Crests/SPR_SciFiMenus_Crest_";
        private const string DarkFlat = "Assets/Synty/InterfaceDarkFantasyMenus/Sprites/Icons_DarkFantasyMenus_Flat/ICON_DarkFantasyMenus_";
        private const string DarkGeneral = "Assets/Synty/InterfaceDarkFantasyMenus/Sprites/General/SPR_DarkFantasyMenus_";
        private const string FantasyCrests = "Assets/Synty/InterfaceFantasyMenus/Sprites/Crests/SPR_FantasyMenus_Crest_";
        private const string WarriorHud = "Assets/Synty/InterfaceFantasyWarriorHUD/Sprites/HUD/SPR_HUD_FantasyWarrior_";

        private static readonly (FactionId faction, string[] pictures)[] Sets =
        {
            (FactionId.Hegemony, new[]
            {
                SciFiCrests + "Symbol_01.png", SciFiCrests + "Symbol_02.png", SciFiCrests + "Symbol_04.png", SciFiCrests + "Symbol_07.png",
                SciFiCrests + "Symbol_19.png", SciFiCrests + "Symbol_11.png", SciFiCrests + "Symbol_12.png", SciFiCrests + "Symbol_14.png",
                SciFiCrests + "Symbol_17.png", SciFiCrests + "Symbol_20.png", SciFiCrests + "Symbol_23.png", SciFiCrests + "Symbol_24.png",
            }),
            (FactionId.Null, new[]
            {
                DarkFlat + "Currency_Rune_01_Clean.png", DarkFlat + "Eye_01_Clean.png", DarkFlat + "Eye_02_Clean.png", DarkFlat + "Hand_01_Clean.png",
                DarkFlat + "Sigil_01_Clean.png", DarkFlat + "Sigil_03_Clean.png", DarkFlat + "Skull_02_Clean.png", DarkFlat + "Skull_03_Clean.png",
                DarkFlat + "Skull_04_Clean.png", DarkFlat + "Spell_01_Clean.png", DarkFlat + "Sigil_05_Clean.png", DarkGeneral + "Sigil_Triangle_Medium_01.png",
            }),
            (FactionId.Covenant, new[]
            {
                FantasyCrests + "Greeble_Dragonhead_01_Gold.png", FantasyCrests + "Greeble_Lionhead_01_Gold.png", FantasyCrests + "Greeble_Sword_01.png",
                FantasyCrests + "Greeble_SwordsCrossed_01.png", FantasyCrests + "Greeble_SwordsCrossed_03.png", FantasyCrests + "Shield_03.png",
                FantasyCrests + "Shield_06.png", FantasyCrests + "Shield_07.png", FantasyCrests + "Wings_01_Gold.png", FantasyCrests + "Wings_02_Gold.png",
                WarriorHud + "Symbol_FleurDeLis_01.png", FantasyCrests + "Greeble_Dragonhead_02_Gold.png",
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
