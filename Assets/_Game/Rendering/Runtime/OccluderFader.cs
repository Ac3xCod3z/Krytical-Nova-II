using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Dither-fades world props that stand between the local camera and the player (client-only, local camera only).
    /// Every <see cref="CheckInterval"/> it sphere-casts camera → player (chest and head). Hit props swap to cached
    /// "KryticalNova/Occluder Dither" clones of their materials and ease their per-renderer _Fade up; once clear they
    /// ease back and get their original materials. Only occluders pay anything; no per-tick allocations.
    /// Ground/terrain are never faded (only <see cref="occluderLayers"/> are tested).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OccluderFader : MonoBehaviour
    {
        private const float CheckInterval = 0.1f;
        private const float CastRadius = 0.35f;
        private const float FadedAmount = 0.82f;
        private const float FadeSeconds = 0.2f;
        private const float SeeThroughAmount = 0.3f;
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        [SerializeField] private LayerMask occluderLayers = 1; // Default
        [SerializeField] private Shader ditherShader;

        private sealed class Occluder
        {
            public Renderer[] renderers;
            public Material[][] originals;
            public Material[][] dithered;
            public float fade;
            public int seenTick;
            public bool swapped;
        }

        private Transform target;
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly Dictionary<Collider, Occluder> byCollider = new Dictionary<Collider, Occluder>();
        private readonly List<Occluder> active = new List<Occluder>();
        private readonly Dictionary<Material, Material> clones = new Dictionary<Material, Material>();
        private MaterialPropertyBlock block;
        private float nextCheck;
        private int tick;

        public void Configure(Transform player, Shader shader)
        {
            target = player;
            if (shader != null) ditherShader = shader;
            if (ditherShader == null) ditherShader = Shader.Find("KryticalNova/Occluder Dither");
        }

        private void Awake() => block = new MaterialPropertyBlock();

        /// <summary>
        /// True while this collider's prop is faded enough to see through. What can be seen through can be clicked
        /// through: pointer picks skip it, so the ground behind a faded building stays selectable.
        /// </summary>
        public bool IsFaded(Collider col) =>
            col != null && byCollider.TryGetValue(col, out var occluder) && occluder != null && occluder.fade >= SeeThroughAmount;

        private void OnDisable()
        {
            for (int i = active.Count - 1; i >= 0; i--) Restore(active[i]);
            active.Clear();
        }

        private void OnDestroy()
        {
            foreach (var m in clones.Values) if (m != null) Destroy(m);
            clones.Clear();
        }

        private void LateUpdate()
        {
            if (target == null || ditherShader == null || !target.gameObject.activeInHierarchy) return;

            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + CheckInterval;
                tick++;
                Cast(target.position + Vector3.up * 1.2f);
                Cast(target.position + Vector3.up * 1.8f);
            }

            float step = Time.deltaTime / FadeSeconds * FadedAmount;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var o = active[i];
                bool occluding = tick - o.seenTick <= 1; // still hit on this or the previous check
                float goal = occluding ? FadedAmount : 0f;
                if (Mathf.Approximately(o.fade, goal) && (occluding || o.fade > 0f)) continue;
                o.fade = Mathf.MoveTowards(o.fade, goal, step);
                if (o.fade <= 0f && !occluding)
                {
                    Restore(o);
                    active.RemoveAt(i);
                    continue;
                }
                ApplyFade(o);
            }
        }

        private void Cast(Vector3 aim)
        {
            Vector3 from = transform.position;
            Vector3 dir = aim - from;
            float dist = dir.magnitude - CastRadius; // stop short of the player
            if (dist <= 0.1f) return;
            int count = Physics.SphereCastNonAlloc(from, CastRadius, dir / (dist + CastRadius), hits, dist,
                occluderLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = hits[i].collider;
                if (col == null || col.transform.IsChildOf(target)) continue;
                if (!byCollider.TryGetValue(col, out var o))
                {
                    o = Build(col);
                    byCollider[col] = o; // null is cached too: colliders with nothing to fade
                }
                if (o == null) continue;
                if (o.seenTick != tick && !active.Contains(o)) active.Add(o);
                o.seenTick = tick;
                if (!o.swapped) Swap(o);
            }
        }

        private Occluder Build(Collider col)
        {
            Renderer[] renderers;
            var lod = col.GetComponentInParent<LODGroup>();
            if (lod != null) renderers = lod.GetComponentsInChildren<Renderer>();
            else
            {
                renderers = col.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0)
                {
                    var parent = col.GetComponentInParent<Renderer>();
                    renderers = parent != null ? new[] { parent } : renderers;
                }
            }

            var kept = new List<Renderer>(renderers.Length);
            foreach (var r in renderers)
                if (r is MeshRenderer && r.enabled && IsOpaque(r.sharedMaterials)) kept.Add(r);
            if (kept.Count == 0) return null;

            var o = new Occluder
            {
                renderers = kept.ToArray(),
                originals = new Material[kept.Count][],
                dithered = new Material[kept.Count][]
            };
            for (int i = 0; i < kept.Count; i++)
            {
                var mats = kept[i].sharedMaterials;
                o.originals[i] = mats;
                o.dithered[i] = new Material[mats.Length];
                for (int m = 0; m < mats.Length; m++) o.dithered[i][m] = CloneFor(mats[m]);
            }
            return o;
        }

        private static bool IsOpaque(Material[] mats)
        {
            foreach (var m in mats) if (m == null || m.renderQueue >= 2500) return false;
            return mats.Length > 0;
        }

        private void Swap(Occluder o)
        {
            for (int i = 0; i < o.renderers.Length; i++)
                if (o.renderers[i] != null) o.renderers[i].sharedMaterials = o.dithered[i];
            o.swapped = true;
            ApplyFade(o);
        }

        private void Restore(Occluder o)
        {
            for (int i = 0; i < o.renderers.Length; i++)
            {
                var r = o.renderers[i];
                if (r == null) continue;
                r.sharedMaterials = o.originals[i];
                r.SetPropertyBlock(null);
            }
            o.swapped = false;
            o.fade = 0f;
        }

        private void ApplyFade(Occluder o)
        {
            foreach (var r in o.renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetFloat(FadeId, o.fade);
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>One dither clone per source material, copying the Synty Generic_Basic (or URP Lit) inputs.</summary>
        private Material CloneFor(Material src)
        {
            if (clones.TryGetValue(src, out var clone)) return clone;
            clone = new Material(ditherShader) { name = src.name + " (Occluder Dither)" };
            CopyTexture(src, clone, "_Albedo_Map", "_Albedo_Map", "_BaseMap", "_MainTex");
            CopyTexture(src, clone, "_Normal_Map", "_Normal_Map", "_BumpMap");
            bool emissive = !src.HasProperty("_Enable_Emission") || src.GetFloat("_Enable_Emission") > 0.5f;
            if (emissive)
            {
                CopyTexture(src, clone, "_Emission_Map", "_Emission_Map", "_EmissionMap");
                CopyColor(src, clone, "_Emission_Color", "_Emission_Color", "_EmissionColor");
            }
            CopyColor(src, clone, "_BaseColor", "_BaseColor", "_Color");
            CopyFloat(src, clone, "_Normal_Amount", "_Normal_Amount", "_BumpScale");
            CopyFloat(src, clone, "_Smoothness", "_Smoothness", "_Glossiness");
            CopyFloat(src, clone, "_Metallic", "_Metallic");
            CopyFloat(src, clone, "_Cutoff", "_Alpha_Clip_Threshold", "_Cutoff");
            clones[src] = clone;
            return clone;
        }

        private static void CopyTexture(Material src, Material dst, string to, params string[] from)
        {
            foreach (var name in from)
            {
                if (!src.HasProperty(name) || src.GetTexture(name) == null) continue;
                dst.SetTexture(to, src.GetTexture(name));
                dst.SetTextureScale(to, src.GetTextureScale(name));
                dst.SetTextureOffset(to, src.GetTextureOffset(name));
                return;
            }
        }

        private static void CopyColor(Material src, Material dst, string to, params string[] from)
        {
            foreach (var name in from)
                if (src.HasProperty(name)) { dst.SetColor(to, src.GetColor(name)); return; }
        }

        private static void CopyFloat(Material src, Material dst, string to, params string[] from)
        {
            foreach (var name in from)
                if (src.HasProperty(name)) { dst.SetFloat(to, src.GetFloat(name)); return; }
        }
    }
}
