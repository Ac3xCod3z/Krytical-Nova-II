using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Persistence.Entities;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// TEMPORARY (item 18 Play proof, b18-coder-13): run KnPlayHere.Batch with KN_PLAYHERE_TWO=1 (no bot); delete afterwards. Rides the
    /// Sky Lift to Concord 040 at noon, lets the crowd come out, shows the debug labels, then measures High, Low and High with no crowd
    /// (main thread, GPU, GC a frame, bodies, cards) and stands by a vendor. Logs "TOWN ..." lines with wall-clock times for the video cut.
    /// </summary>
    [InitializeOnLoad]
    public static class KnTmpTownsfolk
    {
        static KnTmpTownsfolk()
        {
            EditorPrefs.SetBool(KryticalNova.Diagnostics.PerformanceLog.EditorPrefKey, true);
            // KnTwoPlayerProbe registers itself too: take the slot when Play starts, after every delayCall
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.EnteredPlayMode) KnPlayHere.TwoPlayerProbe = Run; };
        }

        static string Gm(string line) =>
            PlayerGm.CommandRunner != null && GmRoster.Host is ulong host ? PlayerGm.CommandRunner(host, line) : "no GM runner";

        static string Now => DateTime.Now.ToString("HH:mm:ss.f");

        static Type FindType(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);

        static void Preset(string name)
        {
            Type prefs = FindType("KryticalNova.UI.GraphicsPreferences"), preset = FindType("KryticalNova.UI.GraphicsPreset");
            prefs?.GetMethod("ApplyPreset", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new[] { Enum.Parse(preset, name) });
        }

        static int Stat(string name) => FindType("KryticalNova.Townsfolk.TownsfolkViewClientSystem")?.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is int n ? n : -1;

        static int Vendors() => UnityEngine.Object.FindObjectsByType(FindType("KryticalNova.Townsfolk.VendorPresenter"), FindObjectsInactive.Exclude).Length;

        static readonly int EmptyState = Animator.StringToHash("Empty");

        static List<Animator> VendorAnimators() =>
            UnityEngine.Object.FindObjectsByType(FindType("KryticalNova.Townsfolk.VendorPresenter"), FindObjectsInactive.Exclude)
                .Cast<Component>().Select(c => c.GetComponent<Animator>()).Where(a => a != null && a.runtimeAnimatorController != null).ToList();

        /// <summary>Vendors by standing state: how many share it and the share of the cycle they are spread over (in step = near 0).</summary>
        static string VendorStep()
        {
            List<Animator> all = VendorAnimators();
            var parts = new List<string>();
            int closePairs = 0;
            foreach (var g in all.GroupBy(a => a.GetCurrentAnimatorStateInfo(0).shortNameHash))
            {
                List<float> ph = g.Select(a => Mathf.Repeat(a.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f)).OrderBy(x => x).ToList();
                float gap = 0f;
                for (int i = 0; i < ph.Count; i++) gap = Mathf.Max(gap, i + 1 < ph.Count ? ph[i + 1] - ph[i] : ph[0] + 1f - ph[i]);
                for (int i = 0; i < ph.Count; i++)
                    for (int k = i + 1; k < ph.Count; k++) { float d = Mathf.Abs(ph[i] - ph[k]); if (Mathf.Min(d, 1f - d) < 0.01f) closePairs++; }
                parts.Add(ph.Count + " over " + (ph.Count > 1 ? 1f - gap : 0f).ToString("F2") + " (" + string.Join(" ", ph.Select(x => x.ToString("F2"))) + ")");
            }
            return all.Count + " vendors in " + parts.Count + " standing states [" + string.Join(", ", parts) + "] (count over share of the cycle they span), pairs within 1% of a cycle " + closePairs +
                   ", speeds " + all.Select(a => a.speed).DefaultIfEmpty(1f).Min().ToString("F2") + "-" + all.Select(a => a.speed).DefaultIfEmpty(1f).Max().ToString("F2");
        }

        /// <summary>Watches the vendors' gesture layer: how many gestures started and the most that started on one frame.</summary>
        static IEnumerator WatchGestures(KnPlayHere.TwoPlayerRun r, string label, float seconds)
        {
            List<Animator> vendors = VendorAnimators();
            var was = new bool[vendors.Count];
            int started = 0, mostOnOneFrame = 0, mostAtOnce = 0;
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                int now = 0, playing = 0;
                for (int i = 0; i < vendors.Count; i++)
                {
                    if (vendors[i] == null || vendors[i].layerCount < 2) continue;
                    bool on = vendors[i].GetCurrentAnimatorStateInfo(1).shortNameHash != EmptyState || vendors[i].IsInTransition(1);
                    if (on && !was[i]) now++;
                    if (on) playing++;
                    was[i] = on;
                }
                started += now;
                mostOnOneFrame = Math.Max(mostOnOneFrame, now);
                mostAtOnce = Math.Max(mostAtOnce, playing);
            }
            r.Log("TOWN gestures " + label + " (" + Now + "): " + vendors.Count + " vendors watched " + seconds.ToString("F0") + " s, " + started +
                  " gestures started, most started on one frame " + mostOnOneFrame + ", most playing at once " + mostAtOnce);
        }

        static string OutFolder
        {
            get
            {
                string f = Environment.GetEnvironmentVariable("KN_TMP_TOWN_OUT");
                if (string.IsNullOrEmpty(f)) f = System.IO.Path.Combine(Application.dataPath, "..", "Logs", "town-shots");
                System.IO.Directory.CreateDirectory(f);
                return f;
            }
        }

        static void SavePng(RenderTexture rt, string name)
        {
            RenderTexture before = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = before;
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(OutFolder, name), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>Bodies in view with both arms out and alike (the bind pose: T or A), of those in view; arms are degrees from straight down.</summary>
        static string ArmCheck()
        {
            int seen = 0, bind = 0, notHuman = 0;
            var worst = new List<string>();
            foreach (Animator a in UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude))
            {
                bool ours = a.GetComponentInParent(FindType("KryticalNova.Townsfolk.TownsfolkBody")) != null || a.GetComponent(FindType("KryticalNova.Townsfolk.VendorPresenter")) != null;
                if (!ours) continue;
                if (!a.GetComponentsInChildren<SkinnedMeshRenderer>().Any(s => s.enabled && s.isVisible)) continue;
                if (!a.isHuman) { notHuman++; continue; }
                Transform lu = a.GetBoneTransform(HumanBodyBones.LeftUpperArm), ll = a.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                Transform ru = a.GetBoneTransform(HumanBodyBones.RightUpperArm), rl = a.GetBoneTransform(HumanBodyBones.RightLowerArm);
                if (lu == null || ll == null || ru == null || rl == null) { notHuman++; continue; }
                seen++;
                float l = Vector3.Angle(ll.position - lu.position, Vector3.down), rr = Vector3.Angle(rl.position - ru.position, Vector3.down);
                if (l > 35f && rr > 35f && Mathf.Abs(l - rr) < 4f)
                {
                    bind++;
                    if (worst.Count < 4) worst.Add(a.transform.root.name + "/" + a.name + " L" + l.ToString("F0") + " R" + rr.ToString("F0") + " controller " + (a.runtimeAnimatorController != null));
                }
            }
            return "bodies in view " + seen + ", in a bind pose (both arms out > 35 deg and alike) " + bind + (notHuman > 0 ? ", not humanoid " + notHuman : "") +
                   (worst.Count > 0 ? " [" + string.Join("; ", worst) + "]" : "");
        }

        /// <summary>Eye-level pictures around the player (the game's own camera looks down: far bodies and cards are out of its view), the far cards' own pictures, and the arm check.</summary>
        static IEnumerator Look(KnPlayHere.TwoPlayerRun r, string label, Vector3? face = null)
        {
            int n = 0;
            foreach (RenderTexture card in Resources.FindObjectsOfTypeAll<RenderTexture>().Where(t => t.name == "Townsfolk card").Take(16))
                SavePng(card, "card_" + label + "_" + (n++) + ".png");
            var go = new GameObject("KnTmp look camera");
            var rt = new RenderTexture(1280, 720, 24);
            try
            {
                Camera cam = go.AddComponent<Camera>();
                if (Camera.main != null) cam.CopyFrom(Camera.main);
                cam.targetTexture = rt;
                cam.fieldOfView = 60f;
                cam.nearClipPlane = 0.1f;
                Vector3 eye = r.Body.transform.position + Vector3.up * 1.7f;
                float first = face.HasValue ? Quaternion.LookRotation(Vector3.ProjectOnPlane(face.Value - eye, Vector3.up)).eulerAngles.y : 0f;
                for (int i = 0; i < 4; i++)
                {
                    go.transform.SetPositionAndRotation(eye, Quaternion.Euler(4f, first + i * 90f, 0f));
                    yield return new WaitForSeconds(1.2f);
                    yield return new WaitForEndOfFrame();
                    SavePng(rt, "look_" + label + "_" + i + ".png");
                    r.Log("TOWN look " + label + " " + i + " (" + Now + "): " + ArmCheck());
                }
                r.Log("TOWN look " + label + ": " + n + " card pictures and 4 views in " + OutFolder);
            }
            finally
            {
                UnityEngine.Object.Destroy(go);
                rt.Release();
                UnityEngine.Object.Destroy(rt);
            }
        }

        static IEnumerator Measure(KnPlayHere.TwoPlayerRun r, string label, float seconds)
        {
            KryticalNova.Diagnostics.PerformanceLog.Section("town." + label);
            using ProfilerRecorder main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            using ProfilerRecorder gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            using ProfilerRecorder view = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "TownsfolkViewClientSystem", 1);
            using ProfilerRecorder vend = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "VendorLooksClientSystem", 1);
            using ProfilerRecorder decide = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "TownLifeDecideSystem", 1);
            var timings = new FrameTiming[1];
            double mainSum = 0, gpuSum = 0, gcSum = 0, viewSum = 0, vendSum = 0, decideSum = 0;
            int frames = 0, gpuFrames = 0, bodies = 0, cards = 0;
            yield return null;
            float until = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < until)
            {
                yield return null;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0) { gpuSum += timings[0].gpuFrameTime; gpuFrames++; }
                mainSum += main.LastValue * 1e-6;
                gcSum += gc.LastValue;
                viewSum += view.LastValue * 1e-6;
                vendSum += vend.LastValue * 1e-6;
                decideSum += decide.LastValue * 1e-6;
                bodies = Math.Max(bodies, Stat("ShownNow"));
                cards = Math.Max(cards, Stat("CardsNow"));
                frames++;
            }
            r.Log($"TOWN measure {label} ({Now}): {frames} frames, fps {frames / seconds:F0}; main {mainSum / frames:F2} ms, gpu {(gpuFrames > 0 ? (gpuSum / gpuFrames).ToString("F2") : "n/a")} ms, " +
                  $"GC {gcSum / frames:F0} B/frame; systems: view {viewSum / frames:F3} ms, vendors {vendSum / frames:F3} ms, decide (server, main part) {decideSum / frames:F3} ms; " +
                  $"bodies {bodies}, cards {cards}; recorders valid main {main.Valid} gc {gc.Valid} view {view.Valid} decide {decide.Valid}");
        }

        // ---- owner notes 14 Nov 2026: life over all of Concord, five bands, no weapons ----

        static Type ServerType => FindType("KryticalNova.Townsfolk.TownsfolkServerSystem");
        static Type ViewType => FindType("KryticalNova.Townsfolk.TownsfolkViewClientSystem");
        static object Static(Type t, string name) => t?.GetField(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null);

        static string Bands()
        {
            int[] b = Static(ViewType, "BandsNow") as int[] ?? new int[5];
            return $"CLOSE {b[0]}, MEDIUM {b[1]}, FAR {b[2]}, cards {b[3]}, too far {b[4]}; last look's triangles LOD0/1/2 {Static(ViewType, "LodTriangles")}";
        }

        /// <summary>Weapons left on townsfolk and vendors: extra renderers on a body, and merged parts in a weapon-mount region.</summary>
        static string WeaponCheck()
        {
            Type body = FindType("KryticalNova.Townsfolk.TownsfolkBody"), vendor = FindType("KryticalNova.Townsfolk.VendorPresenter");
            var barred = new HashSet<KryticalNova.Items.BodyRegion> { KryticalNova.Items.BodyRegion.AttachmentBack, KryticalNova.Items.BodyRegion.AttachmentHipsBack,
                KryticalNova.Items.BodyRegion.AttachmentHipsLeft, KryticalNova.Items.BodyRegion.AttachmentHipsRight };
            int looks = 0, extraRenderers = 0, barredParts = 0, crossFade = 0, withShader = 0;
            var lodGroups = 0;
            foreach (KryticalNova.Items.Visuals.CharacterAppearance a in UnityEngine.Object.FindObjectsByType<KryticalNova.Items.Visuals.CharacterAppearance>(FindObjectsInactive.Include))
            {
                bool ours = a.GetComponent(body) != null || a.GetComponentInChildren(vendor, true) != null;
                if (!ours) continue;
                looks++;
                foreach (Renderer r in a.GetComponentsInChildren<Renderer>(true))
                    if (r != a.Renderer && r.name != "LOD1" && r.name != "LOD2" && !(r is ParticleSystemRenderer)) extraRenderers++;
                foreach (KryticalNova.Items.SidekickPartRef part in a.CurrentParts) if (barred.Contains(part.region)) barredParts++;
                if (a.GetComponent<LODGroup>() != null) lodGroups++;
                Material m = a.Renderer != null ? a.Renderer.sharedMaterial : null;
                if (m != null && m.shader != null)
                {
                    withShader++;
                    if (m.shader.keywordSpace.keywordNames.Contains("LOD_FADE_CROSSFADE")) crossFade++;
                }
            }
            return $"looks {looks} (LOD groups {lodGroups}): other renderers {extraRenderers}, merged weapon-mount parts {barredParts}; shaders with the LOD cross-fade {crossFade} of {withShader}";
        }

        static Transform ConcordRoot() => GameObject.Find("Concord 040 (sky preview)")?.transform;

        /// <summary>Where the crowd stands over a few game hours (the town clock run fast), as a top-down heatmap of Concord with its NavMesh.</summary>
        static IEnumerator Heatmap(KnPlayHere.TwoPlayerRun r, float seconds, float fromHour, float toHour)
        {
            Type server = ServerType;
            FieldInfo clock = server?.GetField("ClockOverride", BindingFlags.Public | BindingFlags.Static);
            FieldInfo record = server?.GetField("RecordPositions", BindingFlags.Public | BindingFlags.Static);
            var positions = Static(server, "Positions") as List<Vector3>;
            Transform root = ConcordRoot();
            if (clock == null || record == null || positions == null || root == null) { r.Log("TOWN heatmap FAIL: no server statics or no Concord root"); yield break; }
            float start = Time.realtimeSinceStartup;
            float hours = fromHour;
            clock.SetValue(null, (Func<(float hours, bool raining)>)(() => (hours, false)));
            record.SetValue(null, true);
            var all = new List<Vector3>();
            int samples = 0;
            try
            {
                while (Time.realtimeSinceStartup < start + seconds)
                {
                    yield return new WaitForSeconds(1f);
                    hours = Mathf.Lerp(fromHour, toHour, (Time.realtimeSinceStartup - start) / seconds);
                    all.AddRange(positions);
                    samples++;
                }
            }
            finally
            {
                clock.SetValue(null, null);
                record.SetValue(null, false);
            }
            // the picture: 640 px over 240 m round the root, the NavMesh grey, every sample a warm dot, the arrival ring yellow
            const int px = 640;
            const float span = 240f;
            var tex = new Texture2D(px, px, TextureFormat.RGBA32, false);
            var pix = new Color32[px * px];
            for (int i = 0; i < pix.Length; i++) pix[i] = new Color32(18, 20, 28, 255);
            Vector2 ToPx(Vector3 w) { Vector3 l = root.InverseTransformPoint(w); return new Vector2((l.x / span + 0.5f) * px, (l.z / span + 0.5f) * px); }
            UnityEngine.AI.NavMeshTriangulation nav = UnityEngine.AI.NavMesh.CalculateTriangulation();
            for (int i = 0; i + 2 < nav.indices.Length; i += 3)
            {
                Vector3 a = nav.vertices[nav.indices[i]], b = nav.vertices[nav.indices[i + 1]], c = nav.vertices[nav.indices[i + 2]];
                if (Mathf.Abs((a.y + b.y + c.y) / 3f - root.position.y) > 30f) continue;
                Vector2 A = ToPx(a), B = ToPx(b), C = ToPx(c);
                int x0 = Mathf.Max(0, (int)Mathf.Min(A.x, Mathf.Min(B.x, C.x))), x1 = Mathf.Min(px - 1, (int)Mathf.Max(A.x, Mathf.Max(B.x, C.x)) + 1);
                int y0 = Mathf.Max(0, (int)Mathf.Min(A.y, Mathf.Min(B.y, C.y))), y1 = Mathf.Min(px - 1, (int)Mathf.Max(A.y, Mathf.Max(B.y, C.y)) + 1);
                float area = (B.x - A.x) * (C.y - A.y) - (C.x - A.x) * (B.y - A.y);
                if (Mathf.Abs(area) < 1e-4f) continue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float w0 = ((B.x - x) * (C.y - y) - (C.x - x) * (B.y - y)) / area, w1 = ((C.x - x) * (A.y - y) - (A.x - x) * (C.y - y)) / area;
                        if (w0 >= 0f && w1 >= 0f && w0 + w1 <= 1f) pix[y * px + x] = new Color32(70, 74, 86, 255);
                    }
            }
            var heat = new float[px * px];
            foreach (Vector3 w in all)
            {
                Vector2 q = ToPx(w);
                for (int dy = -3; dy <= 3; dy++)
                    for (int dx = -3; dx <= 3; dx++)
                    {
                        int x = (int)q.x + dx, y = (int)q.y + dy;
                        if (x < 0 || y < 0 || x >= px || y >= px || dx * dx + dy * dy > 9) continue;
                        heat[y * px + x] += 1f;
                    }
            }
            float most = 1f;
            foreach (float h in heat) most = Mathf.Max(most, h);
            for (int i = 0; i < heat.Length; i++)
                if (heat[i] > 0f)
                {
                    float k = Mathf.Sqrt(heat[i] / most);
                    pix[i] = Color32.Lerp(new Color32(255, 120, 40, 255), new Color32(255, 255, 210, 255), k);
                }
            GameObject arrival = GameObject.Find(SkyLiftLink.ArrivalMarker);
            int near = 0, quarters0 = 0, quarters1 = 0, quarters2 = 0, quarters3 = 0;
            if (arrival != null)
            {
                Vector2 c = ToPx(arrival.transform.position);
                float rad = 28f / span * px;
                for (int k = 0; k < 360; k++)
                {
                    int x = (int)(c.x + Mathf.Cos(k * Mathf.Deg2Rad) * rad), y = (int)(c.y + Mathf.Sin(k * Mathf.Deg2Rad) * rad);
                    if (x >= 0 && y >= 0 && x < px && y < px) pix[y * px + x] = new Color32(255, 220, 0, 255);
                }
                foreach (Vector3 w in all) if (Vector2.Distance(new Vector2(w.x, w.z), new Vector2(arrival.transform.position.x, arrival.transform.position.z)) <= 28f) near++;
            }
            foreach (Vector3 w in all)
            {
                Vector3 l = root.InverseTransformPoint(w);
                if (l.x < 0f && l.z >= 0f) quarters0++; else if (l.x >= 0f && l.z >= 0f) quarters1++; else if (l.x < 0f) quarters2++; else quarters3++;
            }
            tex.SetPixels32(pix);
            tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(OutFolder, "heatmap.png"), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            int n = Mathf.Max(1, all.Count);
            // 14 Nov 2026 (fix lines): every sample on the NavMesh? off = more than 0.3 m from the mesh (inside a planter, off the deck)
            int offMesh = 0;
            foreach (Vector3 w in all)
                if (!UnityEngine.AI.NavMesh.SamplePosition(w, out UnityEngine.AI.NavMeshHit h, 2f, UnityEngine.AI.NavMesh.AllAreas)
                    || Vector2.Distance(new Vector2(h.position.x, h.position.z), new Vector2(w.x, w.z)) > 0.3f) offMesh++;
            r.Log($"TOWN heatmap off the mesh ({Now}): {offMesh} of {all.Count} samples ({offMesh * 100f / n:F2} %)");
            r.Log($"TOWN heatmap ({Now}): {samples} samples of the crowd from {fromHour:F0}:00 to {toHour:F0}:00, {all.Count} positions; near the arrival {near * 100f / n:F0} %; " +
                  $"quarters NW {quarters0 * 100f / n:F0} % NE {quarters1 * 100f / n:F0} % SW {quarters2 * 100f / n:F0} % SE {quarters3 * 100f / n:F0} %; spread points {Static(ServerType, "SpreadPoints")}; " +
                  $"picture {System.IO.Path.Combine(OutFolder, "heatmap.png")}");
        }

        /// <summary>
        /// Eye level beside a planter (14 Nov 2026, fix lines): a small hole in the deck's NavMesh near the arrival (the mesh ends, and
        /// starts again within 8 m), the player put 4 m from it, 20 s to watch the crowd walk round it, then the four eye-level views.
        /// </summary>
        static IEnumerator Planter(KnPlayHere.TwoPlayerRun r, Vector3 near)
        {
            for (int k = 0; k < 48; k++)
            {
                float a = k * 2.4f, d = 8f + k * 0.6f;
                Vector3 probe = near + new Vector3(Mathf.Sin(a) * d, 0f, Mathf.Cos(a) * d);
                if (!UnityEngine.AI.NavMesh.SamplePosition(probe, out UnityEngine.AI.NavMeshHit at, 3f, UnityEngine.AI.NavMesh.AllAreas)) continue;
                if (!UnityEngine.AI.NavMesh.FindClosestEdge(at.position, out UnityEngine.AI.NavMeshHit edge, UnityEngine.AI.NavMesh.AllAreas)) continue;
                Vector3 inward = new Vector3(at.position.x - edge.position.x, 0f, at.position.z - edge.position.z);
                if (inward.sqrMagnitude < 1e-4f) continue;
                inward.Normalize();
                bool OnMesh(Vector3 p) => UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit h, 0.4f, UnityEngine.AI.NavMesh.AllAreas);
                if (OnMesh(edge.position - inward * 1f)) continue;   // not a hole there
                bool small = false;
                for (float m = 2f; m <= 8f; m += 1f) if (OnMesh(edge.position - inward * m)) { small = true; break; }
                if (!small || !OnMesh(edge.position + inward * 4f)) continue;
                r.Put?.Invoke(r.Body, edge.position + inward * 4f);
                r.Log($"TOWN clip planter {Now}: put 4 m from the hole edge at {edge.position}");
                yield return new WaitForSeconds(20f);
                yield return Look(r, "planter", edge.position);
                yield break;
            }
            r.Log("TOWN planter: no small hole found near the arrival");
        }

        /// <summary>A slow wide orbit high over Concord (the camera the crowd's bands follow while it runs), measured part way.</summary>
        static IEnumerator Wide(KnPlayHere.TwoPlayerRun r, string label, float seconds, float measureFrom, float measureFor)
        {
            Transform root = ConcordRoot();
            Camera main = Camera.main;
            if (root == null || main == null) { r.Log("TOWN wide FAIL: no root or camera"); yield break; }
            var go = new GameObject("KnTmp wide camera");
            Camera cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.depth = main.depth + 5f;
            cam.fieldOfView = 55f;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 1000f);
            string oldTag = main.tag;
            main.tag = "Untagged";
            go.tag = "MainCamera";   // the townsfolk view measures its bands from Camera.main
            r.Log($"TOWN clip wide {label} start {Now}");
            float start = Time.realtimeSinceStartup;
            bool measured = false;
            try
            {
                while (Time.realtimeSinceStartup < start + seconds)
                {
                    float k = (Time.realtimeSinceStartup - start) / seconds;
                    float a = Mathf.Lerp(-35f, 35f, k) * Mathf.Deg2Rad;
                    Vector3 at = root.position + new Vector3(Mathf.Sin(a) * -105f, 38f, Mathf.Cos(a) * -105f);
                    go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(root.position + Vector3.up * 2f - at));
                    if (!measured && Time.realtimeSinceStartup >= start + measureFrom)
                    {
                        measured = true;
                        r.Log($"TOWN wide {label} bands {Now}: " + Bands());
                        float until = Time.realtimeSinceStartup + measureFor;
                        IEnumerator m = Measure(r, "wide-" + label, measureFor);
                        while (m.MoveNext())
                        {
                            k = (Time.realtimeSinceStartup - start) / seconds;
                            a = Mathf.Lerp(-35f, 35f, k) * Mathf.Deg2Rad;
                            at = root.position + new Vector3(Mathf.Sin(a) * -105f, 38f, Mathf.Cos(a) * -105f);
                            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(root.position + Vector3.up * 2f - at));
                            yield return m.Current;
                        }
                        continue;
                    }
                    yield return null;
                }
            }
            finally
            {
                main.tag = oldTag;
                UnityEngine.Object.Destroy(go);
                r.Log($"TOWN clip wide {label} end {Now}");
            }
        }

        static int s_Errors;
        static void Count(string message, string stack, LogType type) { if (message != null && message.StartsWith("Array index (")) s_Errors++; }

        static IEnumerator Window(KnPlayHere.TwoPlayerRun r, string what)
        {
            s_Errors = 0;
            yield return new WaitForSeconds(5f);
            r.Log($"TOWN bisect {what}: {s_Errors} out-of-bounds errors in 5 s");
        }

        static IEnumerator Bisect(KnPlayHere.TwoPlayerRun r)
        {
            Application.logMessageReceivedThreaded += Count;
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
            Type pt = FindType("KryticalNova.Townsfolk.VendorPresenter");
            var presenters = UnityEngine.Object.FindObjectsByType(pt, FindObjectsInactive.Exclude).Cast<MonoBehaviour>().ToList();
            var animators = presenters.Select(p => p.GetComponent<Animator>()).Where(a => a != null).ToList();
            static Transform LookRoot(Transform t) { for (; t != null; t = t.parent) if (t.name == "Sidekick look") return t; return null; }
            var skins = presenters.Select(p => LookRoot(p.transform)).Where(t => t != null)
                .SelectMany(t => t.GetComponentsInChildren<SkinnedMeshRenderer>(false)).ToList();
            if (animators.Count > 0)
            {
                Animator a0 = animators[0];
                SkinnedMeshRenderer s0 = skins.Count > 0 ? skins[0] : null;
                r.Log($"TOWN bisect: {presenters.Count} vendors; first animator avatar {(a0.avatar != null ? a0.avatar.name + (a0.avatar.isHuman ? " human" : " generic") : "none")}, " +
                      $"controller {a0.runtimeAnimatorController?.name}, layers {a0.layerCount}; skin bones {(s0 != null ? s0.bones.Length : -1)}, mesh {(s0 != null && s0.sharedMesh != null ? s0.sharedMesh.name + " bindposes " + s0.sharedMesh.bindposeCount : "none")}");
            }
            yield return Window(r, "A all on");
            foreach (MonoBehaviour p in presenters) p.enabled = false;
            yield return Window(r, "B presenters off (no look-at IK)");
            foreach (MonoBehaviour p in presenters) p.enabled = true;
            foreach (Animator a in animators) a.enabled = false;
            yield return Window(r, "C vendor animators off");
            foreach (Animator a in animators) a.enabled = true;
            foreach (SkinnedMeshRenderer s in skins) s.enabled = false;
            yield return Window(r, $"D vendor skins off ({skins.Count})");
            foreach (SkinnedMeshRenderer s in skins) s.enabled = true;
            yield return Window(r, "E all on again");
            Application.logMessageReceivedThreaded -= Count;
        }

        static IEnumerator Run(KnPlayHere.TwoPlayerRun r)
        {
            bool ok = false;
            try
            {
                r.Log($"TOWN start {Now}: " + Gm("time.noon") + " / " + Gm("time.pause"));
                GameObject arrival = GameObject.Find(SkyLiftLink.ArrivalMarker);
                if (arrival == null || SkyLiftLink.Requester == null) { r.Log("TOWN FAIL: no arrival marker or no Sky Lift requester"); yield break; }
                SkyLiftLink.Requester(false);
                float until = Time.realtimeSinceStartup + 12f;
                while (Time.realtimeSinceStartup < until && Vector3.Distance(r.Body.transform.position, arrival.transform.position) > 3f) yield return null;
                r.Log($"TOWN arrived {Now}: body at {r.Body.transform.position}");
                if (Environment.GetEnvironmentVariable("KN_TMP_TOWN_BISECT") == "1") { yield return Bisect(r); ok = true; yield break; }
                yield return new WaitForSeconds(25f);   // 4 a second come out: 80 in ~20 s
                r.Log($"TOWN status {Now}: " + Gm("gm.town") + $" | view bodies {Stat("ShownNow")}, cards {Stat("CardsNow")}, looks {Stat("LooksNow")}, vendors dressed {Vendors()}");
                r.Log($"TOWN bands {Now}: " + Bands());
                r.Log($"TOWN weapons {Now}: " + WeaponCheck());
                if (Environment.GetEnvironmentVariable("KN_TMP_TOWN_MODE") != "old")
                {
                    yield return Heatmap(r, 120f, 7f, 21f);
                    Gm("time.noon");
                    yield return Planter(r, arrival.transform.position);
                    yield return new WaitForSeconds(4f);
                    Preset("High");
                    yield return Wide(r, "High", 30f, 8f, 10f);
                    Preset("Low");
                    yield return new WaitForSeconds(2f);
                    yield return Wide(r, "Low", 22f, 6f, 10f);
                    Preset("High");
                    yield return Measure(r, "High", 8f);
                    r.Log($"TOWN player view High bands {Now}: " + Bands());
                    yield return Look(r, "High");
                    Preset("Low");
                    yield return new WaitForSeconds(2f);
                    yield return Measure(r, "Low", 8f);
                    Preset("High");
                    r.Log($"TOWN no crowd {Now}: " + Gm("gm.town.cap 0 0") + " / " + Gm("gm.town.clear"));
                    yield return new WaitForSeconds(3f);
                    yield return Wide(r, "HighNoCrowd", 16f, 4f, 10f);
                    yield return Measure(r, "HighNoCrowd", 8f);
                    Gm("gm.town.cap -1 -1");
                    yield return new WaitForSeconds(15f);
                    r.Log("TOWN vendors in step? " + Now + ": " + VendorStep());
                    r.Log($"TOWN weapons end {Now}: " + WeaponCheck());
                    ok = true;
                    yield break;
                }

                r.Log("TOWN vendors in step? " + Now + ": " + VendorStep());
                r.Log($"TOWN clip crowd {Now} (8 s, High, no labels)");
                yield return new WaitForSeconds(8f);
                r.Log($"TOWN clip labels {Now} (6 s): " + Gm("gm.town.debug true"));
                yield return new WaitForSeconds(6f);
                Gm("gm.town.debug false");

                Preset("High");
                yield return new WaitForSeconds(3f);
                yield return Measure(r, "High", 10f);
                yield return Look(r, "High");
                Preset("Low");
                r.Log($"TOWN clip Low {Now}");
                yield return new WaitForSeconds(3f);
                yield return Measure(r, "Low", 10f);
                yield return Look(r, "Low");
                Preset("High");
                r.Log($"TOWN no crowd {Now}: " + Gm("gm.town.cap 0 0") + " / " + Gm("gm.town.clear"));
                yield return new WaitForSeconds(3f);
                yield return Measure(r, "HighNoCrowd", 10f);
                Gm("gm.town.cap -1 -1");

                // a vendor up close: the nearest dressed vendor to the arrival
                Type pt = FindType("KryticalNova.Townsfolk.VendorPresenter");
                var vendors = UnityEngine.Object.FindObjectsByType(pt, FindObjectsInactive.Exclude).Cast<Component>()
                    .OrderBy(c => Vector3.Distance(c.transform.position, arrival.transform.position)).ToList();
                if (vendors.Count == 0) r.Log("TOWN vendor: none dressed");
                else
                {
                    Transform v = vendors[0].transform;
                    Vector3 stand = v.position + v.forward * 6f;
                    r.Put?.Invoke(r.Body, stand);
                    r.Log($"TOWN clip vendor {Now}: put 6 m in front of {v.root.name} at {v.position}");
                    yield return WatchGestures(r, "6 m", 8f);
                    r.Put?.Invoke(r.Body, v.position + v.forward * 2.2f);
                    r.Log($"TOWN clip vendor close {Now}");
                    yield return WatchGestures(r, "in reach", 8f);
                    yield return Look(r, "vendor", v.position + Vector3.up * 1.5f);
                    r.Log("TOWN vendors in step? " + Now + ": " + VendorStep());
                }
                ok = true;
            }
            finally
            {
                Preset("High");
                r.Log($"TOWN end {Now}: {(ok ? "DONE" : "NOT DONE")}");
                r.Verdict?.Invoke(ok);
            }
        }
    }
}
