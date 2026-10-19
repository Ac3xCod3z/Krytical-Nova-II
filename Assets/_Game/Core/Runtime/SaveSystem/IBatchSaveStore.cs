using System.Collections.Generic;

namespace KryticalNova.Core
{
    /// <summary>
    /// A store that can write several documents as one change and read many in one trip (online phase 4, milestone 2:
    /// the save service). A player's save is the parts that changed and its header, written together or not at all; a
    /// character's parts are read in one go when it loads. A store without it (files) is written one key at a time.
    /// </summary>
    public interface IBatchSaveStore : ISaveStore
    {
        /// <summary>Writes and deletes together: all of them, or none. Returns at once; the store sends it in order.</summary>
        void WriteBatch(IReadOnlyList<KeyValuePair<string, string>> writes, IReadOnlyList<string> deletes);

        /// <summary>Puts every document of <paramref name="keys"/> that exists into <paramref name="found"/>. False when the store could not be asked.</summary>
        bool TryReadMany(IReadOnlyList<string> keys, IDictionary<string, string> found);
    }
}
