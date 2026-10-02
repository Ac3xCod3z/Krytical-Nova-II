using System;
using UnityEditor;
using UnityEngine;

namespace RipVertices.QuickDestruction.Editor
{
    /// <summary>
    /// Small popup that asks for a profile name and nothing else. Creating a reusable
    /// profile used to open a full save-file browser, which made the author pick a folder
    /// every time even though there is only one sensible destination — the folder the other
    /// profiles already live in. The name is the only real decision, so it is the only thing
    /// asked for here.
    /// </summary>
    sealed class ProfileNamePrompt : EditorWindow
    {
        const float Width = 340f;
        const float Height = 112f;

        string m_name = string.Empty;
        string m_folder = string.Empty;
        Action<string> m_onConfirm;
        bool m_focusRequested;

        internal static void Show(string suggestedName, string folder,
            Action<string> onConfirm)
        {
            var window = CreateInstance<ProfileNamePrompt>();
            window.titleContent = new GUIContent("New Destruction Profile");
            window.m_name = suggestedName ?? string.Empty;
            window.m_folder = folder ?? string.Empty;
            window.m_onConfirm = onConfirm;

            // Centred on the editor rather than at the mouse: this is a decision about the
            // project, not an inline edit of whatever the cursor happened to be over.
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                main.x + (main.width - Width) * 0.5f,
                main.y + (main.height - Height) * 0.5f,
                Width, Height);
            window.ShowModalUtility();
        }

