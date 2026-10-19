using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// One JSON file per key under a root folder ("accounts/3f2a" is "&lt;root&gt;/accounts/3f2a.json"). A write goes to a
    /// temporary file first and then replaces the old one, so a crash mid-write leaves the previous save intact.
    /// <para>A change of several documents, or one with ledger lines (<see cref="WriteBatch"/>: a trade, a player's save),
    /// is all or nothing (online phase 4, milestone 3): it is first written whole to a journal file
    /// (<c>_journal/</c>), then put in place, then the journal is deleted. A journal left by a crash is put in place when
    /// the store is next opened, so either every document of the change is saved or none is.</para>
    /// <para>The ledger is kept in a file a day (<c>_ledger/2026-10-18.log</c>, one line each); a line replayed from a
    /// journal is never written twice.</para>
    /// <para>Made with <c>background</c> on, a change returns at once and is written by a thread of the store's own, in
    /// the order the changes were handed over: the disk took 3.5 ms of a frame for every save, on the frame the save fell
    /// on. What is waiting is the truth meanwhile: a read gives it back. <see cref="Flush"/> waits until the disk has
    /// everything; the game calls it when it quits. Without <c>background</c> every change is on disk when it returns.</para>
    /// </summary>
    public sealed class FileSaveStore : IBatchSaveStore, ILedgerStore, IDisposable
    {
        private const string Extension = ".json";
        private const string TempExtension = ".tmp";
        private const string JournalFolder = "_journal";
        private const string JournalExtension = ".journal";
        private const string LedgerFolder = "_ledger";
        private const string LedgerExtension = ".log";
        private const int IdleMilliseconds = 2000;
        private const int ReplaceAttempts = 6;

        [Serializable]
        private sealed class Doc
        {
            public string key;
            public string text;
            public bool delete;
        }

        // One change, written whole or not at all.
        [Serializable]
        private sealed class Unit
        {
            public List<Doc> docs = new List<Doc>();
            public List<LedgerEntry> ledger = new List<LedgerEntry>();
        }

        private readonly string root;
        private readonly bool background;
        // Changes handed over and not on disk yet, in order; the newest text of each key in them (null: deleted), and how
        // many changes still carry it, so a read gives back what will be there.
        private readonly Queue<Unit> units = new Queue<Unit>();
        private readonly Dictionary<string, string> waiting = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> carried = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly object gate = new object();
        // Held while files are read, written or deleted, so the writer and the game never touch one at once.
        private readonly object disk = new object();
        private Thread writer;
        private bool applying;
        private bool closed;
        private long journalNumber;

        public FileSaveStore(string rootFolder, bool background = false)
        {
            root = Path.GetFullPath(rootFolder);
            this.background = background;
            Recover();
        }

        public string Root => root;
        /// <summary>True when writes are handed to the store's own thread.</summary>
        public bool Background => background;

        // ------------------------------------------------------------------ reading

        public bool TryRead(string key, out string json)
        {
            json = null;
            if (!TryPath(key, out string path)) return false;
            if (background)
                lock (gate)
                    if (waiting.TryGetValue(key, out json)) return !string.IsNullOrEmpty(json);
            lock (disk)
            {
                if (!File.Exists(path)) return false;
                try
                {
                    json = File.ReadAllText(path, Encoding.UTF8);
                    return !string.IsNullOrEmpty(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Save] Could not read '{key}': {e.Message}");
                    return false;
                }
            }
        }

        public bool TryReadMany(IReadOnlyList<string> keys, IDictionary<string, string> found)
        {
            foreach (string key in keys)
                if (TryRead(key, out string json)) found[key] = json;
            return true;
        }

        public IReadOnlyList<string> Keys(string prefix)
        {
            Flush(); // a key that is only waiting has no file yet
            var keys = new List<string>();
            if (!Directory.Exists(root)) return keys;
            foreach (string file in Directory.GetFiles(root, "*" + Extension, SearchOption.AllDirectories))
            {
                if (!file.EndsWith(Extension, StringComparison.Ordinal)) continue;
                string key = file.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                key = key.Substring(0, key.Length - Extension.Length);
                if (key.StartsWith(JournalFolder + "/", StringComparison.Ordinal) || key.StartsWith(LedgerFolder + "/", StringComparison.Ordinal)) continue;
                if (string.IsNullOrEmpty(prefix) || key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(key);
            }
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        // ------------------------------------------------------------------ writing

        public void Write(string key, string json)
        {
            if (!TryPath(key, out _)) return;
            var unit = new Unit();
            unit.docs.Add(new Doc { key = key, text = json ?? string.Empty });
            Hand(unit);
        }

        public void Delete(string key)
        {
            if (!TryPath(key, out _)) return;
            var unit = new Unit();
            unit.docs.Add(new Doc { key = key, delete = true });
            Hand(unit);
        }

        public void WriteBatch(SaveBatch batch)
        {
            if (batch == null || batch.IsEmpty) return;
            var unit = new Unit();
            foreach (KeyValuePair<string, string> write in batch.Writes)
                if (TryPath(write.Key, out _)) unit.docs.Add(new Doc { key = write.Key, text = write.Value ?? string.Empty });
            foreach (string key in batch.Deletes)
                if (TryPath(key, out _)) unit.docs.Add(new Doc { key = key, delete = true });
            foreach (LedgerEntry line in batch.Ledger)
                if (line != null) unit.ledger.Add(line);
            if (unit.docs.Count > 0 || unit.ledger.Count > 0) Hand(unit);
        }

        private void Hand(Unit unit)
        {
            if (!background)
            {
                // The disk's share of a save, with how much text went out.
                int length = 0;
                foreach (Doc doc in unit.docs) length += doc.text != null ? doc.text.Length : 0;
                using var timed = KryticalNova.Diagnostics.PerformanceLog.Time("save.file", unit.docs.Count == 1 ? unit.docs[0].key : "batch", length);
                lock (disk) Apply(unit);
                return;
            }
            lock (gate)
            {
                if (closed)
                {
                    // After the last flush (the game is closing): straight to disk, nothing may be left behind.
                    lock (disk) Apply(unit);
                    return;
                }
                foreach (Doc doc in unit.docs)
                {
                    waiting[doc.key] = doc.delete ? null : doc.text;
                    carried[doc.key] = carried.TryGetValue(doc.key, out int n) ? n + 1 : 1;
                }
                units.Enqueue(unit);
                if (writer == null)
                {
                    writer = new Thread(WriteWaiting) { IsBackground = true, Name = "Save Writer" };
                    writer.Start();
                }
                Monitor.PulseAll(gate);
            }
        }

        // The store's own thread: puts the changes on disk in order. It ends when it has had nothing to do for a while,
        // and is started again by the next change.
        private void WriteWaiting()
        {
            while (true)
            {
                Unit unit;
                lock (gate)
                {
                    while (units.Count == 0)
                    {
                        if (!Monitor.Wait(gate, IdleMilliseconds) && units.Count == 0)
                        {
                            writer = null;
                            Monitor.PulseAll(gate);
                            return;
                        }
                    }
                    unit = units.Peek();
                    applying = true;
                }

                // Named apart from "save.file": this time is not the frame's.
                int length = 0;
                foreach (Doc doc in unit.docs) length += doc.text != null ? doc.text.Length : 0;
                var timed = KryticalNova.Diagnostics.PerformanceLog.Time("save.disk", unit.docs.Count == 1 ? unit.docs[0].key : "batch", length);
                lock (disk) Apply(unit);
                timed.End(length);

                lock (gate)
                {
                    units.Dequeue();
                    Let(unit);
                    applying = false;
                    Monitor.PulseAll(gate);
                }
            }
        }

        private void Let(Unit unit)
        {
            foreach (Doc doc in unit.docs)
            {
                if (!carried.TryGetValue(doc.key, out int n)) continue;
                if (n > 1) carried[doc.key] = n - 1;
                else
                {
                    carried.Remove(doc.key);
                    waiting.Remove(doc.key);
                }
            }
        }

        /// <summary>Waits until everything handed over is on disk. Nothing to wait for without <c>background</c>.</summary>
        public void Flush()
        {
            if (!background) return;
            lock (gate)
            {
                while (units.Count > 0 || applying)
                {
                    // A writer that was stopped with its domain (the Editor recompiling) leaves this thread to finish.
                    if (writer == null || !writer.IsAlive)
                    {
                        writer = null;
                        applying = false;
                        while (units.Count > 0)
                        {
                            Unit unit = units.Dequeue();
                            lock (disk) Apply(unit);
                            Let(unit);
                        }
                        return;
                    }
                    Monitor.Wait(gate, 50);
                }
            }
        }

        /// <summary>Flushes, and from then on writes straight to disk: the game is closing.</summary>
        public void Dispose()
        {
            Flush();
            lock (gate) closed = true;
        }

        // ------------------------------------------------------------------ one change, whole (called holding the disk)

        private void Apply(Unit unit)
        {
            // A single document needs no journal: its file is replaced in one step.
            bool whole = unit.docs.Count > 1 || unit.ledger.Count > 0;
            string journal = whole ? WriteJournal(unit) : null;
            PutInPlace(unit, replayed: false);
            if (journal != null) TryDelete(journal);
        }

        private void PutInPlace(Unit unit, bool replayed)
        {
            foreach (Doc doc in unit.docs)
            {
                if (!TryPath(doc.key, out string path)) continue;
                if (doc.delete) DeleteFile(doc.key, path);
                else WriteFile(doc.key, path, doc.text);
            }
            if (unit.ledger.Count > 0) AppendLedger(unit.ledger, replayed);
        }

        private string WriteJournal(Unit unit)
        {
            try
            {
                string folder = Path.Combine(root, JournalFolder);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, DateTime.UtcNow.Ticks.ToString("D19", CultureInfo.InvariantCulture) + "-" +
                                                   Interlocked.Increment(ref journalNumber).ToString("D6", CultureInfo.InvariantCulture) + JournalExtension);
                string temp = path + TempExtension;
                File.WriteAllText(temp, JsonUtility.ToJson(unit), Encoding.UTF8);
                File.Move(temp, path);
                return path;
            }
            catch (Exception e)
            {
                // Without its journal the change is still written, only not guarded against a crash halfway.
                Debug.LogError($"[Save] Could not write the journal of a change of {unit.docs.Count} document(s): {e.Message}");
                return null;
            }
        }

        // A change the last run journaled but may not have finished: put in place now, before anything is read.
        private void Recover()
        {
            string folder = Path.Combine(root, JournalFolder);
            if (!Directory.Exists(folder)) return;
            string[] journals = Directory.GetFiles(folder, "*" + JournalExtension);
            Array.Sort(journals, StringComparer.Ordinal);
            lock (disk)
            {
                foreach (string journal in journals)
                {
                    try
                    {
                        Unit unit = JsonUtility.FromJson<Unit>(File.ReadAllText(journal, Encoding.UTF8));
                        if (unit != null)
                        {
                            unit.docs ??= new List<Doc>();
                            unit.ledger ??= new List<LedgerEntry>();
                            PutInPlace(unit, replayed: true);
                            Debug.Log($"[Save] A change of {unit.docs.Count} save(s) and {unit.ledger.Count} ledger line(s) that a stop cut short was finished from its journal.");
                        }
                        TryDelete(journal);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Save] The journal {Path.GetFileName(journal)} could not be read and was set aside: {e.Message}");
                        try { File.Move(journal, journal + ".unreadable"); } catch (Exception) { /* left where it is */ }
                    }
                }
                // A journal that was never finished being written is not a change: nothing of it was put in place.
                foreach (string temp in Directory.GetFiles(folder, "*" + TempExtension)) TryDelete(temp);
            }
        }

        private void WriteFile(string key, string path, string json)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + TempExtension;
                File.WriteAllText(temp, json ?? string.Empty, Encoding.UTF8);
                PutInPlace(temp, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not write '{key}': {e.Message}");
            }
        }

        private static void DeleteFile(string key, string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not delete '{key}': {e.Message}");
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogWarning($"[Save] Could not delete {Path.GetFileName(path)}: {e.Message}"); }
        }

        // The new file takes the old one's place in one step. Another program may hold the old file open for a moment
        // (antivirus scanning a file just written, the search indexer): Windows then refuses ("Unable to remove the file
        // to be replaced", seen on the server 18 Oct 2026), so it is tried again a few times, a little later each time
        // (under a second in all), before the failure is reported.
        private static void PutInPlace(string temp, string path)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(temp, path, null);
                    else File.Move(temp, path);
                    return;
                }
                catch (IOException) when (attempt < ReplaceAttempts)
                {
                    Thread.Sleep(50 * attempt);
                }
                catch (UnauthorizedAccessException) when (attempt < ReplaceAttempts)
                {
                    Thread.Sleep(50 * attempt);
                }
            }
        }

        // ------------------------------------------------------------------ the ledger

        // Appends the lines to their day's file. Replayed from a journal, a line already there is left out.
        private void AppendLedger(List<LedgerEntry> lines, bool replayed)
        {
            try
            {
                string folder = Path.Combine(root, LedgerFolder);
                Directory.CreateDirectory(folder);
                var byDay = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
                var known = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (LedgerEntry line in lines)
                {
                    string path = Path.Combine(folder, DayName(line.utc) + LedgerExtension);
                    if (replayed)
                    {
                        if (!known.TryGetValue(path, out string text)) known[path] = text = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
                        if (!string.IsNullOrEmpty(line.id) && text.Contains("\"id\":\"" + line.id + "\"")) continue;
                    }
                    if (!byDay.TryGetValue(path, out StringBuilder sb)) byDay[path] = sb = new StringBuilder();
                    sb.Append(JsonUtility.ToJson(line)).Append('\n');
                }
                foreach (KeyValuePair<string, StringBuilder> day in byDay)
                {
                    // A line a stop cut off halfway ends here, so the next one starts on a line of its own.
                    string start = EndsOpen(day.Key) ? "\n" : string.Empty;
                    File.AppendAllText(day.Key, start + day.Value, Encoding.UTF8);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not write {lines.Count} ledger line(s): {e.Message}");
            }
        }

        private static bool EndsOpen(string path)
        {
            if (!File.Exists(path)) return false;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length == 0) return false;
                stream.Seek(-1, SeekOrigin.End);
                return stream.ReadByte() != '\n';
            }
        }

        private static string DayName(long utcTicks) => new DateTime(utcTicks, DateTimeKind.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public List<LedgerEntry> ReadLedger(string account, long sinceUtc, int limit)
        {
            Flush();
            var found = new List<LedgerEntry>();
            string folder = Path.Combine(root, LedgerFolder);
            if (!Directory.Exists(folder)) return found;
            string[] days = Directory.GetFiles(folder, "*" + LedgerExtension);
            Array.Sort(days, StringComparer.Ordinal);
            string firstDay = DayName(Math.Max(0, sinceUtc));
            lock (disk)
                for (int d = days.Length - 1; d >= 0 && found.Count < limit; d--)
                {
                    if (string.CompareOrdinal(Path.GetFileNameWithoutExtension(days[d]), firstDay) < 0) break;
                    string[] lines = File.ReadAllLines(days[d], Encoding.UTF8);
                    for (int i = lines.Length - 1; i >= 0 && found.Count < limit; i--)
                    {
                        LedgerEntry line = Parse(lines[i]);
                        if (line != null && line.utc >= sinceUtc && (account == null || line.account == account)) found.Add(line);
                    }
                }
            found.Sort((x, y) => y.utc.CompareTo(x.utc));
            return found;
        }

        public int PruneLedger(long beforeUtc)
        {
            string folder = Path.Combine(root, LedgerFolder);
            if (!Directory.Exists(folder)) return 0;
            // A whole day goes once its last moment is older than the limit; the day across the limit stays whole.
            string keepFrom = DayName(Math.Max(0, beforeUtc));
            int dropped = 0;
            lock (disk)
                foreach (string day in Directory.GetFiles(folder, "*" + LedgerExtension))
                {
                    if (string.CompareOrdinal(Path.GetFileNameWithoutExtension(day), keepFrom) >= 0) continue;
                    try
                    {
                        dropped += File.ReadAllLines(day, Encoding.UTF8).Length;
                        File.Delete(day);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Save] Could not drop the old ledger day {Path.GetFileName(day)}: {e.Message}");
                    }
                }
            return dropped;
        }

        private static LedgerEntry Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{') return null;
            try { return JsonUtility.FromJson<LedgerEntry>(line); }
            catch (Exception) { return null; } // a line a stop cut off halfway
        }

        private bool TryPath(string key, out string path)
        {
            path = null;
            if (!SaveKeys.IsValid(key))
            {
                Debug.LogError($"[Save] '{key}' is not a valid save key.");
                return false;
            }
            path = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar) + Extension);
            return true;
        }
    }
}
