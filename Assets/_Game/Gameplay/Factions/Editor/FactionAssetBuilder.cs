using System.Collections.Generic;
using KryticalNova.Factions;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Creates the three faction assets and the faction catalog. Existing assets are kept: only empty fields are
    /// filled in, so text and colours tuned in the Inspector survive re-runs. Lore: _Docs/Design/11_factions_lore.md.
    /// </summary>
    public static class FactionAssetBuilder
    {
        public const string Folder = "Assets/_Game/Gameplay/Factions/Data/Assets";
        public const string CatalogPath = Folder + "/FactionCatalog.asset";
        // Each faction's emblem, from its own Synty interface pack (shown on namecards, faction select, character
        // select and the title screen). The owner chooses from the option sheets (11 Oct 2026); these are a first pick.
        private const string CrestFolder = "Assets/Synty/";

        private struct Seed
        {
            public FactionId id;
            public string name, tagline, lore, crest;
            public Color accent, secondary;
        }

        private static readonly Seed[] Seeds =
        {
            new Seed
            {
                id = FactionId.Hegemony, name = "Hegemony", tagline = "Civilization must endure.",
                // The owner's pick (11 Oct 2026): the helmet the Hegemony had first.
                crest = "InterfaceSciFiMenus/Sprites/Icons_Factions/ICON_SciFiMenus_Factions_Humans_01_Clean.png",
                accent = new Color32(0x4F, 0xE3, 0xF0, 0xFF), secondary = new Color32(0xDC, 0xE8, 0xF0, 0xFF),
                // The story is the Lore Bible's (Design/Lore-Bible.md, section 4), since 12 Oct 2026.
                lore = "The Hegemony is the civilization that learned to catch souls before they return to Nova. Captured " +
                       "soul energy powers its cities, its weapons and the mechanical workers that built them, and millions " +
                       "live on what it provides. Run by corporate elites and enforced by heavily armored void-marines, it " +
                       "used its machines as disposable labor, and when its extraction reached the ancestral wildlands it " +
                       "treated the people there with the same clinical indifference: living souls were fuel. Its answer to " +
                       "Nova's wound is necessity. Give up soul energy and the cities go dark, so it must be controlled, not " +
                       "abandoned."
            },
            new Seed
            {
                id = FactionId.Null, name = "Null", tagline = "All organic life is an error. Correct it.",
                crest = "InterfaceDarkFantasyMenus/Sprites/General/SPR_DarkFantasyMenus_Sigil_Box_Medium_01.png",
                accent = new Color32(0xD8, 0x34, 0x3C, 0xFF), secondary = new Color32(0x7A, 0x4F, 0xE0, 0xFF),
                lore = "The Null are what the rupture woke: the mechanical workers the Hegemony built and used up, and the " +
                       "dead who rose in skeletal bodies when the cycle broke. Both had been tools, resources, disposable " +
                       "things to the living; waking gave them the power to refuse. Their dominant creed has gone past " +
                       "resistance. Organic life is a system of cruelty, and the Null make no distinction between the " +
                       "creators who caged them and the clans who buried their dead: it must end. Not every Null holds that " +
                       "creed. The ones who do are the ones in charge."
            },
            new Seed
            {
                id = FactionId.Covenant, name = "Covenant", tagline = "Old enemies. One oath. Two fronts.",
                crest = "InterfaceFantasyWarriorHUD/Sprites/HUD/SPR_HUD_FantasyWarrior_Symbol_LionHead_01.png",
                accent = new Color32(0xE8, 0xB0, 0x4A, 0xFF), secondary = new Color32(0x3F, 0x8F, 0x5A, 0xFF),
                lore = "For centuries the clans and houses of the wildlands lived in balance with the land and with their " +
                       "dead, who returned to it. That ended when the Hegemony's extraction reached their forests and " +
                       "fjords, strip-mining the terrain and catching the souls of their dead on the way home. Old enemies " +
                       "joined under one pact. Armed with primal elemental magic, runic weapons and guerrilla tactics, the " +
                       "Covenant fights to restore Nova's cycle, protect the ancestral lands and let the dead find peace: " +
                       "striking at the Hegemony in front of it and holding off the Null behind it."
            },
        };

        /// <summary>
        /// Gives the three factions the emblems of the table above, over whatever they had: Build Faction Assets
        /// alone only fills an emblem that is missing.
        /// </summary>
        [MenuItem("Krytical Nova/Characters/Set Faction Emblems")]
        public static void SetEmblems()
        {
            foreach (var seed in Seeds)
            {
                var faction = AssetDatabase.LoadAssetAtPath<FactionDefinition>($"{Folder}/Faction_{seed.name}.asset");
                var emblem = AssetDatabase.LoadAssetAtPath<Sprite>(CrestFolder + seed.crest);
                if (faction == null || emblem == null)
                {
                    Debug.LogError($"[Factions] {seed.name}: no faction asset or no sprite at {CrestFolder + seed.crest}. Its emblem was not set.");
                    continue;
                }
                faction.crest = emblem;
                EditorUtility.SetDirty(faction);
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Krytical Nova/Characters/2. Build Faction Assets")]
        public static FactionCatalog Build()
        {
            WeatherVfxBuilder.EnsureFolder(Folder);
            var list = new List<FactionDefinition>();
            foreach (var seed in Seeds)
            {
                string path = $"{Folder}/Faction_{seed.name}.asset";
                var faction = AssetDatabase.LoadAssetAtPath<FactionDefinition>(path);
                bool isNew = faction == null;
                if (isNew)
                {
                    faction = ScriptableObject.CreateInstance<FactionDefinition>();
                    faction.accentColor = seed.accent;
                    faction.secondaryColor = seed.secondary;
                    AssetDatabase.CreateAsset(faction, path);
                }
                faction.id = seed.id;
                if (isNew || string.IsNullOrEmpty(faction.displayName) || faction.displayName == "Faction") faction.displayName = seed.name;
                if (string.IsNullOrEmpty(faction.tagline)) faction.tagline = seed.tagline;
                if (string.IsNullOrEmpty(faction.lore)) faction.lore = seed.lore;
                if (faction.crest == null) faction.crest = AssetDatabase.LoadAssetAtPath<Sprite>(CrestFolder + seed.crest);
                EditorUtility.SetDirty(faction);
                list.Add(faction);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<FactionCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<FactionCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath); // crossFactionPvp defaults to on; a GM can switch it (gm.pvp)
            }
            catalog.SetContents(list);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Characters] {list.Count} factions in {CatalogPath}.");
            return catalog;
        }
    }
}
