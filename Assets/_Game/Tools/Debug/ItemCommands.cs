using System.Text;
using KryticalNova.Items;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console item commands. Like the GM commands they only run where the game is decided (host, server or
    /// offline), because they call the server-only inventory API, and act on the player picked by <see cref="GmTarget"/>.
    /// </summary>
    public static class ItemCommands
    {
        private const string NoPlayer = "No local player inventory.";

        private static bool TryGetInventory(out PlayerInventory inventory, out string error)
        {
            inventory = null;
            if (!GmTarget.TryGetPlayer(out var player, out error)) return false;
            inventory = player.GetComponent<PlayerInventory>();
            if (inventory == null) error = NoPlayer;
            return inventory != null;
        }

        [Command("item.give", "Give an item by id, asset name or display name, e.g. item.give 2001 or item.give Armor_SOLD_03")]
        [Command("give_item")]
        private static string Give(string item, int count = 1)
        {
            if (!TryGetInventory(out var inv, out var error)) return error;
            if (inv.Catalog == null) return NoPlayer;
            var def = inv.Catalog.Find(item);
            if (def == null) return $"Unknown item '{item}'. Try item.list.";
            int added = inv.ServerGive(def.itemId, count);
            return added > 0 ? $"Gave {added}x {def.displayName}." : "Inventory is full.";
        }

        [Command("item.clear", "Empty the local player's bag (equipment is kept).")]
        [Command("clear_inventory")]
        private static string Clear()
        {
            if (!TryGetInventory(out var inv, out var error)) return error;
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
