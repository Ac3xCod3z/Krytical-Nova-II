using System.Text;
using KryticalNova.Player;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// A probe, not a rule (Explicit: run by name only). The wall walk's prop check did not see the air-conditioner box on Zone01's test
    /// wall (GUI run gui-wall-4, 1 Nov): this opens the zone and lists what is at that spot, and asks the check with and without the
    /// face's own collider left out.
    /// </summary>
    public sealed class Zone01WallPropProbe
    {
        const string Scene = "Assets/_Game/Scenes/World/Towns/Zone01_StarterTown.unity";
        // the node "along" ended at in gui-wall-2..4 (feet on the face), the face's normal +z; and the clean "onto" node
        static readonly Vector3 AcFeet = new Vector3(-13.2f, 4.35f, -45.3f), OntoFeet = new Vector3(-10.1f, 5.18f, -45.3f), Normal = Vector3.forward;

        [Test, Explicit]
        public void ListWhatIsAtTheAirConditioner()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var report = new StringBuilder();
            foreach (var (name, feet) in new[] { ("A/C", AcFeet), ("onto", OntoFeet) })
            {
                report.AppendLine($"[probe] {name} feet {feet}");
                foreach (Collider c in Physics.OverlapSphere(feet + Normal * 0.6f, 0.8f, ~0, QueryTriggerInteraction.Collide))
                    report.AppendLine($"[probe]   {Path(c.transform)} {c.GetType().Name} layer {LayerMask.LayerToName(c.gameObject.layer)} trigger {c.isTrigger} " +
                        $"enabled {c.enabled} bounds {c.bounds.center} size {c.bounds.size}");
                Collider face = Physics.Raycast(feet + Normal * 0.4f, -Normal, out RaycastHit hit, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.collider : null;
                report.AppendLine($"[probe]   face along the normal: {(face != null ? Path(face.transform) + " at " + hit.point : "none")}");
                report.AppendLine($"[probe]   check, face left out: {WallWalkLayout.PropOnFace(feet, Normal, face, null, Physics.DefaultRaycastLayers)}; " +
                    $"face counted: {WallWalkLayout.PropOnFace(feet, Normal, null, null, Physics.DefaultRaycastLayers)}");
                foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    if (r.bounds.Intersects(new Bounds(feet + Normal * 0.6f, Vector3.one * 1.2f)))
                        report.AppendLine($"[probe]   renderer {Path(r.transform)} bounds {r.bounds.center} size {r.bounds.size} collider on it: {r.GetComponent<Collider>() != null}");
            }
            Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        // gui-climb-1's test face (the owner saw a tree), facing -z
        [Test, Explicit]
        public void ListWhatIsAtTheClimbFace()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var report = new StringBuilder();
            Vector3 face = new Vector3(12.6f, 1.2f, -13.1f);
            foreach (Collider c in Physics.OverlapSphere(face, 1.5f, ~0, QueryTriggerInteraction.Collide))
                report.AppendLine($"[probe] collider {Path(c.transform)} {c.GetType().Name} layer {LayerMask.LayerToName(c.gameObject.layer)} " +
                    $"bounds {c.bounds.center} size {c.bounds.size} marker {(c.GetComponentInParent<ClimbableSurface>() is ClimbableSurface m ? m.kind.ToString() : "none")}");
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.bounds.size.magnitude < 40f && r.bounds.Intersects(new Bounds(face, Vector3.one * 3f)))
                    report.AppendLine($"[probe] renderer {Path(r.transform)} bounds {r.bounds.center} size {r.bounds.size}");
            foreach (Terrain t in Terrain.activeTerrains)
                report.AppendLine($"[probe] terrain {t.name}: {t.terrainData.treeInstanceCount} trees");
            Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        // gui-pop-1 (1 Nov): "proxies live 0" under the entity netcode although the terrain has 1600 trees. Which trees TreeProxyField keeps
        // (a prototype with a capsule, at least 3 m tall) and how far the nearest are from the tree runs' start and the origin.
        [Test, Explicit]
        public void ListTheTerrainTreesTheProxiesWouldKeep()
        {
            EditorSceneManager.OpenScene(Scene, OpenSceneMode.Single);
            var report = new StringBuilder();
            var starts = new[] { ("tree runs' start", new Vector3(-6.8f, 0f, -7.5f)), ("origin", Vector3.zero) };
            foreach (Terrain t in Terrain.activeTerrains)
            {
                TerrainData data = t.terrainData;
                report.AppendLine($"[probe] terrain {t.name} scene {t.gameObject.scene.name} at {t.GetPosition()} size {data.size}: {data.treeInstanceCount} trees, " +
                    $"{data.treePrototypes.Length} prototypes; field on it in the saved scene: {t.GetComponent<TreeProxyField>() != null}");
                var ok = new bool[data.treePrototypes.Length];
                var height = new float[data.treePrototypes.Length];
                for (int i = 0; i < data.treePrototypes.Length; i++)
                {
                    GameObject prefab = data.treePrototypes[i].prefab;
                    CapsuleCollider capsule = prefab != null ? prefab.GetComponentInChildren<CapsuleCollider>(true) : null;
                    ok[i] = capsule != null;
                    height[i] = capsule != null ? capsule.height * capsule.transform.lossyScale.y : 0f;
                    report.AppendLine($"[probe]   prototype {i} {(prefab != null ? prefab.name : "none")}: capsule {(capsule != null ? $"height {height[i]:F1} m" : "NONE")}");
                }
                int kept = 0;
                var near = new System.Collections.Generic.List<(float d, Vector3 foot, string start)>();
                foreach (TreeInstance tree in data.treeInstances)
                {
                    int p = tree.prototypeIndex;
                    if (p < 0 || p >= ok.Length || !ok[p] || height[p] * tree.heightScale < 3f) continue;
                    kept++;
                    Vector3 foot = t.GetPosition() + Vector3.Scale(tree.position, data.size);
                    foreach (var (name, at) in starts)
                        near.Add((Vector2.Distance(new Vector2(foot.x, foot.z), new Vector2(at.x, at.z)), foot, name));
                }
                report.AppendLine($"[probe]   kept as climbable: {kept}");
                near.Sort((a, b) => a.d.CompareTo(b.d));
                foreach (var (name, _) in starts)
                {
                    int shown = 0;
                    foreach (var n in near)
                        if (n.start == name && shown++ < 3) report.AppendLine($"[probe]   nearest to the {name}: {n.d:F1} m, foot {n.foot}");
                }
            }
            Debug.Log(report.ToString());
            Assert.Pass(report.ToString());
        }

        static string Path(Transform t)
        {
            string p = t.name;
            for (Transform u = t.parent; u != null; u = u.parent) p = u.name + "/" + p;
            return p;
        }
    }
}
