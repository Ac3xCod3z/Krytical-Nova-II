using System.Collections.Generic;
using System.IO;
using System.Text;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Persistence;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Owner, 13 Nov 2026 (build 17, item 1): names may not contain "GM" (<see cref="CharacterNameRules.LooksLikeGm"/>). The rule
    /// stops new names; characters made before it keep theirs until the owner decides, so this lists them. It only reads a save
    /// folder (a server's, an unzipped backup, or "Copy the Save Database into Files" of the TEST database first); it never changes a
    /// save and never talks to a database itself.
    /// </summary>
    public static class GmNameOffenders
    {
        [MenuItem("Krytical Nova/Online/Saves/List Names With GM...", priority = 83)]
        private static void MenuList()
        {
            string folder = EditorUtility.OpenFolderPanel("A copy of the saves (a save folder; it is only read)", "", "");
            if (string.IsNullOrEmpty(folder)) return;
            if (!Directory.Exists(Path.Combine(folder, "accounts")))
            {
                EditorUtility.DisplayDialog("Names With GM", "There is no accounts folder in " + folder + ": that is not a save folder.", "OK");
                return;
            }
            List<string> found = Find(new FileSaveStore(folder, background: false));
            string report = Report(found);
            Debug.Log("[Save] " + report);
            EditorUtility.DisplayDialog("Names With GM", report, "OK");
        }

        /// <summary>Every saved character whose name breaks the GM rule, as "Name (account / character)", sorted by name.</summary>
        public static List<string> Find(ISaveStore from)
        {
            var found = new List<string>();
            foreach (string key in from.Keys(ServerSave.AccountsPrefix))
            {
                string id = key.Substring(ServerSave.AccountsPrefix.Length);
                if (!SaveKeys.IsId(id)) continue;
                AccountSave account = ServerSave.ReadAccountFrom(from, id);
                if (account?.characters == null) continue;
                foreach (CharacterSave character in account.characters)
                    if (character != null && CharacterNameRules.LooksLikeGm(character.name))
                        found.Add($"{character.name} ({account.accountId} / {character.characterId})");
            }
            found.Sort(System.StringComparer.OrdinalIgnoreCase);
            return found;
        }

        public static string Report(List<string> found)
        {
            if (found.Count == 0) return "No saved character has GM in its name.";
            var text = new StringBuilder(found.Count + " saved character(s) have GM in the name (nothing was changed):\n");
            foreach (string line in found) text.Append("  ").Append(line).Append('\n');
            return text.ToString();
        }
    }
}
