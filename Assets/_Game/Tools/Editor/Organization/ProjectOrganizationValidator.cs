using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace KryticalNova.EditorTools.Organization
{
    /// <summary>
    /// Audits Assets/ against Assets/_Game/_Docs/ProjectOrganization.md.
    /// Krytical Nova/Project/Validate Project Organization — results go to the Console (click to ping).
    /// </summary>
    public static class ProjectOrganizationValidator
    {
        public enum Severity { Error, Warning }

        public readonly struct Issue
        {
            public readonly Severity Severity;
            public readonly string Path;
            public readonly string Message;

            public Issue(Severity severity, string path, string message)
            {
                Severity = severity;
                Path = path;
                Message = message;
            }
        }

        // grassFINAL_REAL, "Copy of X", "New Material", "Untitled", "X (1)", "grass2"...
        private static readonly Regex SloppyName = new Regex(
            @"(?i:_final|final_|_old$|_backup|backup_|^copy of|\bcopy$|^new |^untitled|^temp_)|FINAL|Final$|\(\d+\)$|^[a-z]+\d+$");

        private static readonly Regex UsesEditorApi = new Regex(@"^\s*using\s+UnityEditor\b", RegexOptions.Multiline);

        [MenuItem("Krytical Nova/Project/Validate Project Organization", priority = 120)]
        private static void ValidateMenu()
        {
            var issues = Validate();
            foreach (var issue in issues)
            {
                var context = AssetDatabase.LoadAssetAtPath<Object>(issue.Path);
                string line = $"[Organization] {issue.Path}: {issue.Message}";
                if (issue.Severity == Severity.Error) Debug.LogError(line, context);
                else Debug.LogWarning(line, context);
            }

            int errors = issues.Count(i => i.Severity == Severity.Error);
            int warnings = issues.Count - errors;
            string summary = issues.Count == 0
                ? "The project follows the organization rules."
                : $"{errors} error(s), {warnings} warning(s). Details are in the Console.";
            Debug.Log($"[Organization] Validation finished: {summary}");
            EditorUtility.DisplayDialog("Project Organization", summary, "OK");
        }

        public static List<Issue> Validate()
        {
            var issues = new List<Issue>();
            CheckRoot(issues);
            CheckGameFolder(issues);
            return issues;
        }

        private static void CheckRoot(List<Issue> issues)
        {
            foreach (var path in ProjectOrganizationRules.RootEntries())
            {
                bool isFolder = AssetDatabase.IsValidFolder(path);
                switch (ProjectOrganizationRules.Classify(Path.GetFileName(path), isFolder))
                {
                    case ProjectOrganizationRules.RootKind.LegacyBucket:
                        issues.Add(new Issue(Severity.Error, path,
                            "type-bucket folder at the Assets root. Move its contents to _Game/<System>/ (ours) or _ThirdParty/<Vendor>/ (theirs)."));
                        break;
                    case ProjectOrganizationRules.RootKind.Unsorted:
                        issues.Add(new Issue(Severity.Error, path, isFolder
                            ? "unrecognised root folder. Vendor content goes in _ThirdParty/ (Krytical Nova/Project/Quarantine Root Folders...)."
                            : "loose file at the Assets root. Give it an owning folder, or add it to PinnedRootFiles if a tool requires this path."));
                        break;
                }
            }
        }

        private static void CheckGameFolder(List<Issue> issues)
        {
            string root = ProjectOrganizationRules.GameRoot;
            if (!AssetDatabase.IsValidFolder(root)) return;

            foreach (var file in Directory.GetFiles(root))
            {
                if (file.EndsWith(".meta")) continue;
                issues.Add(new Issue(Severity.Warning, file.Replace('\\', '/'),
                    "file directly in _Game/. Every asset needs an owning system folder."));
            }

            foreach (var raw in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (raw.EndsWith(".meta")) continue;
                string path = raw.Replace('\\', '/');
                if (path.Contains("/_Docs/")) continue;

                string name = Path.GetFileNameWithoutExtension(path);
                if (SloppyName.IsMatch(name))
                    issues.Add(new Issue(Severity.Warning, path,
                        "unclear asset name. Use descriptive PascalCase / Type_Group_Variant names (e.g. Grass_Meadow_Tall)."));

                if (path.EndsWith(".cs")) CheckScript(path, issues);
            }
        }

        private static void CheckScript(string path, List<Issue> issues)
        {
            var folders = path.Split('/').Skip(2).Reverse().Skip(1).ToList(); // parents, below _Game, nearest first
            bool inEditorFolder = folders.Contains("Editor");

            if (!folders.Any(ProjectOrganizationRules.ScriptFolderNames.Contains))
                issues.Add(new Issue(Severity.Warning, path,
                    "script outside a Runtime/, Editor/, Data/ or Tests/ folder of its system."));

            string assembly = CompilationPipeline.GetAssemblyNameFromScriptPath(path) ?? "";
            if (assembly.StartsWith("Assembly-CSharp"))
                issues.Add(new Issue(Severity.Error, path,
                    "not covered by an .asmdef/.asmref, so it compiles into Assembly-CSharp. Add an asmref to the system's assembly."));

            if (!inEditorFolder && !path.Contains("/Tests/"))
            {
                string source = File.ReadAllText(path);
                if (UsesEditorApi.IsMatch(source) && !source.Contains("#if UNITY_EDITOR"))
                    issues.Add(new Issue(Severity.Error, path,
                        "uses UnityEditor outside an Editor/ folder without #if UNITY_EDITOR; player builds will fail."));
            }
        }
    }
}
