using KryticalNova.Factions;

namespace KryticalNova.Items
{
    /// <summary>
    /// A faction's own items (owner, 11 Oct 2026: a faction's furnishings are for that faction only). Such an item
    /// names its faction (<see cref="ItemDefinition.boundFaction"/>) and one question is asked wherever it could
    /// change hands: may this character have it. A trade, a Kiosk sale and a pickup ask it for the receiving
    /// character; a Kiosk still shows the item to everyone. It is kept in the Vault, which is one character's, and
    /// never in the Deep Vault, which an account's characters of any faction share.
    /// </summary>
    public static class ItemFactionRules
    {
        public const string ReasonOtherFaction = "That belongs to another faction: only its own may have it.";
        public const string ReasonTheirFaction = "That is for your own faction only: they cannot take it.";
        public const string ReasonNotDeepVault = "A faction's own items are kept in the Vault, not the Deep Vault.";

        public static bool IsBound(ItemDefinition item) => item != null && item.boundFaction != FactionId.None;

        public static bool MayHave(ItemDefinition item, FactionId faction) => !IsBound(item) || item.boundFaction == faction;
    }
}
