using System.Collections.Generic;
using System.Text;
using KryticalNova.Factions;
using KryticalNova.Forge;
using KryticalNova.Items;
using KryticalNova.Progression;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console item commands. Like the GM commands they only run where the game is decided (host, server or
    /// offline), because they call the server-only inventory API, and act on the player picked by <see cref="GmTarget"/>.
    /// </summary>
    public static class ItemCommands
    {
        private const string NoPlayer = "No local player Pack.";

        private static bool TryGetInventory(out PlayerInventory inventory, out string error)
        {
            inventory = null;
            if (!GmTarget.TryGetPlayer(out var player, out error)) return false;
            inventory = player.GetComponent<PlayerInventory>();
            if (inventory == null) error = NoPlayer;
            return inventory != null;
        }

        [Command("item.give", "Give an item by id, asset name or display name, e.g. item.give 10000 or item.give Armor_SOLD_C01")]
        private static string Give(string item, int count = 1)
        {
            if (!TryGetInventory(out var inv, out var error)) return error;
            if (inv.Catalog == null) return NoPlayer;
            var def = inv.Catalog.Find(item);
            if (def == null) return $"Unknown item '{item}'. Try item.list.";
            int added = inv.ServerGive(def.itemId, count);
            return added > 0 ? $"Gave {added}x {def.displayName}." : "Your Pack is full.";
        }

        [Command("gm.gear", "Give one piece of ladder gear: gm.gear <slot> <level> <rarity> [class]. Slot: weapon, armor, gloves, boots, " +
                            "ring, amulet, cape. The level snaps down to the nearest tier. It matches the targeted player's faction, body, " +
                            "pack and current class unless a class is named, e.g. gm.gear weapon 45 rare carbine or gm.gear armor 100 epic.")]
        private static string Gear(string slot, int level, string rarity, string weaponClass = "")
        {
            if (!TryGetInventory(out var inv, out var error)) return error;
            if (inv.Gear == null || inv.Catalog == null) return NoPlayer;
            if (!System.Enum.TryParse(slot, true, out EquipSlot equipSlot) || !EquipSlots.IsValid(equipSlot) || equipSlot == EquipSlot.Pet)
                return $"Unknown slot '{slot}'. Use weapon, armor, gloves, boots, ring, amulet or cape.";
            if (!System.Enum.TryParse(rarity, true, out ItemRarity itemRarity))
                return $"Unknown rarity '{rarity}'. Use common, uncommon, rare or epic.";

            var wearer = inv.GetComponent<IGearWearer>();
            FactionId faction = wearer != null ? wearer.Faction : FactionId.None;
            BodyFit body = wearer != null ? wearer.Body : BodyFit.None;
            string pack = wearer != null ? wearer.GearPack : null;
            WeaponClassId cls = WeaponClassId.None;
            if (weaponClass.Length > 0)
            {
                if (!WeaponClasses.TryParse(weaponClass, out cls)) return $"Unknown class '{weaponClass}'. {ClassList()}";
                // A named class is looked up in its own faction, so any class's gear can be handed out; equipping still follows the rules.
                if (equipSlot == EquipSlot.Weapon) faction = WeaponClassRules.FactionOf(cls);
            }
            else if (equipSlot == EquipSlot.Weapon)
            {
                var source = inv.GetComponent<IWeaponClassSource>();
                cls = source != null ? source.ActiveClass : wearer != null ? wearer.Discipline : WeaponClassId.None;
            }

            if (!inv.Gear.TryFindItem(equipSlot, itemRarity, level, faction, body, pack, cls, out var entry))
                return $"No {itemRarity} {equipSlot} gear fits this character{(cls != WeaponClassId.None ? $" as a {WeaponClasses.DisplayName(cls)}" : string.Empty)}.";
            var def = inv.Catalog.Get(entry.itemId);
            if (def == null) return $"Item {entry.itemId} is in the gear dictionary but not in the catalog. Re-bake the gear ladder.";
            if (inv.ServerGive(entry.itemId, 1) <= 0) return "Your Pack is full.";
            string note = inv.CanEquip(def, out string reason) ? string.Empty : $" ({reason})";
            return $"Gave {def.displayName}: level {entry.itemLevel} {entry.rarity} {equipSlot}{note}";
        }

        [Command("gm.loadout", "Empty the Pack and re-equip the level-1 starter kit of a class of the targeted player's faction: " +
                               "gm.loadout <class>. No value uses the character's Discipline.")]
        private static string Loadout(string weaponClass = "")
        {
            if (!TryGetInventory(out var inv, out var error)) return error;
            var wearer = inv.GetComponent<IGearWearer>();
            WeaponClassId cls = wearer != null ? wearer.Discipline : WeaponClassId.None;
            if (weaponClass.Length > 0 && !WeaponClasses.TryParse(weaponClass, out cls)) return $"Unknown class '{weaponClass}'. {ClassList()}";
            if (cls == WeaponClassId.None) cls = WeaponClassId.Monoblade;
            if (wearer != null && wearer.Faction != FactionId.None && !WeaponClassRules.BelongsTo(cls, wearer.Faction))
                return $"{WeaponClasses.DisplayName(cls)} belongs to the {WeaponClassRules.FactionOf(cls)}; this character is {wearer.Faction}.";
            return inv.ServerResetToLoadout(cls)
                ? $"Pack emptied; wearing the level-1 {WeaponClasses.DisplayName(cls)} starter kit."
                : $"No starter weapon found for {WeaponClasses.DisplayName(cls)}. Re-bake the gear ladder.";
        }

        private const int KitLevel = 112;
        private const int KitTemperCount = 20;
        private const string KitUsage = "Use gm.kit gear [level] [rarity], gm.kit temper [count], gm.kit pets or gm.kit all.";

        [Command("gm.kit", "Give the targeted player a whole kit at once. gm.kit gear [level] [rarity]: every piece that character can " +
                           "wear at that tier (112 Epic unless told), the weapon of every class of its faction included. gm.kit temper " +
                           "[count]: every Temper item that has a use (20 of each). gm.kit pets: one of each pet. gm.kit all: the three together.")]
        private static string Kit(string kit, string first = "", string second = "")
        {
            if (!TryGetInventory(out var inv, out var error)) return error;
            if (inv.Catalog == null) return NoPlayer;
            switch (kit.ToLowerInvariant())
            {
                case "gear": return KitGear(inv, first, second);
                case "temper": return KitTemper(inv, first);
                case "pets": return KitPets(inv);
                case "all": return KitGear(inv, string.Empty, string.Empty) + "\n" + KitTemper(inv, string.Empty) + "\n" + KitPets(inv);
                default: return $"Unknown kit '{kit}'. {KitUsage}";
            }
        }

        /// <summary>One of every piece the wearer can use at the tier a level snaps to, slot by slot.</summary>
        private static string KitGear(PlayerInventory inv, string levelText, string rarityText)
        {
            if (inv.Gear == null) return NoPlayer;
            int level = KitLevel;
            if (levelText.Length > 0 && !int.TryParse(levelText, out level)) return $"'{levelText}' is not a level. {KitUsage}";
            ItemRarity rarity = ItemRarity.Epic;
            if (rarityText.Length > 0 && !System.Enum.TryParse(rarityText, true, out rarity))
                return $"Unknown rarity '{rarityText}'. Use common, uncommon, rare or epic.";

            var wearer = inv.GetComponent<IGearWearer>();
            FactionId faction = wearer != null ? wearer.Faction : FactionId.None;
            BodyFit body = wearer != null ? wearer.Body : BodyFit.None;
            string pack = wearer != null ? wearer.GearPack : null;

            var fits = new List<GearDictionary.Entry>(16);
            int given = 0, lost = 0, slots = 0;
            foreach (EquipSlot slot in EquipSlots.All)
            {
                if (slot == EquipSlot.Pet) continue;
                // The same matching as gm.gear, but every line of the slot instead of the first.
                fits.Clear();
                foreach (var entry in inv.Gear.Entries)
                {
                    if (entry.slot != slot || entry.rarity != rarity) continue;
                    if (faction != FactionId.None && !FactionRules.Includes(entry.factions, faction)) continue;
                    if (body != BodyFit.None && (entry.bodies & body) == 0) continue;
                    if (slot != EquipSlot.Weapon && !string.IsNullOrEmpty(pack) && !string.IsNullOrEmpty(entry.pack) && entry.pack != pack) continue;
                    fits.Add(entry);
                }
                if (fits.Count == 0) continue;

                // The level snaps down to the nearest tier this slot has (and up, when there is none below).
                int below = int.MinValue, above = int.MaxValue;
                foreach (var entry in fits)
                {
                    if (entry.itemLevel <= level && entry.itemLevel > below) below = entry.itemLevel;
                    if (entry.itemLevel > level && entry.itemLevel < above) above = entry.itemLevel;
                }
                int tier = below != int.MinValue ? below : above;
                slots++;
                foreach (var entry in fits)
                {
                    if (entry.itemLevel != tier || inv.Catalog.Get(entry.itemId) == null) continue;
                    if (inv.ServerGive(entry.itemId, 1) > 0) given++; else lost++;
                }
            }
            if (given == 0 && lost == 0) return $"No {rarity} gear fits this character. Re-bake the gear ladder.";
            return $"Gear kit: {given} {rarity} pieces near level {level} across {slots} slots." + Unfitted(lost);
        }

        /// <summary>Every Temper material that can be used today, straight from the forge's settings.</summary>
        private static string KitTemper(PlayerInventory inv, string countText)
        {
            int count = KitTemperCount;
            if (countText.Length > 0 && (!int.TryParse(countText, out count) || count < 1)) return $"'{countText}' is not a count. {KitUsage}";
            var work = inv.GetComponent<PlayerGearWork>();
            if (work == null || work.Settings == null) return "That player has no forge (run Krytical Nova/Setup/Player Prefab).";
            TemperSettings temper = work.Settings.temper;

            var ids = new List<ushort>(10);
            foreach (AlloyGrade alloy in temper.alloys)
                if (!alloy.prime || GearProgressionRules.PrimeAlloyInUse(temper)) ids.Add(alloy.itemId);
            ids.Add(temper.failsafeItemId);
            ids.Add(temper.luckyFailsafeItemId);
            ids.Add(temper.advancedFailsafeItemId);

            int kinds = 0, lost = 0;
            foreach (ushort id in ids)
            {
                if (id == 0 || inv.Catalog.Get(id) == null) continue;
                if (inv.ServerGive(id, count) > 0) kinds++; else lost++;
            }
            return $"Temper kit: {count} each of {kinds} materials." + Unfitted(lost);
        }

        /// <summary>One of each pet in the catalog.</summary>
        private static string KitPets(PlayerInventory inv)
        {
            int given = 0, lost = 0;
            foreach (ItemDefinition def in inv.Catalog.Items)
            {
                if (def == null || def.equipSlot != EquipSlot.Pet) continue;
                if (inv.ServerGive(def.itemId, 1) > 0) given++; else lost++;
            }
            if (given == 0 && lost == 0) return "There are no pets in the catalog.";
            return $"Pet kit: {given} pets." + Unfitted(lost);
        }

        private static string Unfitted(int lost) => lost > 0 ? $" {lost} did not fit: the Pack is full." : string.Empty;

        private static string ClassList()
        {
            var sb = new StringBuilder("Classes: ");
            for (int i = 0; i < WeaponClasses.All.Length; i++)
                sb.Append(i > 0 ? ", " : string.Empty).Append(WeaponClasses.All[i].ToString().ToLowerInvariant());
            return sb.Append('.').ToString();
        }

        [Command("gm.drop", "Put an item on the ground in front of the targeted player, free for anyone to take: " +
                            "gm.drop <id|name> [count], e.g. gm.drop Pill_Vitality 5.")]
        private static string Drop(string item, int count = 1)
        {
            if (!TryGetInventory(out var inv, out var error)) return error;
            if (inv.Catalog == null) return NoPlayer;
            var def = inv.Catalog.Find(item);
            if (def == null) return $"Unknown item '{item}'. Try item.list.";
            var loot = inv.GetComponent<PlayerLoot>();
            if (loot == null) return "That player cannot drop items (run Krytical Nova/Setup/Player Prefab).";
            count = System.Math.Max(1, System.Math.Min(count, def.maxStack));
            var drop = loot.ServerPlace(new ItemStack(def.itemId, (ushort)count));
            return drop != null ? $"Dropped {LootRules.Label(def, count)}." : "Nothing was dropped (no drop prefab on the player).";
        }

        [Command("item.clear", "Empty the local player's Pack (equipment is kept).")]
        private static string Clear()
        {
            if (!TryGetInventory(out var inv, out var error)) return error;
            inv.ServerClear();
            return "Pack cleared.";
        }

        [Command("item.list", "List every item id in the catalog, optionally filtered by slot (e.g. item.list Armor).")]
        private static string List(string slotFilter = "")
        {
            var inv = PlayerInventory.Local;
            if (inv == null || inv.Catalog == null) return NoPlayer;
            var sb = new StringBuilder();
            foreach (var def in inv.Catalog.Items)
            {
                if (def == null) continue;
                if (slotFilter.Length > 0 && !def.equipSlot.ToString().Equals(slotFilter, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                sb.Append(def.itemId).Append("  ").Append(def.equipSlot).Append("  ").Append(def.displayName)
                  .Append("  (").Append(def.name).AppendLine(")");
            }
            return sb.Length > 0 ? sb.ToString() : "No items.";
        }
    }
}
