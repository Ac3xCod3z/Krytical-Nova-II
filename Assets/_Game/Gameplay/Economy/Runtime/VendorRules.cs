using System;
using System.Collections.Generic;
using KryticalNova.Items;

namespace KryticalNova.Economy
{
    /// <summary>One line of a shop window: the item, what one costs, and how many a character may buy a week (0 = no limit).</summary>
    public readonly struct VendorOffer
    {
        public readonly ItemDefinition Item;
        public readonly long Price;
        public readonly int WeeklyLimit;

        public VendorOffer(ItemDefinition item, long price, int weeklyLimit = 0)
        {
            Item = item;
            Price = price;
            WeeklyLimit = weeklyLimit;
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

        // ------------------------------------------------------------------ the weekly limit

        /// <summary>The vendor's week an in-game day falls in: days 0 to weekDays - 1 are week 0, and a day before 0 is in a week before 0.</summary>
        public static long WeekOf(long day, int weekDays)
        {
            int length = Math.Max(1, weekDays);
            return day >= 0 ? day / length : -((-day - 1) / length) - 1;
        }

        /// <summary>The first in-game day of the next vendor week.</summary>
        public static long NextWeekStarts(long day, int weekDays) => (WeekOf(day, weekDays) + 1) * Math.Max(1, weekDays);

        /// <summary>How many more of an item may be bought this week. int.MaxValue when it has no limit.</summary>
        public static int RemainingThisWeek(int weeklyLimit, int boughtThisWeek) =>
            weeklyLimit <= 0 ? int.MaxValue : Math.Max(0, weeklyLimit - Math.Max(0, boughtThisWeek));

        /// <summary>What was bought this week, given what was saved for the week it was saved in: a new week starts at 0.</summary>
        public static int BoughtIn(long week, long savedWeek, int savedBought) => week == savedWeek ? Math.Max(0, savedBought) : 0;

        /// <summary>"3 h 20 min", "45 min", "under a minute"; empty when it is not known (the clock is stopped).</summary>
        public static string Wait(double seconds)
        {
            if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return string.Empty;
            TimeSpan left = TimeSpan.FromSeconds(seconds);
            if (left.TotalDays >= 1) return $"{(int)left.TotalDays} d {left.Hours} h";
            if (left.TotalHours >= 1) return $"{(int)left.TotalHours} h {left.Minutes:00} min";
            return left.TotalMinutes >= 1 ? $"{(int)left.TotalMinutes} min" : "under a minute";
        }

        /// <summary>The refusal when this week's limit of an item is bought.</summary>
        public static string SoldOutThisWeek(string itemName, int weeklyLimit, double secondsToRestock)
        {
            string wait = Wait(secondsToRestock);
            return $"You have bought this week's {weeklyLimit} {itemName}. More next week" + (wait.Length > 0 ? $": in about {wait}." : ".");
        }

        /// <summary>What a piece of ladder gear costs in a shop: its level, times the price of a level, times its rarity's weight.</summary>
        public static long GearPrice(EconomySettings settings, ItemDefinition item)
        {
            if (settings == null || item == null) return 0;
            double price = Math.Max(1, (int)item.itemLevel) * (double)settings.vendorGearCredsPerLevel * settings.VendorGearMultiplier(item.rarity);
            return Clamp(Math.Round(price));
        }

        /// <summary>
        /// What this vendor asks for one of the item, or false when it does not sell it. <paramref name="day"/> is
        /// the in-game day: a vendor with a rotating shelf sells one of its items each day.
        /// </summary>
        public static bool TryPrice(VendorStock stock, EconomySettings settings, ItemDefinition item, out long price, long day = 0)
        {
            price = 0;
            if (stock == null || item == null) return false;
            if (stock.TryGetListed(item, out price)) return price > 0;
            if (stock.TryGetOfTheDay(day, out VendorListing today) && today.item == item)
            {
                price = today.price;
                return true;
            }
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
            Func<ItemDefinition, bool> canWear, List<VendorOffer> results, long day = 0)
        {
            results.Clear();
            if (stock == null) return;
            foreach (VendorListing listing in stock.listings)
                if (listing.item != null && listing.price > 0) results.Add(new VendorOffer(listing.item, listing.price, listing.weeklyLimit));
            // What the rotating shelf holds today (the Outfitter's colour of the day).
            if (stock.TryGetOfTheDay(day, out VendorListing today)) results.Add(new VendorOffer(today.item, today.price, today.weeklyLimit));
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
