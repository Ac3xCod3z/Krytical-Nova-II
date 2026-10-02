using System.Text;
using KryticalNova.Items;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console item commands. Like the world commands they act on the authority's own player only (host or
    /// offline), because they call the server-only inventory API.
    /// </summary>
    public static class ItemCommands
    {
        private const string NoPlayer = "No local player inventory.";
        private const string NoAuthority = "Only the host/offline player can spawn items.";

        [Command("item.give", "Give an item by id, asset name or display name, e.g. item.give 2001 or item.give Armor_SOLD_03")]
        [Command("give_item")]
        private static string Give(string item, int count = 1)
        {
            var inv = PlayerInventory.Local;
            if (inv == null || inv.Catalog == null) return NoPlayer;
            if (inv.IsSpawned && !inv.IsServer) return NoAuthority;
            var def = inv.Catalog.Find(item);
            if (def == null) return $"Unknown item '{item}'. Try item.list.";
            int added = inv.ServerGive(def.itemId, count);
            return added > 0 ? $"Gave {added}x {def.displayName}." : "Inventory is full.";
        }

        [Command("item.clear", "Empty the local player's bag (equipment is kept).")]
        [Command("clear_inventory")]
        private static string Clear()
        {
            var inv = PlayerInventory.Local;
            if (inv == null) return NoPlayer;
            if (inv.IsSpawned && !inv.IsServer) return NoAuthority;
            inv.ServerClear();
            return "Inventory cleared.";
        }

        [Command("item.list", "List every item id in the catalog, optionally filtered by slot (e.g. item.list Armor).")]
        [Command("list_items")]
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
