using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The concept art of a map is taken in the built map itself: every camera under "--- Zone Build --- / Concept
    /// Shots" is rendered through the game's own renderer (so the comic ink and dots are on, as in play) and written
    /// as a PNG into the place's <c>Concept/</c> folder. A shot named Vista is wide (the Figma painting's ratio), a
    /// shot with an orthographic camera is the layout picture (square), the rest are 16:9.
    /// </summary>
    public static class ZoneShotCapture
    {
        [MenuItem("Krytical Nova/World/Render Concept Shots Of Open Map")]
        public static void CaptureMenu() => Debug.Log("[World] " + Capture());

        public static string Capture()
        {
            Scene scene = SceneManager.GetActiveScene();
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == ZoneMapBuilder.RootName);
            Transform shots = root != null ? root.transform.Find(ZoneMapBuilder.ShotsName) : null;
            if (shots == null) return $"{scene.name} has no concept shots.";
            string folder = ConceptFolder(scene.name);
            WeatherVfxBuilder.EnsureFolder(folder);
            var log = new StringBuilder();
            foreach (Camera cam in shots.GetComponentsInChildren<Camera>(true))
            {
                string shot = cam.name.Replace("Shot: ", string.Empty);
                int w = 1920, h = 1080;
                if (cam.orthographic) { w = 2048; h = 2048; }
                else if (shot == "Vista") { w = 2560; h = 1004; }
                string path = $"{folder}/{Place(scene.name)}_{shot.Replace(' ', '_')}.png";
                Render(cam, w, h, path);
                log.AppendLine(path);
            }
            AssetDatabase.Refresh();
            // Rendering sets the shot cameras up for the capture (aspect, post-processing): keep the map saved.
            if (scene.isDirty) UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            return log.ToString();
        }

        /// <summary>The place's concept folder, e.g. Zones/MeridianSprawl/HomeCity_NewMeridian/Concept.</summary>
        public static string ConceptFolder(string sceneName)
        {
            string region = sceneName == "TheScar" ? "TheScar" : sceneName.Split('_')[0];
            string place = sceneName == "TheScar" ? "TheScar" : sceneName.Substring(region.Length + 1);
            string folder = $"{ZoneKitBuilder.ZonesRoot}/{region}/{place}";
            if (!AssetDatabase.IsValidFolder(folder)) folder = $"{ZoneKitBuilder.ZonesRoot}/{region}/HomeCity_{place}";
            return folder + "/Concept";
        }

        private static string Place(string sceneName) => sceneName;

        private static void Render(Camera cam, int w, int h, string path)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cam.aspect = w / (float)h;
            cam.targetTexture = rt;
            bool was = cam.enabled;
            cam.enabled = true;
            cam.Render();
            cam.enabled = was;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
    }
}
