using KryticalNova.EditorTools.Organization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The two wiring steps of the project, each in one place. Every system that needs a component on the player
    /// prefab, or an object in a zone scene, adds its step here instead of getting a menu item of its own.
    /// Both are idempotent: run them again after pulling changes, after a new system lands, or on a new zone scene.
    /// </summary>
    public static class ProjectSetup
    {
        private const string ZoneSceneTitle = "Set Up Zone Scene";

        /// <summary>Everything the networked Player prefab (and its model prefab) needs, in dependency order.</summary>
        [MenuItem("Krytical Nova/Setup/Player Prefab", priority = 10)]
        public static void PlayerPrefab()
        {
            PlayerAppearanceSetup.Run();   // gear: equipment, Pack, visuals, catalog
            PlayerIdentitySetup.Run();     // identity, look, gear dictionary
            HitReactionSetup.Assign();     // hit reaction clips
            PlayerSystemsSetup.Run();      // saving, pets, Creds, storage, trade, Kiosks, Contacts, Crew
            AssetDatabase.SaveAssets();
            Debug.Log("[Setup] Player prefab wired.");
        }

        [MenuItem("Krytical Nova/Setup/Zone Scene (Open Scene)", priority = 11)]
        private static void ZoneSceneMenu()
        {
            string scene = EditorSceneManager.GetActiveScene().name;
            if (!EditorUtility.DisplayDialog(ZoneSceneTitle,
                    $"Wire '{scene}' as a zone: HUD, zone UI, game feel, matte terrain, post look, atmosphere, minimap " +
                    "renderer, baked GI settings and the hierarchy headers.\n\nThe post look, atmosphere and lighting " +
                    "settings are written from code, so values tuned by hand in this scene are replaced.", "Set Up", "Cancel"))
                return;
            ZoneScene();
        }

        /// <summary>
        /// Prepares the open scene as a playable zone. No dialog, so builders and automation can call it. Terrain,
        /// town and NavMesh are the zone builder's job; this is everything a zone needs whatever it looks like.
        /// </summary>
        public static void ZoneScene()
        {
            EquipmentPreviewSetup.Run();                 // preview layer + renderer, then the HUD controllers
            Zone01Setup.EnsureInteractableLayer();
            GameplayPrefabsSetup.EnsureRegistered();     // clients must know every prefab the server spawns
            if (GameplayPrefabsSetup.EnsureSceneUsesPrefabList()) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Zone01Setup.EnsureZoneUi();                  // NPC nameplates, dialogue, toasts
            Zone01Setup.AddInteractableToPlayerRaycasts();
            Zone01Setup.OpenReadyStalls();               // stalls whose NPC arrived after the town was built
            Zone01Setup.EnsureTradeZone();               // where Kiosks may open (the starter town's plaza)
            GameFeelBuilder.WireScene();
            AudioSetup.RouteScene();                     // the mixer the Settings sliders drive, and who plays into it
            TerrainMatteFixer.Run();
            PostProcessSetup.Apply();
            AtmosphereSetup.Setup();
            MinimapRendererSetup.Setup();
            LightingSetup.Setup();
            SceneHierarchyOrganizer.OrganizeActiveScene();
            Debug.Log($"[Setup] Zone scene '{EditorSceneManager.GetActiveScene().name}' wired. Save the scene to keep it.");
        }
    }
}
