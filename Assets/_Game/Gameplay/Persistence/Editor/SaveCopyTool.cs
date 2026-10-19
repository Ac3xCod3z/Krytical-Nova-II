using System;
using System.Collections.Generic;
using System.IO;
using KryticalNova.Core;
using KryticalNova.Persistence;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Carrying saves across (online phase 4, milestone 2): a server's save files into the save database, and the
    /// database back into files (to study a copy offline, or to go back). The source is only read. An account the
    /// target already has is left alone and named in the report, so nothing there is ever overwritten. The save service
    /// of the chosen database must be running (real: SaveService.exe; test: SaveService.exe --test).
    /// </summary>
    public static class SaveCopyTool
    {
        private const string Menu = "Krytical Nova/Online/Saves/";
        private const string RealUrl = "http://127.0.0.1:7801";
        private const string TestUrl = "http://127.0.0.1:7802";

        private static string KeyFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "KryticalNova-Secrets", "save-service-key.txt");

        [MenuItem(Menu + "Copy Save Files into the Save Database...", priority = 80)]
        private static void MenuCopyIn()
        {
            string folder = EditorUtility.OpenFolderPanel("The server's save folder (it is only read)", DefaultSaveFolder(), "");
            if (string.IsNullOrEmpty(folder)) return;
            if (!Directory.Exists(Path.Combine(folder, "accounts"))) { EditorUtility.DisplayDialog("Copy Save Files", "There is no accounts folder in " + folder + ": that is not a server's save folder.", "OK"); return; }
            if (!ChooseDatabase("Copy the saves in " + folder + " into which database?", out string url, out string which)) return;
            EditorUtility.DisplayDialog("Copy Save Files", CopyIn(folder, url, which), "OK");
        }

        [MenuItem(Menu + "Copy the Save Database into Files...", priority = 81)]
        private static void MenuCopyOut()
        {
            if (!ChooseDatabase("Copy which database into files?", out string url, out string which)) return;
            string folder = EditorUtility.SaveFolderPanel("An empty folder for the copy", "", "SaveDatabaseCopy");
            if (string.IsNullOrEmpty(folder)) return;
            if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length > 0) { EditorUtility.DisplayDialog("Copy the Save Database", "Choose an empty folder: nothing is ever written over.", "OK"); return; }
            EditorUtility.DisplayDialog("Copy the Save Database", CopyOut(url, which, folder), "OK");
        }

        /// <summary>Copies a save folder into a database. Returns the report.</summary>
        public static string CopyIn(string folder, string url, string which)
        {
            if (!Connect(url, out ServiceSaveStore service, out string problem)) return problem;
            var files = new FileSaveStore(folder, background: false);
            int accounts = 0, characters = 0, world = 0;
            var skipped = new List<string>();
            try
            {
                foreach (string id in Ids(files))
                {
                    if (service.TryRead(ServerSave.AccountsPrefix + id, out _)) { skipped.Add(id); continue; }
                    AccountSave account = ServerSave.ReadAccountFrom(files, id);
                    if (account == null) continue;
                    account.accountId = id;
                    var batch = new SaveBatch();
                    batch.Writes.AddRange(ServerSave.SplitDocuments(account));
                    service.WriteBatch(batch);
                    accounts++;
                    characters += account.characters.Count;
                }
                foreach (string key in files.Keys(ServerSave.WorldPrefix))
                {
                    if (!files.TryRead(key, out string json)) continue;
                    if (service.TryRead(key, out _)) { skipped.Add(key); continue; }
                    service.Write(key, json);
                    world++;
                }
                service.Flush();
            }
            finally
            {
                service.Dispose();
            }
            string report = $"Copied into the {which} database: {accounts} account(s) with {characters} character(s), {world} world document(s), each in parts.";
            if (skipped.Count > 0) report += $"\n\nLeft alone because the database already has them ({skipped.Count}): {string.Join(", ", skipped)}.";
            Debug.Log("[Save] " + report.Replace("\n\n", " "));
            return report;
        }

        /// <summary>Copies a database into an empty folder, in the parts layout. Returns the report.</summary>
        public static string CopyOut(string url, string which, string folder)
        {
            if (!Connect(url, out ServiceSaveStore service, out string problem)) return problem;
            Directory.CreateDirectory(folder);
            var files = new FileSaveStore(folder, background: false);
            int accounts = 0, world = 0;
            try
            {
                foreach (string id in Ids(service))
                {
                    AccountSave account = ServerSave.ReadAccountFrom(service, id);
                    if (account == null) continue;
                    account.accountId = id;
                    foreach (KeyValuePair<string, string> document in ServerSave.SplitDocuments(account)) files.Write(document.Key, document.Value);
                    accounts++;
                }
                foreach (string key in service.Keys(ServerSave.WorldPrefix))
                    if (service.TryRead(key, out string json)) { files.Write(key, json); world++; }
            }
            finally
            {
                service.Dispose();
            }
            string report = $"Copied the {which} database into {folder}: {accounts} account(s), {world} world document(s).";
            Debug.Log("[Save] " + report);
            return report;
        }

        private static List<string> Ids(ISaveStore store)
        {
            var ids = new List<string>();
            foreach (string key in store.Keys(ServerSave.AccountsPrefix))
            {
                string id = key.Substring(ServerSave.AccountsPrefix.Length);
                if (SaveKeys.IsId(id)) ids.Add(id);
            }
            return ids;
        }

        private static bool ChooseDatabase(string question, out string url, out string which)
        {
            url = which = null;
            int pick = EditorUtility.DisplayDialogComplex("Which save database?", question +
                "\n\nTEST: krytical_nova_test, for trying things (the save service with --test must be running).\n" +
                "REAL: krytical_nova, your players' saves (SaveService.exe must be running).", "Test database", "Cancel", "Real database");
            if (pick == 1) return false;
            url = pick == 0 ? TestUrl : RealUrl;
            which = pick == 0 ? "TEST" : "REAL";
            return true;
        }

        private static bool Connect(string url, out ServiceSaveStore service, out string problem)
        {
            service = null;
            if (!File.Exists(KeyFile)) { problem = "There is no save service key at " + KeyFile + ": start the save service once."; return false; }
            service = new ServiceSaveStore(url, File.ReadAllText(KeyFile).Trim(), "copy-tool");
            if (service.Reachable(out problem)) return true;
            service.Dispose();
            service = null;
            problem = "The save service at " + url + " is not reachable: " + problem + ". Start it first.";
            return false;
        }

        private static string DefaultSaveFolder() =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "Server", "Windows", "KryticalNovaServer-Save"));
    }
}
