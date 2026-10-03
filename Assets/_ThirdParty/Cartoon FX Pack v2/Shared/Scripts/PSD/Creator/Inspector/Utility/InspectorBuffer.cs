using UnityEngine;
using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Utility
{
#if UNITY_EDITOR
    internal static class DragBuffer
    {
        private static readonly Dictionary<int, float> Float = new Dictionary<int, float>();
        private static readonly Dictionary<int, Vector2> Vec2 = new Dictionary<int, Vector2>();

        public static float Slider(float value, float min, float max)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            bool dragging = Float.TryGetValue(id, out float buffered);
            float display = dragging ? buffered : value;

            EditorGUI.BeginChangeCheck();
            float newVal = EditorGUILayout.Slider(display, min, max);
            if (EditorGUI.EndChangeCheck())
            {
                Float[id] = newVal;
                if (GUIUtility.hotControl == 0) { Float.Remove(id); GUI.changed = true; return newVal; }
                GUI.changed = false;
                return value;
            }

            if (dragging && GUIUtility.hotControl == 0)
            {
                Float.Remove(id);
                GUI.changed = true;
                return buffered;
            }

            GUI.changed = false;
            return value;
        }

        public static Vector2 MinMaxSlider(Vector2 value, float min, float max, out Vector2 display)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            bool dragging = Vec2.TryGetValue(id, out Vector2 buffered);
            display = dragging ? buffered : value;

            float dispMin = display.x, dispMax = display.y;

            EditorGUI.BeginChangeCheck();
            EditorGUILayout.MinMaxSlider(ref dispMin, ref dispMax, min, max);
            if (EditorGUI.EndChangeCheck())
            {
                var newVal = new Vector2(dispMin, dispMax);
                Vec2[id] = newVal;
                display = newVal;
                if (GUIUtility.hotControl == 0)
                {
                    Vec2.Remove(id);
                    GUI.changed = true;
                    return newVal;
                }
                GUI.changed = false;
                return value;
            }

            if (dragging && GUIUtility.hotControl == 0)
            {
                Vec2.Remove(id);
                GUI.changed = true;
                return buffered;
            }

            GUI.changed = false;
            return value;
        }
    }

    internal static class GradientClipboard
    {
        public static void Copy(Gradient g) => GUIUtility.systemCopyBuffer = JsonUtility.ToJson(GradientData.From(g));

        public static bool TryPaste(out Gradient gradient)
        {
            gradient = null;
            if (string.IsNullOrEmpty(GUIUtility.systemCopyBuffer)) return false;
            try
            {
                var data = JsonUtility.FromJson<GradientData>(GUIUtility.systemCopyBuffer);
                if (data?.colorKeys == null || data.alphaKeys == null) return false;
                var g = new Gradient();
                g.SetKeys(
                    Array.ConvertAll(data.colorKeys, k => new GradientColorKey(new Color(k.r, k.g, k.b, k.a), k.time)),
                    Array.ConvertAll(data.alphaKeys, k => new GradientAlphaKey(k.alpha, k.time))
                );
                g.mode = (GradientMode)data.mode;
                gradient = g;
                return true;
            }
            catch { return false; }
        }

        public static bool CanPaste() => TryPaste(out _);

        [Serializable]
        private class GradientData
        {
            public ColorKey[] colorKeys;
            public AlphaKey[] alphaKeys;
            public int mode;

            public static GradientData From(Gradient g) => new GradientData
            {
                colorKeys = Array.ConvertAll(g.colorKeys, k => new ColorKey { r = k.color.r, g = k.color.g, b = k.color.b, a = k.color.a, time = k.time }),
                alphaKeys = Array.ConvertAll(g.alphaKeys, k => new AlphaKey { alpha = k.alpha, time = k.time }),
                mode = (int)g.mode
            };

            [Serializable] public class ColorKey { public float r, g, b, a, time; }
            [Serializable] public class AlphaKey { public float alpha, time; }
        }
    }
#endif
}