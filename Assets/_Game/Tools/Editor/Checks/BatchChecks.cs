using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KryticalNova.EditorTools.Organization;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.Checks
{
    /// <summary>
    /// Runs Validate Project Organization and Check Icons in a batch Unity, with no window and no dialog, so a script
    /// can check a checkout without the owner's Editor:
    /// <c>Unity -batchmode -projectPath &lt;checkout&gt; -executeMethod KryticalNova.EditorTools.Checks.BatchChecks.Run</c>
    /// (run_batch_checks.py beside kn_check.py does this). Writes one line per finding to Logs/batch-checks.txt as
    /// <c>check, severity, path, message</c> separated by tabs, then a <c>done</c> line; exits 1 when there is an error,
    /// 2 when a check itself broke, else 0.
    /// </summary>
    public static class BatchChecks
    {
        public const string ResultsPath = "Logs/batch-checks.txt";

        public static void Run()
        {
            var lines = new List<string>();
            bool errors = false, broke = false;
            void Add(string check, string severity, string path, string message)
            {
                if (severity == "error") errors = true;
                lines.Add(string.Join("\t", check, severity, Clean(path), Clean(message)));
            }

            try
            {
                foreach (ProjectOrganizationValidator.Issue issue in ProjectOrganizationValidator.Validate())
                    Add("organization", issue.Severity == ProjectOrganizationValidator.Severity.Error ? "error" : "warning",
                        issue.Path, issue.Message);
            }
            catch (Exception e)
            {
                broke = true;
                Add("organization", "broke", "", e.ToString());
            }

            try
            {
                foreach (string pack in IconPacks.Missing())
                    Add("icons", "warning", "", $"The {pack} icon pack is not in this copy of the project.");
                foreach (string problem in IconPacks.Check(IconBaker.Library()))
                    Add("icons", "error", "", problem);
            }
            catch (Exception e)
            {
                broke = true;
                Add("icons", "broke", "", e.ToString());
            }

            lines.Add("done");
            Directory.CreateDirectory(Path.GetDirectoryName(ResultsPath));
            File.WriteAllLines(ResultsPath, lines, new UTF8Encoding(false));
            Debug.Log($"[Checks] {lines.Count - 1} finding(s) written to {ResultsPath}.");
            EditorApplication.Exit(broke ? 2 : errors ? 1 : 0);
        }

        private static string Clean(string text) =>
            (text ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
    }
}