        void OnGUI()
        {
            GUILayout.Space(8f);
            GUILayout.Label("Profile name", EditorStyles.boldLabel);

            GUI.SetNextControlName("QDProfileName");
            m_name = EditorGUILayout.TextField(m_name);
            if (!m_focusRequested)
            {
                // Focus and select on the first repaint so the suggested name can be
                // overwritten by typing immediately, without reaching for the mouse.
                EditorGUI.FocusTextInControl("QDProfileName");
                m_focusRequested = true;
            }

            GUILayout.Space(2f);
            EditorGUILayout.LabelField($"Saved in {m_folder}", EditorStyles.miniLabel);

            bool nameValid = !string.IsNullOrWhiteSpace(m_name);
            bool submitted = Event.current.type == EventType.KeyDown &&
                             (Event.current.keyCode == KeyCode.Return ||
                              Event.current.keyCode == KeyCode.KeypadEnter);
            bool cancelled = Event.current.type == EventType.KeyDown &&
                             Event.current.keyCode == KeyCode.Escape;

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(80f))) cancelled = true;
            using (new EditorGUI.DisabledScope(!nameValid))
            {
                if (GUILayout.Button("Create", GUILayout.Width(90f))) submitted = true;
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(6f);

            if (cancelled)
            {
                // Consume the pending request before closing. IMGUI can finish the current
                // event after a modal window is closed; leaving the callback alive made a
                // cancel vulnerable to being observed later as a confirmation.
                ConsumeRequest(ref m_onConfirm, m_name, false);
                Close();
                GUIUtility.ExitGUI();
            }
            if (!submitted || !nameValid) return;

            // Close before the callback: it creates an asset and repaints the owning
            // window, and doing that while this modal is still up leaves it orphaned.
            Action<string> confirm = m_onConfirm;
            m_onConfirm = null;
            Close();
            ConsumeRequest(ref confirm, m_name, true);
            GUIUtility.ExitGUI();
        }

        /// <summary>
        /// Completes a profile-name request at most once. Cancel always consumes the
        /// callback without invoking it, so no asset-creation callback can survive the
        /// modal window that owned it.
        /// </summary>
        internal static bool ConsumeRequest(ref Action<string> request,
            string profileName, bool create)
        {
            Action<string> pending = request;
            request = null;
            if (!create || pending == null || string.IsNullOrWhiteSpace(profileName))
                return false;

            pending(profileName);
            return true;
        }
    }

    internal enum HollowShellBakeChoice
    {
        Cancel,
        UseHollowShell,
        BakeFullSolidAnyway
    }

    /// <summary>
    /// Modal decision shown before a modular Whole Building bake that contains open wall
    /// or floor panels. It makes the automatic Hollow Shell conversion explicit and asks
    /// for the values the tool cannot infer reliably: authored wall thickness and whether
    /// that thickness should also be visible on the intact building.
    /// </summary>
    sealed class HollowShellThicknessPrompt : EditorWindow
    {
        internal enum ThicknessUnit
        {
            Centimeters,
            Meters
        }

        sealed class Result
        {
            public HollowShellBakeChoice Choice = HollowShellBakeChoice.Cancel;
            public float ThicknessMeters;
            public bool ShowShellWhileIntact;
        }

        const string UnitPreferenceKey =
            "RipVertices.QuickDestruction.HollowShellThicknessUnit";
        internal const float MinThicknessMeters = 0.001f;
        internal const float MaxThicknessMeters = 2f;
        const float Width = 470f;
        const float Height = 305f;

        Result m_result;
        string m_sourceName;
        float m_displayThickness;
        bool m_showShellWhileIntact;
        ThicknessUnit m_unit;
        bool m_allowFullSolidAnyway;
        bool m_bakeImmediately = true;
        bool m_focusRequested;

        internal static ThicknessUnit GetPreferredUnit()
        {
            return (ThicknessUnit)Mathf.Clamp(
                EditorPrefs.GetInt(UnitPreferenceKey,
                    (int)ThicknessUnit.Centimeters), 0, 1);
        }

        internal static void SetPreferredUnit(ThicknessUnit value)
        {
            EditorPrefs.SetInt(UnitPreferenceKey,
                Mathf.Clamp((int)value, 0, 1));
        }

        internal static bool IsValidThickness(float meters) =>
            !float.IsNaN(meters) && !float.IsInfinity(meters) &&
            meters >= MinThicknessMeters && meters <= MaxThicknessMeters;

        internal static HollowShellBakeChoice Show(string sourceName,
            float currentThicknessMeters, bool currentShowShellWhileIntact,
            bool allowFullSolidAnyway, out float thicknessMeters,
            out bool showShellWhileIntact, bool bakeImmediately = true)
        {
            var result = new Result
            {
                ThicknessMeters = Mathf.Clamp(currentThicknessMeters,
                    MinThicknessMeters, MaxThicknessMeters),
                ShowShellWhileIntact = currentShowShellWhileIntact
            };
            var window = CreateInstance<HollowShellThicknessPrompt>();
            window.titleContent = new GUIContent("Quick Destruction — Wall Thickness");
            window.m_result = result;
            window.m_sourceName = string.IsNullOrWhiteSpace(sourceName)
                ? "Whole Building"
                : sourceName;
            window.m_allowFullSolidAnyway = allowFullSolidAnyway;
            window.m_bakeImmediately = bakeImmediately;
            window.m_showShellWhileIntact = currentShowShellWhileIntact;
            window.m_unit = GetPreferredUnit();
            window.m_displayThickness = FromMeters(
                result.ThicknessMeters, window.m_unit);

            Rect main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(
                main.x + (main.width - Width) * 0.5f,
                main.y + (main.height - Height) * 0.5f,
                Width, Height);
            window.ShowModalUtility();

            thicknessMeters = result.ThicknessMeters;
            showShellWhileIntact = result.ShowShellWhileIntact;
            return result.Choice;
        }

        internal static float ToMeters(float value, ThicknessUnit unit)
        {
            return unit == ThicknessUnit.Centimeters ? value * 0.01f : value;
        }

        internal static float FromMeters(float meters, ThicknessUnit unit)
        {
            return unit == ThicknessUnit.Centimeters ? meters * 100f : meters;
        }

        void OnGUI()
        {
            GUILayout.Space(10f);
            GUILayout.Label("Open modular walls/floors detected", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                $"{m_sourceName} contains zero-thickness panel meshes. Quick Destruction " +
                "will build a HOLLOW SHELL only for those open panels and their intact " +
                "colliders. Closed modular walls that already have modelled thickness " +
                "keep their authored geometry and depth. The source children are not modified.",
                EditorStyles.wordWrappedLabel);
            GUILayout.Space(8f);

            float metersBeforeUnitChange = ToMeters(m_displayThickness, m_unit);
            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName("QDWallThickness");
            m_displayThickness = EditorGUILayout.FloatField(
                "Wall Thickness", m_displayThickness);
            ThicknessUnit newUnit = (ThicknessUnit)EditorGUILayout.EnumPopup(
                m_unit, GUILayout.Width(112f));
            EditorGUILayout.EndHorizontal();
            if (newUnit != m_unit)
            {
                m_unit = newUnit;
                m_displayThickness = FromMeters(metersBeforeUnitChange, m_unit);
                SetPreferredUnit(m_unit);
            }
            if (!m_focusRequested)
            {
                EditorGUI.FocusTextInControl("QDWallThickness");
                m_focusRequested = true;
            }

            float thicknessMeters = ToMeters(m_displayThickness, m_unit);
            bool valid = IsValidThickness(thicknessMeters);
            string converted = m_unit == ThicknessUnit.Centimeters
                ? $"Stored as {thicknessMeters:0.####} m (1 Unity unit = 1 m)."
                : $"Equivalent to {thicknessMeters * 100f:0.##} cm.";
            EditorGUILayout.HelpBox(valid
                    ? converted
                    : "Enter a thickness from 0.1 cm to 200 cm (0.001 m to 2 m).",
                valid ? MessageType.Info : MessageType.Error);

            GUILayout.Space(4f);
            m_showShellWhileIntact = EditorGUILayout.ToggleLeft(
                "Show Hollow Shell Thickness While Intact",
                m_showShellWhileIntact);
            EditorGUILayout.LabelField(
                m_showShellWhileIntact
                    ? "The intact renderer will use this thickness before the first hit. " +
                      "The authored source modules remain unchanged."
                    : "The intact renderer will keep the authored thin surfaces until the " +
                      "first break. Its colliders still receive this thickness.",
                EditorStyles.wordWrappedLabel);

            bool cancel = Event.current.type == EventType.KeyDown &&
                          Event.current.keyCode == KeyCode.Escape;
            bool submit = Event.current.type == EventType.KeyDown &&
                          (Event.current.keyCode == KeyCode.Return ||
                           Event.current.keyCode == KeyCode.KeypadEnter);

            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Cancel", GUILayout.Width(82f))) cancel = true;
            GUILayout.FlexibleSpace();
            if (m_allowFullSolidAnyway &&
                GUILayout.Button(m_bakeImmediately ? "Bake Full Solid Anyway" : "Keep Full Solid", GUILayout.Width(160f)))
            {
                Complete(HollowShellBakeChoice.BakeFullSolidAnyway,
                    thicknessMeters);
                GUIUtility.ExitGUI();
            }
            using (new EditorGUI.DisabledScope(!valid))
            {
                string button = !m_bakeImmediately ? "Use Hollow Shell" : m_allowFullSolidAnyway
                    ? "Use Hollow Shell & Bake"
                    : "OK — Bake With Hollow Shell";
                if (GUILayout.Button(button, GUILayout.Width(185f))) submit = true;
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8f);

            if (cancel)
            {
                Complete(HollowShellBakeChoice.Cancel, thicknessMeters);
                GUIUtility.ExitGUI();
            }
            if (submit && valid)
            {
                Complete(HollowShellBakeChoice.UseHollowShell, thicknessMeters);
                GUIUtility.ExitGUI();
            }
        }

        void Complete(HollowShellBakeChoice choice, float thicknessMeters)
        {
            if (m_result != null)
            {
                m_result.Choice = choice;
                m_result.ThicknessMeters = Mathf.Clamp(thicknessMeters,
                    MinThicknessMeters, MaxThicknessMeters);
                m_result.ShowShellWhileIntact = m_showShellWhileIntact;
            }
            Close();
        }
    }
}
