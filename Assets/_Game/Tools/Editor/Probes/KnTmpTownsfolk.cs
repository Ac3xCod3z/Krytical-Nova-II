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
                yield return new WaitForSeconds(15f);   // 4 a second come out: 48 in ~12 s
                r.Log($"TOWN status {Now}: " + Gm("gm.town") + $" | view bodies {Stat("ShownNow")}, cards {Stat("CardsNow")}, looks {Stat("LooksNow")}, vendors dressed {Vendors()}");

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
