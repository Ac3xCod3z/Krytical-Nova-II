using System;
using UnityEngine;

namespace KryticalNova.Wallet
{
    /// <summary>
    /// A line for the kept ledger from a system below Economy (a Crew founding). Economy's <c>EconomyLedger</c> writes it
    /// as it writes every other line, so the ledger keeps one way in (owner, 18 Oct 2026). Filled when the game loads.
    /// </summary>
    public static class LedgerNote
    {
        /// <summary>Writes (player, kind from <c>LedgerKinds</c>, note). Filled by Economy.</summary>
        public static Action<Component, string, string> Writer;

        /// <summary>Keeps a line for <paramref name="player"/>; an error, never a silent loss, when nothing writes it.</summary>
        public static void Keep(Component player, string kind, string note)
        {
            if (Writer != null) Writer(player, kind, note);
            else Debug.LogError($"[Ledger] No ledger is loaded: the line \"{kind}: {note}\" was not kept.");
        }
    }
}
