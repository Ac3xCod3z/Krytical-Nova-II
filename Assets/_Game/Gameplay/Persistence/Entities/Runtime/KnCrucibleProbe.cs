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
        /// <summary>=1: the ONE test war on the Crucible instead of the proofs (#346 flow, War Bots, gates).</summary>
        public const string WarVariable = "KN_PLAYHERE_CRUCIBLE_WAR";
        public static bool War => Environment.GetEnvironmentVariable(WarVariable) == "1";
        public static bool On => Environment.GetEnvironmentVariable(Variable) == "1" || War;

        /// <summary>The test war answers every Crossfire prompt yes (join, bot vote, Merc), still showing the real prompt.</summary>
        sealed class YesPrompts : IRequestPromptSink
        {
            public IRequestPromptSink Inner;
            public Action<string> Log;
            public readonly List<(float at, Action<bool> answer, string id)> Pending = new List<(float, Action<bool>, string)>();
            public int Shown;
            public void Show(string id, string eyebrow, string text, float seconds, Action<bool> answer)
            {
                Shown++;
                Log($"prompt '{id}' ({eyebrow}): {text} [{seconds:F0} s] -> answering yes in 2 s");
                Inner?.Show(id, eyebrow, text, seconds, answer);
                Pending.Add((Time.realtimeSinceStartup + 2f, answer, id));
            }
            public void Hide(string id) => Inner?.Hide(id);
            public void Tick()
            {
                for (int i = Pending.Count - 1; i >= 0; i--)
                    if (Time.realtimeSinceStartup >= Pending[i].at)
                    {
                        var (_, answer, id) = Pending[i];
                        Pending.RemoveAt(i);
                        Inner?.Hide(id);
                        answer(true);
                        Log($"prompt '{id}': answered yes");
                    }
            }
        }

        static Camera s_ShotCamera;
        static Vector3 s_ShotPos;
        static Quaternion s_ShotRot;
        static bool s_ShotOn, s_FogOff;

        static void BeforeCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (s_FogOff) RenderSettings.fog = false;
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
            if (War)
            {
                try { yield return RunWar(root, P, teleport, serverPosition, Log, ok => failures += ok ? 0 : 1); }
                finally { s_ShotOn = false; RenderPipelineManager.beginCameraRendering -= BeforeCamera; }
                Log(failures == 0 ? "test war PASS" : "test war FAILED");
                verdict(failures == 0);
                yield break;
            }
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

                // 4b. a jump between two walkable places whose arc crosses a Blocker (rim -> Hegemony yard over Blocker_0003, 31 m):
                // the landing is fine, so only the Blocker can stop it ("The jump arc or landing area is blocked.")
                Vector3 rim = P(114f, 46f);
                teleport(rim);
                yield return new WaitForSeconds(2f);
                Log($"SEGMENT jumpblock start frame {Time.frameCount}");
                Vector3 yard = P(124f, 36f);
                BodyClicks.Jump(yard);
                float bt = Time.realtimeSinceStartup;
                float maxX = float.MinValue;
                while (Time.realtimeSinceStartup - bt < 4f) { yield return null; maxX = Mathf.Max(maxX, root.InverseTransformPoint(serverPosition()).x); }
                bool blockOk = maxX < 117.5f;
                Log($"SEGMENT jumpblock end frame {Time.frameCount}");
                Log($"jump over a Blocker: {(blockOk ? "PASS" : "FAIL")}; from {root.InverseTransformPoint(rim):F1} to {root.InverseTransformPoint(yard):F1}, " +
                    $"the body reached local x {maxX:F1} at most (the wall at 117.5), stands at {root.InverseTransformPoint(serverPosition()):F1}");
                if (!blockOk) failures++;

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
                // why the sky may not show (run 1 of the Play proofs, 17 Nov: a flat purple sky): the fog, the dome, the camera
                Camera c = s_ShotCamera;
                Log($"sky check: fog {RenderSettings.fog} {RenderSettings.fogMode} density {RenderSettings.fogDensity:F5} colour {RenderSettings.fogColor}; " +
                    $"camera '{(c != null ? c.name : "none")}' far clip {(c != null ? c.farClipPlane : 0f):F0} at {(c != null ? root.InverseTransformPoint(c.transform.position) : Vector3.zero):F1}");
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true).Where(r => r.name.IndexOf("Sky", StringComparison.OrdinalIgnoreCase) >= 0).Take(4))
                    Log($"sky check: renderer '{r.name}' active {r.gameObject.activeInHierarchy} enabled {r.enabled} visible {r.isVisible} shader '{(r.sharedMaterial != null ? r.sharedMaterial.shader.name : "none")}' " +
                        $"bounds centre {root.InverseTransformPoint(r.bounds.center):F0} size {r.bounds.size:F0}");
                Log($"sky check: moon globals _KN_MoonDir {Shader.GetGlobalVector("_KN_MoonDir")} _KN_BodyTexParams {Shader.GetGlobalVector("_KN_BodyTexParams")} " +
                    $"_KN_BodyTexBrightness {Shader.GetGlobalVector("_KN_BodyTexBrightness")} _KN_MoonTex {(Shader.GetGlobalTexture("_KN_MoonTex") != null ? Shader.GetGlobalTexture("_KN_MoonTex").name : "none")}; " +
                    $"moon light forward {(moon != null ? -moon.transform.forward : Vector3.zero):F2}");
                bool fogWas = RenderSettings.fog;
                Log($"SEGMENT aurora_nofog start frame {Time.frameCount}");
                float nf = Time.realtimeSinceStartup;
                s_FogOff = true;   // applied just before the camera renders (the presenter sets the fog every frame)
                while (Time.realtimeSinceStartup - nf < 3f) yield return null;
                s_FogOff = false;
                RenderSettings.fog = fogWas;
                Log($"SEGMENT aurora_nofog end frame {Time.frameCount}");
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
                s_FogOff = false;
                RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            }
            Log(failures == 0 ? "all parts PASS" : $"{failures} part(s) FAILED");
            verdict(failures == 0);
        }

        static IEnumerator RunWar(Transform root, Func<float, float, float, Vector3> P, Action<Vector3> teleport, Func<Vector3> serverPosition,
            Action<string> Log, Action<bool> verdict)
        {
            float wait = Time.realtimeSinceStartup;
            while (!KryticalNova.Networking.GmRoster.Host.HasValue && Time.realtimeSinceStartup - wait < 30f) yield return null;
            ulong? host = KryticalNova.Networking.GmRoster.Host;
            string Gm(string line)
            {
                if (!(host is ulong h) || KryticalNova.Networking.PlayerGm.CommandRunner == null) return "NO GM runner";
                string a = KryticalNova.Networking.PlayerGm.CommandRunner(h, line);
                Log($"'{line}' -> {a}");
                return a;
            }
            var yes = new YesPrompts { Inner = ClientPresentationRegistry.Prompts, Log = Log };
            ClientPresentationRegistry.RegisterPrompts(yes);
            try
            {
                // the Tester waits in Zone01's town square: the join prompt reaches every bracket player wherever they are
                yield return new WaitForSeconds(2f);
                Log($"WAR the Tester at {serverPosition():F1} before the war");
                Gm("gm.crossfire status");
                Gm("gm.crossfire start");
                // a GM's war skips the join window (no join prompt): the Tester joins at the Marshal while the Muster takes players
                // (KryticalNova.Crossfire.CrossfireJoinLink.Requester, reached by name: this Editor probe takes no reference to Crossfire),
                // votes yes for War Bots in the yard, and the Muster runs on to the War by itself (the gates open on the clock)
                yield return new WaitForSeconds(2f);
                var link = Type.GetType("KryticalNova.Crossfire.CrossfireJoinLink, KryticalNova.Crossfire");
                var requester = link?.GetField("Requester")?.GetValue(null) as Action;
                Log(requester != null ? "WAR asking to join at the Marshal" : "WAR FAIL: no CrossfireJoinLink.Requester (no client join system?)");
                requester?.Invoke();
                string status = "";
                float m0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - m0 < 90f)
                {
                    yes.Tick();
                    yield return null;
                    if (Time.realtimeSinceStartup - m0 > 6f && root.InverseTransformPoint(serverPosition()).sqrMagnitude < 250f * 250f && !s_ShotOn)
                    {
                        // in the yard: film the Hegemony gate from the rim from now on (the leaves slide when the War starts)
                        s_ShotPos = root.TransformPoint(new Vector3(96f, 14f, -14f));
                        s_ShotRot = Quaternion.LookRotation(root.TransformPoint(new Vector3(135f, 0f, 0f)) - s_ShotPos);
                        s_ShotOn = true;
                        Log($"WAR the Tester is in the Crucible at local {root.InverseTransformPoint(serverPosition()):F1}; SEGMENT wargate start frame {Time.frameCount}");
                    }
                    if (Time.frameCount % 120 == 0) { status = Gm("gm.crossfire status"); if (status.Contains("War,")) break; }
                }
                Vector3 inWar = root.InverseTransformPoint(serverPosition());
                Log($"WAR phase: {status}; prompts shown {yes.Shown}; the Tester stands at local {inWar:F1}");
                bool atCrucible = Mathf.Abs(inWar.x) < 200f && Mathf.Abs(inWar.z) < 200f;
                if (!atCrucible) Log("WAR FAIL: the Tester is not in the Crucible (join prompt not taken?)");
                // the gate from the rim, high, looking into the Hegemony yard: the leaves slide and the side streams out
                if (!s_ShotOn)
                {
                    s_ShotPos = root.TransformPoint(new Vector3(96f, 14f, -14f));
                    s_ShotRot = Quaternion.LookRotation(root.TransformPoint(new Vector3(135f, 0f, 0f)) - s_ShotPos);
                    s_ShotOn = true;
                    Log($"SEGMENT wargate start frame {Time.frameCount}");
                }
                float g0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - g0 < 12f) { yes.Tick(); yield return null; }
                Log($"SEGMENT wargate end frame {Time.frameCount}");
                // the fight (owner, 17 Nov 2026: the factions' War Bots fight, the kills show on the HUD counters): first the arena from
                // the south rim, low enough to read the bodies, while the sides march in; then the Tester at the heart, on the game's own
                // follow camera, in the middle of it
                s_ShotPos = root.TransformPoint(new Vector3(0f, 22f, -58f));
                s_ShotRot = Quaternion.LookRotation(root.TransformPoint(new Vector3(0f, -7f, 8f)) - s_ShotPos);
                PathCheck(root, Log);
                DumpBots(root, Log);
                Log($"SEGMENT warfight start frame {Time.frameCount}");
                float f0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - f0 < 25f) { yes.Tick(); yield return null; }
                Log($"SEGMENT warfight end frame {Time.frameCount}");
                s_ShotOn = false;
                teleport(root.TransformPoint(new Vector3(0f, -6.3f, -12f)));
                Log($"SEGMENT warheart start frame {Time.frameCount}");
                float h0 = Time.realtimeSinceStartup, hLog = 0f;
                while (Time.realtimeSinceStartup - h0 < 60f)
                {
                    yes.Tick();
                    if (Time.realtimeSinceStartup - h0 >= hLog) { hLog += 10f; Gm("gm.crossfire status"); DumpBots(root, Log); }
                    yield return null;
                }
                Log($"SEGMENT warheart end frame {Time.frameCount}");
                // a death in the war and the rise at the side's shrine (the follow camera on the Tester)
                Vector3 before = root.InverseTransformPoint(serverPosition());
                Log($"SEGMENT wardeath start frame {Time.frameCount}");
                Gm("gm.kill crit");
                float d0 = Time.realtimeSinceStartup, nextLog = 0f;
                while (Time.realtimeSinceStartup - d0 < 16f)
                {
                    yes.Tick();
                    if (Time.realtimeSinceStartup - d0 >= nextLog) { nextLog += 2f; Log($"WAR death watch +{Time.realtimeSinceStartup - d0:F0} s: the Tester at local {root.InverseTransformPoint(serverPosition()):F1}"); }
                    yield return null;
                }
                Log($"SEGMENT wardeath end frame {Time.frameCount}");
                Vector3 after = root.InverseTransformPoint(serverPosition());
                Transform shrine = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("MARK_RespawnStand", StringComparison.OrdinalIgnoreCase) || t.name.StartsWith("MARK_RespawnShrine", StringComparison.OrdinalIgnoreCase) || (t.name.IndexOf("shrine", StringComparison.OrdinalIgnoreCase) >= 0 && !t.name.StartsWith("UCX_")))
                    .OrderBy(t => (root.InverseTransformPoint(t.position) - after).sqrMagnitude).FirstOrDefault();
                Log($"WAR death: before {before:F1}, after {after:F1}; the nearest shrine marker is " +
                    (shrine != null ? $"'{shrine.name}' at {root.InverseTransformPoint(shrine.position):F1} ({Vector3.Distance(root.InverseTransformPoint(shrine.position), after):F1} m away)" : "none"));
                Gm("gm.crossfire status");
                Gm("gm.crossfire end");
                float e0 = Time.realtimeSinceStartup;
                Log($"SEGMENT warend start frame {Time.frameCount}");
                while (Time.realtimeSinceStartup - e0 < 8f) { yes.Tick(); yield return null; }
                Log($"SEGMENT warend end frame {Time.frameCount}");
                Gm("gm.crossfire status");
                Log($"WAR summary: prompts {yes.Shown}, war reached {status.Contains("War,")}, Tester in the Crucible {atCrucible}");
                verdict(status.Contains("War,") && atCrucible && yes.Shown > 0);
            }
            finally
            {
                if (ReferenceEquals(ClientPresentationRegistry.Prompts, yes)) ClientPresentationRegistry.RegisterPrompts(yes.Inner);
            }
        }


        /// <summary>
        /// The War Bots as the server holds them (Master, 17 Nov 2026: why did nobody fight?): per side the count, the mean place, the mean
        /// distance to the heart, how many have a walk goal, a target, are retreating, and how long the stillest has stood still (12 s still
        /// with a goal puts a bot back on its Rally Point). Read by name from KryticalNova.WarBots.Entities (this Editor probe references
        /// no War Bots type).
        /// </summary>
        static void DumpBots(Transform root, Action<string> Log)
        {
            try
            {
                var sysType = Type.GetType("KryticalNova.WarBots.Entities.WarBotServerSystem, KryticalNova.WarBots.Entities");
                var server = KryticalNova.Networking.Entities.KnBootstrap.ServerWorld;
                if (sysType == null || server == null || !server.IsCreated) { Log("BOTS: no War Bot system type or server world"); return; }
                object sys = server.GetExistingSystemManaged(sysType);
                if (sys == null) { Log("BOTS: the server world runs no WarBotServerSystem"); return; }
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                var list = sysType.GetField("m_Bots", flags)?.GetValue(sys) as System.Collections.IList;
                double now = sysType.GetField("m_Now", flags)?.GetValue(sys) is double d ? d : 0;
                if (list == null) { Log("BOTS: no m_Bots list"); return; }
                Vector3 heart = root.TransformPoint(new Vector3(0f, -6.3f, 0f));
                var sides = new Dictionary<string, (int n, Vector3 sum, float dist, int goal, int target, int retreat, double still, string acts)>();
                foreach (object bot in list)
                {
                    Type bt = bot.GetType();
                    string side = bt.GetField("Side").GetValue(bot).ToString();
                    var body = bt.GetField("Body").GetValue(bot) as GameObject;
                    Vector3 pos = body != null ? body.transform.position : Vector3.zero;
                    bool hasGoal = (bool)bt.GetField("HasGoal").GetValue(bot);
                    int target = (int)bt.GetField("Target").GetValue(bot);
                    bool retreat = (bool)bt.GetField("Retreating").GetValue(bot);
                    double stillSince = (double)bt.GetField("StillSince").GetValue(bot);
                    string act = bt.GetField("PendingAct").GetValue(bot).ToString();
                    sides.TryGetValue(side, out var t);
                    t.n++; t.sum += pos; t.dist += Vector3.Distance(new Vector3(pos.x, 0f, pos.z), new Vector3(heart.x, 0f, heart.z));
                    if (hasGoal) t.goal++; if (target != 0) t.target++; if (retreat) t.retreat++;
                    t.still = Math.Max(t.still, now - stillSince);
                    if ((t.acts ?? "").Length < 60) t.acts = (t.acts == null ? "" : t.acts + ",") + act;
                    if (t.n == 1 && hasGoal)
                    {
                        Vector3 goal = (Vector3)bt.GetField("Goal").GetValue(bot);
                        var path = new UnityEngine.AI.NavMeshPath();
                        bool from = UnityEngine.AI.NavMesh.SamplePosition(pos, out UnityEngine.AI.NavMeshHit fh, 3f, UnityEngine.AI.NavMesh.AllAreas);
                        bool to = UnityEngine.AI.NavMesh.SamplePosition(goal, out UnityEngine.AI.NavMeshHit th, 3f, UnityEngine.AI.NavMesh.AllAreas);
                        if (from && to) UnityEngine.AI.NavMesh.CalculatePath(fh.position, th.position, UnityEngine.AI.NavMesh.AllAreas, path);
                        Log($"BOTS {side} first bot at local {root.InverseTransformPoint(pos):F1} goal local {root.InverseTransformPoint(goal):F1}: on NavMesh {from}, goal on NavMesh {to}, path {(from && to ? path.status.ToString() + " " + path.corners.Length + " corners" : "not tried")}");
                    }
                    sides[side] = t;
                }
                foreach (var kv in sides)
                {
                    var t = kv.Value;
                    Log($"BOTS {kv.Key}: {t.n} bots, mean local {root.InverseTransformPoint(t.sum / Math.Max(1, t.n)):F1}, mean {t.dist / Math.Max(1, t.n):F0} m from the heart, " +
                        $"{t.goal} walking to a goal, {t.target} with a target, {t.retreat} retreating, stillest {t.still:F0} s; acts {t.acts}");
                }
            }
            catch (Exception e) { Log("BOTS: the read threw " + e.GetType().Name + ": " + e.Message); }
        }

        /// <summary>A NavMesh path from each side's landing to the heart, as the server's planner would ask for it.</summary>
        static void PathCheck(Transform root, Action<string> Log)
        {
            Vector3 heart = root.TransformPoint(new Vector3(0f, -6.3f, 0f));
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true).Where(x => x.name.StartsWith("MARK_Landing_")))
            {
                var path = new UnityEngine.AI.NavMeshPath();
                bool from = UnityEngine.AI.NavMesh.SamplePosition(t.position, out UnityEngine.AI.NavMeshHit fh, 3f, UnityEngine.AI.NavMesh.AllAreas);
                bool to = UnityEngine.AI.NavMesh.SamplePosition(heart, out UnityEngine.AI.NavMeshHit th, 3f, UnityEngine.AI.NavMesh.AllAreas);
                float len = 0f;
                if (from && to && UnityEngine.AI.NavMesh.CalculatePath(fh.position, th.position, UnityEngine.AI.NavMesh.AllAreas, path))
                    for (int i = 1; i < path.corners.Length; i++) len += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                Log($"PATH {t.name} -> heart: landing on NavMesh {from}, heart on NavMesh {to}, {(from && to ? path.status + $", {path.corners.Length} corners, {len:F1} m" : "not tried")}");
            }
        }
    }
}
#endif
