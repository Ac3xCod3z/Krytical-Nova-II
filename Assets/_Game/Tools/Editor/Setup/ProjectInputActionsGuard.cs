using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Keeps the Input System's project-wide actions pointing at the live input asset. The window keys (Pack, Map,
    /// Settings ...) are read from <see cref="InputSystem.actions"/>. When the asset is reimported under a running
    /// Editor, most often in a Multiplayer Play Mode clone, that pointer is left on the destroyed copy: the asset is
    /// loaded and fine, but every window key stops working until the Editor restarts. The Editor's own record of
    /// which asset it is (the build settings entry the Input System writes) still resolves, so the pointer is set
    /// again from there. Does nothing while the pointer is healthy, and nothing in a build.
    /// </summary>
    [InitializeOnLoad]
    internal static class ProjectInputActionsGuard
    {
        // The key the Input System stores its project-wide actions under (ProjectWideActionsBuildProvider).
        private const string ConfigKey = "com.unity.input.settings.actions";

        static ProjectInputActionsGuard()
        {
            EditorApplication.delayCall += Repair;
            EditorApplication.projectChanged += Repair;
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.ExitingEditMode) Repair();
            };
        }

        private static void Repair()
        {
            InputActionAsset current = InputSystem.actions;
            // Never assigned (a project without project-wide actions), or alive: nothing to do.
            if (ReferenceEquals(current, null) || current != null) return;
            if (!EditorBuildSettings.TryGetConfigObject(ConfigKey, out InputActionAsset live) || live == null) return;
            try
            {
                InputSystem.actions = live;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Input] The project-wide input actions point at a destroyed asset and could not be set again: {e.Message}");
            }
        }
    }
}
