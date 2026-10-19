using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Persistence;
using KryticalNova.Player;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>
    /// The item lines of the kept ledger (online phase 4, milestone 3): what one player's account gained or gave up in one
    /// economy action, with the other side when there is one. Creds are not written here: the wallet tallies every Cred
    /// itself (<see cref="PlayerWallet"/>), so the ledger adds up to the wallets whatever moved them. A line is saved with
    /// the player's next save: the action saves right after (with <see cref="ServerSave.SaveTogether"/> when it changes
    /// another player or a world document too), so the line and the change it describes are one.
    /// <para>Every economy action, now and after a rework (owner, 18 Oct 2026), writes its item lines here.</para>
    /// </summary>
    public static class EconomyLedger
    {
        /// <summary>A line for <paramref name="player"/>, naming <paramref name="other"/> when there is one; null while nothing is loaded.</summary>
        public static LedgerEntry Line(Component player, string kind, Component other = null, string note = null)
        {
            PlayerSaveAgent agent = player != null ? player.GetComponent<PlayerSaveAgent>() : null;
            LedgerEntry line = agent != null ? agent.NewLedgerLine(kind) : null;
            if (line == null) return null;
            line.note = note;
            if (other != null && other.TryGetComponent(out PlayerSaveAgent them))
            {
                line.otherAccount = them.AccountId;
                line.otherCharacter = them.CharacterId;
                line.otherName = other.TryGetComponent(out PlayerIdentity identity) ? identity.CharacterName : null;
            }
            return line;
        }

        /// <summary>Adds <paramref name="stack"/>, gained (<paramref name="sign"/> 1) or given up (-1).</summary>
        public static LedgerEntry Item(this LedgerEntry line, ItemCatalog catalog, ItemStack stack, int sign)
        {
            if (line == null || stack.IsEmpty) return line;
            ItemDefinition item = catalog != null ? catalog.Get(stack.itemId) : null;
            return line.With(stack.itemId, sign * stack.count, item != null ? item.displayName : null);
        }

        /// <summary>Keeps the line (when it says anything) for the player's next save.</summary>
        public static void Keep(LedgerEntry line)
        {
            if (line != null && (line.items.Count > 0 || line.creds != 0 || !string.IsNullOrEmpty(line.note))) ServerSave.Ledger(line);
        }
    }
}
