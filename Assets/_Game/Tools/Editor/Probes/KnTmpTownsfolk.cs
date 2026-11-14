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

                r.Log($"TOWN clip crowd {Now} (8 s, High, no labels)");
                yield return new WaitForSeconds(8f);
                r.Log($"TOWN clip labels {Now} (6 s): " + Gm("gm.town.debug true"));
                yield return new WaitForSeconds(6f);
                Gm("gm.town.debug false");

                Preset("High");
                yield return new WaitForSeconds(3f);
                yield return Measure(r, "High", 10f);
                Preset("Low");
                r.Log($"TOWN clip Low {Now}");
                yield return new WaitForSeconds(3f);
                yield return Measure(r, "Low", 10f);
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
                    yield return new WaitForSeconds(5f);
                    r.Put?.Invoke(r.Body, v.position + v.forward * 2.2f);
                    r.Log($"TOWN clip vendor close {Now}");
                    yield return new WaitForSeconds(5f);
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
