using System.Text;
using KryticalNova.Factions;
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
