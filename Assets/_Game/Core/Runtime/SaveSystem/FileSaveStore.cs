using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// One JSON file per key under a root folder ("accounts/3f2a" is "&lt;root&gt;/accounts/3f2a.json"). A write goes to a
    /// temporary file first and then replaces the old one, so a crash mid-write leaves the previous save intact.
    /// <para>Made with <c>background</c> on, a write returns at once and the file is written by a thread of the
    /// store's own: the disk took 3.5 ms of a frame for every save, on the frame the save fell on. What is waiting
    /// is the truth meanwhile: a read gives it back, a second write of the same key replaces it (only the newest
    /// goes to disk), a delete takes it out. <see cref="Flush"/> waits until the disk has everything; the game
    /// calls it when it quits. Without <c>background</c> every write is on disk when it returns, as before.</para>
    /// </summary>
    public sealed class FileSaveStore : ISaveStore, IDisposable
    {
        private const string Extension = ".json";
        private const string TempExtension = ".tmp";
        private const int IdleMilliseconds = 2000;

        private readonly string root;
        private readonly bool background;
        // What has been handed over and is not on disk yet, newest text for each key. Guarded by itself.
        private readonly Dictionary<string, string> waiting = new Dictionary<string, string>(StringComparer.Ordinal);
        // Held while a file is read, written or deleted, so the writer and the game never touch one at once.
        private readonly object disk = new object();
        private string writingKey, writingJson;
        private Thread writer;
        private bool closed;

        public FileSaveStore(string rootFolder, bool background = false)
        {
            root = Path.GetFullPath(rootFolder);
            this.background = background;
        }

        public string Root => root;
        /// <summary>True when writes are handed to the store's own thread.</summary>
        public bool Background => background;

        public bool TryRead(string key, out string json)
        {
            json = null;
            if (!TryPath(key, out string path)) return false;
            if (background)
                lock (waiting)
                {
                    if (waiting.TryGetValue(key, out json) || (writingKey == key && (json = writingJson) != null)) return !string.IsNullOrEmpty(json);
                }
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

        public void Write(string key, string json)
        {
            if (!TryPath(key, out string path)) return;
            if (!background)
            {
                // The disk's share of a save, with how much text went out.
                using var timed = KryticalNova.Diagnostics.PerformanceLog.Time("save.file", key, json != null ? json.Length : 0);
                lock (disk) WriteFile(key, path, json);
                return;
            }
            lock (waiting)
            {
                if (closed)
                {
                    // After the last flush (the game is closing): straight to disk, nothing may be left behind.
                    lock (disk) WriteFile(key, path, json);
                    return;
                }
                waiting[key] = json ?? string.Empty;
                if (writer == null)
                {
                    writer = new Thread(WriteWaiting) { IsBackground = true, Name = "Save Writer" };
                    writer.Start();
                }
                Monitor.PulseAll(waiting);
            }
        }

        private void WriteFile(string key, string path, string json)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp = path + TempExtension;
                File.WriteAllText(temp, json ?? string.Empty, Encoding.UTF8);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not write '{key}': {e.Message}");
            }
        }

        // The store's own thread: takes what is waiting, one key at a time, and puts it on disk. It ends when it has
        // had nothing to do for a while, and is started again by the next write.
        private void WriteWaiting()
        {
            while (true)
            {
                string key = null, json = null;
                lock (waiting)
                {
                    while (waiting.Count == 0)
                    {
                        if (!Monitor.Wait(waiting, IdleMilliseconds) && waiting.Count == 0)
                        {
                            writer = null;
                            return;
                        }
                    }
                    foreach (KeyValuePair<string, string> first in waiting)
                    {
                        key = first.Key;
                        json = first.Value;
                        break;
                    }
                    waiting.Remove(key);
                    writingKey = key;
                    writingJson = json;
                }

                // Named apart from "save.file": this time is not the frame's.
                var timed = KryticalNova.Diagnostics.PerformanceLog.Time("save.disk", key, json.Length);
                lock (disk)
                {
                    if (TryPath(key, out string path)) WriteFile(key, path, json);
                }
                timed.End(json.Length);

                lock (waiting)
                {
                    writingKey = writingJson = null;
                    Monitor.PulseAll(waiting);
                }
            }
        }

        /// <summary>Waits until everything handed over is on disk. Nothing to wait for without <c>background</c>.</summary>
        public void Flush()
        {
            if (!background) return;
            lock (waiting)
            {
                while (waiting.Count > 0 || writingKey != null)
                {
                    // A writer that was stopped with its domain (the Editor recompiling) leaves this thread to finish.
                    if (writer == null || !writer.IsAlive)
                    {
                        writer = null;
                        if (writingKey != null && !waiting.ContainsKey(writingKey)) waiting[writingKey] = writingJson;
                        writingKey = writingJson = null;
                        foreach (KeyValuePair<string, string> left in waiting)
                            if (TryPath(left.Key, out string path))
                                lock (disk) WriteFile(left.Key, path, left.Value);
                        waiting.Clear();
                        return;
                    }
                    Monitor.Wait(waiting, 50);
                }
            }
        }

        /// <summary>Flushes, and from then on writes straight to disk: the game is closing.</summary>
        public void Dispose()
        {
            Flush();
            lock (waiting) closed = true;
        }

        public void Delete(string key)
        {
            if (!TryPath(key, out string path)) return;
            if (background)
                lock (waiting) waiting.Remove(key);
            // A write of this key that is under way finishes first (it holds the disk): the delete is what stands.
            lock (disk)
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
        }

        public IReadOnlyList<string> Keys(string prefix)
        {
            Flush(); // a key that is only waiting has no file yet
            var keys = new List<string>();
            if (!Directory.Exists(root)) return keys;
            foreach (string file in Directory.GetFiles(root, "*" + Extension, SearchOption.AllDirectories))
            {
                string key = file.Substring(root.Length).TrimStart('\\', '/').Replace('\\', '/');
                key = key.Substring(0, key.Length - Extension.Length);
                if (string.IsNullOrEmpty(prefix) || key.StartsWith(prefix, StringComparison.Ordinal)) keys.Add(key);
            }
            keys.Sort(StringComparer.Ordinal);
            return keys;
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
