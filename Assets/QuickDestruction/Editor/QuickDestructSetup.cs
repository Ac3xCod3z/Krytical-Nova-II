using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>
    /// One-click destructible presets on the GameObject context menu. Each command analyzes
    /// the selection, assigns a shared preset material and bakes with an in-memory preset
    /// profile, so a usable destructible needs no manual profile or material setup.
    /// </summary>
    public static class QuickDestructSetup
    {
        const string MenuRoot = "GameObject/Quick Destruction/";

        [MenuItem(MenuRoot + "Make Destructible (Concrete)", false, 10)]
        static void MakeConcrete() => MakeDestructible(FracturePattern.UniformConcrete, "Concrete");

        [MenuItem(MenuRoot + "Make Destructible (Glass)", false, 11)]
        static void MakeGlass() => MakeDestructible(FracturePattern.RadialGlass, "Glass");

        [MenuItem(MenuRoot + "Make Destructible (Wood)", false, 12)]
        static void MakeWood() => MakeDestructible(FracturePattern.DirectionalWood, "Wood");

        [MenuItem(MenuRoot + "Make Destructible (Brick)", false, 13)]
        static void MakeBrick() => MakeDestructible(FracturePattern.BrickCells, "Brick");

        [MenuItem(MenuRoot + "Make Destructible (Slabs)", false, 14)]
        static void MakeSlabs() => MakeDestructible(FracturePattern.LayeredSlabs, "Slab");

        [MenuItem(MenuRoot + "Make Destructible (Pillars)", false, 15)]
        static void MakePillars() => MakeDestructible(FracturePattern.ColumnarPillars, "Basalt");

        [MenuItem(MenuRoot + "Make Destructible (Impact)", false, 16)]
        static void MakeImpact() => MakeDestructible(FracturePattern.ImpactShatter, "Stone");

        [MenuItem(MenuRoot + "Make Destructible (Concrete)", true)]
        static bool ValidateConcrete() => HasMeshSelection();

        [MenuItem(MenuRoot + "Make Destructible (Glass)", true)]
        static bool ValidateGlass() => HasMeshSelection();

        [MenuItem(MenuRoot + "Make Destructible (Wood)", true)]
        static bool ValidateWood() => HasMeshSelection();

        [MenuItem(MenuRoot + "Make Destructible (Brick)", true)]
        static bool ValidateBrick() => HasMeshSelection();

        [MenuItem(MenuRoot + "Make Destructible (Slabs)", true)]
        static bool ValidateSlabs() => HasMeshSelection();

        [MenuItem(MenuRoot + "Make Destructible (Pillars)", true)]
        static bool ValidatePillars() => HasMeshSelection();

        [MenuItem(MenuRoot + "Make Destructible (Impact)", true)]
        static bool ValidateImpact() => HasMeshSelection();

        static bool HasMeshSelection()
        {
            return Selection.activeGameObject != null &&
                   Selection.activeGameObject.GetComponentInChildren<MeshFilter>() != null;
        }

        static void MakeDestructible(FracturePattern pattern, string label)
        {
            int baked = 0;
            foreach (GameObject go in Selection.gameObjects)
            {
                if (go == null || go.GetComponentInChildren<MeshFilter>() == null)
                    continue;

                var report = MeshAnalyzer.Analyze(go);
                if (report.Status == AnalysisSeverity.Blocked)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine($"'{go.name}' cannot be baked with the {label} preset:");
                    sb.AppendLine();
                    foreach (var item in report.Items)
                    {
                        if (item.Severity != AnalysisSeverity.Blocked) continue;
                        sb.AppendLine("- " + item.Title);
                        if (!string.IsNullOrEmpty(item.Fix))
                            sb.AppendLine("  Fix: " + item.Fix);
                    }
                    Debug.LogWarning("[Quick Destruction] " + sb);
                    EditorUtility.DisplayDialog(
                        "Quick Destruction - " + label + " Preset", sb.ToString(), "OK");
                    continue;
                }

                bool allowUnsafeFractureInput = false;
                if (!ValidatePresetFractureInput(
                        report, pattern, out string topologyWarning))
                {
                    // The temporary validation profile is replaced below by the actual
                    // preset instance. Only source topology is inspected here, so no
                    // generated asset or source object is changed by this confirmation.
                    if (!MeshAnalyzer.IsRecoverableUnsafeInput(topologyWarning) ||
                        Application.isBatchMode ||
                        !EditorUtility.DisplayDialog(
                            "Quick Destruction - " + label + " warning",
                            topologyWarning +
                            "\n\nContinue with this preset anyway? Every generated " +
                            "fragment is still validated before it can be saved.",
                            "MAKE DESTRUCTIBLE ANYWAY", "CANCEL"))
                        continue;
                    allowUnsafeFractureInput = true;
                }

                // Assign the shared preset material so the prop looks the part immediately.
                Material presetMaterial = PresetMaterialLibrary.GetMaterial(pattern, 0);
                var renderer = go.GetComponentInChildren<MeshRenderer>();
                if (renderer != null && presetMaterial != null)
                {
                    Undo.RecordObject(renderer, "Quick Destruction " + label + " Preset");
                    renderer.sharedMaterial = presetMaterial;
                }

                // In-memory profile, never saved as an asset. This is only safe because
                // nothing between CreateInstance and the end of the bake triggers an
                // "Unloading unused Assets" pass (no NewScene, no AssetDatabase.Refresh);
                // keep it that way. The finally guarantees the instance never leaks.
                var profile = ScriptableObject.CreateInstance<DestructionProfile>();
                profile.name = label + " Preset";
                profile.ApplyPreset(pattern);
                // Built-in one-click presets use the source material on cut faces.
                // Do not create an unused generated interior material asset.
                profile.InteriorMaterial = null;
                try
                {
                    BakeOutcome outcome = BakePipeline.Bake(go, profile, true,
                        allowUnsafeFractureInput);
                    if (outcome.Success)
                    {
                        baked++;
                        Debug.Log($"[Quick Destruction] '{go.name}' baked with the {label} " +
                            $"preset ({outcome.FragmentCount} fragments, " +
                            $"{outcome.Seconds:0.0} s). Output: {outcome.PrefabPath}");
                    }
                    else if (outcome.Cancelled)
                    {
                        Debug.Log($"[Quick Destruction] {label} bake of '{go.name}' was " +
                            "cancelled. Previous output was left untouched.");
                    }
                    else
                    {
                        string message = $"The {label} bake of '{go.name}' failed:\n\n" +
                            outcome.Error;
                        Debug.LogError("[Quick Destruction] " + message);
                        EditorUtility.DisplayDialog(
                            "Quick Destruction - " + label + " Preset", message, "OK");
                    }
                }
                finally
                {
                    Object.DestroyImmediate(profile);
                }
            }

            if (baked > 1)
                Debug.Log($"[Quick Destruction] {label} preset: baked {baked} destructibles.");
        }

        static bool ValidatePresetFractureInput(AnalysisReport report,
            FracturePattern pattern, out string warning)
        {
            var profile = ScriptableObject.CreateInstance<DestructionProfile>();
            try
            {
                profile.hideFlags = HideFlags.HideAndDontSave;
                profile.ApplyPreset(pattern);
                return MeshAnalyzer.ValidateClosedFractureInput(
                    report, profile, out warning);
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
