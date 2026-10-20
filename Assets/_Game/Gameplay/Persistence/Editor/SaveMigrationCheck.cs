using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KryticalNova.Core;
using KryticalNova.Persistence;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Save migrations, before a release ships (online phase 4): runs a copy of real saves through every step this build
    /// would take when they load, and says what would fail. The copy is a save folder (a server's, an unzipped backup, or
    /// the copy tool's "Copy the Save Database into Files"); it is only read. Every part whose system has a version
    /// (<see cref="IMigratingSave"/> on the Player prefab) is stepped up; a part no system on the Player prefab saves any
    /// more is listed too.
    /// </summary>
    public static class SaveMigrationCheck
    {
        [MenuItem("Krytical Nova/Online/Saves/Check Saves Against This Build...", priority = 82)]
        private static void MenuCheck()
        {
            string folder = EditorUtility.OpenFolderPanel("A copy of the saves (a save folder; it is only read)", "", "");
            if (string.IsNullOrEmpty(folder)) return;
            if (!Directory.Exists(Path.Combine(folder, "accounts")))
            {
                EditorUtility.DisplayDialog("Check Saves", "There is no accounts folder in " + folder + ": that is not a save folder.", "OK");
                return;
            }
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerSystemsSetup.PlayerPrefabPath);
            string report = Check(new FileSaveStore(folder, background: false), PartMigrators(player), out int failed, out _);
            Debug.Log("[Save] " + report);
            EditorUtility.DisplayDialog(failed == 0 ? "Check Saves: all good" : "Check Saves: " + failed + " would not load", report, "OK");
        }

        /// <summary>Every save part the Player prefab knows, by its key: its migrator when it has a version, else null.</summary>
        public static Dictionary<string, IMigratingSave> PartMigrators(GameObject player)
        {
            var parts = new Dictionary<string, IMigratingSave>(StringComparer.Ordinal);
            if (player == null) return parts;
            foreach (ISaveParticipant participant in player.GetComponentsInChildren<ISaveParticipant>(true))
                parts[participant.SaveKey] = participant as IMigratingSave;
            return parts;
        }

        /// <summary>
        /// The check itself: every account in <paramref name="from"/>, every part of it and of its characters, stepped up by its
        /// system. <paramref name="failed"/> counts the parts that would stop a character from loading.
        /// </summary>
        public static string Check(ISaveStore from, IReadOnlyDictionary<string, IMigratingSave> parts, out int failed, out int upgraded)
        {
            int failures = 0, steppedUp = 0;
            int accounts = 0, characters = 0, checkedParts = 0;
            var problems = new List<string>();
            var unknown = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var steps = new SortedDictionary<string, int>(StringComparer.Ordinal);

            foreach (string key in from.Keys(ServerSave.AccountsPrefix))
            {
                string id = key.Substring(ServerSave.AccountsPrefix.Length);
                if (!SaveKeys.IsId(id)) continue;
                AccountSave account = ServerSave.ReadAccountFrom(from, id);
                if (account == null) continue;
                accounts++;
                void One(SavePart part, string whose)
                {
                    if (part == null || string.IsNullOrEmpty(part.json)) return;
                    checkedParts++;
                    if (!parts.TryGetValue(part.key, out IMigratingSave migrator)) { unknown[part.key] = unknown.TryGetValue(part.key, out int n) ? n + 1 : 1; return; }
                    if (migrator == null) return;
                    int at = SaveVersions.Of(part.json);
                    try
                    {
                        SaveVersions.Upgrade(part.json, migrator);
                        if (at < migrator.SaveVersion)
                        {
                            steppedUp++;
                            string step = $"{part.key} {at} to {migrator.SaveVersion}";
                            steps[step] = steps.TryGetValue(step, out int n) ? n + 1 : 1;
                        }
                    }
                    catch (Exception e)
                    {
                        failures++;
                        problems.Add($"{whose}, '{part.key}' (version {at}): {e.Message}");
                    }
                }
                foreach (SavePart part in account.parts) One(part, $"account {id}");
                foreach (CharacterSave character in account.characters)
                {
                    if (character == null) continue;
                    characters++;
                    foreach (SavePart part in character.parts) One(part, $"{character.name} ({id})");
                }
            }

            failed = failures;
            upgraded = steppedUp;
            var report = new StringBuilder();
            report.Append($"Checked {accounts} account(s), {characters} character(s), {checkedParts} part(s): ");
            report.Append(failed == 0 ? "every one loads in this build." : $"{failed} would NOT load (the character is refused and its save kept as it is).");
            if (steps.Count > 0)
            {
                report.Append("\n\nStepped up:");
                foreach (KeyValuePair<string, int> step in steps) report.Append($"\n  {step.Key}: {step.Value} part(s)");
            }
            if (problems.Count > 0)
            {
                report.Append("\n\nWould not load:");
                foreach (string problem in problems.GetRange(0, Math.Min(20, problems.Count))) report.Append("\n  " + problem);
                if (problems.Count > 20) report.Append($"\n  ...and {problems.Count - 20} more.");
            }
            if (unknown.Count > 0)
            {
                report.Append("\n\nSaved by no system on the Player prefab (kept, not read):");
                foreach (KeyValuePair<string, int> key in unknown) report.Append($"\n  {key.Key}: {key.Value} part(s)");
            }
            return report.ToString();
        }
    }
}
