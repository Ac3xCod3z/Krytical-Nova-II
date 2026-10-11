using System.Collections.Generic;
using KryticalNova.Crafting;
using KryticalNova.Hostiles;
using KryticalNova.Items;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds what the Fabricator knows from the tables below: the recipes (Alloys from the grade under them, a
    /// Chroma Cell from its four colours, a Dossier from its four Pages), which skill each Dossier teaches, and the
    /// rows of the Hostile drop table that hand the materials out. Run again at any time: a recipe, or a drop
    /// chance, that was tuned on its asset is kept, and only what is missing is added.
    /// The drop chances and the Dossier level are placeholders (11 Oct 2026).
    /// </summary>
    public static class CraftingBuilder
    {
        public const string SettingsPath = "Assets/_Game/Gameplay/Crafting/Data/Assets/CraftingSettings.asset";
        private const string SkillFolder = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/SelfBuffSkills/";
        private const string HostileSettingsPath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/HostileSettings.asset";
        private const string CatalogPath = "Assets/_Game/Gameplay/Items/Data/Assets/ItemCatalog.asset";

        public const string GroupAlloys = "ALLOYS";
        public const string GroupColour = "COLOUR";
        public const string GroupDossiers = "DOSSIERS";
        /// <summary>The level a character needs to bind a Dossier, and to read one (Twelve Sky 2: level 80).</summary>
        private const int DossierLevel = 80;
        /// <summary>Pages and Dossiers drop from Hostiles of this level and above.</summary>
        private const int DossierDropLevel = 70;

        private static readonly (ushort Dossier, string SkillAsset)[] Dossiers =
        {
            (CraftItemIds.BacklashDossier, "Skill_HeavenlyRepulsion"),
            (CraftItemIds.IronNerveDossier, "Skill_ReverseFlow"),
            (CraftItemIds.ShatterpointDossier, "Skill_HeavenlyDestruction"),
        };

        [MenuItem("Krytical Nova/Items/11. Build Crafting Recipes")]
        public static void Build()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            if (catalog == null) { Debug.LogError($"[Crafting] No item catalog at {CatalogPath}."); return; }

            WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(SettingsPath).Replace('\\', '/'));
            var settings = AssetDatabase.LoadAssetAtPath<CraftingSettings>(SettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<CraftingSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
            }

            int added = 0;
            // Four of one grade make one of the next. Trinity Platinum is not made this way.
            added += Add(settings, 1, GroupAlloys, GearMaterialIds.Iridion, 0, (GearMaterialIds.Alumite, 4));
            added += Add(settings, 2, GroupAlloys, GearMaterialIds.Orichalc, 0, (GearMaterialIds.Iridion, 4));
            added += Add(settings, 3, GroupAlloys, GearMaterialIds.Eversteel, 0, (GearMaterialIds.Orichalc, 4));
            added += Add(settings, 10, GroupColour, CraftItemIds.ChromaCell, 0,
                (CraftItemIds.CyanCell, 1), (CraftItemIds.MagentaCell, 1), (CraftItemIds.YellowCell, 1), (CraftItemIds.BlackCell, 1));
            // The weapon's dye is made of the same four (owner, 11 Oct 2026).
            added += Add(settings, 11, GroupColour, CraftItemIds.PrismCell, 0,
                (CraftItemIds.CyanCell, 1), (CraftItemIds.MagentaCell, 1), (CraftItemIds.YellowCell, 1), (CraftItemIds.BlackCell, 1));
            for (int i = 0; i < Dossiers.Length; i++)
            {
                ushort dossier = Dossiers[i].Dossier;
                added += Add(settings, (ushort)(20 + i), GroupDossiers, dossier, DossierLevel,
                    (CraftItemIds.PageOf(dossier, 1), 1), (CraftItemIds.PageOf(dossier, 2), 1),
                    (CraftItemIds.PageOf(dossier, 3), 1), (CraftItemIds.PageOf(dossier, 4), 1));
            }
            EditorUtility.SetDirty(settings);

            int linked = LinkSkills(catalog);
            int drops = AddDrops(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Crafting] {settings.recipes.Count} recipe(s) ({added} new), {linked} Dossier skill(s) linked, {drops} drop row(s) added. " +
                      "Chances and levels are placeholders: tune them on the assets.");
        }

        private static int Add(CraftingSettings settings, ushort id, string group, ushort result, int level, params (ushort Item, int Count)[] uses)
        {
            if (settings.TryGet(id, out _)) return 0; // it may have been tuned
            var ingredients = new CraftIngredient[uses.Length];
            for (int i = 0; i < uses.Length; i++) ingredients[i] = new CraftIngredient(uses[i].Item, uses[i].Count);
            settings.recipes.RemoveAll(recipe => recipe.id == id);
            settings.recipes.Add(new CraftRecipe { id = id, group = group, resultItemId = result, resultCount = 1, ingredients = ingredients, requiredLevel = level });
            return 1;
        }

        /// <summary>Each Dossier skill names the Dossier that unlocks it, and lists its Pages by the names players see.</summary>
        private static int LinkSkills(ItemCatalog catalog)
        {
            int linked = 0;
            foreach ((ushort dossier, string asset) in Dossiers)
            {
                var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>($"{SkillFolder}{asset}.asset");
                ItemDefinition item = catalog.Get(dossier);
                if (skill == null || item == null) { Debug.LogError($"[Crafting] {asset} or its Dossier ({dossier}) is missing: run Items > 5. Generate Utility Items first."); continue; }
                var pages = new string[CraftItemIds.PagesPerDossier];
                for (int page = 1; page <= pages.Length; page++)
                {
                    ItemDefinition pageItem = catalog.Get(CraftItemIds.PageOf(dossier, page));
                    pages[page - 1] = pageItem != null ? pageItem.displayName : $"Page {page}";
                }
                skill.unlockItemId = dossier;
                skill.requiredCraftingArchives = pages;
                skill.acquisitionNote = $"{item.displayName}: a rare drop, or bound from its four Pages by a Fabricator";
                EditorUtility.SetDirty(skill);
                linked++;
            }
            return linked;
        }

        /// <summary>The Hostile drop table hands out the colours, the Pages and, rarely, what they make.</summary>
        private static int AddDrops(ItemCatalog catalog)
        {
            var hostiles = AssetDatabase.LoadAssetAtPath<HostileSettings>(HostileSettingsPath);
            if (hostiles == null) { Debug.LogWarning($"[Crafting] No Hostile settings at {HostileSettingsPath}: no drops added."); return 0; }
            var rows = new List<MaterialDrop>(hostiles.materialDrops ?? System.Array.Empty<MaterialDrop>());
            int added = 0;

            void Row(ushort itemId, float chance, int minLevel)
            {
                ItemDefinition item = catalog.Get(itemId);
                if (item == null || rows.Exists(row => row.item == item)) return; // its chance may have been tuned
                rows.Add(new MaterialDrop { item = item, chancePercent = chance, minLevel = minLevel });
                added++;
            }

            Row(CraftItemIds.CyanCell, 1.5f, 0);
            Row(CraftItemIds.MagentaCell, 1.5f, 0);
            Row(CraftItemIds.YellowCell, 1.5f, 0);
            Row(CraftItemIds.BlackCell, 1.5f, 0);
            Row(CraftItemIds.ChromaCell, 0.2f, 0);
            Row(CraftItemIds.PrismCell, 0.2f, 0);
            foreach ((ushort dossier, string _) in Dossiers)
            {
                for (int page = 1; page <= CraftItemIds.PagesPerDossier; page++) Row(CraftItemIds.PageOf(dossier, page), 0.25f, DossierDropLevel);
                Row(dossier, 0.02f, DossierDropLevel);
            }
            if (added == 0) return 0;
            hostiles.materialDrops = rows.ToArray();
            EditorUtility.SetDirty(hostiles);
            return added;
        }
    }
}
