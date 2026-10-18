using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KryticalNova.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Makes every place's kit (Design/Zone-Build-Kits.md): for each Synty prefab the doc names for a place, a prefab
    /// variant in <c>Zones/&lt;Region&gt;/&lt;Place&gt;/Kit/&lt;Group&gt;/</c> (or once in the region's <c>_RegionKit</c>
    /// when the whole region shares it), brought to world scale on its root by <see cref="WorldScaleSettings.Fit"/>,
    /// so anything placed from a kit is already the right size beside a person. The list is
    /// <c>World/Data/ZoneKits.json</c>, written from the doc; the vendor prefabs are never touched. A variant that
    /// exists is left alone, so a hand-made change to one survives a rebuild. Works a few prefabs per Editor tick and
    /// writes its progress to <see cref="ProgressPath"/>, so a call from outside the Editor returns at once.
    /// </summary>
    public static class ZoneKitBuilder
    {
        public const string KitsPath = "Assets/_Game/World/Data/ZoneKits.json";
        public const string ZonesRoot = "Assets/_Game/World/Zones";
        public const string ProgressPath = "Library/KryticalNova.ZoneKits.txt";
        private const int PerTick = 12;

        [Serializable] private sealed class KitPrefab { public string path, group, kind, named; }
        [Serializable] private sealed class KitPlace { public string id, region, folder, scene, title; public List<KitPrefab> prefabs = new List<KitPrefab>(); }
        [Serializable] private sealed class KitFile { public List<KitPlace> places = new List<KitPlace>(); }

        private struct Job { public KitPlace place; public KitPrefab prefab; }

        private static Queue<Job> queue;
        private static StringBuilder log;
        private static int made, kept, scaled, missing, total;
        private static Scene preview;
        private static WorldScaleSettings scale;

        [MenuItem("Krytical Nova/World/Build Zone Kits")]
        public static void BuildMenu() => Start();

        /// <summary>The kit folder of a place, e.g. <c>Zones/MeridianSprawl/Z1_TransitRow/Kit</c>.</summary>
        public static string KitFolder(string placeFolder) => $"{ZonesRoot}/{placeFolder}/Kit";

        public static void Start()
        {
            if (queue != null) { Debug.Log("[World] Zone kits are already being built."); return; }
            var file = Load();
            if (file == null) return;
            scale = WorldScaleBuilder.Load();
            queue = new Queue<Job>();
            log = new StringBuilder();
            made = kept = scaled = missing = 0;
            foreach (KitPlace place in file.places)
                foreach (KitPrefab prefab in place.prefabs)
                    queue.Enqueue(new Job { place = place, prefab = prefab });
            total = queue.Count;
            preview = EditorSceneManager.NewPreviewScene();
            Write($"started {DateTime.Now:HH:mm:ss}: {total} prefabs in {file.places.Count} places");
            EditorApplication.update += Tick;
        }

        private static KitFile Load()
        {
            if (!File.Exists(KitsPath)) { Debug.LogError($"[World] {KitsPath} is missing: write it from Zone-Build-Kits.md first."); return null; }
            return JsonUtility.FromJson<KitFile>(File.ReadAllText(KitsPath));
        }

        private static void Tick()
        {
            try
            {
                AssetDatabase.StartAssetEditing();
                for (int n = 0; n < PerTick && queue.Count > 0; n++) Make(queue.Dequeue());
            }
            finally { AssetDatabase.StopAssetEditing(); }

            if (queue.Count > 0)
            {
                if ((total - queue.Count) % 240 < PerTick) Write($"{total - queue.Count} / {total}");
                return;
            }
            EditorApplication.update -= Tick;
            EditorSceneManager.ClosePreviewScene(preview);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            queue = null;
            string summary = $"done {DateTime.Now:HH:mm:ss}: {made} made, {kept} already there, {scaled} brought to world scale, {missing} sources missing";
            Write(summary + "\n" + log);
            Debug.Log("[World] Zone kits " + summary);
        }

        private static void Make(Job job)
        {
            string name = Path.GetFileNameWithoutExtension(job.prefab.path);
            string folder = $"{KitFolder(job.place.folder)}/{job.prefab.group}";
            if (job.place.folder.EndsWith("/_RegionKit")) folder = $"{ZonesRoot}/{job.place.folder}/{job.prefab.group}";
            string target = $"{folder}/{name}.prefab";
            if (File.Exists(target)) { kept++; return; }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(job.prefab.path);
            if (source == null) { missing++; log.AppendLine($"missing source {job.prefab.path} ({job.place.id})"); return; }

            WeatherVfxBuilder.EnsureFolder(folder);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
            try
            {
                float factor = 1f;
                if (!string.IsNullOrEmpty(job.prefab.kind))
                {
                    Vector3 size = Size(instance);
                    factor = scale.Fit(job.prefab.kind, size);
                    if (!Mathf.Approximately(factor, 1f))
                    {
                        instance.transform.localScale = source.transform.localScale * factor;
                        scaled++;
                        log.AppendLine($"{name} ({job.prefab.kind}) {Measure(job.prefab.kind, size):0.0} m x {factor:0.00}");
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(instance, target);
                made++;
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        private static float Measure(string kind, Vector3 size) =>
            scale.TryGet(kind, out ScaleKind k) ? WorldScaleSettings.Measure(k.axis, size) : size.y;

        /// <summary>The model's size as it stands: every renderer's bounds together.</summary>
        public static Vector3 Size(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return Vector3.zero;
            Bounds b = renderers[0].bounds;
            foreach (Renderer r in renderers)
                if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
            return b.size;
        }

        private static void Write(string line)
        {
            File.WriteAllText(ProgressPath, line + Environment.NewLine);
        }
    }
}
