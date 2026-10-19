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
    /// Saves through the save service (online phase 4, milestone 1) instead of files: the service alone talks to the
    /// database. Works like <see cref="FileSaveStore"/> with <c>background</c> on: a write returns at once and is sent by a
    /// thread of the store's own (what is waiting is the truth meanwhile); <see cref="Flush"/> waits until the service has
    /// everything.
    /// <para>Every document has a version. A write names the version this store last read or wrote, so it can never
    /// overwrite a newer one written by another server: such a write is refused and reported loudly.</para>
    /// <para>Accounts are locked (<see cref="TryLock"/>): one server at a time holds a player's account, and renews the hold
    /// every minute; a server that stops (crashes) lets it run out, so the player can join elsewhere.</para>
    /// </summary>
    public sealed class ServiceSaveStore : ISaveStore, IDisposable
    {
        public const int LockSeconds = 180;
        private const int RenewMilliseconds = 60000;
        private const int IdleMilliseconds = 2000;

        private readonly HttpClient http;
        private readonly string holder;
        private readonly Dictionary<string, long> versions = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> waiting = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> held = new HashSet<string>(StringComparer.Ordinal);
        private readonly object gate = new object();
        private Thread writer;
        private string writingKey;
        private bool closed;
        private readonly Timer renew;

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

        private readonly Timer health;

        // ------------------------------------------------------------------ ISaveStore

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

        public void Write(string key, string json)
        {
            if (!SaveKeys.IsValid(key)) return;
            lock (gate)
            {
                waiting[key] = json ?? string.Empty;
                if (closed) { WriteNow(key); return; }
                if (writer == null)
                {
                    writer = new Thread(WriteWaiting) { IsBackground = true, Name = "Save Service Writer" };
                    writer.Start();
                }
                Monitor.PulseAll(gate);
            }
        }

        public void Delete(string key)
        {
            if (!SaveKeys.IsValid(key)) return;
            lock (gate)
            {
                waiting.Remove(key);
                versions.Remove(key);
            }
            try { Send(HttpMethod.Delete, "doc?key=" + Uri.EscapeDataString(key), null).Dispose(); }
            catch (Exception e) { Debug.LogError($"[Save] Could not delete '{key}' on the save service: {e.GetBaseException().Message}"); }
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

        // The store's own thread: sends what is waiting, one key at a time, and stops when idle for a while.
        private void WriteWaiting()
        {
            while (true)
            {
                string key;
                lock (gate)
                {
                    while (waiting.Count == 0)
                    {
                        if (!Monitor.Wait(gate, IdleMilliseconds) && waiting.Count == 0)
                        {
                            writer = null;
                            Monitor.PulseAll(gate);
                            return;
                        }
                    }
                    key = null;
                    foreach (string first in waiting.Keys) { key = first; break; }
                    writingKey = key;
                }
                WriteNow(key);
                lock (gate)
                {
                    writingKey = null;
                    Monitor.PulseAll(gate);
                }
            }
        }

        // Sends the newest text of one key, at the version this store knows (0: it must be new).
        private void WriteNow(string key)
        {
            string json;
            long expected;
            lock (gate)
            {
                if (!waiting.TryGetValue(key, out json)) return;
                waiting.Remove(key);
                expected = versions.TryGetValue(key, out long known) ? known : 0;
            }
            try
            {
                string body = "{\"key\":\"" + key + "\",\"body\":" + Quote(json) + ",\"expected\":" + expected + "}";
                using HttpResponseMessage response = Send(HttpMethod.Put, "doc", body);
                Written result = JsonUtility.FromJson<Written>(Text(response));
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    Debug.LogError($"[Save] '{key}' was changed elsewhere (version {result.version}, this server had {expected}): this save was not written, so the newer one stays.");
                    return;
                }
                response.EnsureSuccessStatusCode();
                lock (gate) versions[key] = result.version;
            }
            catch (Exception e)
            {
                // The service did not answer: keep the text waiting so the next attempt sends it, unless newer text came.
                lock (gate) if (!waiting.ContainsKey(key)) waiting[key] = json;
                Debug.LogError($"[Save] Could not save '{key}' to the save service: {e.GetBaseException().Message}. It is kept and sent again.");
                Thread.Sleep(1000);
            }
        }

        /// <summary>Waits until the service has every write handed over (at most <paramref name="seconds"/>).</summary>
        public void Flush(float seconds = 15f)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(seconds);
            lock (gate)
            {
                // No writer thread (closing): send what is left here, as long as there is time.
                while (writer == null && waiting.Count > 0 && DateTime.UtcNow < until)
                    foreach (string key in new List<string>(waiting.Keys)) WriteNow(key);
                while ((waiting.Count > 0 || writingKey != null) && DateTime.UtcNow < until)
                    Monitor.Wait(gate, 100);
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

        public void Dispose()
        {
            lock (gate) closed = true;
            Flush();
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
            http.Dispose();
        }

        // ------------------------------------------------------------------ health, for the control page

        /// <summary>
        /// Whether the save service and its database answer, checked every 10 seconds off the game's thread: "ok" with
        /// the number of saves, or what is down. For the control page's Saves chip.
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
                HealthAnswer answer = JsonUtility.FromJson<HealthAnswer>(Text(response));
                Healthy = true;
                Health = "ok, " + answer.documents + " saves";
            }
            catch (Exception)
            {
                Healthy = false;
                Health = "service DOWN";
            }
        }

        [Serializable] private sealed class HealthAnswer { public bool ok; public long documents; }

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
        [Serializable] private sealed class Written { public bool written; public long version; }
        [Serializable] private sealed class LockAnswer { public bool granted; public string holder; }
        [Serializable] private sealed class KeyList { public List<string> keys; }
    }
}
