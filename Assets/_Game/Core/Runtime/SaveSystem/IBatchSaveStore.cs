using System.Collections.Generic;

namespace KryticalNova.Core
{
    /// <summary>
    /// A store that writes several documents and ledger lines as one change and reads many in one trip (online phase 4).
    /// A player's save is the parts that changed and its header, written together or not at all; a trade is both
    /// players' saves and the ledger lines, together (milestone 3). The save service does it in one database
    /// transaction; files through a journal (<see cref="FileSaveStore"/>).
    /// </summary>
    public interface IBatchSaveStore : ISaveStore
    {
        /// <summary>Writes, deletes and ledger lines together: all of them, or none. Returns at once; the store keeps the order.</summary>
        void WriteBatch(SaveBatch batch);

        /// <summary>Puts every document of <paramref name="keys"/> that exists into <paramref name="found"/>. False when the store could not be asked.</summary>
        bool TryReadMany(IReadOnlyList<string> keys, IDictionary<string, string> found);
    }
}
