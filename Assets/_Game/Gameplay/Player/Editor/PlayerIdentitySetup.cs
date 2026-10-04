using KryticalNova.CharacterCreation;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Player;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Wires character identity onto the player prefabs. Idempotent: safe to re-run after re-baking character data.
    ///  - Player_01 (model): CharacterLook next to CharacterAppearance, so a recipe can restyle the model.
    ///  - Player (network prefab): PlayerIdentity with the character database, and the gear dictionary on the inventory.
    /// </summary>
    public static class PlayerIdentitySetup
    {
        private const string ModelPrefabPath = "Assets/_Game/Characters/Player/Player_01/Player_01.prefab";
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";

        [MenuItem("Krytical Nova/Characters/4. Set Up Player Identity Components")]
        public static void Run()
        {
            var database = AssetDatabase.LoadAssetAtPath<CharacterCreationDatabase>(CharacterDataBaker.DatabasePath);
            var dictionary = AssetDatabase.LoadAssetAtPath<GearDictionary>(SidekickGearGenerator.DictionaryPath);
            if (database == null || dictionary == null)
            {
                Debug.LogError("[Characters] Character database or gear dictionary missing. Run 'Bake Everything' first.");
                return;
            }

            var model = PrefabUtility.LoadPrefabContents(ModelPrefabPath);
            try
            {
                var appearance = model.GetComponentInChildren<CharacterAppearance>(true);
                if (appearance == null)
                {
                    Debug.LogError($"[Characters] {ModelPrefabPath} has no CharacterAppearance. Run Krytical Nova/Items/3 first.");
                    return;
                }
                Ensure<CharacterLook>(appearance.gameObject);
                PrefabUtility.SaveAsPrefabAsset(model, ModelPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(model);
            }

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                SetField(Ensure<PlayerIdentity>(root), "database", database);
                var inventory = root.GetComponent<PlayerInventory>();
                if (inventory != null) SetField(inventory, "gearDictionary", dictionary);
                else Debug.LogWarning($"[Characters] {PlayerPrefabPath} has no PlayerInventory; gear rules are not wired.");
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Characters] Player identity components set up on Player_01 and Player.");
        }

        // Explicit Unity null check: in the Editor a missing GetComponent result is a "fake null", which ?? doesn't catch.
        private static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        private static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
