using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector.Utility
{
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    internal static class IconCache
    {
        private static readonly Dictionary<string, Texture2D> Cache = new();

        static IconCache()
        {
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
#endif
        }

        public static Texture2D Get(string resourcePath)
        {
            if (Cache.TryGetValue(resourcePath, out var tex) && tex != null) return tex;

            tex = Resources.Load<Texture2D>(resourcePath);
            Cache[resourcePath] = tex;
            return tex;
        }

        public static GUIContent Content(string resourcePath, string tooltip = null)
        {
            var tex = Get(resourcePath);
            return tooltip != null ? new GUIContent(tex, tooltip) : new GUIContent(tex);
        }

        private static void Clear()
        {
            Cache.Clear();
#if UNITY_EDITOR
            AssemblyReloadEvents.beforeAssemblyReload -= Clear;
#endif
        }
    }
}