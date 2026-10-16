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
                if (!Directory.Exists(saveFolder) || Directory.GetFileSystemEntries(saveFolder).Length == 0)
                    return "[Backup] Nothing to back up yet (" + saveFolder + " is empty).";
                Directory.CreateDirectory(backupFolder);
                string path = Path.Combine(backupFolder, FileNameFor(DateTime.Now, reason));
                string partial = path + ".partial";
                if (File.Exists(partial)) File.Delete(partial);
                ZipFile.CreateFromDirectory(saveFolder, partial, System.IO.Compression.CompressionLevel.Fastest, false);
                if (File.Exists(path)) File.Delete(path);
                File.Move(partial, path);
                int removed = 0;
                foreach (string old in ToDelete(Directory.GetFiles(backupFolder, Prefix + "*" + Extension), kept))
                {
                    File.Delete(old);
                    removed++;
                }
                return $"[Backup] {Path.GetFileName(path)} ({new FileInfo(path).Length / 1024} KB)" + (removed > 0 ? $", {removed} old one(s) removed." : ".");
            }
            catch (Exception e)
            {
                return "[Backup] Failed: " + e.Message;
            }
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
