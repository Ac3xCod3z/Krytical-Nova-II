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
        private const string DarkFantasySprites = "Assets/Synty/InterfaceDarkFantasyHUD/Sprites/HUD/";
        private const string FantasyWarriorSprites = "Assets/Synty/InterfaceFantasyWarriorHUD/Sprites/HUD/";
        private const string MarkerShaderName = "KryticalNova/GroundMarker";
        private const string MarkersObjectName = "World Markers";

        // Looked up by name: the UI assembly is excluded from server builds, so it isn't referenced here.
        private const string HudType = "KryticalNova.UI.ProgressionHUD, KryticalNova.UI";
        private const string SettingsType = "KryticalNova.UI.HudSettingsController, KryticalNova.UI";
        private const string NetworkPanelType = "KryticalNova.UI.NetworkSessionPanel, KryticalNova.UI";
        private const string MarkersType = "KryticalNova.UI.WorldMarkerPresenter, KryticalNova.UI";
        private const string GearType = "KryticalNova.UI.GearUIController, KryticalNova.UI";
        private const string MinimapType = "KryticalNova.UI.MinimapController, KryticalNova.UI";
        private const string WorldMapType = "KryticalNova.UI.WorldMapController, KryticalNova.UI";
        private const string ChatType = "KryticalNova.UI.ChatWindowController, KryticalNova.UI";
        private const string DuelType = "KryticalNova.UI.DuelHudController, KryticalNova.UI";
        private const string PartyType = "KryticalNova.UI.PartyHudController, KryticalNova.UI";
        // Controllers added by later systems: each is ensured on the HUD object when its type exists.
        private static readonly string[] MoreControllers =
        {
            "KryticalNova.UI.PetHudController, KryticalNova.UI",
            "KryticalNova.UI.AmountDialogController, KryticalNova.UI",
            "KryticalNova.UI.RequestPromptController, KryticalNova.UI",
            "KryticalNova.UI.PlayerMenuController, KryticalNova.UI",
            "KryticalNova.UI.LootHudController, KryticalNova.UI",
            "KryticalNova.UI.StorageWindowController, KryticalNova.UI",
            "KryticalNova.UI.TradeWindowController, KryticalNova.UI",
            "KryticalNova.UI.KioskWindowController, KryticalNova.UI",
            "KryticalNova.UI.ContactsWindowController, KryticalNova.UI",
            "KryticalNova.UI.CrewWindowController, KryticalNova.UI",
            "KryticalNova.UI.GearWorkWindowController, KryticalNova.UI",
            "KryticalNova.UI.HeraldWindowController, KryticalNova.UI",
            "KryticalNova.UI.PlayerNamecardController, KryticalNova.UI",
            "KryticalNova.UI.EmoteWheelController, KryticalNova.UI",
        };

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
            AssignMixer(hudObject.GetComponent(settingsType));
            EnsureComponent(hudObject, networkType);
            var gearType = System.Type.GetType(GearType);
            if (gearType != null) EnsureComponent(hudObject, gearType);
            var minimapType = System.Type.GetType(MinimapType);
            if (minimapType != null) EnsureComponent(hudObject, minimapType);
            var worldMapType = System.Type.GetType(WorldMapType);
            if (worldMapType != null) EnsureComponent(hudObject, worldMapType);
            var chatType = System.Type.GetType(ChatType);
            if (chatType != null) EnsureComponent(hudObject, chatType);
            var duelType = System.Type.GetType(DuelType);
            if (duelType != null) EnsureComponent(hudObject, duelType);
            var partyType = System.Type.GetType(PartyType);
            if (partyType != null) EnsureComponent(hudObject, partyType);
            foreach (string typeName in MoreControllers)
            {
                var type = System.Type.GetType(typeName);
                if (type != null) EnsureComponent(hudObject, type);
                else Debug.LogWarning($"[HudSetup] HUD controller {typeName} not found; skipped.");
            }

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

        /// <summary>Hands the Settings controller the game's mixer, so its Audio page has something to drive.</summary>
        private static void AssignMixer(Component settings)
        {
            var mixer = AudioSetup.EnsureMixer();
            if (settings == null || mixer == null) return;
            var so = new SerializedObject(settings);
            SerializedProperty property = so.FindProperty("mixer");
            if (property == null || property.objectReferenceValue == mixer) return;
            property.objectReferenceValue = mixer;
            so.ApplyModifiedProperties();
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

            // Each faction's markers come from the HUD pack its interface is skinned with.
            AssignFactionRings(so.FindProperty("nullRings"), DarkFantasySprites,
                moveOuter: "SPR_FX_DarkFantasy_Sigil_Ring_Medium_01", moveInner: "SPR_FX_DarkFantasy_Sigil_Ring_Small_01",
                target: "SPR_HUD_DarkFantasy_Ring_SelectedEnemy_01_Clean", targetOuter: "SPR_FX_DarkFantasy_Sigil_Ring_Large_05",
                moveColor: new Color(0.8f, 0.5f, 1.7f, 1f));
            AssignFactionRings(so.FindProperty("covenantRings"), FantasyWarriorSprites,
                moveOuter: "SPR_HUD_FantasyWarrior_LoadingRing_01_Clean", moveInner: "SPR_HUD_FantasyWarrior_Objective_01_Ring_Clean",
                target: "SPR_HUD_FantasyWarrior_Ring_SelectedEnemy_01_Clean", targetOuter: "SPR_HUD_FantasyWarrior_Tracery_Circle_08",
                moveColor: new Color(0.45f, 1.5f, 0.65f, 1f));
            so.ApplyModifiedProperties();
        }

        private static void AssignFactionRings(SerializedProperty rings, string folder, string moveOuter, string moveInner,
            string target, string targetOuter, Color moveColor)
        {
            rings.FindPropertyRelative("moveOuterRing").objectReferenceValue = Load(folder + moveOuter + ".png");
            rings.FindPropertyRelative("moveInnerRing").objectReferenceValue = Load(folder + moveInner + ".png");
            rings.FindPropertyRelative("targetRing").objectReferenceValue = Load(folder + target + ".png");
            rings.FindPropertyRelative("targetOuterRing").objectReferenceValue = Load(folder + targetOuter + ".png");
            rings.FindPropertyRelative("moveColor").colorValue = moveColor;
        }

        private static Texture2D Texture(string spriteName) => Load(HudSprites + spriteName + ".png");

        private static Texture2D Load(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) Debug.LogError($"[HudSetup] Missing Synty texture: {path}");
            return texture;
        }
    }
}
