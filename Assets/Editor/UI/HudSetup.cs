using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Wires the Synty-skinned HUD into the open scene: settings/layout controller and network panel on the
    /// Progression UI document, and the ground marker presenter with its shader and ring textures.
    /// Idempotent — safe to run again after pulling changes.
    /// </summary>
    public static class HudSetup
    {
        private const string HudSprites = "Assets/Synty/InterfaceSciFiSoldierHUD/Sprites/HUD/SPR_HUD_SciFiSoldier_";
        private const string MarkerShaderName = "KryticalNova/GroundMarker";
        private const string MarkersObjectName = "World Markers";

        // Looked up by name: the UI assembly is excluded from server builds, so it isn't referenced here.
        private const string HudType = "KryticalNova.UI.ProgressionHUD, KryticalNova.UI";
        private const string SettingsType = "KryticalNova.UI.HudSettingsController, KryticalNova.UI";
        private const string NetworkPanelType = "KryticalNova.UI.NetworkSessionPanel, KryticalNova.UI";
        private const string MarkersType = "KryticalNova.UI.WorldMarkerPresenter, KryticalNova.UI";
        private const string GearType = "KryticalNova.UI.GearUIController, KryticalNova.UI";
        private const string MinimapType = "KryticalNova.UI.MinimapController, KryticalNova.UI";

        [MenuItem("Krytical Nova/UI/Set Up HUD In Open Scene")]
        public static void SetUpOpenScene()
        {
            var hudType = System.Type.GetType(HudType);
            var settingsType = System.Type.GetType(SettingsType);
            var networkType = System.Type.GetType(NetworkPanelType);
            var markersType = System.Type.GetType(MarkersType);
            if (hudType == null || settingsType == null || networkType == null || markersType == null)
            {
                Debug.LogError("[HudSetup] UI types not found. Wait for scripts to compile and try again.");
                return;
            }

            var hud = Object.FindAnyObjectByType(hudType, FindObjectsInactive.Include) as Component;
            if (hud == null)
            {
                Debug.LogError("[HudSetup] No ProgressionHUD in the open scene.");
                return;
            }

            GameObject hudObject = hud.gameObject;
            EnsureComponent(hudObject, settingsType);
            EnsureComponent(hudObject, networkType);
            var gearType = System.Type.GetType(GearType);
            if (gearType != null) EnsureComponent(hudObject, gearType);
            var minimapType = System.Type.GetType(MinimapType);
            if (minimapType != null) EnsureComponent(hudObject, minimapType);

            var markers = Object.FindAnyObjectByType(markersType, FindObjectsInactive.Include) as Component;
            if (markers == null)
            {
                var go = new GameObject(MarkersObjectName);
                Undo.RegisterCreatedObjectUndo(go, "Create World Markers");
                go.layer = LayerMask.NameToLayer("Ignore Raycast");
                markers = Undo.AddComponent(go, markersType);
            }
            AssignMarkerAssets(markers);

            EditorSceneManager.MarkSceneDirty(hudObject.scene);
            Debug.Log($"[HudSetup] HUD wired in '{hudObject.scene.name}'. Save the scene to keep it.");
        }

        private static void EnsureComponent(GameObject go, System.Type type)
        {
            if (go.GetComponent(type) == null) Undo.AddComponent(go, type);
        }

        private static void AssignMarkerAssets(Component markers)
        {
            var so = new SerializedObject(markers);
            so.FindProperty("markerShader").objectReferenceValue = Shader.Find(MarkerShaderName);
            so.FindProperty("moveOuterRing").objectReferenceValue = Texture("Ring_Medium_05_Active");
            so.FindProperty("moveInnerRing").objectReferenceValue = Texture("Ring_Hexagon_Medium_01_Clean");
            so.FindProperty("targetRing").objectReferenceValue = Texture("Ring_SelectedEnemy_01_Clean");
            so.FindProperty("targetOuterRing").objectReferenceValue = Texture("Ring_Large_01");
            so.ApplyModifiedProperties();
        }

        private static Texture2D Texture(string spriteName)
        {
            string path = HudSprites + spriteName + ".png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) Debug.LogError($"[HudSetup] Missing Synty texture: {path}");
            return texture;
        }
    }
}
