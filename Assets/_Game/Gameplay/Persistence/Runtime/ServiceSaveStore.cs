using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Persistence
{
    /// <summary>
    /// Saves through the save service (online phase 4) instead of files: the service alone talks to the database. Works
    /// like <see cref="FileSaveStore"/> with <c>background</c> on: a write returns at once and is sent by a thread of the
    /// store's own (what is waiting is the truth meanwhile); <see cref="Flush"/> waits until the service has everything.
    /// <para>Every document has a version. A write names the version this store last read or wrote, so it can never
    /// overwrite a newer one written by another server: such a write is refused and reported loudly.</para>
    /// <para>Writes go as batches (milestone 2): a player's save is the parts that changed and its header, written
    /// together or not at all (<see cref="WriteBatch"/>); batches are sent in the order they were handed over. Many
    /// documents are read in one trip (<see cref="TryReadMany"/>).</para>
    /// <para>Milestone 3: a batch carries ledger lines too (a trade's, a sale's), in the same transaction, and an id, so a
    /// batch sent again after its answer was lost is not applied twice nor refused as stale.</para>
    /// <para>Accounts are locked (<see cref="TryLock"/>): one server at a time holds a player's account, and renews the hold
    /// every minute; a server that stops (crashes) lets it run out, so the player can join elsewhere.</para>
    /// </summary>
    public sealed class ServiceSaveStore : IBatchSaveStore, ILedgerStore, ISaveResultStore, IDisposable
    {
        public const int LockSeconds = 180;
        private const int RenewMilliseconds = 60000;
        private const int IdleMilliseconds = 2000;

        private sealed class Batch
        {
            public readonly string Id = Guid.NewGuid().ToString("N");
            public readonly List<KeyValuePair<string, string>> Writes = new List<KeyValuePair<string, string>>();
            public readonly List<string> Deletes = new List<string>();
            public readonly List<string> Ledger = new List<string>();
            public Action<bool> Done;   // the caller's SaveBatch.Done: told on the writer thread
        }

        private static void Tell(Batch batch, bool written)
        {
            if (batch.Done == null) return;
            try { batch.Done(written); }
            catch (Exception e) { Debug.LogException(e); }
        }

        private readonly HttpClient http;
        private readonly string holder;
        private readonly Dictionary<string, long> versions = new Dictionary<string, long>(StringComparer.Ordinal);
        // What is handed over and not on the service yet: the newest text of each key (null: a delete), and how many
        // batches still carry it, so a read gives back what will be there.
        private readonly Dictionary<string, string> waiting = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> carried = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Queue<Batch> batches = new Queue<Batch>();
        private readonly HashSet<string> held = new HashSet<string>(StringComparer.Ordinal);
        private readonly object gate = new object();
        private readonly Timer renew;
        private readonly Timer health;
        private Thread writer;
        private bool sending;
        private bool closed;
        // Set just before the connection is disposed: from then on nothing is sent, the writer thread leaves and nobody waits.
        private volatile bool disposed;

        /// <summary>True while the store's own writer thread lives (it leaves when idle for a while, and when the store is disposed).</summary>
        public bool WriterRunning { get { lock (gate) return writer != null; } }

        /// <summary>The service this store talks to (gm.maxall runs only when it is the test lane, 7802).</summary>
        public Uri Address => http.BaseAddress;

        /// <param name="baseUrl">The service, for example http://127.0.0.1:7801.</param>
        /// <param name="key">The service key (save-service-key.txt).</param>
        /// <param name="holder">Who this server is, for the account locks.</param>
        public ServiceSaveStore(string baseUrl, string key, string holder)
        {
            http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(5) };
            http.DefaultRequestHeaders.Add("X-KN-Key", key);
            this.holder = holder;
            renew = new Timer(_ => RenewLocks(), null, RenewMilliseconds, RenewMilliseconds);
            health = new Timer(_ => CheckHealth(), null, 0, 10000);
        }

        // ------------------------------------------------------------------ reading

        public bool TryRead(string key, out string json)
        {
            json = null;
            if (!SaveKeys.IsValid(key)) return false;
            lock (gate)
                if (waiting.TryGetValue(key, out string pending)) { json = pending; return !string.IsNullOrEmpty(json); }
            try
            {
                using HttpResponseMessage response = Send(HttpMethod.Get, "doc?key=" + Uri.EscapeDataString(key), null);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    lock (gate) versions[key] = 0;
                    return false;
                }
                response.EnsureSuccessStatusCode();
                Doc doc = JsonUtility.FromJson<Doc>(Text(response));
                lock (gate) versions[key] = doc.version;
                json = doc.body;
                return !string.IsNullOrEmpty(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not read '{key}' from the save service: {e.GetBaseException().Message}");
                return false;
            }
        }

        public bool TryReadMany(IReadOnlyList<string> keys, IDictionary<string, string> found)
        {
            var ask = new List<string>(keys.Count);
            lock (gate)
                foreach (string key in keys)
                {
                    if (!SaveKeys.IsValid(key)) continue;
                    if (waiting.TryGetValue(key, out string pending)) { if (!string.IsNullOrEmpty(pending)) found[key] = pending; }
                    else ask.Add(key);
                }
            if (ask.Count == 0) return true;
            try
            {
                var body = new StringBuilder("{\"keys\":[");
                for (int i = 0; i < ask.Count; i++) body.Append(i > 0 ? "," : "").Append(Quote(ask[i]));
                using HttpResponseMessage response = Send(HttpMethod.Post, "read", body.Append("]}").ToString());
                response.EnsureSuccessStatusCode();
                DocList list = JsonUtility.FromJson<DocList>("{\"docs\":" + Text(response) + "}");
                lock (gate)
                {
                    foreach (string key in ask) versions[key] = 0; // missing unless found below
                    foreach (Doc doc in list.docs ?? new List<Doc>())
                    {
                        versions[doc.key] = doc.version;
                        if (!string.IsNullOrEmpty(doc.body)) found[doc.key] = doc.body;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not read {ask.Count} save(s) from the save service: {e.GetBaseException().Message}");
                return false;
            }
        }

        public IReadOnlyList<string> Keys(string prefix)
        {
            Flush(); // a key that is only waiting is not on the service yet
            try
            {
                using HttpResponseMessage response = Send(HttpMethod.Get, "keys?prefix=" + Uri.EscapeDataString(prefix ?? string.Empty), null);
                response.EnsureSuccessStatusCode();
                return JsonUtility.FromJson<KeyList>("{\"keys\":" + Text(response) + "}").keys ?? new List<string>();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not list saves on the save service: {e.GetBaseException().Message}");
                return new List<string>();
            }
        }

        // ------------------------------------------------------------------ writing

        public void Write(string key, string json)
        {
            var one = new SaveBatch();
            one.Write(key, json);
            WriteBatch(one);
        }

        public void Delete(string key)
        {
            var one = new SaveBatch();
            one.Deletes.Add(key);
            WriteBatch(one);
        }

        public void WriteBatch(SaveBatch change)
        {
            if (change == null) return;
            var batch = new Batch();
            foreach (KeyValuePair<string, string> write in change.Writes)
                if (SaveKeys.IsValid(write.Key)) batch.Writes.Add(new KeyValuePair<string, string>(write.Key, write.Value ?? string.Empty));
            foreach (string key in change.Deletes)
                if (SaveKeys.IsValid(key)) batch.Deletes.Add(key);
            foreach (LedgerEntry line in change.Ledger)
                if (line != null) batch.Ledger.Add(JsonUtility.ToJson(line));
            batch.Done = change.Done;
            if (batch.Writes.Count == 0 && batch.Deletes.Count == 0 && batch.Ledger.Count == 0) { Tell(batch, true); return; }

            if (disposed)
            {
                Debug.LogError($"[Save] A save of {batch.Writes.Count} write(s), {batch.Deletes.Count} delete(s) and {batch.Ledger.Count} ledger line(s) " +
                               "was handed to a save store that is already closed: it is not written.");
                Tell(batch, false);
                return;
            }
            lock (gate)
            {
                foreach (KeyValuePair<string, string> write in batch.Writes) Hold(write.Key, write.Value);
                foreach (string key in batch.Deletes) Hold(key, null);
                batches.Enqueue(batch);
                if (closed) { SendWaiting(); return; }
                if (writer == null)
                {
                    writer = new Thread(WriteWaiting) { IsBackground = true, Name = "Save Service Writer" };
                    writer.Start();
                }
                Monitor.PulseAll(gate);
            }
        }

        private void Hold(string key, string text)
        {
            waiting[key] = text;
            carried[key] = carried.TryGetValue(key, out int n) ? n + 1 : 1;
        }

        private void Let(string key)
        {
            if (!carried.TryGetValue(key, out int n)) return;
            if (n > 1) carried[key] = n - 1;
            else
            {
                carried.Remove(key);
                waiting.Remove(key);
            }
        }

        // The store's own thread: sends the batches in order, and stops when idle for a while, or at once when the store is
        // disposed (12 Nov 2026: a disposed store's thread kept a batch and tried it every second for ever).
        private void WriteWaiting()
        {
            while (true)
            {
                lock (gate)
                {
                    while (batches.Count == 0 && !disposed)
                    {
                        if (!Monitor.Wait(gate, IdleMilliseconds) && batches.Count == 0)
                        {
                            writer = null;
                            Monitor.PulseAll(gate);
                            return;
                        }
                    }
                    if (disposed)
                    {
                        writer = null;
                        Monitor.PulseAll(gate);
                        return;
                    }
                }
                SendNext();
            }
        }

        // Sends what is left on the calling thread (the store is closing).
        private void SendWaiting()
        {
            DateTime until = DateTime.UtcNow.AddSeconds(15);
            while (batches.Count > 0 && !disposed && DateTime.UtcNow < until) SendNext();
        }

        private const int ReadLimit = 500;
        private readonly HashSet<string> refreshFirst = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// A2: the service's versions of these keys are read before the next batch is sent (on the writer thread): a document sent
        /// again whole after a refusal also carries parts the refused batch did not, and another writer may have changed those too.
        /// </summary>
        public void RefreshBeforeNext(IEnumerable<string> keys)
        {
            if (keys == null) return;
            lock (gate) foreach (string key in keys) if (SaveKeys.IsValid(key)) refreshFirst.Add(key);
        }

        // Sends the oldest batch with the versions this store knows (0: the document must be new).
        private void SendNext()
        {
            List<string> refresh = null;
            lock (gate)
                if (refreshFirst.Count > 0) { refresh = new List<string>(refreshFirst); refreshFirst.Clear(); }
            if (refresh != null && !disposed) RefreshVersions(refresh);
            Batch batch;
            string body;
            lock (gate)
            {
                if (batches.Count == 0 || disposed) return;
                batch = batches.Peek();
                sending = true;
                var text = new StringBuilder(256).Append("{\"id\":").Append(Quote(batch.Id)).Append(",\"writes\":[");
                for (int i = 0; i < batch.Writes.Count; i++)
                {
                    string key = batch.Writes[i].Key;
                    long expected = versions.TryGetValue(key, out long known) ? known : 0;
                    text.Append(i > 0 ? "," : "").Append("{\"key\":").Append(Quote(key)).Append(",\"body\":").Append(Quote(batch.Writes[i].Value))
                        .Append(",\"expected\":").Append(expected).Append('}');
                }
                text.Append("],\"deletes\":[");
                for (int i = 0; i < batch.Deletes.Count; i++) text.Append(i > 0 ? "," : "").Append(Quote(batch.Deletes[i]));
                text.Append("],\"ledger\":[");
                for (int i = 0; i < batch.Ledger.Count; i++) text.Append(i > 0 ? "," : "").Append(batch.Ledger[i]);
                body = text.Append("]}").ToString();
            }
            bool? word = null;
            try
            {
                using HttpResponseMessage response = Send(HttpMethod.Post, "batch", body);
                string answer = Text(response);
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    BatchAnswer refused = JsonUtility.FromJson<BatchAnswer>(answer);
                    // Save fix A (owner pick): the service's copy wins this once, and the store takes its version (0: gone, the
                    // next save makes it new), so the next save of the document is written instead of being refused for ever.
                    if (!string.IsNullOrEmpty(refused.conflict))
                        lock (gate) versions[refused.conflict] = refused.conflictVersion > 0 ? refused.conflictVersion : 0;
                    // A2 (lead, 12 Nov 2026): every key of the refused batch, not only the one the service names, takes the service's
                    // version before the word is told, so the whole copy the next save sends is written in one try.
                    var carried = new List<string>(batch.Writes.Count + batch.Deletes.Count);
                    foreach (KeyValuePair<string, string> write in batch.Writes) carried.Add(write.Key);
                    carried.AddRange(batch.Deletes);
                    RefreshVersions(carried);
                    Debug.LogError($"[Save] '{refused.conflict}' was changed elsewhere (it is at version {refused.conflictVersion}): this save of " +
                                   $"{batch.Writes.Count + batch.Deletes.Count} document(s) was not written, so the newer one stays. The next save of it builds on the service's version.");
                    word = false;
                }
                else if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
                {
                    // Owner, 12 Nov 2026: a batch the service will never take (too many items, too big, a bad key) is FINAL. It is
                    // told false once and leaves the line, so the saves behind it go on; it is never sent again as it is.
                    // With A2 the refusal un-marks what it carried, so the next save sends the document again.
                    Debug.LogError($"[Save] The save service refused this save of {batch.Writes.Count} write(s), {batch.Deletes.Count} delete(s) and " +
                                   $"{batch.Ledger.Count} ledger line(s) for good ({(int)response.StatusCode}: {answer}). It is not sent again as it is; " +
                                   "the next save sends the document again.");
                    word = false;
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    BatchAnswer done = JsonUtility.FromJson<BatchAnswer>(answer);
                    lock (gate)
                    {
                        foreach (KeyVersion written in done.versions ?? new List<KeyVersion>()) versions[written.key] = written.version;
                        foreach (string key in batch.Deletes) versions.Remove(key);
                    }
                    word = true;
                }
                // told before the batch leaves the line: a Flush that returns has every word of what it waited for
                // (12 Nov 2026: the first run of the conflict tests read "not told yet" right after Flush)
                if (word.HasValue) Tell(batch, word.Value);
                lock (gate)
                {
                    batches.Dequeue();
                    foreach (KeyValuePair<string, string> write in batch.Writes) Let(write.Key);
                    foreach (string key in batch.Deletes) Let(key);
                    sending = false;
                    Monitor.PulseAll(gate);
                }
            }
            catch (Exception e)
            {
                // The service did not answer: the batch stays first in line and is sent again.
                lock (gate)
                {
                    sending = false;
                    Monitor.PulseAll(gate);
                }
                // The store was disposed under this send: its connection is gone, nothing is sent again, and Dispose has said
                // what was left. No line and no wait here.
                if (disposed) return;
                Debug.LogError($"[Save] Could not save to the save service: {e.GetBaseException().Message}. It is kept and sent again.");
                Thread.Sleep(1000);
            }
        }

        // The service's versions of these keys (gone: 0), versions only. Writer thread, outside the gate. On a failure the versions
        // stay as they are (the named key already has fix A's version) and it is said, never silently.
        private void RefreshVersions(List<string> keys)
        {
            if (keys == null || keys.Count == 0) return;
            try
            {
                for (int start = 0; start < keys.Count; start += ReadLimit)   // the service reads at most 500 keys a call
                {
                    int end = Math.Min(keys.Count, start + ReadLimit);
                    var body = new StringBuilder("{\"keys\":[");
                    for (int i = start; i < end; i++) body.Append(i > start ? "," : "").Append(Quote(keys[i]));
                    using HttpResponseMessage response = Send(HttpMethod.Post, "read", body.Append("]}").ToString());
                    response.EnsureSuccessStatusCode();
                    DocList list = JsonUtility.FromJson<DocList>("{\"docs\":" + Text(response) + "}");
                    lock (gate)
                    {
                        for (int i = start; i < end; i++) versions[keys[i]] = 0;
                        foreach (Doc doc in list.docs ?? new List<Doc>()) versions[doc.key] = doc.version;
                    }
                }
            }
            catch (Exception e)
            {
                if (disposed) return;   // the store closed under the read: there is no next save
                Debug.LogError($"[Save] Could not refresh the versions of {keys.Count} document(s) after a refused save ({e.GetBaseException().Message}): the next save may be refused again.");
            }
        }

        /// <summary>
        /// Waits until the service has every write handed over (at most <paramref name="seconds"/>); false when time ran out first
        /// (what is left stays in line and is sent again). On a disposed store it does not wait: false when anything was left.
        /// </summary>
        public bool Flush(float seconds = 15f)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(seconds);
            lock (gate)
            {
                if (writer == null && batches.Count > 0) SendWaiting();
                while ((batches.Count > 0 || sending) && !disposed && DateTime.UtcNow < until)
                    Monitor.Wait(gate, 100);
                return batches.Count == 0 && !sending;
            }
        }

        // ------------------------------------------------------------------ the ledger

        public List<LedgerEntry> ReadLedger(string account, long sinceUtc, int limit)
        {
            Flush(); // lines still waiting are not on the service yet
            try
            {
                string path = "ledger?since=" + sinceUtc + "&limit=" + limit + (string.IsNullOrEmpty(account) ? "" : "&account=" + Uri.EscapeDataString(account));
                using HttpResponseMessage response = Send(HttpMethod.Get, path, null);
                response.EnsureSuccessStatusCode();
                return JsonUtility.FromJson<LedgerList>("{\"lines\":" + Text(response) + "}").lines ?? new List<LedgerEntry>();
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not read the ledger from the save service: {e.GetBaseException().Message}");
                return new List<LedgerEntry>();
            }
        }

        public int PruneLedger(long beforeUtc)
        {
            try
            {
                using HttpResponseMessage response = Send(HttpMethod.Post, "ledger/prune", "{\"before\":" + beforeUtc + "}");
                response.EnsureSuccessStatusCode();
                return JsonUtility.FromJson<PruneAnswer>(Text(response)).dropped;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] Could not trim the ledger on the save service: {e.GetBaseException().Message}");
                return -1;
            }
        }

        // ------------------------------------------------------------------ account locks

        /// <summary>
        /// Takes the lock on <paramref name="owner"/> ("accounts/&lt;id&gt;") for this server. False, with the server that
        /// holds it, when another one does; true when this server holds it now.
        /// </summary>
        public bool TryLock(string owner, out string heldBy)
        {
            heldBy = null;
            try
            {
                using HttpResponseMessage response = Send(HttpMethod.Post, "lock", LockBody(owner, LockSeconds));
                response.EnsureSuccessStatusCode();
                LockAnswer answer = JsonUtility.FromJson<LockAnswer>(Text(response));
                heldBy = answer.holder;
                if (!answer.granted) return false;
                lock (gate) held.Add(owner);
                return true;
            }
            catch (Exception e)
            {
                heldBy = "(the save service did not answer)";
                Debug.LogError($"[Save] Could not lock '{owner}' on the save service: {e.GetBaseException().Message}");
                return false;
            }
        }

        /// <summary>
        /// Lets the account go once every save handed over before has reached the service, so another server never loads
        /// an older copy. Off the game's thread; a lock that cannot be let go runs out by itself.
        /// </summary>
        public void Unlock(string owner)
        {
            lock (gate) if (!held.Remove(owner)) return;
            Task.Run(() =>
            {
                Flush();
                try { Send(HttpMethod.Post, "unlock", LockBody(owner, 0)).Dispose(); }
                catch (Exception e) { Debug.LogWarning($"[Save] Could not unlock '{owner}' (it runs out by itself): {e.GetBaseException().Message}"); }
            });
        }

        private void RenewLocks()
        {
            string[] owners;
            lock (gate) owners = new List<string>(held).ToArray();
            foreach (string owner in owners)
            {
                try { Send(HttpMethod.Post, "lock", LockBody(owner, LockSeconds)).Dispose(); }
                catch (Exception e) { Debug.LogWarning($"[Save] Could not renew the lock on '{owner}': {e.GetBaseException().Message}"); }
            }
        }

        private string LockBody(string owner, int seconds) =>
            "{\"owner\":" + Quote(owner) + ",\"holder\":" + Quote(holder) + ",\"seconds\":" + seconds + "}";

        /// <summary>True when the service answers with the right key; otherwise why not, in plain words.</summary>
        public bool Reachable(out string problem)
        {
            problem = null;
            try
            {
                using HttpResponseMessage response = Send(HttpMethod.Get, "health", null);
                if (response.StatusCode == HttpStatusCode.Unauthorized) { problem = "the save service refused this server's key"; return false; }
                response.EnsureSuccessStatusCode();
                return true;
            }
            catch (Exception e)
            {
                problem = "the save service does not answer (" + e.GetBaseException().Message + ")";
                return false;
            }
        }

        public void Dispose() => Close(15f);

        /// <summary>
        /// Closes the store: waits up to <paramref name="flushSeconds"/> for the service to take what is in line (15 s for
        /// <see cref="Dispose"/>), lets the accounts go and closes the connection. Saves the service did not take in that time are
        /// lost with the store: that is said ONCE, with how many, and the writer thread ends. A second call does nothing.
        /// </summary>
        public void Close(float flushSeconds)
        {
            lock (gate)
            {
                if (closed) return;
                closed = true;
            }
            Flush(flushSeconds);
            renew.Dispose();
            health.Dispose();
            string[] owners;
            lock (gate)
            {
                owners = new List<string>(held).ToArray();
                held.Clear();
            }
            // Everything is saved by now: the accounts are let go here, before the connection closes.
            foreach (string owner in owners)
            {
                try { Send(HttpMethod.Post, "unlock", LockBody(owner, 0)).Dispose(); }
                catch (Exception) { /* it runs out by itself */ }
            }
            int left;
            lock (gate)
            {
                disposed = true;            // before the connection goes: a send that fails from here on is silent
                left = batches.Count;
                Monitor.PulseAll(gate);     // the writer thread and anybody in Flush leave
            }
            http.Dispose();
            if (left > 0)
                Debug.LogError($"[Save] The save store closed with {left} save(s) the save service had not confirmed: they are not sent again.");
        }

        // ------------------------------------------------------------------ health, for the control page

        /// <summary>
        /// Whether the save service and its database answer, checked every 10 seconds off the game's thread: "ok" with
        /// the number of saves and the database's size, or what is down. For the control page's Saves chip.
        /// </summary>
        public string Health { get; private set; } = "checking";

        /// <summary>True when the last check found the service and the database answering.</summary>
        public bool Healthy { get; private set; }

        private void CheckHealth()
        {
            try
            {
                using HttpResponseMessage response = Send(HttpMethod.Get, "health", null);
                if (response.StatusCode == HttpStatusCode.Unauthorized) { Healthy = false; Health = "the service refused this server's key"; return; }
                if (!response.IsSuccessStatusCode) { Healthy = false; Health = "database DOWN"; return; }
                string text = Text(response);
                HealthAnswer answer = JsonUtility.FromJson<HealthAnswer>(text);
                Healthy = true;
                // Unity's reader makes an empty object of "backup": null (the tests' service keeps no copies).
                bool keepsCopies = !System.Text.RegularExpressions.Regex.IsMatch(text, @"""backup""\s*:\s*null");
                Health = $"ok, {answer.documents} saves, {answer.bytes / (1024 * 1024.0):0.#} MB" + (keepsCopies ? NightlyCopy(answer.backup) : string.Empty);
            }
            catch (Exception)
            {
                Healthy = false;
                Health = "service DOWN";
            }
        }

        /// <summary>
        /// The database's nightly copy (online phase 4, milestone 6), for the control page's Saves chip: how old the last one
        /// is, or what is wrong. Empty for a service that keeps none (the tests').
        /// </summary>
        public static string NightlyCopy(BackupAnswer backup)
        {
            if (backup == null) return string.Empty;
            if (!string.IsNullOrEmpty(backup.problem)) return ", backup PROBLEM: " + backup.problem;
            if (string.IsNullOrEmpty(backup.last) || !DateTime.TryParse(backup.last, out DateTime made)) return ", no nightly copy yet";
            double hours = (DateTime.Now - made).TotalHours;
            string age = hours < 1 ? "under an hour" : $"{hours:0} h";
            return hours > 26 ? $", nightly copy {age} OLD" : $", nightly copy {age} ago" + (backup.offsite ? " (in Google Drive)" : " (NOT off this PC)");
        }

        // ------------------------------------------------------------------ the wire

        // Off the game's thread: Unity's own context never waits on itself.
        private HttpResponseMessage Send(HttpMethod method, string path, string json)
        {
            return Task.Run(() =>
            {
                var request = new HttpRequestMessage(method, path);
                if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                return http.SendAsync(request);
            }).GetAwaiter().GetResult();
        }

        private static string Text(HttpResponseMessage response) =>
            Task.Run(() => response.Content.ReadAsStringAsync()).GetAwaiter().GetResult();

        // A JSON string literal.
        private static string Quote(string text)
        {
            var sb = new StringBuilder(text.Length + 16).Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        [Serializable] private sealed class Doc { public string key; public string body; public long version; }
        [Serializable] private sealed class DocList { public List<Doc> docs; }
        [Serializable] private sealed class KeyVersion { public string key; public long version; }
        [Serializable] private sealed class BatchAnswer { public bool written; public string conflict; public long conflictVersion; public List<KeyVersion> versions; }
        [Serializable] private sealed class LockAnswer { public bool granted; public string holder; }
        [Serializable] private sealed class KeyList { public List<string> keys; }
        [Serializable] private sealed class HealthAnswer { public bool ok; public long documents; public long bytes; public BackupAnswer backup; }
        /// <summary>The save service's word on its nightly copies (null fields when it keeps none).</summary>
        [Serializable] public sealed class BackupAnswer { public string last; public long bytes; public bool offsite; public int kept; public string problem; }
        [Serializable] private sealed class LedgerList { public List<LedgerEntry> lines; }
        [Serializable] private sealed class PruneAnswer { public int dropped; }
    }
}
