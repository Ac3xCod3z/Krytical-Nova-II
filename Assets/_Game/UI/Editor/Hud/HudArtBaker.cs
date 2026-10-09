using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Draws a uGUI prefab (a Synty interface piece, or one composed from them) into a picture with nothing round
    /// it. Two uses: looking at a piece while choosing and composing, and baking the parts of a faction's interface
    /// that UI Toolkit shows as one image (a minimap dial's frame, a window's crest). The prefab is only instanced
    /// in a preview scene; nothing of it is saved or changed.
    /// </summary>
    public static class HudArtBaker
    {
        /// <summary>
        /// The renderer the bake camera uses: one that draws transparents. Its features (the comic ink and dots) are
        /// switched off while a piece is drawn and put back after; the asset is not saved.
        /// </summary>
        private const string RendererName = "UIPreview_Renderer";
        private const int LargestSide = 4096;

        /// <summary>
        /// The piece as a picture with true transparency. <paramref name="pixelsPerUnit"/> is picture pixels per
        /// canvas unit (the packs are drawn for a 3840 x 2160 canvas: 0.5 gives the size they have at 1080p).
        /// <paramref name="prepare"/> may switch children off or fill in sample values before it is drawn.
        /// </summary>
        public static Texture2D Render(GameObject prefab, float pixelsPerUnit, Action<GameObject> prepare, out Rect drawn, float padding = 8f)
        {
            drawn = default;
            if (prefab == null) return null;
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture target = null;
            int renderer = RendererIndex(RendererName);
            var features = new System.Collections.Generic.List<ScriptableRendererFeature>();
            if (renderer >= 0 && GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
                foreach (ScriptableRendererFeature feature in pipeline.rendererDataList[renderer].rendererFeatures)
                    if (feature != null && feature.isActive) features.Add(feature);
            try
            {
                foreach (ScriptableRendererFeature feature in features) feature.SetActive(false);
                var canvasGo = new GameObject("Bake Canvas", typeof(RectTransform));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasGo, scene);
                var canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                var canvasRect = (RectTransform)canvasGo.transform;
                canvasRect.sizeDelta = new Vector2(3840f, 2160f);

                var piece = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                piece.transform.SetParent(canvasRect, false);
                piece.SetActive(true);
                prepare?.Invoke(piece);
                Canvas.ForceUpdateCanvases();
                Canvas.ForceUpdateCanvases(); // layout groups inside layout groups settle on the second pass

                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRect, piece.transform);
                bounds.Expand(new Vector3(padding * 2f, padding * 2f, 0f));
                drawn = new Rect(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y);
                float scale = Mathf.Min(pixelsPerUnit, LargestSide / Mathf.Max(bounds.size.x, bounds.size.y));
                int width = Mathf.Max(4, Mathf.CeilToInt(bounds.size.x * scale)), height = Mathf.Max(4, Mathf.CeilToInt(bounds.size.y * scale));

                var cameraGo = new GameObject("Bake Camera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraGo, scene);
                var camera = cameraGo.AddComponent<Camera>();
                camera.scene = scene; // a game camera: preview cameras are given no canvases to draw
                camera.orthographic = true;
                camera.orthographicSize = bounds.size.y * 0.5f;
                camera.aspect = bounds.size.x / bounds.size.y;
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = 2000f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.allowHDR = false;
                camera.transform.position = canvasRect.TransformPoint(new Vector3(bounds.center.x, bounds.center.y, 0f)) - canvasRect.forward * 1000f;
                camera.transform.rotation = canvasRect.rotation;
                var data = cameraGo.AddComponent<UniversalAdditionalCameraData>();
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.antialiasing = AntialiasingMode.None;
                if (renderer >= 0) data.SetRenderer(renderer);
                canvas.worldCamera = camera;

                // Drawn twice, over black and over white: what differs between the two is how see-through a
                // pixel is, exactly, whatever the piece's own blending did.
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 1 };
                camera.targetTexture = target;
                Color[] onBlack = Draw(camera, target, Color.black);
                Color[] onWhite = Draw(camera, target, Color.white);
                camera.targetTexture = null;

                var pixels = new Color[onBlack.Length];
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color b = onBlack[i], w = onWhite[i];
                    float alpha = Mathf.Clamp01(1f - ((w.r - b.r) + (w.g - b.g) + (w.b - b.b)) / 3f);
                    if (alpha <= 0.002f) { pixels[i] = new Color(0f, 0f, 0f, 0f); continue; }
                    pixels[i] = new Color(
                        Mathf.LinearToGammaSpace(Mathf.Clamp01(b.r / alpha)),
                        Mathf.LinearToGammaSpace(Mathf.Clamp01(b.g / alpha)),
                        Mathf.LinearToGammaSpace(Mathf.Clamp01(b.b / alpha)), alpha);
                }
                var picture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                picture.SetPixels(pixels);
                picture.Apply();
                return picture;
            }
            finally
            {
                foreach (ScriptableRendererFeature feature in features) feature.SetActive(true);
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>Bakes the piece to a PNG file (outside the project for looking at, or under Assets as art).</summary>
        public static bool Bake(GameObject prefab, string pngPath, float pixelsPerUnit, Action<GameObject> prepare = null)
        {
            Texture2D picture = Render(prefab, pixelsPerUnit, prepare, out _);
            if (picture == null) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(pngPath));
            File.WriteAllBytes(pngPath, picture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(picture);
            return true;
        }

        private static Color[] Draw(Camera camera, RenderTexture target, Color background)
        {
            camera.backgroundColor = background;
            camera.Render();
            var read = new Texture2D(target.width, target.height, TextureFormat.RGBAHalf, false, true);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            Color[] pixels = read.GetPixels();
            UnityEngine.Object.DestroyImmediate(read);
            return pixels;
        }

        private static int RendererIndex(string rendererName)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                var list = urp.rendererDataList;
                for (int i = 0; i < list.Length; i++)
                    if (list[i] != null && list[i].name == rendererName) return i;
            }
            return -1;
        }
    }
}
