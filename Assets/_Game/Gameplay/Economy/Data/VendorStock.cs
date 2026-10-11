using System;
using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.NPC;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>One thing on a vendor's shelf: the item and what one of it costs.</summary>
    [Serializable]
    public struct VendorListing
    {
        public ItemDefinition item;
        [Tooltip("Creds for one.")]
        [Min(0)] public long price;
    }

    /// <summary>
    /// What one NPC sells. Listed items carry their own price. A vendor may also sell gear by rule: every piece of the
    /// gear ladder of one rarity inside a level range, priced from its level (<see cref="EconomySettings"/>), so new
    /// gear appears on the shelf without anyone listing it. A buyer is only shown gear they can wear.
    /// Every price here is a number to tune; nothing in code holds one.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Economy/Vendor Stock")]
    public sealed class VendorStock : ScriptableObject
    {
        [Tooltip("The NPC who sells this. Its definition must offer the Shop service.")]
        public NpcDefinition vendor;
        [Tooltip("The name over the shop window. Empty = the vendor's own name.")]
        public string shopName = string.Empty;
        public List<VendorListing> listings = new List<VendorListing>();

        [Header("One a day")]
        [Tooltip("One of these is on the shelf each in-game day, in turn (the Outfitter's colour cells). Empty = nothing rotates.")]
        public List<VendorListing> rotation = new List<VendorListing>();

        [Header("Gear sold by rule")]
        [Tooltip("Sells every ladder piece of the rarity below within the level range, to those who can wear it.")]
        public bool sellsLadderGear;
        public ItemRarity gearRarity = ItemRarity.Common;
        [Min(1)] public int gearMinLevel = 1;
        [Min(1)] public int gearMaxLevel = 28;

        public string Title => !string.IsNullOrEmpty(shopName) ? shopName : vendor != null ? vendor.displayName : name;

        /// <summary>True when the gear rule covers this piece. Who may wear it is the buyer's matter, not the shelf's.</summary>
        public bool SellsByRule(ItemDefinition item) =>
            sellsLadderGear && item != null && item.IsLadderGear && item.equipSlot != EquipSlot.Pet
            && item.rarity == gearRarity && item.itemLevel >= gearMinLevel && item.itemLevel <= gearMaxLevel;

        /// <summary>What the rotating shelf holds on an in-game day, or false when nothing rotates.</summary>
        public bool TryGetOfTheDay(long day, out VendorListing listing)
        {
            listing = default;
            if (rotation == null || rotation.Count == 0) return false;
            listing = rotation[(int)(((day % rotation.Count) + rotation.Count) % rotation.Count)];
            return listing.item != null && listing.price > 0;
        }

        /// <summary>The listed price of an item, or false when it is not listed.</summary>
        public bool TryGetListed(ItemDefinition item, out long price)
        {
            price = 0;
            if (item == null) return false;
            foreach (VendorListing listing in listings)
            {
                if (listing.item != item) continue;
                price = listing.price;
                return true;
            }
            return false;
        }
    }
}
