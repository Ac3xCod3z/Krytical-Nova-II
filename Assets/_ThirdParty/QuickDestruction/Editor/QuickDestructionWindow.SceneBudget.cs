using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RipVertices.QuickDestruction.Editor
{
    public partial class QuickDestructionWindow
    {
        static readonly string[] s_sceneDebrisRuleOptions =
        {
            "Scene Limit", "Keep All Debris", "All Debris After Rest",
            "Small Debris After Rest", "Dynamic Debris After Rest",
            "Small and Runtime After Rest"
        };

        internal static DestructionBudgetManager FindSceneDebrisBudget(Scene scene)
        {
#if UNITY_6000_4_OR_NEWER
            var managers = Object.FindObjectsByType<DestructionBudgetManager>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
            var managers = Object.FindObjectsByType<DestructionBudgetManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var managers = Object.FindObjectsOfType<DestructionBudgetManager>(true);
#endif
            foreach (DestructionBudgetManager manager in managers)
                if (manager.gameObject.scene == scene && (manager.gameObject.hideFlags & HideFlags.DontSave) == 0)
                    return manager;
            return null;
        }

        internal static DestructionBudgetManager CreateSceneDebrisBudget(Scene scene)
        {
            DestructionBudgetManager existing = FindSceneDebrisBudget(scene);
            if (existing != null) return existing;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            var go = new GameObject("Quick Destruction Scene Budget");
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Create Scene Debris Budget");
            DestructionBudgetManager manager = Undo.AddComponent<DestructionBudgetManager>(go);
            Undo.CollapseUndoOperations(undoGroup);
            return manager;
        }

        void DrawSceneDebrisBudget()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return;
            DestructionBudgetManager manager = FindSceneDebrisBudget(scene);
            QDUI.BeginCard();
            QDUI.Header("Scene Debris Budget");
            bool enabled = manager != null && manager.SceneDebrisCleanup;
            bool requested = QDUI.ToggleRow("Scene Debris Cleanup", enabled);
            if (requested != enabled)
            {
                manager = manager != null ? manager : CreateSceneDebrisBudget(scene);
                Undo.RecordObject(manager, "Toggle Scene Debris Cleanup");
                manager.SceneDebrisCleanup = requested;
                EditorUtility.SetDirty(manager);
                EditorSceneManager.MarkSceneDirty(scene);
                GUIUtility.ExitGUI();
            }
            if (enabled)
            {
                EditorGUI.BeginChangeCheck();
                Undo.RecordObject(manager, "Edit Scene Debris Budget");
                var rule = (SceneDebrisCleanupRule)QDUI.PopupRow(
                    "Debris Cleanup Rule", (int)manager.CleanupRule, s_sceneDebrisRuleOptions);
                if (rule != manager.CleanupRule)
                {
                    manager.CleanupRule = rule;
                    EditorGUI.EndChangeCheck();
                    EditorUtility.SetDirty(manager);
                    EditorSceneManager.MarkSceneDirty(scene);
                    // The new rule changes the control tree; obtain a fresh Layout.
                    GUIUtility.ExitGUI();
                }
                if (manager.CleanupRule == SceneDebrisCleanupRule.SceneLimit)
                {
                    manager.MaxSceneDebris = QDUI.IntSliderRow("Max Scene Debris", manager.MaxSceneDebris, 0, 5000);
                    manager.MaxSceneDebrisTriangles = QDUI.IntSliderRow("Scene Triangle Budget", manager.MaxSceneDebrisTriangles, 0, 2000000);
                }
                if (manager.CleanupRule != SceneDebrisCleanupRule.KeepAll)
                {
                    manager.SceneDebrisLifetime = QDUI.SliderRow("Scene Despawn After (s)", manager.SceneDebrisLifetime, 1f, 300f, "0");
                    if (manager.CleanupRule == SceneDebrisCleanupRule.SmallAfterDelay ||
                        manager.CleanupRule == SceneDebrisCleanupRule.SmallAndRuntimeAfterDelay)
                        manager.SmallDebrisMaxVolume = QDUI.SliderRow("Small Debris Volume (m3)", manager.SmallDebrisMaxVolume, 0.001f, 1f, "0.###");
                }
                if (manager.CleanupRule != SceneDebrisCleanupRule.KeepAll)
                    manager.SceneDebrisRemovalsPerFrame = QDUI.IntSliderRow("Removals Per Frame", manager.SceneDebrisRemovalsPerFrame, 1, 64);
                if (EditorGUI.EndChangeCheck())
                {
                    EditorUtility.SetDirty(manager);
                    EditorSceneManager.MarkSceneDirty(scene);
                }
            }
            GUILayout.Label(
                "Applies to loose debris in the active scene, except profiles with Keep Debris enabled. " +
                "All removal rules wait for continuous rest on a supporting surface (30 seconds by default). " +
                "Movement or loss of support restarts the wait. All Debris After Rest is the default for new managers. " +
                "Small and Runtime keeps large baked pieces and removes small pieces or runtime-generated debris after rest. " +
                "Scene Limit removes the smallest eligible excess; the count may stay above its cap while debris is airborne " +
                "or still resting. Keep All disables removal. Dynamic includes sleeping bodies but excludes frozen proxies. " +
                "Save the scene to preserve this setup.", QDUI.HelpText);
            QDUI.EndCard();
        }

        void DrawSceneCleanupOverrideNotice()
        {
            if (m_profile != null && m_profile.KeepDebris)
            {
                QDUI.InfoBox("Keep Debris is enabled for this profile. Scene rules, profile timers and automatic budget eviction cannot remove its pieces. Change Keep Debris in Runtime.");
                return;
            }
            DestructionBudgetManager manager = FindSceneDebrisBudget(SceneManager.GetActiveScene());
            if (manager != null && manager.isActiveAndEnabled && manager.SceneDebrisCleanup)
                QDUI.InfoBox("The active scene uses " + s_sceneDebrisRuleOptions[Mathf.Clamp((int)manager.CleanupRule, 0, s_sceneDebrisRuleOptions.Length - 1)] +
                    ". Automatic removal in this profile is overridden. Change the rule in Runtime > Scene Debris Budget.");
        }
    }
}
