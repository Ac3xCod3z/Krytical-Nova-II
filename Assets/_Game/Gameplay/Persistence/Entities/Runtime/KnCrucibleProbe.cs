#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KryticalNova.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The Crucible's Play proofs (build 19, 17 Nov 2026; env <see cref="Variable"/>=1, inside "Play DOTS here"): the Rally Gates' 3 s
    /// opening through the real Crossfire clock (gm.crossfire start / skip), a run up a grand stair, the failed jump out over the highest
    /// yard wall (the Blocker layer), a wall-run on a Rim wall, the sky (aurora, moon), the lava and its haze from a fixed camera, and the
    /// frame time at the dais, a yard and a gate. Each part logs its frame numbers so the capture (<see cref="KnPlayHere.CaptureVariable"/>)
    /// can be cut into GIFs. The Crucible is found by its root's name; nothing here references the Crossfire or World assemblies.
    /// Editor only.
    /// </summary>
    static class KnCrucibleProbe
    {
        public const string Variable = "KN_PLAYHERE_CRUCIBLE";
        public static bool On => Environment.GetEnvironmentVariable(Variable) == "1";

        static Camera s_ShotCamera;
        static Vector3 s_ShotPos;
        static Quaternion s_ShotRot;
        static bool s_ShotOn;

        static void BeforeCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (!s_ShotOn || cam != s_ShotCamera) return;
            cam.transform.SetPositionAndRotation(s_ShotPos, s_ShotRot);
        }

        public static IEnumerator Run(GameObject body, GameObject own, Action<string> log, Action<Vector3> teleport,
            Func<Vector3> serverPosition, Action<bool> verdict)
        {
            void Log(string s) => log("crucible: " + s);
            GameObject rootGo = GameObject.Find("Crucible");
            if (rootGo == null) { Log("FAIL; no 'Crucible' root in the scene"); verdict(false); yield break; }
            Transform root = rootGo.transform;
            int blockerLayer = LayerMask.NameToLayer("Blocker");
            int solid = Physics.DefaultRaycastLayers & ~(blockerLayer >= 0 ? 1 << blockerLayer : 0);
            Vector3 P(float x, float z, float yHint = 0f)
            {
                Vector3 w = root.TransformPoint(new Vector3(x, yHint, z));
                if (Physics.Raycast(w + Vector3.up * 12f, Vector3.down, out RaycastHit hit, 40f, solid, QueryTriggerInteraction.Ignore)) w.y = hit.point.y;
                return w;
            }
            var player = own != null ? own.GetComponent<KryticalNova.Player.PointAndClickPlayer>() : null;
            s_ShotCamera = player != null && player.GameplayCamera != null ? player.GameplayCamera : Camera.main;
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            int failures = 0;
            try
            {
                ulong? host = null;
                float wait = Time.realtimeSinceStartup;
                while (!KryticalNova.Networking.GmRoster.Host.HasValue && Time.realtimeSinceStartup - wait < 30f) yield return null;
                host = KryticalNova.Networking.GmRoster.Host;
                string Gm(string line)
                {
                    if (!(host is ulong h) || KryticalNova.Networking.PlayerGm.CommandRunner == null) return "NO GM runner";
                    string a = KryticalNova.Networking.PlayerGm.CommandRunner(h, line);
                    Log($"'{line}' -> {a}");
                    return a;
                }

                // 0. arrive on the Hegemony landing (the arena floor)
                teleport(P(77.44f, 0f, -7.4f));
                yield return new WaitForSeconds(3f);
                Log($"arrived at {serverPosition():F1} (root at {root.position:F0})");

                // 1. frame time at three places (the Editor's Play, one client + the server in this process: not a build's numbers)
                foreach (var (name, at) in new[] { ("dais", P(0f, -8f, -6.3f)), ("Hegemony yard", P(140f, 0f)), ("Covenant gate, rim side", P(-108f, 0f)) })
                {
                    teleport(at);
                    yield return new WaitForSeconds(2.5f);
                    var dt = new List<float>();
                    float t0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - t0 < 5f) { yield return null; dt.Add(Time.unscaledDeltaTime * 1000f); }
                    dt.Sort();
                    float avg = dt.Average(), p95 = dt[(int)(dt.Count * 0.95f)], worst = dt[dt.Count - 1];
                    Log($"perf {name}: {dt.Count} frames, avg {avg:F1} ms ({1000f / avg:F0} fps), p95 {p95:F1} ms, worst {worst:F1} ms");
                }

                // 2. the Rally Gates through the real clock: a fixed camera on the Covenant gate from the rim
                Transform[] leaves = root.GetComponentsInChildren<Transform>(true)
                    .Where(t => t.name.StartsWith("GateLeaf_") && !t.name.Contains("Sealed") && t.GetComponent<Collider>() == null).ToArray();
                Log($"gate leaves found: {leaves.Length} ({string.Join(", ", leaves.Select(l => l.name))})");
                var shut = leaves.ToDictionary(l => l, l => l.position);
                teleport(P(-100f, 6f));
                s_ShotPos = root.TransformPoint(new Vector3(-98f, 6f, 0f));
                s_ShotRot = Quaternion.LookRotation(root.TransformPoint(new Vector3(-118f, 3f, 0f)) - s_ShotPos);
                s_ShotOn = true;
                yield return new WaitForSeconds(2f);
                Log($"SEGMENT gate start frame {Time.frameCount}");
                Gm("gm.crossfire status");
                Gm("gm.crossfire start");
                float opened = -1f, moveStart = -1f, moveEnd = -1f;
                for (int skip = 0; skip < 4 && moveStart < 0f; skip++)
                {
                    Gm("gm.crossfire skip");
                    float s0 = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - s0 < 3f)
                    {
                        yield return null;
                        if (leaves.Any(l => (l.position - shut[l]).sqrMagnitude > 0.0004f)) { moveStart = Time.realtimeSinceStartup; break; }
                    }
                }
                if (moveStart >= 0f)
                {
                    Log($"gate leaves started moving at frame {Time.frameCount}");
                    Vector3 last = Vector3.zero;
                    float still = Time.realtimeSinceStartup;
                    while (Time.realtimeSinceStartup - moveStart < 8f)
                    {
                        yield return null;
                        Vector3 now = leaves.Length > 0 ? leaves[0].position : Vector3.zero;
                        if ((now - last).sqrMagnitude > 1e-6f) { still = Time.realtimeSinceStartup; last = now; }
                        else if (Time.realtimeSinceStartup - still > 0.5f) { moveEnd = still; break; }
                    }
                    opened = moveEnd > 0f ? moveEnd - moveStart : -1f;
                    foreach (Transform l in leaves) Log($"  leaf {l.name} moved {(l.position - shut[l]).magnitude:F2} m");
                }
                Log($"SEGMENT gate end frame {Time.frameCount}");
                Gm("gm.crossfire status");
                bool gateOk = opened > 2.4f && opened < 3.6f;
                Log($"gates: {(gateOk ? "PASS" : "FAIL")}; the opening took {opened:F2} s (3 s wanted)");
                if (!gateOk) failures++;
                s_ShotOn = false;

                // 3. the stair run: the Hegemony grand stair, from the arena floor to the rim
                teleport(P(72f, 0f, -7.5f));
                yield return new WaitForSeconds(2f);
                Log($"SEGMENT stair start frame {Time.frameCount}");
                Vector3 top = P(106f, 0f, 0f);
                BodyClicks.Ground(top, true);
                float st = Time.realtimeSinceStartup, topY = float.MinValue;
                while (Time.realtimeSinceStartup - st < 10f)
                {
                    yield return null;
                    Vector3 sp = serverPosition();
                    topY = Mathf.Max(topY, sp.y);
                    if (Vector3.Distance(new Vector3(sp.x, 0f, sp.z), new Vector3(top.x, 0f, top.z)) < 1.5f) break;
                }
                Vector3 end = serverPosition();
                bool stairOk = Vector3.Distance(new Vector3(end.x, 0f, end.z), new Vector3(top.x, 0f, top.z)) < 2f;
                Log($"SEGMENT stair end frame {Time.frameCount}");
                Log($"stair run: {(stairOk ? "PASS" : "FAIL")}; ended at {end:F1} ({Time.realtimeSinceStartup - st:F1} s), top {top:F1}");
                if (!stairOk) failures++;

                // 4. the failed jump out over the highest wall (Blocker_0011, the Hegemony yard's outer wall, 40 m top)
                Vector3 inside = P(150f, 0f);
                teleport(inside);
                yield return new WaitForSeconds(2f);
                Log($"SEGMENT jumpout start frame {Time.frameCount}");
                Vector3 outside = root.TransformPoint(new Vector3(172f, 0f, 0f));
                BodyClicks.Jump(outside);
                float jt = Time.realtimeSinceStartup;
                float farX = float.MinValue;
                while (Time.realtimeSinceStartup - jt < 4f) { yield return null; farX = Mathf.Max(farX, root.InverseTransformPoint(serverPosition()).x); }
                Vector3 after = root.InverseTransformPoint(serverPosition());
                bool jumpOk = farX < 159.6f;
                Log($"SEGMENT jumpout end frame {Time.frameCount}");
                Log($"jump out: {(jumpOk ? "PASS" : "FAIL")}; the body reached local x {farX:F1} at most (wall face at 159.6), stands at {after:F1}");
                if (!jumpOk) failures++;

                // 5. the wall-run: the Rim wall UCX_wall_0441 (local x -117.7, z 40..114, 8.87 m, faces +x), not a stair side
                teleport(P(-110f, 77f));
                yield return new WaitForSeconds(2f);
                Log($"SEGMENT wallrun start frame {Time.frameCount}");
                Vector3 onWall = root.TransformPoint(new Vector3(-117.7f, 5f, 77f));
                Vector3 normal = root.TransformDirection(Vector3.right);
                float y0 = serverPosition().y, wy = y0;
                BodyClicks.Wall(onWall, normal, true, false);
                float wt = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - wt < 6f) { yield return null; wy = Mathf.Max(wy, serverPosition().y); }
                bool wallOk = wy - y0 > 2f;
                Log($"SEGMENT wallrun end frame {Time.frameCount}");
                Log($"wall-run: {(wallOk ? "PASS" : "FAIL")}; rose {wy - y0:F2} m on the wall (on-wall flag {BodyClicks.LocalOnWall})");
                if (!wallOk) failures++;

                // 6. the sky, the moon, the lava and its haze from fixed cameras (the body waits on the dais)
                teleport(P(0f, -8f, -6.3f));
                yield return new WaitForSeconds(1.5f);
                Light moon = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                    .Where(l => l.type == LightType.Directional && l.isActiveAndEnabled).OrderByDescending(l => l.intensity).FirstOrDefault();
                Vector3 eye = root.TransformPoint(new Vector3(0f, 2f, -40f));
                s_ShotPos = eye;
                s_ShotRot = Quaternion.LookRotation(Quaternion.AngleAxis(-35f, Vector3.right) * root.TransformDirection(Vector3.forward));
                s_ShotOn = true;
                Log($"SEGMENT aurora start frame {Time.frameCount}");
                yield return new WaitForSeconds(8f);
                Log($"SEGMENT aurora end frame {Time.frameCount}");
                if (moon != null)
                {
                    s_ShotRot = Quaternion.LookRotation(-moon.transform.forward);
                    Log($"SEGMENT moon start frame {Time.frameCount} (light '{moon.name}' intensity {moon.intensity:F2}, elevation {Vector3.Angle(-moon.transform.forward, Vector3.ProjectOnPlane(-moon.transform.forward, Vector3.up)):F0} deg)");
                    yield return new WaitForSeconds(4f);
                    Log($"SEGMENT moon end frame {Time.frameCount}");
                }
                else Log("moon: no active directional light found");
                // the lava close-up: the first ArenaFloor haze box (LAVA_022 haze_boxes[0], centre -24, -5.99, -56)
                Vector3 lava = root.TransformPoint(new Vector3(-24f, -7.4f, -56f));
                s_ShotPos = lava + root.TransformDirection(new Vector3(6f, 3.5f, 9f));
                s_ShotRot = Quaternion.LookRotation(lava - s_ShotPos);
                Log($"SEGMENT lava start frame {Time.frameCount}");
                yield return new WaitForSeconds(8f);
                Log($"SEGMENT lava end frame {Time.frameCount}");
                s_ShotOn = false;
            }
            finally
            {
                s_ShotOn = false;
                RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            }
            Log(failures == 0 ? "all parts PASS" : $"{failures} part(s) FAILED");
            verdict(failures == 0);
        }
    }
}
#endif
