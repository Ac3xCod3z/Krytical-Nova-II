using System.Collections.Generic;

namespace KryticalNova.Core
{
    /// <summary>
    /// One change to the saves that happens whole or not at all (online phase 4, milestone 3): the documents it writes and
    /// deletes, and the ledger lines that say what moved. A player's autosave is one; a trade is one for both players; a
    /// Kiosk sale is one for the buyer and the Kiosks. A crash before it reaches the store loses all of it, never part.
    /// </summary>
    public sealed class SaveBatch
    {
        public readonly List<KeyValuePair<string, string>> Writes = new List<KeyValuePair<string, string>>();
        public readonly List<string> Deletes = new List<string>();
        public readonly List<LedgerEntry> Ledger = new List<LedgerEntry>();

        public bool IsEmpty => Writes.Count == 0 && Deletes.Count == 0 && Ledger.Count == 0;

        public void Write(string key, string json) => Writes.Add(new KeyValuePair<string, string>(key, json ?? string.Empty));
    }
}
