using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Every settings or tuning asset a player system takes must be wired on the player prefab (architecture stage 7).
    /// Many systems fall back to a copy of the asset's numbers kept in code when it is missing, so a wiring mistake
    /// would otherwise go unseen and the two copies drift apart; this makes it fail here instead.
    /// </summary>
    public sealed class PlayerSettingsWiringTests
    {
        private const string PlayerPrefab = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";

        [Test]
        public void EverySettingsFieldOnThePlayerIsWired()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefab);
            Assert.IsNotNull(prefab, PlayerPrefab + " is missing");
            var missing = new List<string>();
            foreach (MonoBehaviour behaviour in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                for (System.Type type = behaviour.GetType(); type != null && type != typeof(MonoBehaviour); type = type.BaseType)
                {
                    foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (!typeof(ScriptableObject).IsAssignableFrom(field.FieldType) && field.FieldType != typeof(ScriptableObject)) continue;
                        if (!field.IsPublic && field.GetCustomAttribute<SerializeField>() == null) continue;
                        string name = field.Name.ToLowerInvariant();
                        bool settingsAsset = field.FieldType.Name.EndsWith("Settings") || field.FieldType.Name.EndsWith("TuningData");
                        if (!settingsAsset && !name.Contains("settings") && !name.Contains("tuning")) continue;
                        if (field.GetValue(behaviour) as Object == null)
                            missing.Add($"{behaviour.GetType().Name}.{field.Name} on {behaviour.gameObject.name}");
                    }
                }
            }
            Assert.IsEmpty(missing, "Settings not wired on the player prefab (run Krytical Nova ▸ Setup ▸ Player Prefab):\n" + string.Join("\n", missing));
        }
    }
}
