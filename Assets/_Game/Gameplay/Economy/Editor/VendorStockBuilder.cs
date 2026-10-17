using System.Collections.Generic;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.NPC;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.Economy
{
    /// <summary>
    /// Builds what the vendors sell: one <see cref="VendorStock"/> per vendor in the table below, and the catalog
    /// that lists them. A new vendor, or a new item on a shelf, is a row here. Run again at any time: a price that
    /// was tuned on the asset is kept, and only what is missing is added. A weekly limit in the table is written
    /// where the asset has none (0); one tuned on the asset is kept.
    /// The prices are placeholders (owner, 10 Oct 2026: "arbitrary amounts for now, will tune later").
    /// </summary>
    public static class VendorStockBuilder
    {
        public const string Folder = "Assets/_Game/Gameplay/Economy/Data/Assets/Vendors";
        public const string CatalogPath = Folder + "/VendorCatalog.asset";
        private const string NpcFolder = "Assets/_Game/Gameplay/NPC/Data/Assets";
        private const string Consumables = "Assets/_Game/Gameplay/Items/Data/Assets/Consumables/";
        private const string Materials = "Assets/_Game/Gameplay/Items/Data/Assets/GearMaterials/";
        private const string CraftMaterials = "Assets/_Game/Gameplay/Items/Data/Assets/CraftMaterials/";

        private struct Shelf
        {
            public string Asset, Npc;
            public (string Item, long Price)[] Listings;
            /// <summary>Sold one a day, in turn.</summary>
            public (string Item, long Price)[] Rotation;
            /// <summary>How many of a listed item one character may buy each in-game week.</summary>
            public (string Item, int Limit)[] WeeklyLimits;
            public bool Gear;
            public ItemRarity GearRarity;
            public int GearFrom, GearTo;
        }

        private static readonly Shelf[] Shelves =
        {
            // Stims in three sizes, the Flux Flask (refilled in its dialogue), Recall Tags and what a pet eats.
            new Shelf
            {
                Asset = "VS_Apothecary", Npc = "NPC_HerbMaster",
                Listings = new[]
                {
                    (Consumables + "Pill_Vitality.asset", 30L),
                    (Consumables + "Pill_Chi.asset", 30L),
                    (Consumables + "Pill_DualHarmony.asset", 80L),
                    (Consumables + "Stim_HP_M.asset", 120L),
                    (Consumables + "Stim_Flux_M.asset", 120L),
                    (Consumables + "Stim_Dual_M.asset", 320L),
                    (Consumables + "Stim_HP_L.asset", 400L),
                    (Consumables + "Stim_Flux_L.asset", 400L),
                    (Consumables + "Stim_Dual_L.asset", 1_000L),
                    (Consumables + "Recall_Tag.asset", 150L),
                    (Consumables + "Flux_Flask.asset", 600L),
                    (Consumables + "Pet_Ration.asset", 50L),
                },
            },
            // The two basic Alloys and the plain material of the other forge jobs, then Common gear by rule.
            new Shelf
            {
                Asset = "VS_Armorer", Npc = "NPC_Blacksmith",
                Listings = new[]
                {
                    (Materials + "Alloy_Alumite.asset", 500L),
                    (Materials + "Alloy_Iridion.asset", 1_500L),
                    (Materials + "Fusion_Catalyst.asset", 10_000L),
                    (Materials + "Tuning_Chip.asset", 10_000L),
                },
                // One of each forge material a week, per character (owner, 17 Oct 2026): the rest comes from Hostiles.
                WeeklyLimits = new[]
                {
                    (Materials + "Alloy_Alumite.asset", 1),
                    (Materials + "Alloy_Iridion.asset", 1),
                    (Materials + "Fusion_Catalyst.asset", 1),
                    (Materials + "Tuning_Chip.asset", 1),
                },
                Gear = true, GearRarity = ItemRarity.Common, GearFrom = 1, GearTo = 28,
            },
            // The four colours of a Chroma Cell, one of them each in-game day.
            new Shelf
            {
                Asset = "VS_Outfitter", Npc = "NPC_Tailor", // the Outfitter keeps its stall's first id
                Listings = new (string, long)[0],
                Rotation = new[]
                {
                    (CraftMaterials + "Cell_Cyan.asset", 1_500L),
                    (CraftMaterials + "Cell_Magenta.asset", 1_500L),
                    (CraftMaterials + "Cell_Yellow.asset", 1_500L),
                    (CraftMaterials + "Cell_Black.asset", 1_500L),
                },
            },
        };

        [MenuItem("Krytical Nova/Items/10. Build Vendor Stock")]
        public static void Build()
        {
            EnsureFolder(Folder);
            var catalog = AssetDatabase.LoadAssetAtPath<VendorCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<VendorCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            int added = 0;
            foreach (Shelf shelf in Shelves)
            {
                var vendor = AssetDatabase.LoadAssetAtPath<NpcDefinition>($"{NpcFolder}/{shelf.Npc}.asset");
                if (vendor == null) { Debug.LogError($"[Vendors] No NPC definition {shelf.Npc}: {shelf.Asset} skipped."); continue; }

                string path = $"{Folder}/{shelf.Asset}.asset";
                var stock = AssetDatabase.LoadAssetAtPath<VendorStock>(path);
                bool created = stock == null;
                if (created)
                {
                    stock = ScriptableObject.CreateInstance<VendorStock>();
                    AssetDatabase.CreateAsset(stock, path);
                    // The gear rule is set when the shelf is made and is the asset's to tune after that.
                    stock.sellsLadderGear = shelf.Gear;
                    stock.gearRarity = shelf.GearRarity;
                    stock.gearMinLevel = Mathf.Max(1, shelf.GearFrom);
                    stock.gearMaxLevel = Mathf.Max(stock.gearMinLevel, shelf.GearTo);
                }
                stock.vendor = vendor;

                foreach ((string itemPath, long price) in shelf.Listings)
                {
                    var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
                    if (item == null) { Debug.LogError($"[Vendors] {shelf.Asset}: no item at {itemPath}."); continue; }
                    if (stock.TryGetListed(item, out _)) continue; // its price may have been tuned
                    stock.listings.Add(new VendorListing { item = item, price = price });
                    added++;
                }
                if (shelf.Rotation != null)
                    foreach ((string itemPath, long price) in shelf.Rotation)
                    {
                        var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
                        if (item == null) { Debug.LogError($"[Vendors] {shelf.Asset}: no item at {itemPath}."); continue; }
                        if (stock.rotation.Exists(listing => listing.item == item)) continue; // its price may have been tuned
                        stock.rotation.Add(new VendorListing { item = item, price = price });
                        added++;
                    }
                if (shelf.WeeklyLimits != null)
                    foreach ((string itemPath, int limit) in shelf.WeeklyLimits)
                    {
                        var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
                        int at = item != null ? stock.listings.FindIndex(listing => listing.item == item) : -1;
                        if (at < 0) { Debug.LogError($"[Vendors] {shelf.Asset}: no listing for {itemPath} to limit."); continue; }
                        VendorListing listing = stock.listings[at];
                        if (listing.weeklyLimit != 0) continue; // tuned on the asset
                        listing.weeklyLimit = Mathf.Max(0, limit);
                        stock.listings[at] = listing;
                    }
                EditorUtility.SetDirty(stock);
                if (!catalog.stocks.Contains(stock)) catalog.stocks.Add(stock);

                if ((vendor.offers & NpcOffer.Shop) == 0)
                    Debug.LogWarning($"[Vendors] {vendor.name} does not offer the Shop service yet: run Krytical Nova > Zone > Refresh NPC Text From Seeds.");
            }
            catalog.stocks.RemoveAll(stock => stock == null);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Vendors] {catalog.stocks.Count} vendor(s) stocked, {added} listing(s) added. Prices are placeholders: tune them on the assets in {Folder}.");
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
        }
    }
}
