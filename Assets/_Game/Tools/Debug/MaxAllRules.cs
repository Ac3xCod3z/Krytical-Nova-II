using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Pets;
using KryticalNova.Progression;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// The rules of gm.maxall (owner's night list item 22, 13 Nov 2026), free of the game so they can be tested: where it may run, which
    /// gear it gives, what that gear carries, where each piece goes, and what a pet at 100 % looks like.
    /// </summary>
    public static class MaxAllRules
    {
        // The owner's numbers for "max everything" (item 22, 13 Nov 2026). A GM test tool, not game tuning: they live with the command.
        public const long Creds = 2_000_000;
        public const int ForgeMaterials = 999;
        public const int PassItems = 50;
        public const int Cells = 50;
        public const int PassDays = 3;
        /// <summary>gm.craftkit's "n times over": enough of every recipe's materials for this many crafts.</summary>
        public const int CraftKitTimes = 50;
        public const float FullGrowth = 100f;
        public const string ConfirmWord = "confirm";

        /// <summary>The test lane's save service (krytical_nova_test). The real one (7801) is in <c>KnServerSettings.RealLanePorts</c>.</summary>
        public const int TestSavePort = 7802;

        /// <summary>
        /// Why gm.maxall may not run here, or null when it may. <paramref name="saveService"/> is the save service the server saves
        /// through (null when it saves to files). It runs only on the test database: a server on the test save service (7802), or an
        /// Editor session saving to files (Editor Play never saves to the real database). A built server saving to files, or any
        /// server on another save service (the real one is 7801), is refused.
        /// </summary>
        public static string GateProblem(bool editor, Uri saveService, IReadOnlyList<int> realPorts)
        {
            if (saveService != null)
            {
                if (realPorts != null)
                    for (int i = 0; i < realPorts.Count; i++)
                        if (realPorts[i] == saveService.Port)
                            return $"this server saves to the REAL save service (port {saveService.Port}, the real saves). gm.maxall runs only on the test database (save service {TestSavePort}).";
                return saveService.Port == TestSavePort
                    ? null
                    : $"this server saves to the save service on port {saveService.Port}, not the test one ({TestSavePort}). gm.maxall runs only on the test database.";
            }
            return editor
                ? null
                : $"this server saves to files, not the test save service ({TestSavePort}). gm.maxall runs only on the test database.";
        }

        public static bool IsConfirm(string word) => string.Equals((word ?? string.Empty).Trim(), ConfirmWord, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The caller's own Rare and Epic gear: every ladder level of every slot, of their faction, body and lineage (outfit pieces
        /// by <paramref name="pack"/>) and, for weapons, of their class line. One piece per slot, rarity and level, the first in
        /// the dictionary's order (as gm.gear picks). Common and Uncommon, and pets, are never in it.
        /// </summary>
        public static List<GearDictionary.Entry> PickGear(IReadOnlyList<GearDictionary.Entry> entries, FactionId faction, BodyFit body,
            string pack, WeaponClassId weaponClass)
        {
            var picked = new List<GearDictionary.Entry>();
            if (entries == null) return picked;
            var seen = new HashSet<(EquipSlot, ItemRarity, byte)>();
            foreach (GearDictionary.Entry entry in entries)
            {
                if (!entry.IsValid || (entry.rarity != ItemRarity.Rare && entry.rarity != ItemRarity.Epic)) continue;
                if (entry.slot == EquipSlot.None || entry.slot == EquipSlot.Pet) continue;
                // the same matching as GearDictionary.TryFindItem (gm.gear), every level instead of one
                if (faction != FactionId.None && !FactionRules.Includes(entry.factions, faction)) continue;
                if (body != BodyFit.None && (entry.bodies & body) == 0) continue;
                if (entry.slot == EquipSlot.Weapon)
                {
                    if (weaponClass != WeaponClassId.None && entry.weaponClass != weaponClass) continue;
                }
                else if (!string.IsNullOrEmpty(pack) && !string.IsNullOrEmpty(entry.pack) && entry.pack != pack) continue;
                if (!seen.Add((entry.slot, entry.rarity, entry.itemLevel))) continue;
                picked.Add(entry);
            }
            return picked;
        }

        /// <summary>What a given piece carries: Rare comes clean, Epic at the Temper cap and Fusion 12.</summary>
        public static ulong GearState(ItemRarity rarity, int temperCap) =>
            rarity == ItemRarity.Epic
                ? default(GearItemState).WithTemper(Math.Max(0, Math.Min(temperCap, GearItemState.MaxTemper))).WithFusion(GearItemState.MaxFusion).Pack()
                : 0UL;

        /// <summary>
        /// Puts the waiting pieces (one per cell: gear and pets do not stack) into the empty cells, in order, and moves each one it
        /// placed from <paramref name="waiting"/> to <paramref name="placed"/>. Returns how many went in.
        /// </summary>
        public static int FillEmpty(ItemStack[] cells, List<ItemStack> waiting, List<ItemStack> placed)
        {
            if (cells == null || waiting == null) return 0;
            int count = 0;
            for (int i = 0; i < cells.Length && count < waiting.Count; i++)
            {
                if (!cells[i].IsEmpty) continue;
                cells[i] = waiting[count];
                placed?.Add(waiting[count]);
                count++;
            }
            waiting.RemoveRange(0, count);
            return count;
        }

        /// <summary>A pet item's state at 100 % growth and fully fed (the XP is kept when it is already higher).</summary>
        public static ulong FullPet(ulong data, in PetTuning tuning)
        {
            PetItemState state = PetItemState.Unpack(data);
            PetItemState full = PetRules.WithGrowth(state, FullGrowth, tuning);
            if (full.XpUnits < state.XpUnits) full = state;
            return PetRules.Feed(full).Pack();
        }

        /// <summary>The "did not fit" line: each name once with how many, in the order they were met.</summary>
        public static string DidNotFit(IReadOnlyList<string> names)
        {
            if (names == null || names.Count == 0) return "Did not fit: nothing (everything found room).";
            var order = new List<string>();
            var counts = new Dictionary<string, int>();
            foreach (string name in names)
            {
                if (counts.TryGetValue(name, out int n)) counts[name] = n + 1;
                else { counts[name] = 1; order.Add(name); }
            }
            var parts = new List<string>(order.Count);
            foreach (string name in order) parts.Add(counts[name] > 1 ? $"{name} x{counts[name]}" : name);
            return $"Did not fit (Pack, Vault and Deep Vault full), {names.Count}: " + string.Join(", ", parts) + ".";
        }
    }
}
