using UnityEditor;
using UnityEngine;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>
    /// The runtime break-effect pool ("Quick Destruction FX Pool") is normally torn down with
    /// Play Mode. This fallback removes pools left by older package versions or an interrupted
    /// script/domain reload, together with their pooled particle systems and audio sources.
    ///
    /// Editor-only: it changes nothing at runtime or in a build, where DontDestroyOnLoad objects
    /// are reclaimed normally.
    /// </summary>
    [InitializeOnLoad]
    static class DestructionFxPoolCleanup
    {
        // Must match the name assigned in DestructionBreakEffects.GetOrCreate().
        const string PoolRootName = "Quick Destruction FX Pool";

        static DestructionFxPoolCleanup()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // Wait until Play has fully stopped, then remove any pool that survived the exit.
            if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.delayCall += () => CleanupLeakedPools(false);
        }

        static void CleanupLeakedPools(bool logWhenEmpty)
        {
            // The pool is legitimately alive and in use while playing.
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            // FindObjectsOfTypeAll reaches hidden / DontSave objects that ordinary scene
            // queries skip, which is exactly what a leaked pool is.
            var all = Resources.FindObjectsOfTypeAll<GameObject>();
            int removed = 0;
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null || go.name != PoolRootName) continue;
                if (go.transform.parent != null) continue;   // only the pool root itself
                if (EditorUtility.IsPersistent(go)) continue; // never a project asset / prefab
                Object.DestroyImmediate(go);
                removed++;
            }

            // Automatic cleanup is intentionally silent. A stale pool can be a benign
            // consequence of an interrupted domain reload, and the user has no action to
            // take after it has already been removed. Explicit diagnostic calls can still
            // request a result in the Console.
            if (logWhenEmpty)
            {
                if (removed > 0)
                    Debug.Log($"[Quick Destruction] Removed {removed} stale FX pool " +
                              "object(s), including pooled particles / audio.");
                else
                    Debug.Log("[Quick Destruction] No stale FX pool objects found.");
            }
        }
    }
}
