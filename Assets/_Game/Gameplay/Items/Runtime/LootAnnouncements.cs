using KryticalNova.Core;
using KryticalNova.Social;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// News of a rare find (owner, 15 Oct 2026): when a piece of Epic loot falls from a Hostile, a boss, or any other
    /// source that asks, every player on the server reads it on the HUD's looping ticker and in chat, with the item's
    /// name in the colour of its rarity (purple for Epic). A source calls <see cref="ServerFound"/> once for each item
    /// it hands out; the rarity that is worth announcing is a setting of that source (<c>HostileSettings</c>).
    /// </summary>
    public static class LootAnnouncements
    {
        /// <summary>True when an item of this rarity is announced: switched on, and at or above the lowest rarity named.</summary>
        public static bool IsAnnounced(bool enabled, ItemRarity from, ItemRarity rarity) => enabled && rarity >= from;

        /// <summary>The line every player reads: "Arinis found Sunlord Blade."</summary>
        public static string Text(string player, string itemName) => $"{player} found {itemName}.";

        /// <summary>
        /// Server (or offline): tells every player that <paramref name="finder"/> found <paramref name="item"/>.
        /// Nothing happens when the item is below the rarity worth announcing.
        /// </summary>
        public static void ServerFound(GameObject finder, ItemDefinition item, bool enabled, ItemRarity from)
        {
            if (finder == null || item == null || !IsAnnounced(enabled, from, item.rarity)) return;
            string player = ServerChat.NameOf(finder);
            ServerChat.Announce?.Invoke(AnnouncementKind.Drop, Text(player, item.displayName), player + "|" + item.displayName, (byte)item.rarity);
        }
    }
}
