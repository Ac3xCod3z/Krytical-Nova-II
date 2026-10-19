using System;
using System.Collections.Generic;

namespace KryticalNova.Core
{
    /// <summary>Keeps everything in memory. For tests.</summary>
    public sealed class MemorySaveStore : IBatchSaveStore, ILedgerStore
    {
        private readonly Dictionary<string, string> documents = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<LedgerEntry> ledger = new List<LedgerEntry>();

        public int Writes { get; private set; }
        /// <summary>How many batches were written (a trade is one).</summary>
        public int Batches { get; private set; }

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

        public void WriteBatch(SaveBatch batch)
        {
            if (batch == null || batch.IsEmpty) return;
            Batches++;
            foreach (KeyValuePair<string, string> write in batch.Writes) Write(write.Key, write.Value);
            foreach (string key in batch.Deletes) Delete(key);
            ledger.AddRange(batch.Ledger);
        }

        public bool TryReadMany(IReadOnlyList<string> keys, IDictionary<string, string> found)
        {
            foreach (string key in keys)
                if (TryRead(key, out string json)) found[key] = json;
            return true;
        }

        public List<LedgerEntry> ReadLedger(string account, long sinceUtc, int limit)
        {
            var lines = new List<LedgerEntry>();
            for (int i = ledger.Count - 1; i >= 0 && lines.Count < limit; i--)
                if ((account == null || ledger[i].account == account) && ledger[i].utc >= sinceUtc) lines.Add(ledger[i]);
            return lines;
        }

        public int PruneLedger(long beforeUtc) => ledger.RemoveAll(line => line.utc < beforeUtc);
    }
}
