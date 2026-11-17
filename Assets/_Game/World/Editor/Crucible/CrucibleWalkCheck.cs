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
        static readonly System.Collections.Generic.HashSet<string> s_Backfaces = new System.Collections.Generic.HashSet<string>();
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
            // the war opens the Rally Gates; the routes are walked open (the shut gates' carved blockers are proven to stop the walk)
            int openedGates = 0;
            foreach (NavMeshObstacle ob in cf.GetComponentsInChildren<NavMeshObstacle>(true))
                if (ob.name.StartsWith("Rally Gate")) { ob.enabled = false; openedGates++; }
            // a disabled obstacle's carve only lifts on the NavMesh system's next tick, which a batch run never gives: re-bake in memory
            var surface = root.GetComponent<Unity.AI.Navigation.NavMeshSurface>();
            if (surface != null) surface.BuildNavMesh();
            log.AppendLine($"  ({openedGates} Rally Gates opened for the walk; NavMesh re-baked in memory, not saved)");
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
            if (s_Backfaces.Count > 0) log.AppendLine("  ground found only from below (flipped faces): " + string.Join(", ", s_Backfaces));
            log.AppendLine(failures == 0 ? "Walk check: PASS" : $"Walk check: FAIL ({failures} routes)");
            File.WriteAllText("Library/KryticalNova.CrucibleWalk.txt", log.ToString());
            return log.ToString();
        }

        static int Route(string name, Vector3 from, Vector3 to, int solid, StringBuilder log)
        {
            var path = new NavMeshPath();
            bool sa = NavMesh.SamplePosition(from, out NavMeshHit a, 3f, NavMesh.AllAreas), sb = NavMesh.SamplePosition(to, out NavMeshHit b, 3f, NavMesh.AllAreas);
            if (!sa || !sb) { log.AppendLine($"  FAIL {name}: no NavMesh within 3 m of the {(sa ? "end" : "start")} ({(sa ? to : from)})"); return 1; }
            if (!NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
            {
                Vector3 last = path.corners.Length > 0 ? path.corners[path.corners.Length - 1] : a.position;
                log.AppendLine($"  FAIL {name}: no complete NavMesh path ({path.status}), stops at {last}");
                return 1;
            }
            int walls = 0, high = 0, noGround = 0;
            Vector3 firstNoGround = Vector3.zero, firstWall = Vector3.zero;
            float length = 0f;
            Vector3 prev = path.corners[0];
            float walkY = path.corners[0].y;
            float prevY = Ground(prev, solid, out _);
            for (int c = 1; c < path.corners.Length; c++)
            {
                Vector3 p0 = path.corners[c - 1], p1 = path.corners[c];
                float seg = Vector3.Distance(p0, p1);
                length += seg;
                for (float s = Step; s <= seg; s += Step)
                {
                    Vector3 p = Vector3.Lerp(p0, p1, s / seg);
                    // follow the walked surface like a body: the NavMesh within 1 m of the last step's height (a straight line between far
                    // corners floats over stairs; a wide search jumps onto stair cheeks)
                    p.y = walkY;
                    if (NavMesh.SamplePosition(p, out NavMeshHit onMesh, 1f, NavMesh.AllAreas)) { p = onMesh.position; walkY = p.y; }
                    float y = Ground(p, solid, out bool hit);
                    if (!hit) { if (noGround++ == 0) firstNoGround = p; continue; }
                    if (Mathf.Abs(y - prevY) > MaxStep) high++;
                    Vector3 foot = new Vector3(p.x, y + Radius + 0.35f, p.z), head = new Vector3(p.x, y + Height - Radius, p.z);
                    if (Physics.CheckCapsule(foot, head, Radius * 0.9f, solid, QueryTriggerInteraction.Ignore)) { if (walls++ == 0) firstWall = p; }
                    prevY = y;
                }
            }
            bool ok = walls == 0 && high == 0 && noGround == 0;
            log.AppendLine($"  {(ok ? "ok  " : "FAIL")} {name}: {length:F1} m, wall blocks {walls}, too-high steps {high}, no-ground steps {noGround}{(noGround > 0 ? $" (first at {firstNoGround})" : "")}{(walls > 0 ? $" (first wall at {firstWall})" : "")}");
            return ok ? 0 : 1;
        }

        /// <summary>The walkable ground under a point (top of whatever solid is within 1.2 m above to 0.6 m below the NavMesh point).</summary>
        static float Ground(Vector3 p, int solid, out bool hit)
        {
            hit = Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out RaycastHit h, 1.2f + MaxDrop, solid, QueryTriggerInteraction.Ignore);
            if (!hit)
            {
                // diagnosis: is the ground there but facing down (a flipped mesh)?
                bool was = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = true;
                if (Physics.Raycast(p + Vector3.up * 1.2f, Vector3.down, out RaycastHit back, 1.2f + MaxDrop, solid, QueryTriggerInteraction.Ignore))
                    s_Backfaces.Add(back.collider.name);
                Physics.queriesHitBackfaces = was;
            }
            return hit ? h.point.y : p.y;
        }
    }
}
