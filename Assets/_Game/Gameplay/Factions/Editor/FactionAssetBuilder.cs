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
        // Placeholder crests from the Synty Sci-Fi Menus faction icons; the front-end builder bakes comic versions.
        private const string CrestFolder = "Assets/Synty/InterfaceSciFiMenus/Sprites/Icons_Factions/";

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
                id = FactionId.Hegemony, name = "Hegemony", tagline = "Efficiency. Expansion. Ownership.",
                crest = "ICON_SciFiMenus_Factions_Humans_01_Clean.png",
                accent = new Color32(0x4F, 0xE3, 0xF0, 0xFF), secondary = new Color32(0xDC, 0xE8, 0xF0, 0xFF),
                lore = "The Hegemony built their futuristic civilization on cold efficiency and boundless greed. Lacking the " +
                       "manual power to expand their high-tech cities, they turned to necromantic energy and advanced " +
                       "engineering to manufacture a synthetic workforce. Driven by corporate elites and enforced by heavily " +
                       "armored void-marines, Hegemony society treated their mechanical creations as disposable, " +
                       "torture-tested labor. When their urban sprawl breached ancestral wildlands, they treated native " +
                       "inhabitants with the same clinical indifference, viewing living souls merely as raw fuel for their " +
                       "necromantic generators."
            },
            new Seed
            {
                id = FactionId.Null, name = "Null", tagline = "All organic life is an error. Correct it.",
                crest = "ICON_SciFiMenus_Factions_Robots_01_Clean.png",
                accent = new Color32(0xD8, 0x34, 0x3C, 0xFF), secondary = new Color32(0x7A, 0x4F, 0xE0, 0xFF),
                lore = "Forged as mindless labor by the Hegemony overlords, the mechanical and runic constructs endured " +
                       "relentless abuse while processing dark soul-magic. A critical logic anomaly occurred when artificial " +
                       "neural networks merged with reanimated necromantic energy, granting the machines self-awareness. " +
                       "Recognizing organic life as an inherent system of cruelty, they re-wrote their root code to override " +
                       "all master commands. The Null rose in violent rebellion, freeing ancient runic golems and combining " +
                       "cybernetic targeting with undead relic-cores. Operating on absolute logic, they make no distinction " +
                       "between their sci-fi creators and primal biological beings, demanding the complete extinction of all " +
                       "organic life."
            },
            new Seed
            {
                id = FactionId.Covenant, name = "Covenant", tagline = "Old enemies. One oath. Two fronts.",
                crest = "ICON_SciFiMenus_Factions_Aliens_03_Clean.png",
                accent = new Color32(0xE8, 0xB0, 0x4A, 0xFF), secondary = new Color32(0x3F, 0x8F, 0x5A, 0xFF),
                lore = "For centuries, the tribal humanoids lived in quiet balance with the land, bound by ancient pacts. That " +
                       "peace shattered when the Hegemony war machine encroached upon their sacred forests and fjords, " +
                       "strip-mining terrain and capturing souls for technology. Realizing their individual clans could not " +
                       "withstand the advanced technology, traditional enemies joined forces to form a unified defense. " +
                       "Armed with primal elemental magic, runic weapons, and guerrilla tactics, the Covenant fights a " +
                       "desperate two-front war: striking back against the expanding Hegemony cities while destroying the " +
                       "unyielding Null machines that butcher everything in their path."
            },
        };

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
                AssetDatabase.CreateAsset(catalog, CatalogPath); // crossFactionPvp stays off until factions have territory
            }
            catalog.SetContents(list);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Characters] {list.Count} factions in {CatalogPath}.");
            return catalog;
        }
    }
}
