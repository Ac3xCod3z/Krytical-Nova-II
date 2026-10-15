using KryticalNova.Items;

namespace KryticalNova.Autopilot
{
    /// <summary>
    /// Which drops Magnet takes, free of scene objects so it can be tested. Every choice is a tick box: gear by
    /// its rarity (one box each), Stims, and everything else (materials, permits, chips). Creds are always taken.
    /// </summary>
    public static class MagnetRules
    {
        /// <summary>
        /// Whether Magnet runs at all: it is switched on, and the character has Autopilot time left (or a GM gave
        /// them free mode). Magnet works with Autopilot off, but never without time (owner, 15 Oct 2026).
        /// </summary>
        public static bool MayRun(AutopilotPreferences preferences, float secondsLeft, bool free) =>
            preferences != null && preferences.magnetOn && (free || secondsLeft > 0f);

        public static bool Wants(AutopilotPreferences preferences, ItemDefinition item)
        {
            if (preferences == null || item == null || !preferences.magnetOn) return false;
            if (LootRules.IsCreds(item.itemId)) return true; // Creds are always worth picking up
            if (IsGear(item)) return preferences.TakesGear(item.rarity);
            if (IsStim(item)) return preferences.magnetStims;
            return preferences.magnetMaterials;
        }

        public static bool IsGear(ItemDefinition item) => item.equipSlot != EquipSlot.None;
        public static bool IsStim(ItemDefinition item) => item.restoreHealth > 0f || item.restoreChi > 0f;
    }
}
