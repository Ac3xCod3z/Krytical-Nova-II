using System.Collections.Generic;
using KryticalNova.NPC;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>
    /// Every vendor's stock, so the player's shopping can find what the NPC in front of it sells, and what any item
    /// would fetch when sold back. A new vendor is a <see cref="VendorStock"/> asset added here.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Economy/Vendor Catalog")]
    public sealed class VendorCatalog : ScriptableObject
    {
        public List<VendorStock> stocks = new List<VendorStock>();

        /// <summary>What this NPC sells, or null when it has no shop.</summary>
        public VendorStock For(NpcDefinition vendor)
        {
            if (vendor == null) return null;
            foreach (VendorStock stock in stocks)
                if (stock != null && stock.vendor == vendor) return stock;
            return null;
        }

        /// <summary>The same, by the definition's asset name: what goes over the network.</summary>
        public VendorStock For(string vendorName)
        {
            if (string.IsNullOrEmpty(vendorName)) return null;
            foreach (VendorStock stock in stocks)
                if (stock != null && stock.vendor != null && stock.vendor.name == vendorName) return stock;
            return null;
        }
    }
}
