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

        static string Path(Transform t)
        {
            string p = t.name;
            for (Transform u = t.parent; u != null; u = u.parent) p = u.name + "/" + p;
            return p;
        }
    }
}
