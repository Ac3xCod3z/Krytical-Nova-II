using KryticalNova.UI.Menus;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Editor switch for where Play starts. On: Play always begins at the Title screen, like a build, whatever scene
    /// is open. Off (the default): Play runs the open scene, so pressing Play in the zone still drops straight into
    /// it with the last-played character.
    /// </summary>
    [InitializeOnLoad]
    public static class FrontEndPlayMode
    {
        private const string MenuPath = "Krytical Nova/Front End/Play From Title Screen";
        // Editor preferences are shared by every project on the machine; keep this one per project.
        private static readonly string PrefKey = "KryticalNova.FrontEnd.PlayFromTitle." + Application.dataPath.GetHashCode();

        static FrontEndPlayMode() => EditorApplication.delayCall += Apply;

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefKey, false);
            set
            {
                EditorPrefs.SetBool(PrefKey, value);
                Apply();
            }
        }

        [MenuItem(MenuPath, priority = 40)]
        private static void Toggle() => Enabled = !Enabled;

        [MenuItem(MenuPath, true)]
        private static bool ValidateToggle()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void Apply()
        {
            EditorSceneManager.playModeStartScene = Enabled
                ? AssetDatabase.LoadAssetAtPath<SceneAsset>(FrontEndBuilder.ScenePath(FrontEnd.TitleScene))
                : null;
        }
    }
}
