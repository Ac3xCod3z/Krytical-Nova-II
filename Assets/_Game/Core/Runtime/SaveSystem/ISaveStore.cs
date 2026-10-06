using System.Collections.Generic;

namespace KryticalNova.Core
{
    /// <summary>
    /// Where the server keeps what must outlive a session: a flat set of named JSON documents. Files on the host
    /// today (<see cref="FileSaveStore"/>); a database or a cloud service replaces it later without any caller
    /// changing. Keys are lower-case paths such as "accounts/3f2a…" or "world/crews".
    /// </summary>
    public interface ISaveStore
    {
        /// <summary>False when nothing is stored under the key (or it cannot be read).</summary>
        bool TryRead(string key, out string json);

        /// <summary>Replaces the document. A reader never sees a half-written one.</summary>
        void Write(string key, string json);

        void Delete(string key);

        /// <summary>Every key that starts with <paramref name="prefix"/> (for example "accounts/").</summary>
        IReadOnlyList<string> Keys(string prefix);
    }
}
