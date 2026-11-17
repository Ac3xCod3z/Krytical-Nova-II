using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.EditorTools.Crucible
{
    /// <summary>
    /// The Crucible's baked light (Master's pick 2, 17 Nov 2026): Zone01's global probe volume is pinned to the exact box it covers
    /// today (Local mode, same centre and size), so its probes do not move; a local volume covers the Crucible; then ONE Day + Night
    /// bake (LightingSetup's scenarios, street lamps on only at night). Town pictures at three spots, day and night, are taken before
    /// and after so any change in Zone01's look can be seen. Writes Library/KryticalNova.CrucibleLighting.txt.
    /// </summary>
    public static class CrucibleLighting
    {
        const string Day = "Day", Night = "Night";
        public static readonly Vector3 VolumeCentre = new Vector3(0f, 8f, 0f), VolumeSize = new Vector3(340f, 50f, 340f);

        public static void BakeBatch()
        {
            string folder = "Library/CrucibleLighting";
            string[] a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-crucibleShots") folder = a[i + 1];
            string r;
            try { r = Bake(folder); }
            catch (System.Exception e) { Debug.LogError("[Crucible] " + e); EditorApplication.Exit(1); return; }
            Debug.Log(r);
            EditorApplication.Exit(r.Contains("FAIL") ? 1 : 0);
        }

        public static string Bake(string folder)
        {
            var log = new StringBuilder("[Crucible] lighting\n");
            Directory.CreateDirectory(folder);
            var scene = EditorSceneManager.OpenScene(CrucibleBuilder.Zone01Path, OpenSceneMode.Single);
            GameObject crucible = scene.GetRootGameObjects().FirstOrDefault(g => g.name == CrucibleBuilder.RootName);
            if (crucible == null) return "FAIL: no Crucible in Zone01";

            TownShots(folder, "before", log);

            // Zone01's own volume: pinned where it is (a Global volume would grow to take in a map 3 km out)
            ProbeVolume town = Object.FindObjectsByType<ProbeVolume>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(v => !v.transform.IsChildOf(crucible.transform));
            if (town == null) return "FAIL: Zone01 has no probe volume";
            log.AppendLine($"Zone01 volume was {town.mode} at {town.transform.position} size {town.size}.");
            if (town.mode == ProbeVolume.Mode.Global) { town.mode = ProbeVolume.Mode.Local; EditorUtility.SetDirty(town); }

            Transform t = crucible.transform.Find("Probe Volume (Crucible)");
            if (t == null) { t = new GameObject("Probe Volume (Crucible)").transform; t.SetParent(crucible.transform, false); }
            t.localPosition = VolumeCentre;
            var vol = t.GetComponent<ProbeVolume>() ?? t.gameObject.AddComponent<ProbeVolume>();
            vol.mode = ProbeVolume.Mode.Local;
            vol.size = VolumeSize;
            log.AppendLine($"Crucible volume: local {VolumeSize} at {t.position}.");

            // the same two scenarios Zone01 bakes, one after the other, synchronously (batch)
            MethodInfo setLamps = typeof(LightingSetup).GetMethod("SetLamps", BindingFlags.NonPublic | BindingFlags.Static);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (string scenario in new[] { Day, Night })
            {
                setLamps?.Invoke(null, new object[] { scenario == Night });
                ProbeReferenceVolume.instance.lightingScenario = scenario;
                bool ok = Lightmapping.Bake();
                log.AppendLine($"Bake {scenario}: {(ok ? "done" : "FAIL")} at {watch.Elapsed.TotalMinutes:F1} min.");
                if (!ok) return log.ToString();
            }
            setLamps?.Invoke(null, new object[] { true });
            ProbeReferenceVolume.instance.lightingScenario = Day;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            long bytes = Directory.GetFiles(Path.GetDirectoryName(CrucibleBuilder.Zone01Path), "APV_Zone01_StarterTown*")
                .Where(f => !f.EndsWith(".meta")).Sum(f => new FileInfo(f).Length);
            log.AppendLine($"APV data now {bytes / 1048576f:F1} MB (was about 38 MB).");

            TownShots(folder, "after", log);
            File.WriteAllText("Library/KryticalNova.CrucibleLighting.txt", log.ToString());
            return log.ToString();
        }

        /// <summary>Three town spots (the plaza, the Sky Lift by the Marshal, a street), at noon and at midnight.</summary>
        static void TownShots(string folder, string tag, StringBuilder log)
        {
            var npcs = Object.FindObjectsByType<KryticalNova.NPC.NpcInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Vector3 plaza = npcs.Length > 0 ? npcs.Aggregate(Vector3.zero, (s, n) => s + n.transform.position) / npcs.Length : Vector3.zero;
            var marshal = npcs.FirstOrDefault(n => n.Definition != null && n.Definition.name == "NPC_GuardCaptain");
            Vector3 lift = marshal != null ? marshal.transform.position : plaza + new Vector3(20f, 0f, 0f);
            Vector3 street = SurfaceGraphBuilder.TestRouteFrom;
            var spots = new[] { ("plaza", plaza), ("skylift", lift), ("street", street) };

            var env = Object.FindAnyObjectByType<KryticalNova.World.EnvironmentPresenter>();
            var camGo = new GameObject("[town check camera]") { hideFlags = HideFlags.HideAndDontSave };
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f; cam.farClipPlane = 1500f;
            var rt = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            foreach ((string when, float hours, string scenario) in new[] { ("day", 12f, Day), ("night", 0f, Night) })
            {
                Preview(env, hours);
                if (ProbeReferenceVolume.instance.isInitialized) ProbeReferenceVolume.instance.lightingScenario = scenario;
                foreach ((string name, Vector3 at) in spots)
                {
                    Vector3 eye = at + new Vector3(-14f, 7f, -14f);
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at + Vector3.up * 1.5f - eye));
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                    tex.Apply();
                    File.WriteAllBytes(Path.Combine(folder, $"town_{name}_{when}_{tag}.png"), tex.EncodeToPNG());
                }
            }
            RenderTexture.active = null;
            Object.DestroyImmediate(camGo); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            log.AppendLine($"Town pictures ({tag}): plaza {plaza}, Sky Lift {lift}, street {street}, day + night.");
        }

        static void Preview(KryticalNova.World.EnvironmentPresenter env, float hours)
        {
            if (env == null) return;
            var so = new SerializedObject(env);
            so.FindProperty("previewHours").floatValue = hours;
            so.ApplyModifiedPropertiesWithoutUndo();
            typeof(KryticalNova.World.EnvironmentPresenter).GetMethod("ApplyPreview", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(env, null);
        }
    }
}
