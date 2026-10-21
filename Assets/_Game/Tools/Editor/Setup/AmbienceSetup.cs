using System;
using System.Collections.Generic;
using System.IO;
using KryticalNova.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The zones' ambience (Sound Bible, section 5). The music-gen skill's ambience_bake.py bakes seamless loops into
    /// <see cref="AmbienceFolder"/> and writes, per zone, its beds (2D, always / day / night) and its points (3D loops
    /// on scene objects, found by an anchor rule) into the sound-scout skill's ambience.json. This makes each zone's
    /// <see cref="ZoneAmbience"/> asset, and in each zone scene a "Zone Ambience" object with the player and, under
    /// it, one <see cref="AmbienceEmitter"/> on every anchor. Safe to run again: the emitters are made afresh, the
    /// rest of the scene is untouched.
    /// </summary>
    public static class AmbienceSetup
    {
        public const string AmbienceFolder = "Assets/_Game/Audio/Ambience/Zones";
        private const string ManifestPath = ".claude/skills/sound-scout/ambience.json";
        private const string RootName = "Zone Ambience";
        private const string EmittersName = "Emitters";

        [Serializable] private sealed class Manifest { public List<ZoneRow> zones = new List<ZoneRow>(); }

        [Serializable]
        private sealed class ZoneRow
        {
            public string name, scene;
            public List<BedRow> beds = new List<BedRow>();
            public List<PointRow> points = new List<PointRow>();
        }

        [Serializable] private sealed class BedRow { public string name, file, when; public float volume = 1f; }

        [Serializable]
        private sealed class PointRow
        {
            public string name, file;
            public float volume = 1f, minDistance = 5f, maxDistance = 40f;
            public List<string> anchorNames = new List<string>();
            public string anchorParent, anchorPrefix, anchorContains;
        }

        public static string AssetPath(string zone) => $"{MusicSetup.DataFolder}/ZoneAmbience_{zone}.asset";

        [MenuItem("Krytical Nova/Setup/Ambience", priority = 15)]
        public static string BuildAll()
        {
            if (!File.Exists(ManifestPath)) return $"No {ManifestPath}: bake with the music-gen skill's ambience_bake.py first.";
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            AssetDatabase.Refresh();
            AudioSetup.ApplyImportSettings();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Cancelled.";
            string back = EditorSceneManager.GetActiveScene().path;
            var report = new List<string>();
            foreach (ZoneRow zone in manifest.zones) report.Add(BuildZone(zone));
            if (!string.IsNullOrEmpty(back)) EditorSceneManager.OpenScene(back, OpenSceneMode.Single);
            string all = string.Join(" | ", report);
            Debug.Log("[Ambience] " + all);
            return all;
        }

        private static string BuildZone(ZoneRow zone)
        {
            // The asset: the beds.
            var asset = AssetDatabase.LoadAssetAtPath<ZoneAmbience>(AssetPath(zone.name));
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<ZoneAmbience>();
                AssetDatabase.CreateAsset(asset, AssetPath(zone.name));
            }
            var beds = new List<ZoneAmbience.Bed>();
            foreach (BedRow row in zone.beds)
            {
                AudioClip clip = Clip(row.file);
                if (clip == null) continue;
                beds.Add(new ZoneAmbience.Bed { clip = clip, volume = row.volume, when = When(row.when) });
            }
            asset.beds = beds.ToArray();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            // The scene: the player and the emitters.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(zone.scene) == null) return $"{zone.name}: no scene at {zone.scene}";
            Scene scene = EditorSceneManager.OpenScene(zone.scene, OpenSceneMode.Single);
            GameObject root = null;
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == RootName) root = go;
            if (root == null) root = new GameObject(RootName);
            AudioMixerGroup group = AmbienceGroup();
            if (!root.TryGetComponent(out ZoneAmbiencePlayer player)) player = root.AddComponent<ZoneAmbiencePlayer>();
            Assign(player, "ambience", asset);
            Assign(player, "output", group);

            Transform emitters = root.transform.Find(EmittersName);
            if (emitters != null) UnityEngine.Object.DestroyImmediate(emitters.gameObject);
            emitters = new GameObject(EmittersName).transform;
            emitters.SetParent(root.transform, false);
            var placed = new List<string>();
            foreach (PointRow row in zone.points)
            {
                AudioClip clip = Clip(row.file);
                if (clip == null) continue;
                List<Transform> anchors = Anchors(scene, row);
                var group2 = new GameObject(row.name).transform;
                group2.SetParent(emitters, false);
                int n = 0;
                foreach (Transform anchor in anchors)
                {
                    var go = new GameObject($"{row.name} {++n:00}");
                    go.transform.SetParent(group2, false);
                    go.transform.position = CentreOf(anchor);
                    var source = go.AddComponent<AudioSource>();
                    source.clip = clip;
                    source.loop = true;
                    source.playOnAwake = false;
                    source.volume = row.volume;
                    source.spatialBlend = 1f;
                    source.rolloffMode = AudioRolloffMode.Logarithmic;
                    source.minDistance = row.minDistance;
                    source.maxDistance = row.maxDistance;
                    source.dopplerLevel = 0f;
                    source.priority = 160;
                    source.outputAudioMixerGroup = group;
                    go.AddComponent<AmbienceEmitter>();
                }
                placed.Add($"{row.name} x{n}");
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return $"{zone.name}: {beds.Count} bed(s), {string.Join(", ", placed)}";
        }

        // SerializedProperty dropped a cross-assembly reference without a word on 21 Oct 2026 (MenuMusicSetup), so
        // the fields are set directly and checked.
        private static void Assign(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var info = target.GetType().GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            info.SetValue(target, value);
            EditorUtility.SetDirty(target);
            if (!ReferenceEquals(info.GetValue(target), value)) Debug.LogError($"[Ambience] {target.name}.{field} did not take {value}.");
        }

        private static List<Transform> Anchors(Scene scene, PointRow row)
        {
            Transform own = RootNameTransform(scene);
            var found = new List<Transform>();
            foreach (GameObject top in scene.GetRootGameObjects())
                foreach (Transform t in top.GetComponentsInChildren<Transform>(true))
                {
                    if (own != null && t.IsChildOf(own)) continue;
                    bool hit = row.anchorNames != null && row.anchorNames.Contains(t.name)
                               || !string.IsNullOrEmpty(row.anchorContains) && t.name.Contains(row.anchorContains)
                               || !string.IsNullOrEmpty(row.anchorParent) && t.parent != null && t.parent.name == row.anchorParent
                                  && (string.IsNullOrEmpty(row.anchorPrefix) || t.name.StartsWith(row.anchorPrefix));
                    if (hit) found.Add(t);
                }
            // A match inside another match (a billboard's own LOD child) is the same thing: one sound for it.
            found.RemoveAll(t => found.Exists(other => other != t && t.IsChildOf(other)));
            return found;
        }

        private static Transform RootNameTransform(Scene scene)
        {
            foreach (GameObject go in scene.GetRootGameObjects())
                if (go.name == RootName) return go.transform;
            return null;
        }

        /// <summary>Where a thing's sound comes from: the middle of what is drawn of it, or its position.</summary>
        private static Vector3 CentreOf(Transform anchor)
        {
            var renderers = anchor.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return anchor.position;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
            return bounds.center;
        }

        private static AudioClip Clip(string file)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{AmbienceFolder}/{file}");
            if (clip == null) Debug.LogWarning($"[Ambience] No clip at {AmbienceFolder}/{file}.");
            return clip;
        }

        private static AmbienceTime When(string when)
        {
            switch ((when ?? "").ToLowerInvariant())
            {
                case "day": return AmbienceTime.Day;
                case "night": return AmbienceTime.Night;
                default: return AmbienceTime.Always;
            }
        }

        private static AudioMixerGroup AmbienceGroup()
        {
            AudioMixer mixer = AudioSetup.EnsureMixer();
            AudioMixerGroup[] groups = mixer != null ? mixer.FindMatchingGroups("Ambience") : new AudioMixerGroup[0];
            return groups.Length > 0 ? groups[0] : null;
        }
    }
}
