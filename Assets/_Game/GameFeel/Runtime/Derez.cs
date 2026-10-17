using System.Collections;
using System.Collections.Generic;
using KryticalNova.Items.Visuals;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// A player de-rezzing (owner, 17 Oct 2026): killed off the ground, the body and its pet are deleted where they are
    /// instead of falling. Every renderer of the body (gear and weapons with it) and of the pet is given a copy of the
    /// de-rez material with its own texture and colour, the body glitches for a moment, then the de-rez sweeps it from
    /// the head down (KryticalNova/Player/Derez); when it has gone the renderers stay off until the respawn brings
    /// them back. The body's own materials are put back while it is hidden, so it returns as it was.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Derez : MonoBehaviour
    {
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int TopId = Shader.PropertyToID("_Top");
        private static readonly int BottomId = Shader.PropertyToID("_Bottom");
        private static readonly int CenterId = Shader.PropertyToID("_Center");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int GlitchId = Shader.PropertyToID("_GlitchAmount");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        // Where each kind of model keeps its texture: URP Lit, Sidekick's palette, the old built-in name.
        private static readonly int[] TextureIds = { Shader.PropertyToID("_BaseMap"), Shader.PropertyToID("_ColorMap"), Shader.PropertyToID("_MainTex") };
        private static readonly int[] ColorIds = { Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("_Color") };

        private const float GlitchPeak = 0.12f;

        private struct Part
        {
            public Renderer Renderer;
            public Material[] Own;
            public int Group; // 0 the body, 1 the pet: each is swept over its own height
            public bool Swept; // drawn when it started: de-rezzed. Off then (a holstered blade): only kept hidden
        }

        private readonly List<Part> parts = new List<Part>(32);
        // Effects worn on the body (the Ascension halo): not de-rezzed, switched off with it and back on with it.
        private readonly List<GameObject> worn = new List<GameObject>(2);
        private readonly List<Material> made = new List<Material>(32);
        private readonly Bounds[] groups = new Bounds[2];
        private Coroutine running;

        /// <summary>The body (and its pet) are hidden: de-rezzed and not yet back.</summary>
        public bool Gone { get; private set; }

        public static Derez For(Transform actor)
        {
            if (actor == null) return null;
            return actor.TryGetComponent(out Derez derez) ? derez : actor.gameObject.AddComponent<Derez>();
        }

        /// <summary>De-rezzes the body and its pet, in <paramref name="tint"/>, then hides them.</summary>
        public void Play(Material template, Color tint, float glitchSeconds, float sweepSeconds)
        {
            Restore();
            Collect();
            if (template == null || parts.Count == 0) { HideNow(); return; }
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (!part.Swept) continue;
                var copies = new Material[part.Own.Length];
                for (int m = 0; m < copies.Length; m++) copies[m] = CopyOf(template, part.Own[m], tint);
                part.Renderer.sharedMaterials = copies;
            }
            running = StartCoroutine(Run(Mathf.Max(0f, glitchSeconds), Mathf.Max(0.1f, sweepSeconds)));
        }

        private void SetWornShown(bool shown)
        {
            if (!shown)
            {
                worn.Clear();
                foreach (NovaHalo halo in GetComponentsInChildren<NovaHalo>())
                {
                    if (!halo.gameObject.activeSelf) continue;
                    worn.Add(halo.gameObject);
                    halo.gameObject.SetActive(false);
                }
                return;
            }
            foreach (GameObject effect in worn) if (effect != null) effect.SetActive(true);
            worn.Clear();
        }

        /// <summary>Gone at once, with nothing shown (a late joiner, or nothing to de-rez with).</summary>
        public void HideNow()
        {
            Restore();
            if (parts.Count == 0) Collect();
            SetHidden(true);
            if (worn.Count == 0) SetWornShown(false);
        }

        /// <summary>Back as it was (the respawn).</summary>
        public void Show()
        {
            Restore();
            if (parts.Count == 0) Collect();
            SetHidden(false);
            SetWornShown(true);
            parts.Clear();
        }

        private IEnumerator Run(float glitchSeconds, float sweepSeconds)
        {
            float clock = 0f;
            // A moment of glitch: the body stutters sideways in the faction's colour.
            while (clock < glitchSeconds)
            {
                float flicker = Mathf.PerlinNoise(Time.time * 23f, 0.37f);
                Drive(0f, GlitchPeak * Mathf.Lerp(0.35f, 1f, clock / glitchSeconds) * (0.6f + 0.8f * flicker));
                clock += Time.deltaTime;
                yield return null;
            }
            // The sweep: from the head down, quick at first and slowing as it reaches the feet. The halo goes as it starts.
            SetWornShown(false);
            clock = 0f;
            while (clock < sweepSeconds)
            {
                float t = clock / sweepSeconds;
                Drive(1f - (1f - t) * (1f - t), GlitchPeak * (1f - t) * 0.6f);
                clock += Time.deltaTime;
                yield return null;
            }
            running = null;
            SetHidden(true);
            Restore(); // hidden now: the body's own materials go back for the respawn
        }

        private void Drive(float progress, float glitch)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (part.Renderer == null || !part.Swept) continue;
                Bounds b = groups[part.Group];
                foreach (Material material in part.Renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    material.SetFloat(ProgressId, progress);
                    material.SetFloat(GlitchId, glitch);
                    material.SetFloat(TopId, b.max.y);
                    material.SetFloat(BottomId, b.min.y);
                    material.SetVector(CenterId, b.center);
                }
            }
        }

        private void Collect()
        {
            parts.Clear();
            AddFrom(transform, 0);
            var follower = GetComponent<PetFollower>();
            Transform pet = follower != null ? follower.PetTransform : null;
            if (pet != null) AddFrom(pet, 1);
            bool[] any = new bool[2];
            foreach (Part part in parts)
            {
                if (!part.Swept) continue;
                Bounds b = part.Renderer.bounds;
                if (!any[part.Group]) { groups[part.Group] = b; any[part.Group] = true; }
                else groups[part.Group].Encapsulate(b);
            }
        }

        private void AddFrom(Transform root, int group)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue; // not particles, trails or lines
                if (renderer.forceRenderingOff) continue;
                if (renderer.GetComponentInParent<NovaHalo>() != null || IsEffect(renderer)) continue; // worn effects, not the body
                parts.Add(new Part { Renderer = renderer, Own = renderer.sharedMaterials, Group = group, Swept = renderer.enabled });
            }
        }

        /// <summary>A trail or another effect drawn on the body (its own shader, not the body's look).</summary>
        private static bool IsEffect(Renderer renderer)
        {
            Material material = renderer.sharedMaterial;
            string shader = material != null && material.shader != null ? material.shader.name : string.Empty;
            return shader.StartsWith("KryticalNova/Nova") || shader.Contains("smear");
        }

        private void SetHidden(bool hidden)
        {
            Gone = hidden;
            // The character LOD must not show a body (or its pet) the de-rez has hidden.
            KryticalNova.Rendering.CharacterLod.Hold(transform, hidden);
            var follower = GetComponent<PetFollower>();
            if (follower != null && follower.PetTransform != null) KryticalNova.Rendering.CharacterLod.Hold(follower.PetTransform, hidden);
            foreach (Part part in parts)
                if (part.Renderer != null) part.Renderer.forceRenderingOff = hidden;
        }

        private void Restore()
        {
            if (running != null)
            {
                StopCoroutine(running);
                running = null;
            }
            foreach (Part part in parts)
                if (part.Renderer != null && part.Own != null && part.Swept) part.Renderer.sharedMaterials = part.Own;
            foreach (Material material in made) if (material != null) Destroy(material);
            made.Clear();
        }

        private Material CopyOf(Material template, Material own, Color tint)
        {
            var copy = new Material(template) { name = template.name + " (" + (own != null ? own.name : "none") + ")" };
            made.Add(copy);
            copy.SetColor(TintId, tint);
            if (own == null) return copy;
            foreach (int id in TextureIds)
                if (own.HasTexture(id) && own.GetTexture(id) != null)
                {
                    copy.SetTexture(BaseMapId, own.GetTexture(id));
                    copy.SetTextureScale(BaseMapId, own.GetTextureScale(id));
                    copy.SetTextureOffset(BaseMapId, own.GetTextureOffset(id));
                    break;
                }
            foreach (int id in ColorIds)
                if (own.HasColor(id)) { copy.SetColor(BaseColorId, own.GetColor(id)); break; }
            if (own.HasTexture(EmissionMapId) && own.GetTexture(EmissionMapId) != null) copy.SetTexture(EmissionMapId, own.GetTexture(EmissionMapId));
            if (own.HasColor(EmissionColorId) && own.IsKeywordEnabled("_EMISSION")) copy.SetColor(EmissionColorId, own.GetColor(EmissionColorId));
            return copy;
        }

        private void OnDestroy() => Restore();
    }
}
