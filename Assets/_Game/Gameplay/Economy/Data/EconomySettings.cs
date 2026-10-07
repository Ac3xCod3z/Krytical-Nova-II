using System;
using KryticalNova.Items;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>What a player at a level is meant to earn per hour of play: one point of the income target.</summary>
    [Serializable]
    public struct IncomePoint
    {
        public int level;
        public long credsPerHour;
    }

    /// <summary>A price worth weighing against income in the inspector's preview (a fee, a planned cost).</summary>
    [Serializable]
    public struct ReferenceCost
    {
        public string label;
        public long creds;
    }

    /// <summary>One length a Kiosk can be opened for, and what opening it for that long costs.</summary>
    [Serializable]
    public struct KioskTier
    {
        public string label;
        [Min(1)] public int days;
        [Tooltip("The item used up to open a Kiosk for this long. 0 = free to everyone.")]
        public ushort permitItemId;
        [Tooltip("What the permit will cost in the Nova Store, shown beside a locked option. Display only.")]
        public string storePrice;

        public bool IsFree => permitItemId == 0;
    }

    /// <summary>
    /// Every number of the economy in one place: how much a wallet holds, how much may change hands, what things may
    /// cost, and how much a player is meant to earn. They belong together because they only make sense together: a
    /// price cap is only fair measured against the hours it takes to earn. The inspector shows that side by side.
    /// Every value here is a proposal to be tuned; nothing in code may hard-code one.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Economy/Economy Settings")]
    public sealed class EconomySettings : ScriptableObject
    {
        private const int RarityCount = 5;

        [Header("Wallet")]
        [Tooltip("The most Creds one character can carry. The Vault and Deep Vault each hold as much again.")]
        [Min(0)] public long walletCap = 999_999_999_999;

        [Header("Trade (player to player)")]
        [Tooltip("The most Creds one side may put into a single trade.")]
        [Min(0)] public long tradeCredsPerTrade = 5_000_000;
        [Tooltip("The most Creds an account may send in trades per day (UTC), all its characters together.")]
        [Min(0)] public long tradeCredsPerDay = 20_000_000;
        [Tooltip("Item stacks each side may offer in one trade.")]
        [Range(1, 24)] public int tradeSlots = 12;
        [Tooltip("How close the two players must be to trade, in metres.")]
        [Min(1f)] public float tradeRange = 6f;
        [Tooltip("Seconds a trade request waits for an answer.")]
        [Min(5f)] public float tradeRequestSeconds = 30f;

        [Header("Kiosks")]
        [Tooltip("Top price of one item in a Kiosk, by rarity: Common, Uncommon, Rare, Epic, Legendary.")]
        public long[] kioskPriceCapByRarity = { 50_000, 500_000, 5_000_000, 20_000_000, 20_000_000 };
        [Tooltip("Top price per unit of anything that stacks (Stims, materials), whatever its rarity.")]
        [Min(0)] public long kioskStackablePriceCap = 10_000;
        [Tooltip("Top price of a pet at 100% growth. A pet's cap rises from its rarity's cap at 0% to this.")]
        [Min(0)] public long kioskPetFullGrowthPriceCap = 20_000_000;
        [Tooltip("Share of every Kiosk sale that leaves the game instead of reaching the seller.")]
        [Range(0f, 50f)] public float kioskSalesTaxPercent = 5f;
        [Tooltip("Item stacks one Kiosk can list.")]
        [Range(1, 48)] public int kioskSlots = 24;
        [Tooltip("How close a buyer must stand to a Kiosk, in metres.")]
        [Min(1f)] public float kioskRange = 5f;
        [Tooltip("How long a Kiosk can be opened for. The first free tier is the default.")]
        public KioskTier[] kioskTiers =
        {
            new KioskTier { label = "3 days", days = 3 },
            new KioskTier { label = "14 days", days = 14, permitItemId = 9020, storePrice = "$1.99" },
            new KioskTier { label = "30 days", days = 30, permitItemId = 9021, storePrice = "$3.99" },
        };

        [Header("Income target (what the faucets are tuned to pay)")]
        [Tooltip("Creds a player is meant to earn per hour of hunting at a level. Hostile drops, Contracts and shops are " +
                 "balanced against this when they are built; the inspector compares every cap and price with it.")]
        public IncomePoint[] incomeTarget =
        {
            new IncomePoint { level = 1, credsPerHour = 3_000 },
            new IncomePoint { level = 20, credsPerHour = 25_000 },
            new IncomePoint { level = 40, credsPerHour = 90_000 },
            new IncomePoint { level = 60, credsPerHour = 250_000 },
            new IncomePoint { level = 80, credsPerHour = 600_000 },
            new IncomePoint { level = 100, credsPerHour = 1_200_000 },
            new IncomePoint { level = 112, credsPerHour = 2_000_000 },
        };

        [Tooltip("Prices the inspector's preview weighs against income. Nothing reads these: they are for tuning, so list " +
                 "any fee or cost being considered.")]
        public ReferenceCost[] referenceCosts =
        {
            new ReferenceCost { label = "Crew founding fee", creds = 10_000_000 },
            new ReferenceCost { label = "Fusion FS1 (gear spec)", creds = 100_000 },
            new ReferenceCost { label = "Fusion FS1 to FS12 (gear spec)", creds = 4_500_000 },
            new ReferenceCost { label = "Ascension, one attempt (Herald)", creds = 500_000 },
            new ReferenceCost { label = "Ascension +0 to +96, on average (Herald)", creds = 137_700_000 },
        };

        /// <summary>The income target at a level, read between the points on a straight line.</summary>
        public long CredsPerHour(int level)
        {
            if (incomeTarget == null || incomeTarget.Length == 0) return 0;
            if (level <= incomeTarget[0].level) return incomeTarget[0].credsPerHour;
            for (int i = 1; i < incomeTarget.Length; i++)
            {
                IncomePoint from = incomeTarget[i - 1], to = incomeTarget[i];
                if (level > to.level) continue;
                if (to.level <= from.level) return to.credsPerHour;
                double t = (level - from.level) / (double)(to.level - from.level);
                return (long)Math.Round(from.credsPerHour + (to.credsPerHour - from.credsPerHour) * t);
            }
            return incomeTarget[incomeTarget.Length - 1].credsPerHour;
        }

        /// <summary>Hours of play at a level's income target to earn an amount (0 when no target is set).</summary>
        public double HoursToEarn(long creds, int level)
        {
            long perHour = CredsPerHour(level);
            return perHour > 0 ? creds / (double)perHour : 0.0;
        }

        /// <summary>The top price of one unit of an item in a Kiosk.</summary>
        /// <param name="petGrowth01">For a pet, its growth from 0 to 1; negative for everything else.</param>
        public long KioskPriceCap(ItemRarity rarity, bool stackable, float petGrowth01 = -1f)
        {
            if (stackable) return kioskStackablePriceCap;
            long byRarity = RarityCap(rarity);
            if (petGrowth01 < 0f) return byRarity;
            long full = Math.Max(byRarity, kioskPetFullGrowthPriceCap);
            return byRarity + (long)Math.Round((full - byRarity) * (double)Mathf.Clamp01(petGrowth01));
        }

        private long RarityCap(ItemRarity rarity)
        {
            if (kioskPriceCapByRarity == null || kioskPriceCapByRarity.Length == 0) return 0;
            return kioskPriceCapByRarity[Mathf.Clamp((int)rarity, 0, kioskPriceCapByRarity.Length - 1)];
        }

        /// <summary>What the seller receives of a sale after the tax, and the tax itself.</summary>
        public long AfterTax(long price, out long tax)
        {
            tax = price <= 0 ? 0 : (long)Math.Floor(price * (double)Mathf.Clamp(kioskSalesTaxPercent, 0f, 100f) / 100.0);
            return Math.Max(0, price - tax);
        }

        /// <summary>The tier at an index, or the first one when the index is out of range.</summary>
        public KioskTier Tier(int index)
        {
            if (kioskTiers == null || kioskTiers.Length == 0) return new KioskTier { label = "3 days", days = 3 };
            return kioskTiers[index >= 0 && index < kioskTiers.Length ? index : 0];
        }

        private void OnValidate()
        {
            if (kioskPriceCapByRarity == null || kioskPriceCapByRarity.Length != RarityCount)
                Array.Resize(ref kioskPriceCapByRarity, RarityCount);
        }
    }
}
