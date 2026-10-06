using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// One JSON file per key under a root folder ("accounts/3f2a" is "&lt;root&gt;/accounts/3f2a.json"). A write goes to a
    /// temporary file first and then replaces the old one, so a crash mid-write leaves the previous save intact.
    /// </summary>
    public sealed class FileSaveStore : ISaveStore
    {
        private const string Extension = ".json";
        private const string TempExtension = ".tmp";

        private readonly string root;

        public FileSaveStore(string rootFolder)
        {
            root = Path.GetFullPath(rootFolder);
        }

        public string Root => root;

        public bool TryRead(string key, out string json)
        {
            json = null;
            if (!TryPath(key, out string path) || !File.Exists(path)) return false;
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

        public void Write(string key, string json)
        {
            if (!TryPath(key, out string path)) return;
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

        public void Delete(string key)
        {
            if (!TryPath(key, out string path)) return;
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Could not delete '{key}': {e.Message}");
            }
        }

        public IReadOnlyList<string> Keys(string prefix)
        {
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
