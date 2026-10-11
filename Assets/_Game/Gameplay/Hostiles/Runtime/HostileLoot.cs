using System;
using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Pets;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>What one kill rolled: Creds, and the items that fall.</summary>
    public struct HostileLootRoll
    {
        public long Creds;
        public ItemDefinition Stim;
        public ItemDefinition Gear;
    }

    /// <summary>
    /// What a Hostile kill drops, where the game is decided: Creds (a pile of notes that pays the wallet of whoever
    /// picks it up), Stims and gear. All of it falls where the Hostile died and is held for the player who holds
    /// the kill for the usual time. Chances are
    /// <see cref="HostileSettings"/>, scaled by the level gap and the server drop rate. The pet of the player who
    /// holds the kill gets its kill XP here too.
    /// </summary>
    public static class HostileLoot
    {
        private static readonly List<ItemDefinition> candidates = new List<ItemDefinition>(32);
        private static readonly List<ItemDefinition> materials = new List<ItemDefinition>(4);
        private static readonly Func<float> RandomRoll = () => UnityEngine.Random.value;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Listen()
        {
            HostileKillRewards.Rewarded -= OnRewarded;
            HostileKillRewards.Rewarded += OnRewarded;
        }

        private static void OnRewarded(HostileKill kill)
        {
            if (kill.Credited == null || kill.Hostile == null) return;
            GameObject player = kill.Credited.gameObject;

            if (player.TryGetComponent(out PlayerPet pet)) pet.ServerCreditHostileKill();

            HostileSettings settings = kill.Hostile.Settings;
            HostileStatCurve curve = kill.Hostile.Curve;
            if (settings == null || curve == null) return;

            HostileLootRoll roll = Roll(kill.Hostile.Stats, curve, settings, kill.DropPercent, kill.GearDrops, HostileRates.Drops, UnityEngine.Random.value,
                UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value, UnityEngine.Random.value);

            if (!player.TryGetComponent(out PlayerLoot loot)) return;
            var owner = player.GetComponent<NetworkObject>();
            // Creds fall like everything else, as a pile of notes; picking them up pays the wallet.
            if (roll.Creds > 0)
                loot.ServerPlaceAt(new ItemStack(LootRules.CredsItemId, (ushort)Math.Min(roll.Creds, LootRules.MaxCredsPerDrop)), kill.Position, owner);
            if (roll.Stim != null) loot.ServerPlaceAt(new ItemStack(roll.Stim.itemId, 1), kill.Position, owner);
            if (roll.Gear != null) loot.ServerPlaceAt(new ItemStack(roll.Gear.itemId, 1), kill.Position, owner);

            RollMaterials(kill.Hostile.Stats.Level, settings, kill.DropPercent, HostileRates.Drops, RandomRoll, materials);
            foreach (ItemDefinition material in materials) loot.ServerPlaceAt(new ItemStack(material.itemId, 1), kill.Position, owner);
        }

        /// <summary>
        /// The crafting materials one kill drops: every row of <see cref="HostileSettings.materialDrops"/> that
        /// suits the Hostile's level is rolled on its own, scaled like the rest by the level gap and the server
        /// drop rate. <paramref name="roll"/> gives a number from 0 to 1 each time it is asked.
        /// </summary>
        public static void RollMaterials(int level, HostileSettings settings, float dropPercent, float rate, Func<float> roll,
            List<ItemDefinition> results)
        {
            results.Clear();
            float scale = Mathf.Max(0f, dropPercent) / 100f * Mathf.Max(0f, rate);
            if (scale <= 0f || settings == null || settings.materialDrops == null || roll == null) return;
            foreach (MaterialDrop drop in settings.materialDrops)
            {
                if (drop.item == null || level < drop.minLevel || (drop.maxLevel > 0 && level > drop.maxLevel)) continue;
                if (roll() < drop.chancePercent / 100f * scale) results.Add(drop.item);
            }
        }

        /// <summary>
        /// One kill's drops from five rolls of 0..1, so it can be tested and simulated. <paramref name="dropPercent"/>
        /// is the level gap's (100 = normal) and <paramref name="rate"/> the server's.
        /// </summary>
        public static HostileLootRoll Roll(in HostileStats stats, HostileStatCurve curve, HostileSettings settings, float dropPercent,
            bool gearAllowed, float rate, float credsChanceRoll, float credsAmountRoll, float stimRoll, float gearRoll, float pickRoll)
        {
            var roll = new HostileLootRoll();
            float scale = Mathf.Max(0f, dropPercent) / 100f * Mathf.Max(0f, rate);
            if (scale <= 0f) return roll;

            if (credsChanceRoll < curve.credsDropChance * scale)
            {
                float spread = 1f + (credsAmountRoll * 2f - 1f) * curve.credsSpread;
                roll.Creds = Math.Max(1, (long)Mathf.Round(stats.Creds * spread));
            }

            if (settings.stims != null && settings.stims.Length > 0 && stimRoll < settings.stimChancePercent / 100f * scale)
                roll.Stim = settings.stims[Mathf.Min(settings.stims.Length - 1, (int)(pickRoll * settings.stims.Length))];

            if (gearAllowed)
            {
                bool bruiser = stats.Tier == HostileTier.Bruiser;
                float chance = (bruiser ? settings.bruiserGearChancePercent : settings.gearChancePercent) / 100f * scale;
                if (gearRoll < chance) roll.Gear = PickGear(settings.catalog, stats.Level, settings.gearLevelsBelow, bruiser, pickRoll);
            }
            return roll;
        }

        /// <summary>
        /// A piece of ladder gear for a Hostile of this level: of the highest item level at or under it, within the
        /// window below. A Bruiser drops the best rarity that level range has; anything else drops any of them.
        /// </summary>
        public static ItemDefinition PickGear(ItemCatalog catalog, int level, int levelsBelow, bool bestRarity, float pickRoll)
        {
            if (catalog == null) return null;
            candidates.Clear();
            int lowest = Mathf.Max(1, level - Mathf.Max(0, levelsBelow));
            ItemRarity best = ItemRarity.Common;
            foreach (ItemDefinition item in catalog.Items)
            {
                if (item == null || item.equipSlot == EquipSlot.None || item.equipSlot == EquipSlot.Pet) continue;
                if (item.itemLevel < lowest || item.itemLevel > level) continue;
                candidates.Add(item);
                if (item.rarity > best) best = item.rarity;
            }
            if (bestRarity) candidates.RemoveAll(item => item.rarity != best);
            if (candidates.Count == 0) return null;
            return candidates[Mathf.Min(candidates.Count - 1, (int)(Mathf.Clamp01(pickRoll) * candidates.Count))];
        }
    }
}
