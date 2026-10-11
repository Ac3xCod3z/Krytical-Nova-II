using System;
using System.Collections.Generic;
using KryticalNova.Items;

namespace KryticalNova.Economy
{
    /// <summary>One line of a shop window: the item and what one costs.</summary>
    public readonly struct VendorOffer
    {
        public readonly ItemDefinition Item;
        public readonly long Price;

        public VendorOffer(ItemDefinition item, long price)
        {
            Item = item;
            Price = price;
        }
    }

    /// <summary>
    /// The rules of NPC shops, free of networking so they can be tested: what a vendor asks for an item, what any
    /// item fetches when sold to one, and which sales must be confirmed first. The window and the server both ask
    /// here, so the window never shows a price the server would not honour.
    /// </summary>
    public static class VendorRules
    {
        public const string ReasonNotSold = "That is not sold here.";
        public const string ReasonNotBought = "No vendor will buy that.";
        public const string ReasonNotEnoughCreds = "You do not have enough Creds.";
        public const string ReasonWalletFull = "Your wallet cannot hold that many Creds.";
        public const string ReasonNeedsConfirm = "That sale has to be confirmed first.";
        public const string ReasonTooFar = "Move closer to the vendor.";

        /// <summary>What a piece of ladder gear costs in a shop: its level, times the price of a level, times its rarity's weight.</summary>
        public static long GearPrice(EconomySettings settings, ItemDefinition item)
        {
            if (settings == null || item == null) return 0;
            double price = Math.Max(1, (int)item.itemLevel) * (double)settings.vendorGearCredsPerLevel * settings.VendorGearMultiplier(item.rarity);
            return Clamp(Math.Round(price));
        }

        /// <summary>What this vendor asks for one of the item, or false when it does not sell it.</summary>
        public static bool TryPrice(VendorStock stock, EconomySettings settings, ItemDefinition item, out long price)
        {
            price = 0;
            if (stock == null || item == null) return false;
            if (stock.TryGetListed(item, out price)) return price > 0;
            if (!stock.SellsByRule(item)) return false;
            price = GearPrice(settings, item);
            return price > 0;
        }

        /// <summary>
        /// What one of an item is worth to the shops: what a vendor who lists it asks, what its level and rarity come
        /// to for ladder gear, or else what its rarity alone is worth. 0 for what no vendor deals in (Creds).
        /// </summary>
        public static long ShopValue(VendorCatalog catalog, EconomySettings settings, ItemDefinition item)
        {
            if (settings == null || item == null || LootRules.IsCreds(item.itemId)) return 0;
            if (catalog != null)
                foreach (VendorStock stock in catalog.stocks)
                    if (stock != null && stock.TryGetListed(item, out long listed) && listed > 0) return listed;
            if (item.IsLadderGear) return GearPrice(settings, item);
            return settings.VendorFallbackValue(item.rarity);
        }

        /// <summary>What a vendor pays for one of an item: a share of what the shops value it at. 0 = it will not buy it.</summary>
        public static long SellValue(VendorCatalog catalog, EconomySettings settings, ItemDefinition item)
        {
            long value = ShopValue(catalog, settings, item);
            if (value <= 0 || settings == null) return 0;
            return Clamp(Math.Floor(value * (double)Math.Max(0f, settings.vendorSellBackPercent) / 100.0));
        }

        /// <summary>
        /// True when a sale must be said back to the player and agreed to before it happens: anything of the rarity
        /// the settings name or better, any pet (its growth goes with it), and any gear that has been worked at the
        /// forge, whatever its rarity.
        /// </summary>
        public static bool NeedsConfirm(EconomySettings settings, ItemDefinition item, in ItemStack stack)
        {
            if (item == null) return false;
            ItemRarity from = settings != null ? settings.vendorConfirmFromRarity : ItemRarity.Rare;
            if (item.rarity >= from) return true;
            if (item.equipSlot == EquipSlot.Pet) return true;
            return item.IsLadderGear && !GearItemState.Of(stack).IsClean;
        }

        /// <summary>The price of several: false when it would overflow or the count is not a positive number.</summary>
        public static bool TryTotal(long unit, int count, out long total)
        {
            total = 0;
            if (unit < 0 || count <= 0) return false;
            if (unit != 0 && count > long.MaxValue / unit) return false;
            total = unit * count;
            return true;
        }

        /// <summary>
        /// A vendor's shelf as one buyer sees it: the listed items in their order, then the gear the rule covers that
        /// the buyer can wear (<paramref name="canWear"/>), by slot and then by level.
        /// </summary>
        public static void List(VendorStock stock, EconomySettings settings, IReadOnlyList<ItemDefinition> allItems,
            Func<ItemDefinition, bool> canWear, List<VendorOffer> results)
        {
            results.Clear();
            if (stock == null) return;
            foreach (VendorListing listing in stock.listings)
                if (listing.item != null && listing.price > 0) results.Add(new VendorOffer(listing.item, listing.price));
            if (!stock.sellsLadderGear || allItems == null) return;

            int first = results.Count;
            foreach (ItemDefinition item in allItems)
            {
                if (!stock.SellsByRule(item) || stock.TryGetListed(item, out _)) continue;
                if (canWear != null && !canWear(item)) continue;
                long price = GearPrice(settings, item);
                if (price > 0) results.Add(new VendorOffer(item, price));
            }
            results.Sort(first, results.Count - first, GearOrder.Instance);
        }

        private sealed class GearOrder : IComparer<VendorOffer>
        {
            public static readonly GearOrder Instance = new GearOrder();

            public int Compare(VendorOffer a, VendorOffer b)
            {
                int slot = ((int)a.Item.equipSlot).CompareTo((int)b.Item.equipSlot);
                if (slot != 0) return slot;
                int weapon = ((int)a.Item.weaponClass).CompareTo((int)b.Item.weaponClass);
                if (weapon != 0) return weapon;
                int level = a.Item.itemLevel.CompareTo(b.Item.itemLevel);
                return level != 0 ? level : a.Item.itemId.CompareTo(b.Item.itemId);
            }
        }

        private static long Clamp(double value) => value <= 0 ? 0 : value >= long.MaxValue ? long.MaxValue : (long)value;
    }
}
