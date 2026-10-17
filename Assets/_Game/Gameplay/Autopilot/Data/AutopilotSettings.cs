using UnityEngine;

namespace KryticalNova.Autopilot
{
    /// <summary>One Pass (Autopilot or Autobuff): how many days it adds, which item it is, and what the store will ask for it.</summary>
    [System.Serializable]
    public struct PassTier
    {
        public string label;
        [Min(1)] public int days;
        [Tooltip("The Pass item's id (UtilityItemGenerator). 0: not an item.")]
        public ushort passItemId;
        [Tooltip("What the Nova Store will ask, as text for now (the store is not built).")]
        public string storePrice;
    }

    /// <summary>
    /// The limits of Autopilot (hunting by itself), Magnet (picking up by itself) and the Autobuff (buffing by itself). What a player chooses within
    /// them is that player's own <see cref="AutopilotPreferences"/>. Change a number here, never in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Autopilot/Autopilot Settings")]
    public sealed class AutopilotSettings : ScriptableObject
    {
        [Header("Access")]
        [Tooltip("The Autopilot Passes: each adds its days of Autopilot, by the clock, whether Autopilot is on or not (owner, 15 Oct 2026). The 3-day one also drops from Hostiles.")]
        public PassTier[] autopilotTiers =
        {
            new PassTier { label = "3 days", days = 3, passItemId = 9030, storePrice = "$0.99" },
            new PassTier { label = "14 days", days = 14, passItemId = 9034, storePrice = "$2.99" },
            new PassTier { label = "30 days", days = 30, passItemId = 9035, storePrice = "$4.99" },
        };
        [Tooltip("The most days of Autopilot a character may hold at once: a Pass that would go past it is refused, not wasted.")]
        [Min(1)] public int autopilotMaxDays = 90;
        [Tooltip("Off: Autopilot only starts inside a Hostile Zone and stops on leaving it.")]
        public bool allowedOutsideHostileZones;

        [Header("Pace: never faster than a person")]
        [Tooltip("Seconds between two things Autopilot does: a random time between these.")]
        public Vector2 actionDelaySeconds = new Vector2(0.3f, 0.5f);
        [Tooltip("Seconds between Magnet's looks at the ground.")]
        [Min(0.05f)] public float magnetScanSeconds = 0.25f;

        [Header("What a player may choose")]
        [Tooltip("Hunt radius round the spot Autopilot was switched on, metres: the least (x) and the most (y).")]
        public Vector2 huntRadius = new Vector2(10f, 40f);
        [Tooltip("How many Hostiles to gather before the area skill: the most a player may ask for.")]
        [Range(1, 8)] public int maxGatherCount = 8;
        [Tooltip("Hostiles count as gathered within this many metres of the player.")]
        [Min(1f)] public float gatherRadius = 5f;
        [Tooltip("Metres past the Hostile Zone's edge at which the server switches Autopilot off.")]
        [Min(0f)] public float zoneExitMargin = 20f;

        [Header("Autobuff")]
        [Tooltip("The Autobuff Passes: each adds its days to the licence, by the clock (owner, 15 Oct 2026). The 3-day one also drops from Hostiles.")]
        public PassTier[] autobuffTiers =
        {
            new PassTier { label = "3 days", days = 3, passItemId = 9031, storePrice = "$0.99" },
            new PassTier { label = "14 days", days = 14, passItemId = 9032, storePrice = "$2.99" },
            new PassTier { label = "30 days", days = 30, passItemId = 9033, storePrice = "$4.99" },
        };
        [Tooltip("The most days of Autobuff a character may hold at once: a Pass that would go past it is refused, not wasted.")]
        [Min(1)] public int autobuffMaxDays = 90;

        /// <summary>The Autobuff Pass an item is, or null when it is none.</summary>
        public PassTier? AutobuffTierFor(ushort itemId) => TierFor(autobuffTiers, itemId);

        /// <summary>The Autopilot Pass an item is, or null when it is none.</summary>
        public PassTier? AutopilotTierFor(ushort itemId) => TierFor(autopilotTiers, itemId);

        private static PassTier? TierFor(PassTier[] tiers, ushort itemId)
        {
            if (itemId == 0 || tiers == null) return null;
            foreach (PassTier tier in tiers)
                if (tier.passItemId == itemId) return tier;
            return null;
        }
    }
}
