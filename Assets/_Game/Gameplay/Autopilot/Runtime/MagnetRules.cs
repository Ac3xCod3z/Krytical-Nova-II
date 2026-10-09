using System;
using KryticalNova.Items;

namespace KryticalNova.Autopilot
{
    /// <summary>
    /// Which drops Magnet takes, free of scene objects so it can be tested. The name lists win over everything
    /// else: "never" first, then "always". Otherwise Stims by their switch and gear by its rarity; anything else
    /// (materials, permits, chips) is always worth picking up.
    /// </summary>
    public static class MagnetRules
    {
        public static bool Wants(AutopilotPreferences preferences, ItemDefinition item)
        {
            if (preferences == null || item == null || !preferences.magnetOn) return false;
            if (Listed(preferences.neverTake, item.displayName)) return false;
            if (Listed(preferences.alwaysTake, item.displayName)) return true;
            if (IsGear(item)) return item.rarity >= preferences.minGearRarity;
            if (IsStim(item)) return preferences.magnetStims;
            return true;
        }

        public static bool IsGear(ItemDefinition item) => item.equipSlot != EquipSlot.None;
        public static bool IsStim(ItemDefinition item) => item.restoreHealth > 0f || item.restoreChi > 0f;

        private static bool Listed(string[] names, string name)
        {
            if (names == null || string.IsNullOrEmpty(name)) return false;
            foreach (string listed in names)
                if (string.Equals(listed, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
