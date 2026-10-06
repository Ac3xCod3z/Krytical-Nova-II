using System;
using System.Collections.Generic;

namespace KryticalNova.Core
{
    /// <summary>Keeps everything in memory. For tests.</summary>
    public sealed class MemorySaveStore : ISaveStore
    {
        private readonly Dictionary<string, string> documents = new Dictionary<string, string>(StringComparer.Ordinal);

        public int Writes { get; private set; }

        public bool TryRead(string key, out string json) => documents.TryGetValue(key ?? string.Empty, out json) && !string.IsNullOrEmpty(json);

        public void Write(string key, string json)
        {
            if (!SaveKeys.IsValid(key)) return;
            documents[key] = json ?? string.Empty;
            Writes++;
        }

        public void Delete(string key) => documents.Remove(key ?? string.Empty);

        public IReadOnlyList<string> Keys(string prefix)
        {
            var keys = new List<string>();
            foreach (string key in documents.Keys)
                if (string.IsNullOrEmpty(prefix) || key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(key);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
    }
}
