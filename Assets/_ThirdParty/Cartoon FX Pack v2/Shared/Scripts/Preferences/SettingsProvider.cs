using UnityEngine;
using PSD.Preferences.Theming;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Preferences
{
#if UNITY_EDITOR
    public static class SettingsProvider
    {
        private const string ThemeGuidKey = "CTFX2_ThemeObjectGuid";
        private const string DefaultThemePath = "PSD/Default Theme";

        private static ThemeObject cachedTheme;
        private static bool loaded = false;

        public static ThemeObject SelectedTheme
        {
            get
            {
                if (!loaded) Load();
                return cachedTheme;
            }
            set
            {
                cachedTheme = value != null ? value : LoadDefaultTheme();
                Save();
            }
        }

        private static ThemeObject LoadDefaultTheme()
        {
            ThemeObject defaultTheme = Resources.Load<ThemeObject>(DefaultThemePath);

            if (defaultTheme == null)
                Debug.LogWarning($"[CTFX2] Default theme not found at Resources/{DefaultThemePath}");
            return defaultTheme;
        }

        private static void Load()
        {
            loaded = true;

            string guid = EditorPrefs.GetString(ThemeGuidKey, "");

            if (!string.IsNullOrEmpty(guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    cachedTheme = AssetDatabase.LoadAssetAtPath<ThemeObject>(path);
                    if (cachedTheme != null) return;
                }
            }

            cachedTheme = LoadDefaultTheme();
        }

        private static void Save()
        {
            if (cachedTheme == null)
            {
                EditorPrefs.DeleteKey(ThemeGuidKey);
                return;
            }

            string path = AssetDatabase.GetAssetPath(cachedTheme);
            string guid = AssetDatabase.AssetPathToGUID(path);
            EditorPrefs.SetString(ThemeGuidKey, guid);
        }

        [SettingsProvider]
        public static UnityEditor.SettingsProvider CreateSettingsProvider()
        {
            var provider = new UnityEditor.SettingsProvider("Preferences/CTFX2", SettingsScope.User)
            {
                label = "CTFX2",

                guiHandler = (searchContext) =>
                {
                    EditorGUILayout.Space(8);
                    EditorGUILayout.LabelField("CTFX2", EditorStyles.boldLabel);
                    EditorGUILayout.Space(4);

                    EditorGUI.BeginChangeCheck();

                    ThemeObject newTheme = (ThemeObject)EditorGUILayout.ObjectField(
                        "Theme",
                        SelectedTheme,
                        typeof(ThemeObject),
                        allowSceneObjects: false
                    );

                    if (EditorGUI.EndChangeCheck())
                        SelectedTheme = newTheme;

                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(
                        "Select a ThemeObject asset to customise the look of CTFX2 tools.",
                        MessageType.Info
                    );

                    EditorGUILayout.Space(8);
                    if (GUILayout.Button("Reset to Defaults", GUILayout.Width(150)))
                        SelectedTheme = null;
                },

                keywords = new System.Collections.Generic.HashSet<string>
                { "CTFX2", "Cartoon", "FX", "Theme", "Pack" }
            };

            return provider;
        }
    }
#endif
}