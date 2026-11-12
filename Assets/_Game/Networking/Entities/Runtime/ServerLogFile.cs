using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// The entity server's text log as a file too (owner, 8 Nov 2026: the log lived only in the server app's window). One file per run,
    /// server_&lt;date&gt;_&lt;time&gt;.log, in Google Drive (<see cref="DriveFolder"/>), or beside the server in PerformanceLogs\ when Drive is
    /// not mounted. Every Debug line goes in (the [GM], join/leave, refusals, shop/trade/save lines and disconnect reasons), flushed
    /// once a second; secrets are masked; files older than <see cref="KeepDays"/> days are removed. Never stops the server: any
    /// failure just leaves the file off.
    /// </summary>
    public static class ServerLogFile
    {
        public const string DriveFolder = @"G:\My Drive\KryticalNova\Docs\ServerLogs";
        public const int KeepDays = 14;

        static StreamWriter s_Writer;
        static readonly object s_Lock = new object();
        static DateTime s_LastFlush;

        // token=..., password: ..., key "..." and long bearer-like strings are masked before a line is written
        static readonly Regex Secret = new Regex(@"(?i)\b(token|password|passwd|secret|apikey|api_key|key|authorization|bearer)\b(\s*[:=]\s*|\s+)(""[^""]*""|\S+)",
            RegexOptions.Compiled);
        static readonly Regex LongBlob = new Regex(@"\b[A-Za-z0-9_\-\.]{60,}\b", RegexOptions.Compiled);

        /// <summary>The line as written: secrets masked (pure, tested).</summary>
        public static string Mask(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            line = Secret.Replace(line, m => m.Groups[1].Value + m.Groups[2].Value + "***");
            return LongBlob.Replace(line, "***");
        }

        /// <summary>Starts the file. Safe to call twice; returns where it writes, or null when it could not start.</summary>
        public static string Start()
        {
            if (s_Writer != null) return null;
            try
            {
                string folder = Folder();
                string path = Path.Combine(folder, $"server_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
                s_Writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
                s_LastFlush = DateTime.UtcNow;
                Application.logMessageReceivedThreaded += OnLog;
                Application.quitting += Stop;
                Prune(folder);
                Debug.Log("[KnServer] Text log also written to " + path);
                return path;
            }
            catch (Exception e)
            {
                s_Writer = null;
                Debug.LogWarning("[KnServer] Text log file not started (" + e.GetType().Name + "): the window log still works.");
                return null;
            }
        }

        static string Folder()
        {
            try
            {
                if (Directory.Exists(Path.GetPathRoot(DriveFolder)))
                {
                    Directory.CreateDirectory(DriveFolder);
                    return DriveFolder;
                }
            }
            catch (Exception) { }
            string local = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "PerformanceLogs");
            Directory.CreateDirectory(local);
            return local;
        }

        static void Prune(string folder)
        {
            try
            {
                DateTime cut = DateTime.Now.AddDays(-KeepDays);
                foreach (string f in Directory.GetFiles(folder, "server_*.log"))
                    if (File.GetLastWriteTime(f) < cut) File.Delete(f);
            }
            catch (Exception) { }
        }

        static void OnLog(string message, string stack, LogType type)
        {
            lock (s_Lock)
            {
                if (s_Writer == null) return;
                try
                {
                    string kind = type == LogType.Log ? "" : type == LogType.Warning ? " WARN" : " " + type.ToString().ToUpperInvariant();
                    s_Writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff}{kind} {Mask(message)}");
                    if (type == LogType.Exception || type == LogType.Error)
                        foreach (string l in (stack ?? "").Split('\n')) if (l.Length > 0) s_Writer.WriteLine("    " + Mask(l.TrimEnd()));
                    if ((DateTime.UtcNow - s_LastFlush).TotalSeconds >= 1 || type != LogType.Log) { s_Writer.Flush(); s_LastFlush = DateTime.UtcNow; }
                }
                catch (Exception) { s_Writer = null; Application.logMessageReceivedThreaded -= OnLog; }
            }
        }

        public static void Stop()
        {
            lock (s_Lock)
            {
                Application.logMessageReceivedThreaded -= OnLog;
                try { s_Writer?.Flush(); s_Writer?.Dispose(); } catch (Exception) { }
                s_Writer = null;
            }
        }
    }
}
