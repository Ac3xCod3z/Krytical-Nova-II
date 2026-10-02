using UnityEditor;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>
    /// Preserves the user's Scene View Gizmos choice across the Play Mode transition.
    /// Unity can restore a separate play-mode Scene View state, which must not make
    /// Quick Destruction appear to enable Gizmos on the user's behalf.
    /// </summary>
    [InitializeOnLoad]
    static class PlayModeSceneViewGuard
    {
        const string SessionPrefix = "QuickDestruction.PlayModeSceneView.";
        const string CapturedKey = SessionPrefix + "Captured";
        const string CountKey = SessionPrefix + "Count";

        static PlayModeSceneViewGuard()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    CaptureSceneViewStates();
                    break;

                case PlayModeStateChange.EnteredPlayMode:
                    // Run after Unity has restored its play-mode Scene View state.
                    EditorApplication.delayCall += ApplyCapturedSceneViewStates;
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    RestoreSceneViewStates();
                    break;
            }
        }

        static void CaptureSceneViewStates()
        {
            int count = SceneView.sceneViews.Count;
            SessionState.SetInt(CountKey, count);
            for (int i = 0; i < count; i++)
            {
                var view = SceneView.sceneViews[i] as SceneView;
                SessionState.SetBool(SessionPrefix + "Gizmos." + i,
                    view != null && view.drawGizmos);
            }
            SessionState.SetBool(CapturedKey, true);
        }

        static void ApplyCapturedSceneViewStates()
        {
            if (!EditorApplication.isPlaying) return;
            if (!SessionState.GetBool(CapturedKey, false)) return;

            int count = SessionState.GetInt(CountKey, 0);
            int available = SceneView.sceneViews.Count;
            for (int i = 0; i < count && i < available; i++)
            {
                if (SceneView.sceneViews[i] is not SceneView view) continue;
                view.drawGizmos = SessionState.GetBool(
                    SessionPrefix + "Gizmos." + i, view.drawGizmos);
                view.Repaint();
            }
        }

        static void RestoreSceneViewStates()
        {
            if (!SessionState.GetBool(CapturedKey, false)) return;

            int count = SessionState.GetInt(CountKey, 0);
            int available = SceneView.sceneViews.Count;
            for (int i = 0; i < count && i < available; i++)
            {
                if (SceneView.sceneViews[i] is not SceneView view) continue;
                view.drawGizmos = SessionState.GetBool(
                    SessionPrefix + "Gizmos." + i, true);
                view.Repaint();
            }

            SessionState.SetBool(CapturedKey, false);
        }
    }
}
