using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Rendering;
using KryticalNova.UI.Menus;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.EditorTools
{
    // Faction select's champions: the stage they stand on behind the canvas, and the weapon glow on the menus' renderer.
    public static partial class FrontEndBuilder
    {
        // Who stands for each faction: the lineage the faction is known by, in its top Epic set.
        private static readonly (FactionId faction, string lineage)[] Champions =
        {
            (FactionId.Hegemony, "Soldier"), (FactionId.Null, "Skeleton"), (FactionId.Covenant, "Viking"),
        };

        /// <summary>
        /// The stage of faction select, in the scene behind the canvas: where the champions stand, the wall behind
        /// them, the pedestal copied under each, and one soft light on them all (each has its own lamps besides).
        /// </summary>
        private static FactionShowcase AddFactionShowcase(Camera camera, CharacterCreationDatabase database)
        {
            var root = new GameObject("Faction Showcase");
            Parent(root, "--- Environment ---");
            var stage = new GameObject("Stage").transform;
            stage.SetParent(root.transform, false);
            stage.localPosition = new Vector3(0f, 0f, 4.5f); // nine metres from the camera: three champions side by side

            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "Pedestal";
            Object.DestroyImmediate(pedestal.GetComponent<Collider>());
            pedestal.transform.SetParent(root.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, -0.05f, 0f); // top face at the champion's feet
            pedestal.transform.localScale = new Vector3(1.5f, 0.05f, 1.5f);
            pedestal.GetComponent<Renderer>().sharedMaterial = pedestalMaterial;
            pedestal.SetActive(false); // the pattern: the showcase puts a copy under each champion

            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = "Backdrop";
            Object.DestroyImmediate(backdrop.GetComponent<Collider>());
            backdrop.transform.SetParent(root.transform, false);
            backdrop.transform.localPosition = new Vector3(0f, 2f, 10f);
            backdrop.transform.localScale = new Vector3(70f, 35f, 1f);
            var backdropRenderer = backdrop.GetComponent<Renderer>();
            backdropRenderer.sharedMaterial = backdropMaterial;
            backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backdropRenderer.receiveShadows = false;

            var lights = new GameObject("Showcase Lights");
            Parent(lights, "--- Lighting & FX ---");
            var key = AddLight(lights.transform, "Key", LightType.Directional, new Color(0.8f, 0.82f, 1f), 0.55f);
            key.transform.rotation = Quaternion.Euler(38f, -24f, 0f);
            key.shadows = LightShadows.Soft;

            var showcase = root.AddComponent<FactionShowcase>();
            SetRef(showcase, "database", database);
            SetRef(showcase, "modelPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath));
            SetRef(showcase, "gear", AssetDatabase.LoadAssetAtPath<KryticalNova.Items.GearDictionary>(SidekickGearGenerator.DictionaryPath));
            SetRef(showcase, "items", AssetDatabase.LoadAssetAtPath<KryticalNova.Items.ItemCatalog>(SidekickGearGenerator.CatalogPath));
            SetRef(showcase, "progression", AssetDatabase.LoadAssetAtPath<KryticalNova.Items.GearProgressionSettings>(PlayerSystemsSetup.GearProgressionPath));
            SetRef(showcase, "viewCamera", camera);
            SetRef(showcase, "stage", stage);
            SetRef(showcase, "pedestal", pedestal);
            SetRef(showcase, "backdrop", backdropRenderer);
            SetRef(showcase, "idleClip", PlayerIdleClip());
            SetRef(showcase, "drawnIdleClip", PlayerMotorClip("drawnIdleClip"));
            SetRef(showcase, "drawClip", PlayerMotorClip("equipClip"));

            var so = new SerializedObject(showcase);
            SerializedProperty list = so.FindProperty("champions");
            list.arraySize = Champions.Length;
            for (int i = 0; i < Champions.Length; i++)
            {
                SerializedProperty each = list.GetArrayElementAtIndex(i);
                each.FindPropertyRelative("faction").intValue = (int)Champions[i].faction;
                each.FindPropertyRelative("lineage").stringValue = Champions[i].lineage;
                each.FindPropertyRelative("body").intValue = (int)BodyType.Masculine;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return showcase;
        }

        /// <summary>A clip the player's own motor plays (the stance with a weapon drawn, the draw), so a champion moves as a player does.</summary>
        private static AnimationClip PlayerMotorClip(string field)
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var motor = player != null ? player.GetComponentInChildren<KryticalNova.Player.PlayerAnimancerMotor>(true) : null;
            SerializedProperty property = motor != null ? new SerializedObject(motor).FindProperty(field) : null;
            var clip = property != null ? property.objectReferenceValue as AnimationClip : null;
            if (clip == null) Debug.LogWarning($"[FrontEnd] The player's motor has no '{field}' clip: the champions stand without it.");
            return clip;
        }

        /// <summary>
        /// The weapon glow (GlowHalo) on the renderer the menus' cameras draw with, set as it is on the world's
        /// renderer. Without it a champion's tempered weapon would not glow. A renderer that has it is left alone.
        /// </summary>
        private static void EnsureMenuGlow()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)) return;
            ScriptableRendererData menu = null;
            GlowHaloFeature like = null;
            foreach (ScriptableRendererData data in urp.rendererDataList)
            {
                if (data == null) continue;
                if (data.name == MenuRendererName) menu = data;
                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                    if (feature is GlowHaloFeature halo && data.name != MenuRendererName) like = halo;
            }
            if (menu == null) return;
            foreach (ScriptableRendererFeature feature in menu.rendererFeatures)
                if (feature is GlowHaloFeature) return;

            var glow = ScriptableObject.CreateInstance<GlowHaloFeature>();
            if (like != null) EditorUtility.CopySerialized(like, glow);
            glow.name = "GlowHalo";
            AssetDatabase.AddObjectToAsset(glow, menu);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(glow, out _, out long id);
            var so = new SerializedObject(menu);
            SerializedProperty features = so.FindProperty("m_RendererFeatures"), map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = glow;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(menu);
            AssetDatabase.SaveAssetIfDirty(menu);
            Debug.Log($"[FrontEnd] Added the weapon glow to {MenuRendererName}.");
        }
    }
}
