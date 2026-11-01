using System.Collections.Generic;
using KryticalNova.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// The planters at the Zone01 spawn are vaultable by NGO's own rules (owner, 31 Oct: "we built the vaulting for the original game
    /// right where he spawns"). Opens the real scene and asks <see cref="PlayerVaultMotor.PlanForGhost"/> (the NGO motor's TryPlan) about a walk
    /// from the spawn straight across each of the nearest hedge planters; logs NGO's own reason for each. Explicit: it loads the town.
    /// </summary>
    public class VaultSpawnPlanterTests
    {
        const string ScenePath = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";
        const string PlayerPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";

        [Test, Explicit("loads Zone01_StarterTown")]
        public void TheSpawnPlanters_AreVaultable_ByTheNgoRules()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            Assert.IsNotNull(prefab, PlayerPath);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                var motor = body.GetComponent<PlayerVaultMotor>();
                var click = body.GetComponent<PointAndClickPlayer>();
                Assert.IsNotNull(motor, "the player has a vault motor");
                int layers = click != null ? click.RaycastLayers.value : Physics.DefaultRaycastLayers;
                Assert.IsTrue(NavMesh.SamplePosition(new Vector3(1f, 0f, -10f), out NavMeshHit spawn, 3f, NavMesh.AllAreas), "the spawn is on the NavMesh");
                Vector3 feet = spawn.position;
                body.transform.position = feet + Vector3.up * 50f;   // out of the way of every probe

                var hedges = new List<Transform>();
                foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t.name.StartsWith("SM_Env_Hedge", System.StringComparison.Ordinal)) hedges.Add(t);
                hedges.Sort((a, b) => Vector3.Distance(a.position, feet).CompareTo(Vector3.Distance(b.position, feet)));
                Assert.Greater(hedges.Count, 0, "the town has hedge planters");

                int vaults = 0;
                for (int i = 0; i < hedges.Count && i < 4; i++)
                {
                    Transform h = hedges[i];
                    Vector3 middle = h.GetComponentInChildren<Renderer>() is Renderer r ? r.bounds.center : h.position;
                    var colliders = h.GetComponentsInChildren<Collider>();
                    var parentCollider = h.parent != null ? h.parent.GetComponentInChildren<Collider>() : null;
                    Debug.Log($"[KnVault] planter {i + 1} '{h.name}' at {middle.x:F1},{middle.z:F1} ({Vector3.Distance(middle, feet):F1} m): layer {h.gameObject.layer}, " +
                              $"{colliders.Length} collider(s) on it, parent '{(h.parent != null ? h.parent.name : "-")}' collider {(parentCollider != null ? parentCollider.GetType().Name + " layer " + parentCollider.gameObject.layer : "none")}");
                    var bounds = h.GetComponentInChildren<Renderer>() is Renderer rb ? rb.bounds : new Bounds(middle, Vector3.one);
                    var there = Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
                    var names = new List<string>();
                    foreach (var c in there) names.Add($"{c.name} ({c.GetType().Name}, layer {c.gameObject.layer}{(c.isTrigger ? ", trigger" : "")}, in raycast layers {(((1 << c.gameObject.layer) & layers) != 0 ? "yes" : "NO")})");
                    Debug.Log($"[KnVault]   colliders in the planter's box {bounds.size.x:F1}x{bounds.size.y:F1}x{bounds.size.z:F1} m: " +
                              (names.Count == 0 ? "NONE" : string.Join("; ", names)));
                    Vector3 way = middle - feet;
                    way.y = 0f;
                    way.Normalize();
                    foreach (float past in new[] { 4f, 6f })
                    {
                        if (!NavMesh.SamplePosition(middle + way * past, out NavMeshHit goal, 1f, NavMesh.AllAreas)) continue;
                        string why = motor.PlanForGhost(feet, goal.position, float.PositiveInfinity, layers, out PlayerVaultMotor.GhostVault v);
                        if (why == null) vaults++;
                        Debug.Log($"[KnVault]   walk {past} m past it: {(why == null ? $"VAULT (top {v.Top:F2} m, take-off {v.Takeoff.x:F1},{v.Takeoff.z:F1}, landing {v.Landing.x:F1},{v.Landing.z:F1})" : "no vault: \"" + why + "\"")}");
                    }
                }
                Assert.Greater(vaults, 0, "NGO's rules vault at least one spawn planter (see the [KnVault] lines for why not)");
            }
            finally { Object.DestroyImmediate(body); }
        }
    }
}
