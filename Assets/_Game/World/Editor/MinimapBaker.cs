using System.IO;
using KryticalNova.World.Minimap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes the open zone into one top-down texture for the HUD minimap: an orthographic, north-up render of the
    /// terrain's footprint through the game's own renderer (so it carries the comic outlines and dotted shadows).
    /// The runtime minimap then just scrolls/rotates this texture: no extra camera, no RenderTexture.
    /// Re-bake after editing the zone.
    /// </summary>
    public static class MinimapBaker
    {
        private const int Resolution = 2048;
        private const string ZonesRoot = "Assets/_Game/World/Zones";

        [MenuItem("Krytical Nova/World/Bake Minimap (Open Scene)")]
        public static void Bake()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var terrain = Object.FindAnyObjectByType<Terrain>();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogError("[Minimap] No Terrain in the open scene; nothing to bake.");
                return;
            }

            Vector3 size = terrain.terrainData.size;
            Vector3 origin = terrain.transform.position;
            float worldSize = Mathf.Max(size.x, size.z);
            var center = new Vector2(origin.x + size.x * 0.5f, origin.z + size.z * 0.5f);
            float top = origin.y + size.y + 40f;

            string folder = $"{ZonesRoot}/{scene.name.Split('_')[0]}";
            WeatherVfxBuilder.EnsureFolder(folder);
            string pngPath = $"{folder}/Minimap_{scene.name}.png";
            string dataPath = $"{folder}/Minimap_{scene.name}.asset";

            RenderTopDown(new Vector3(center.x, top, center.y), worldSize, top - origin.y + 10f, pngPath);
            ImportMap(pngPath);

            var data = AssetDatabase.LoadAssetAtPath<ZoneMinimapData>(dataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<ZoneMinimapData>();
                AssetDatabase.CreateAsset(data, dataPath);
            }
            data.map = AssetDatabase.LoadAssetAtPath<Texture2D>(pngPath);
            data.worldCenter = center;
            data.worldSize = worldSize;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            var anchor = Object.FindAnyObjectByType<ZoneMinimapAnchor>(FindObjectsInactive.Include);
            if (anchor == null)
            {
                var go = new GameObject("Zone Minimap");
                Undo.RegisterCreatedObjectUndo(go, "Create Zone Minimap");
                anchor = go.AddComponent<ZoneMinimapAnchor>();
            }
            Undo.RecordObject(anchor, "Assign Minimap");
            anchor.EditorSetData(data);
            EditorUtility.SetDirty(anchor);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[Minimap] Baked {worldSize:0} m of '{scene.name}' into {pngPath}. Save the scene to keep the anchor.");
        }

        private static void RenderTopDown(Vector3 position, float worldSize, float depth, string pngPath)
        {
            var go = new GameObject("Minimap Bake Camera") { hideFlags = HideFlags.HideAndDontSave };
            var rt = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            bool fog = RenderSettings.fog;
            try
            {
                go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Vector3.down, Vector3.forward));
                var cam = go.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = worldSize * 0.5f;
                cam.aspect = 1f;
                cam.nearClipPlane = 0.5f;
                cam.farClipPlane = depth + 20f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.04f, 0.07f, 0.1f, 1f);
                // Leave out characters and UI-only layers; the HUD draws live markers for those.
                cam.cullingMask = ~LayerMask.GetMask("Ignore Raycast", "UI", "UIPreview");
                var data = go.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

                RenderSettings.fog = false;
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(Path.Combine(Directory.GetCurrentDirectory(), pngPath), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                RenderSettings.fog = fog;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(go);
            }
        }

        private static void ImportMap(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true; // the HUD shows it heavily minified; mips keep it clean
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = Resolution;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
    }
}
