using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace KryticalNova.Persistence
{
    /// <summary>
    /// Zipped copies of the server's save folder: on start, every few hours, on stop, and on the GM's word
    /// (<c>gm.backup</c>, before an update). Every save is on disk first. A backup is written under a temporary name and
    /// renamed when whole, so a stop in the middle never leaves a broken one; the oldest beyond the number kept go.
    /// Named by time, newest last when sorted: <c>save_2026-10-16_14-05-00_start.zip</c>.
    /// <para>A server that saves through the save service (online phase 4) also puts every document of the database into
    /// the zip, under <c>database/</c>, in the same layout as a save folder: unzipped, the copy tool (Krytical Nova ▸
    /// Online ▸ Saves) puts it back into a database.</para>
    /// </summary>
    public static class ServerBackups
    {
        public const string Prefix = "save_";
        public const string Extension = ".zip";

        /// <summary>Backs up <paramref name="saveFolder"/> into <paramref name="backupFolder"/>. Returns what it did, for the log.</summary>
        public static string MakeNow(string saveFolder, string backupFolder, int kept, string reason)
        {
            try
            {
                ServerSave.FlushAll();
                var service = ServerSave.Store as ServiceSaveStore;
                if (service == null && (!Directory.Exists(saveFolder) || Directory.GetFileSystemEntries(saveFolder).Length == 0))
                    return "[Backup] Nothing to back up yet (" + saveFolder + " is empty).";
                Directory.CreateDirectory(saveFolder);
                Directory.CreateDirectory(backupFolder);
                string path = Path.Combine(backupFolder, FileNameFor(DateTime.Now, reason));
                string partial = path + ".partial";
                if (File.Exists(partial)) File.Delete(partial);
                ZipFile.CreateFromDirectory(saveFolder, partial, System.IO.Compression.CompressionLevel.Fastest, false);
                int fromDatabase = 0;
                if (service != null)
                {
                    fromDatabase = AddDatabase(service, partial, out string problem);
                    if (problem != null)
                    {
                        File.Delete(partial);
                        return "[Backup] Failed: the save database could not be read (" + problem + "). The last good backup is kept.";
                    }
                }
                if (File.Exists(path)) File.Delete(path);
                File.Move(partial, path);
                int removed = 0;
                foreach (string old in ToDelete(Directory.GetFiles(backupFolder, Prefix + "*" + Extension), kept))
                {
                    File.Delete(old);
                    removed++;
                }
                return $"[Backup] {Path.GetFileName(path)} ({new FileInfo(path).Length / 1024} KB" + (service != null ? $", {fromDatabase} saves from the database" : "") +
                       ")" + (removed > 0 ? $", {removed} old one(s) removed." : ".");
            }
            catch (Exception e)
            {
                return "[Backup] Failed: " + e.Message;
            }
        }

        // Every document of the save database into the zip, as database/<key>.json, read many at a time.
        private static int AddDatabase(ServiceSaveStore service, string zipPath, out string problem)
        {
            problem = null;
            IReadOnlyList<string> keys = service.Keys(string.Empty);
            int written = 0;
            using (ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Update))
            {
                for (int start = 0; start < keys.Count; start += 400)
                {
                    var chunk = new List<string>();
                    for (int i = start; i < Math.Min(keys.Count, start + 400); i++) chunk.Add(keys[i]);
                    var found = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (!service.TryReadMany(chunk, found)) { problem = "the save service did not answer"; return written; }
                    foreach (KeyValuePair<string, string> document in found)
                    {
                        ZipArchiveEntry entry = zip.CreateEntry("database/" + document.Key + ".json", System.IO.Compression.CompressionLevel.Fastest);
                        using (var writer = new StreamWriter(entry.Open(), new System.Text.UTF8Encoding(false))) writer.Write(document.Value);
                        written++;
                    }
                }
            }
            return written;
        }

        /// <summary>The file name of a backup made at <paramref name="when"/> for <paramref name="reason"/> (letters and digits only).</summary>
        public static string FileNameFor(DateTime when, string reason)
        {
            var tag = new System.Text.StringBuilder();
            foreach (char c in reason ?? string.Empty)
                if (char.IsLetterOrDigit(c) && tag.Length < 20) tag.Append(char.ToLowerInvariant(c));
            return Prefix + when.ToString("yyyy-MM-dd_HH-mm-ss", System.Globalization.CultureInfo.InvariantCulture)
                   + (tag.Length > 0 ? "_" + tag : string.Empty) + Extension;
        }

        /// <summary>The backups to delete so that only the newest <paramref name="kept"/> remain (names sort by time).</summary>
        public static List<string> ToDelete(IEnumerable<string> backups, int kept)
        {
            var all = new List<string>(backups);
            all.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(a), Path.GetFileName(b)));
            int excess = all.Count - Mathf.Max(1, kept);
            return excess > 0 ? all.GetRange(0, excess) : new List<string>();
        }
    }
}
