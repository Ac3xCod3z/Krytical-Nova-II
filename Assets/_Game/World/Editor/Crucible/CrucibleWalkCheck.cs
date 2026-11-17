using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.EditorTools.Crucible
{
    /// <summary>
    /// The capsule walk check of the owner's list (section 3b, "done when"): from each Rally Point landing to the heart on the NavMesh
    /// the walk plans (Fable's three 129.5 m routes), and up every stair flight, a 0.3 m x 1.8 m capsule is moved every 0.25 m and must
    /// meet no wall (wall blocks), no step over 0.45 m (too-high steps) and always ground within 0.6 m below (no-ground steps).
    /// Writes Library/KryticalNova.CrucibleWalk.txt; batch exits 1 on any failure.
    /// </summary>
    public static class CrucibleWalkCheck
    {
        const float Step = 0.25f, Radius = 0.3f, Height = 1.8f, MaxStep = 0.45f, MaxDrop = 0.6f;

        public static void RunBatch() { string r = Run(); Debug.Log(r); EditorApplication.Exit(r.Contains("FAIL") ? 1 : 0); }

        [MenuItem("Krytical Nova/World/Crucible/Walk Check")]
        public static void RunMenu() => Debug.Log(Run());

        public static string Run()
        {
            var scene = EditorSceneManager.OpenScene(CrucibleBuilder.Zone01Path, OpenSceneMode.Single);
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == CrucibleBuilder.RootName);
            if (root == null) return "FAIL: no Crucible in Zone01";
            Physics.SyncTransforms();
            var log = new StringBuilder("[Crucible] walk check\n");
            int blocker = LayerMask.NameToLayer(CrucibleBuilder.BlockerLayer);
            int solid = Physics.DefaultRaycastLayers & ~(blocker >= 0 ? 1 << blocker : 0);
            int failures = 0;
            Transform cf = root.transform.Find("Crossfire");
            Vector3 heart = root.transform.TransformPoint(new Vector3(0f, -6.3f, 0f));
            foreach (string faction in new[] { "Covenant", "Hegemony", "Null" })
            {
                Transform landing = cf.Find("MARK_Landing_" + faction);
                if (landing == null) { log.AppendLine("FAIL: no landing " + faction); failures++; continue; }
                failures += Route($"{faction} landing -> heart", landing.position, heart, solid, log);
            }
            CrucibleData data = CrucibleData.Load();
            foreach (JObject f in data.Flights)
            {
                Vector3 a = root.transform.TransformPoint(CrucibleData.V(f["start_unity"])), b = root.transform.TransformPoint(CrucibleData.V(f["end_unity"]));
                Vector3 dir = (b - a); dir.y = 0f; dir.Normalize();
                failures += Route("stair " + (string)f["name"], a - dir * 1.5f, b + dir * 1.5f, solid, log);
            }
            log.AppendLine(failures == 0 ? "Walk check: PASS" : $"Walk check: FAIL ({failures} routes)");
            File.WriteAllText("Library/KryticalNova.CrucibleWalk.txt", log.ToString());
            return log.ToString();
        }

        static int Route(string name, Vector3 from, Vector3 to, int solid, StringBuilder log)
        {
            var path = new NavMeshPath();
            if (!NavMesh.SamplePosition(from, out NavMeshHit a, 3f, NavMesh.AllAreas) || !NavMesh.SamplePosition(to, out NavMeshHit b, 3f, NavMesh.AllAreas)
                || !NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                log.AppendLine($"  FAIL {name}: no complete NavMesh path ({path.status})");
                return 1;
            }
            int walls = 0, high = 0, noGround = 0;
            float length = 0f;
            Vector3 prev = path.corners[0];
            float prevY = Ground(prev, solid, out _);
            for (int c = 1; c < path.corners.Length; c++)
            {
                Vector3 p0 = path.corners[c - 1], p1 = path.corners[c];
                float seg = Vector3.Distance(p0, p1);
                length += seg;
                for (float s = Step; s <= seg; s += Step)
                {
                    Vector3 p = Vector3.Lerp(p0, p1, s / seg);
                    float y = Ground(p, solid, out bool hit);
                    if (!hit) { noGround++; continue; }
                    if (Mathf.Abs(y - prevY) > MaxStep) high++;
                    Vector3 foot = new Vector3(p.x, y + Radius + 0.35f, p.z), head = new Vector3(p.x, y + Height - Radius, p.z);
                    if (Physics.CheckCapsule(foot, head, Radius * 0.9f, solid, QueryTriggerInteraction.Ignore)) walls++;
                    prevY = y;
                }
            }
            bool ok = walls == 0 && high == 0 && noGround == 0;
            log.AppendLine($"  {(ok ? "ok  " : "FAIL")} {name}: {length:F1} m, wall blocks {walls}, too-high steps {high}, no-ground steps {noGround}");
            return ok ? 0 : 1;
        }

        /// <summary>The walkable ground under a point (top of whatever solid is within 1.2 m above to 0.6 m below the NavMesh point).</summary>
        static float Ground(Vector3 p, int solid, out bool hit)
        {
            hit = Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out RaycastHit h, 1.2f + MaxDrop, solid, QueryTriggerInteraction.Ignore);
            return hit ? h.point.y : p.y;
        }
    }
}
