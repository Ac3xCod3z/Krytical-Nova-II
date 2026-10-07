using KryticalNova.DevTools;
using QFSW.QC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools.GM
{
    /// <summary>
    /// Builds the in-game GM console and puts it in the open zone scene. The console is the vendor's Quantum Console
    /// prefab, nested untouched inside our own prefab with four overrides: it starts closed, it draws above the
    /// HUD, it shows commands and their answers only (not the engine log), and its own Esc toggle is off (Esc belongs to the game's windows; <see cref="GmConsoleGate"/> opens it
    /// on the GM Console key instead). Safe to run again: what exists is kept.
    /// </summary>
    public static class GmConsoleSetup
    {
        public const string Folder = "Assets/_Game/Tools/Debug/Prefabs";
        public const string PrefabPath = Folder + "/GmConsole.prefab";
        private const string KeysPath = Folder + "/GmConsoleKeys.asset";
        private const string VendorConsole = "Assets/Plugins/QFSW/Quantum Console/Source/Prefabs/Quantum Console (SRP).prefab";
        private const string ObjectName = "GmConsole";
        private const int SortingOrder = 1000;

        [MenuItem("Krytical Nova/Setup/GM Console (Open Scene)", priority = 13)]
        public static void Run()
        {
            GameObject prefab = EnsurePrefab();
            if (prefab == null) return;
            if (EnsureInScene(prefab)) Debug.Log($"[Setup] GM console added to '{EditorSceneManager.GetActiveScene().name}'. Save the scene.");
            else Debug.Log("[Setup] The GM console is already in this scene.");
        }

        public static GameObject EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null && existing.GetComponent<GmConsoleGate>() != null && existing.GetComponentInChildren<QuantumConsole>(true) != null)
                return existing;

            var vendor = AssetDatabase.LoadAssetAtPath<GameObject>(VendorConsole);
            if (vendor == null)
            {
                Debug.LogError($"[Setup] Quantum Console prefab not found at {VendorConsole}.");
                return null;
            }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Game/Tools/Debug", "Prefabs");

            var root = new GameObject(ObjectName);
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(vendor, root.transform);
                var console = instance.GetComponentInChildren<QuantumConsole>(true);
                if (console == null)
                {
                    Debug.LogError($"[Setup] {VendorConsole} has no QuantumConsole component.");
                    return null;
                }

                var serialized = new SerializedObject(console);
                serialized.FindProperty("_keyConfig").objectReferenceValue = EnsureKeys();
                serialized.FindProperty("_activateOnStartup").boolValue = false;
                serialized.FindProperty("_interceptDebugLogger").boolValue = false; // commands and their answers only
                serialized.ApplyModifiedPropertiesWithoutUndo();

                foreach (Canvas canvas in instance.GetComponentsInChildren<Canvas>(true))
                {
                    if (!canvas.isRootCanvas) continue;
                    canvas.sortingOrder = SortingOrder;
                    EditorUtility.SetDirty(canvas);
                }

                var gate = new SerializedObject(root.AddComponent<GmConsoleGate>());
                gate.FindProperty("console").objectReferenceValue = console;
                gate.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>The console's keys, with its own show / hide key removed.</summary>
        private static QuantumKeyConfig EnsureKeys()
        {
            var keys = AssetDatabase.LoadAssetAtPath<QuantumKeyConfig>(KeysPath);
            if (keys != null) return keys;
            keys = ScriptableObject.CreateInstance<QuantumKeyConfig>();
            keys.ToggleConsoleVisibilityKey = KeyCode.None;
            AssetDatabase.CreateAsset(keys, KeysPath);
            return keys;
        }

        /// <summary>True when the console was added; false when the scene already had one.</summary>
        public static bool EnsureInScene(GameObject prefab)
        {
            if (Object.FindObjectsByType<GmConsoleGate>(FindObjectsInactive.Include).Length > 0) return false;
            var scene = EditorSceneManager.GetActiveScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = ObjectName;
            EditorSceneManager.MarkSceneDirty(scene);
            return true;
        }
    }
}
