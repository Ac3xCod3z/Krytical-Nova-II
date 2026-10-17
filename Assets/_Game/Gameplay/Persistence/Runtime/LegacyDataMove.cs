#if !UNITY_SERVER
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Persistence
{
    /// <summary>
    /// The owner renamed the company to "Ascension Studios" and the game to "Krytical Nova" (16 Oct 2026). Unity keeps a
    /// PC's own data under those names: the folder in AppData/LocalLow (local saves, the server list it keeps) and the
    /// settings (PlayerPrefs, in the registry). The first time a game or the Editor starts under the new names, this
    /// copies the old ones across once, so nothing looks lost: files that already exist under the new name are kept,
    /// the old folder stays as it was (a backup), and a marker file stops it from ever running again. A dedicated server
    /// keeps its saves beside itself and is not touched.
    /// </summary>
    public static class LegacyDataMove
    {
        public const string OldCompany = "DefaultCompany";
        public const string OldProduct = "Krytical Nova II";
        private const string Marker = "moved-from-krytical-nova-ii.txt";
        // Unity's own logs and caches are not ours to carry.
        private static readonly string[] Skipped = { "Player.log", "Player-prev.log", "Unity", "TestResults.xml" };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        private static void MoveOnce()
        {
            if (Application.companyName == OldCompany && Application.productName == OldProduct) return;
            try
            {
                string now = Application.persistentDataPath;
                string localLow = Path.GetDirectoryName(Path.GetDirectoryName(now));
                if (string.IsNullOrEmpty(localLow)) return;
                string before = Path.Combine(localLow, OldCompany, OldProduct);
                if (File.Exists(Path.Combine(now, Marker)) || !Directory.Exists(before)) return;
                Directory.CreateDirectory(now);
                int files = CopyMissing(before, now);
                bool settings = CopySettings();
                File.WriteAllText(Path.Combine(now, Marker),
                    $"Carried over from {before} on {DateTime.Now:yyyy-MM-dd HH:mm}: {files} file(s), settings {(settings ? "copied" : "not found")}. The old folder is kept as it was.");
                Debug.Log($"[Save] The game's new name: carried {files} file(s) over from {before}{(settings ? " and the settings" : string.Empty)}.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Save] Could not carry the data over from the old game name: " + e.Message);
            }
        }

        private static int CopyMissing(string from, string to)
        {
            int copied = 0;
            foreach (string file in Directory.GetFiles(from))
            {
                string name = Path.GetFileName(file);
                if (Array.IndexOf(Skipped, name) >= 0) continue;
                string target = Path.Combine(to, name);
                if (File.Exists(target)) continue;
                File.Copy(file, target);
                copied++;
            }
            foreach (string folder in Directory.GetDirectories(from))
            {
                string name = Path.GetFileName(folder);
                if (Array.IndexOf(Skipped, name) >= 0) continue;
                string target = Path.Combine(to, name);
                Directory.CreateDirectory(target);
                copied += CopyMissing(folder, target);
            }
            return copied;
        }

        // PlayerPrefs live in the registry under the company and product names (the Editor's under Unity\UnityEditor):
        // Windows' own reg copy moves them, once, before the game reads any.
        private static bool CopySettings()
        {
            if (Application.platform != RuntimePlatform.WindowsPlayer && Application.platform != RuntimePlatform.WindowsEditor) return false;
            string root = Application.isEditor ? @"HKCU\Software\Unity\UnityEditor\" : @"HKCU\Software\";
            string from = root + OldCompany + @"\" + OldProduct;
            string to = root + Application.companyName + @"\" + Application.productName;
            if (Reg($"query \"{from}\"") != 0) return false;
            return Reg($"copy \"{from}\" \"{to}\" /s /f") == 0;
        }

        private static int Reg(string arguments)
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "reg.exe"), arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (Process process = Process.Start(info))
            {
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                return process.WaitForExit(10000) ? process.ExitCode : -1;
            }
        }
    }
}
#endif
