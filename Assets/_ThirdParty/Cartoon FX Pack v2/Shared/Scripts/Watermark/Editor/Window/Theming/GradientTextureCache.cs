using System.Collections.Generic;
using UnityEngine;

namespace laplahce.Watermark.Theming
{
    public sealed class GradientTextureCache
    {
        private const int DEFAULT_WIDTH = 128;

        private readonly Dictionary<(Color, Color), Texture2D> cache = new Dictionary<(Color, Color), Texture2D>();

        public Texture2D Get(Color left, Color right)
        {
            var key = (left, right);
            if (cache.TryGetValue(key, out var existing) && existing != null) return existing;

            var texture = Build(left, right);
            cache[key] = texture;
            return texture;
        }
        public Texture2D Get(Color color) => Get(color, color);

        public void Dispose()
        {
            foreach (var texture in cache.Values)
                if (texture != null) Object.DestroyImmediate(texture);
            cache.Clear();
        }

        private static Texture2D Build(Color left, Color right)
        {
            var texture = new Texture2D(DEFAULT_WIDTH, 1, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[DEFAULT_WIDTH];
            for (int x = 0; x < DEFAULT_WIDTH; x++)
                pixels[x] = Color.Lerp(left, right, x / (float)(DEFAULT_WIDTH - 1));

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
