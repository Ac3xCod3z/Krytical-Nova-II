using System.Collections.Generic;

namespace KryticalNova.Core
{
    /// <summary>
    /// A world document that is a list of separate entries (online phase 4, milestone 4): every Crew, every character's
    /// Contacts, every Kiosk, every Sponsor link. The saves keep each entry as a document of its own
    /// (<c>world/crews/&lt;crew id&gt;</c>) beside a small header listing them, so a change to one Crew writes that Crew only,
    /// and two servers changing two different Crews never write over each other.
    /// </summary>
    public interface IWorldEntries
    {
        /// <summary>
        /// Every entry, in order: its key (lower-case letters, digits, '-' or '_': an id) and its text. Two entries never share
        /// a key.
        /// </summary>
        void CollectEntries(List<KeyValuePair<string, string>> into);

        /// <summary>Puts the entries back from their texts, in the order given (the order they were collected in).</summary>
        void RestoreEntries(IReadOnlyList<string> texts);
    }
}
