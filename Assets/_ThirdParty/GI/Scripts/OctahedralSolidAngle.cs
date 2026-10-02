using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    public static class OctahedralSolidAngle
    {
        private static Dictionary<int, Texture2D> _map = new Dictionary<int, Texture2D>();

        public static Texture2D Get(int res)
        {
            if (_map.ContainsKey(res))
                return _map[res];

            var name = "OctahedralSolidAngle" + res;
            var texture = Resources.Load<Texture2D>(name);

            if (texture == null)
            {
                texture = build(res);

                // needs to be updated manually in unity to be 16bit
                var data = texture.EncodeToPNG();
                var path = Application.dataPath + "/GI/Resources/" + name + ".png";
                File.WriteAllBytes(path, data);
            }

            _map[res] = texture;

            Get(32);

            return texture;
        }

        private static Texture2D build(int res)
        {
            var data = new ushort[res * res];

            for (int y = 0; y < res; y++)
                for (int x = 0; x < res; x++)
                {
                    float value = octahedralSolidAngle(new Vector2(x + 0.5f, y + 0.5f) / (float)res);
                    data[x + res * y] = (ushort)(value * ushort.MaxValue);
                }

            var texture = new Texture2D(res, res, UnityEngine.Experimental.Rendering.GraphicsFormat.R16_UNorm, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
            texture.SetPixelData(data, 0);
            texture.Apply();

            return texture;
        }

        private static Vector3 octahedralMapToDirection(Vector2 uv)
        {
            uv = new Vector2(uv.x * 2.0f - 1.0f, uv.y * 2.0f - 1.0f);

            Vector3 vec = new Vector3(uv.x, 1.0f - Mathf.Abs(uv.x) - Mathf.Abs(uv.y), uv.y);
            float t = Mathf.Clamp01(-vec.y);

            if (vec.x >= 0.0)
                vec.x -= t;
            else
                vec.x += t;

            if (vec.z >= 0.0)
                vec.z -= t;
            else
                vec.z += t;

            return vec.normalized;
        }

        private static float sphericalExcess(Vector3 a, Vector3 b, Vector3 c)
        {
            float cos_ab = Vector3.Dot(a, b);
            float sin_ab = 1.0f - cos_ab * cos_ab;
            float cos_bc = Vector3.Dot(b, c);
            float sin_bc = 1.0f - cos_bc * cos_bc;
            float cos_ca = Vector3.Dot(c, a);
            float cos_c = cos_ca - cos_ab * cos_bc;
            float sin_c = Mathf.Sqrt(sin_ab * sin_bc - cos_c * cos_c);
            float inv = (1.0f - cos_ab) * (1.0f - cos_bc);

            return 2.0f * Mathf.Atan2(sin_c, Mathf.Sqrt((sin_ab * sin_bc * (1.0f + cos_bc) * (1.0f + cos_ab)) / inv) + cos_c);
        }

        private static float octahedralSolidAngle(Vector2 texcoord)
        {
            Vector3 direction10 = octahedralMapToDirection(texcoord + new Vector2(0.5f, -0.5f) / (float)Defines.PROBE_RES);
            Vector3 direction01 = octahedralMapToDirection(texcoord + new Vector2(-0.5f, 0.5f) / (float)Defines.PROBE_RES);

            float solid_angle0 = sphericalExcess(
                octahedralMapToDirection(texcoord + new Vector2(-.5f, -.5f) / (float)Defines.PROBE_RES),
                direction10,
                direction01);

            float solid_angle1 = sphericalExcess(
                octahedralMapToDirection(texcoord + new Vector2(.5f, .5f) / (float)Defines.PROBE_RES),
                direction01,
                direction10);

            return solid_angle0 + solid_angle1;
        }
    }
}