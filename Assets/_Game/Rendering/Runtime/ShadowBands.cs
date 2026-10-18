using System.Collections.Generic;
using KryticalNova.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Fewer shadow draws on the lower presets (batching pass, 18 Oct 2026): every caster is drawn again into each
    /// shadow cascade, and Zone01 has about 7,500 of them. World meshes the LOD builder sorted (<see cref="GeneratedLod"/>)
    /// that are smaller than the preset's <see cref="WorldLodSettings.Tier.noShadowBelowSize"/> cast none; High and
    /// Ultra keep every shadow. What each renderer cast before is kept and put back when the band lifts, when
    /// <see cref="Enabled"/> is switched off (<c>gm.batching shadows off</c>, to measure) and when the session ends, so
    /// a Play session never leaves a scene changed.
    /// </summary>
    public static class ShadowBands
    {
        private static readonly Dictionary<Renderer, ShadowCastingMode> Original = new Dictionary<Renderer, ShadowCastingMode>();
        private static readonly List<Renderer> Found = new List<Renderer>(16);
        private static float belowSize;
        private static bool enabled = true, hooked;

        /// <summary>On as the game ships; off draws every shadow whatever the preset.</summary>
        public static bool Enabled
        {
            get => enabled;
            set { enabled = value; Refresh(); }
        }

        /// <summary>Renderers that cast no shadow now because of the band.</summary>
        public static int Lifted { get; private set; }

        /// <summary>The preset's size, in metres (0 = every shadow). Applied now and to every scene loaded after.</summary>
        public static void SetBelowSize(float metres)
        {
            if (!Application.isPlaying) return;
            if (!hooked)
            {
                SceneManager.sceneLoaded += (scene, mode) => Refresh();
                hooked = true;
            }
            if (Mathf.Approximately(metres, belowSize) && Lifted > 0) return;
            belowSize = Mathf.Max(0f, metres);
            Refresh();
        }

        /// <summary>Every renderer back to what it cast before. The session's end calls it.</summary>
        public static void RestoreAll()
        {
            foreach (KeyValuePair<Renderer, ShadowCastingMode> pair in Original)
                if (pair.Key != null) pair.Key.shadowCastingMode = pair.Value;
            Original.Clear();
            Lifted = 0;
        }

        private static void Refresh()
        {
            if (!Application.isPlaying) return;
            using var timed = PerformanceLog.Time("shadow.bands");
            float limit = enabled ? belowSize : 0f;
            int lifted = 0;
            foreach (GeneratedLod lod in Object.FindObjectsByType<GeneratedLod>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                lod.GetComponentsInChildren(true, Found);
                foreach (Renderer r in Found)
                {
                    if (!(r is MeshRenderer)) continue;
                    bool known = Original.TryGetValue(r, out ShadowCastingMode before);
                    if (!known) before = r.shadowCastingMode;
                    if (before == ShadowCastingMode.Off) continue;
                    Vector3 size = r.bounds.size;
                    bool lift = limit > 0f && Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < limit;
                    if (lift)
                    {
                        if (!known) Original[r] = before;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        lifted++;
                    }
                    else if (known)
                    {
                        r.shadowCastingMode = before;
                        Original.Remove(r);
                    }
                }
            }
            Lifted = lifted;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Original.Clear();
            belowSize = 0f;
            enabled = true;
            Lifted = 0;
        }
    }
}
