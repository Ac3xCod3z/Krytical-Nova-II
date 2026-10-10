using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Rendering
{
    /// <summary>How one glowing thing glows. The numbers come from settings assets (a Temper look, a pet stage).</summary>
    public struct GlowHaloLook
    {
        public Color color;
        /// <summary>Brightness right at the outline. Above 1 the inner edge burns towards white.</summary>
        public float intensity;
        /// <summary>How far the light reaches from the outline, in metres.</summary>
        public float width;
        /// <summary>How deep it breathes: the share of its brightness it loses at the low point, 0 to 1.</summary>
        public float pulse;
        /// <summary>Breaths per second.</summary>
        public float pulseSpeed;
        /// <summary>How much wider the halo is at the top of a breath than at the bottom, as a share of its width.</summary>
        public float pulseWidth;
        /// <summary>
        /// How brightly it twinkles: small sparks that flash for a moment each, in the light and on the part of the
        /// model that glows. 0 = none.
        /// </summary>
        public float twinkle;
        /// <summary>
        /// Lightning in the light, in the light's own colour: thin jagged arcs that crawl round the outline for an
        /// instant each. 1 = as tuned on the renderer feature, 0 = none.
        /// </summary>
        public float lightning;
        /// <summary>
        /// Flame, in the light's own colour: tongues that rise from the outline and stream towards the tip. 1 = all
        /// of the light is flame, 0 = the steady halo. Mixes freely with lightning.
        /// </summary>
        public float flame;
        /// <summary>
        /// Embers: the light is broken up into a ragged, drifting body that shifts between hotter and deeper shades
        /// of its own colour, like a flame seen close. 1 = as tuned on the renderer feature, 0 = the smooth light.
        /// Lies over the flame when there is one.
        /// </summary>
        public float ember;
    }

    /// <summary>Which part of a model glows.</summary>
    public enum GlowZoneMode : byte
    {
        /// <summary>All of it.</summary>
        Whole = 0,
        /// <summary>Only what lies inside the zone's box: the blade of a sword, the head of an axe.</summary>
        Only = 1,
        /// <summary>None of it: something that rides on a glowing thing and stays dark, like a bow's string.</summary>
        None = 2,
    }

    /// <summary>
    /// The part of a model that glows, as boxes in the model's own space: the box the glow is kept to (if any), and
    /// a box that is kept dark whatever else (if any). The rest of the model casts no light, has none laid over it,
    /// and keeps the light off the air beside it: the glow of a blade stops where its hilt begins instead of
    /// spilling round the hilt.
    /// </summary>
    [System.Serializable]
    public struct GlowZone
    {
        public GlowZoneMode mode;
        [Tooltip("Only: the part that glows. In the model's own space (for a weapon: z runs from the grip towards the tip).")]
        public Bounds box;
        [Tooltip("A part that never glows, whatever the mode: a gun's grip, the shaft that runs through an axe head. No size = none.")]
        public Bounds dark;

        public bool HasDark => dark.size.x > 0f && dark.size.y > 0f && dark.size.z > 0f;
    }

    /// <summary>
    /// One glowing thing: the renderers that make its outline and the look of its light. While it has renderers it is
    /// on the list <see cref="GlowHaloFeature"/> draws from; nothing is added to the model itself. Every halo breathes
    /// on its own beat, so a crowd of them does not pulse in step.
    /// </summary>
    public sealed class GlowHalo
    {
        private struct Source
        {
            public Renderer renderer;
            public int submeshes;
            public Transform space;     // what the zone's box is measured in
            public GlowZone zone;
        }

        private static readonly List<GlowHalo> ActiveList = new List<GlowHalo>(32);
        private static readonly List<Renderer> Scratch = new List<Renderer>(8);

        /// <summary>Every halo that has something to light, for the renderer feature.</summary>
        public static IReadOnlyList<GlowHalo> Active => ActiveList;

        private readonly List<Source> sources = new List<Source>(4);
        private readonly float beat = Random.value;
        private GlowHaloLook look;

        public int RendererCount => sources.Count;
        public Renderer RendererAt(int index) => sources[index].renderer;
        public int SubmeshCountAt(int index) => sources[index].submeshes;
        /// <summary>How brightly it twinkles, 0 for not at all.</summary>
        public float Twinkle => Mathf.Max(0f, look.twinkle);
        /// <summary>How strong the lightning in its light is, and how much of its light is flame (0 = none).</summary>
        public float Lightning => Mathf.Max(0f, look.lightning);
        public float Flame => Mathf.Clamp01(look.flame);
        /// <summary>How strongly its light is broken up into embers (0 = not at all).</summary>
        public float Ember => Mathf.Max(0f, look.ember);
        /// <summary>True when something is drawn in its light that has to travel with it: sparks, lightning, flame or embers.</summary>
        public bool Pinned => Twinkle > 0f || Lightning > 0f || Flame > 0f || Ember > 0f;
        /// <summary>Its own number, 0 to 1, for as long as it lives: what makes its breath, sparks and arcs its own.</summary>
        public float Beat => beat;

        /// <summary>
        /// What its sparks and aura are pinned to, so they travel with it: the origin of the first model it is on (a
        /// weapon's grip) and the way that model points (towards a weapon's tip). False when that model is gone.
        /// </summary>
        public bool TryGetPin(out Vector3 origin, out Vector3 towardsTip)
        {
            origin = towardsTip = default;
            if (sources.Count == 0 || sources[0].space == null) return false;
            origin = sources[0].space.position;
            towardsTip = sources[0].space.forward;
            return true;
        }

        // Enter Play Mode runs without a domain reload, so the list must be emptied by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ActiveList.Clear();

        public void Set(in GlowHaloLook value) => look = value;

        /// <summary>
        /// Adds a model's outline to this halo: its meshes, skinned or not. Particles, trails and lines are effects
        /// riding on the model and cast no halo. <paramref name="zone"/> says which part of the model glows, in the
        /// model's own space. Renderers whose name holds one of <paramref name="dark"/> never glow, wherever they
        /// are (a bow's string); those whose name holds one of <paramref name="skip"/> are not part of the model's
        /// outline at all (a shell laid over it).
        /// </summary>
        public void Add(GameObject model, in GlowZone zone = default, IReadOnlyList<string> dark = null, IReadOnlyList<string> skip = null)
        {
            if (model == null) return;
            model.GetComponentsInChildren(true, Scratch);
            foreach (Renderer renderer in Scratch)
            {
                if (Named(renderer.name, skip) || Holds(renderer)) continue;
                int submeshes;
                if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null) submeshes = skinned.sharedMesh.subMeshCount;
                else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                    submeshes = filter.sharedMesh.subMeshCount;
                else continue;
                GlowZone own = Named(renderer.name, dark) ? new GlowZone { mode = GlowZoneMode.None } : zone;
                sources.Add(new Source { renderer = renderer, submeshes = submeshes, space = model.transform, zone = own });
            }
            Scratch.Clear();
            if (sources.Count > 0 && !ActiveList.Contains(this)) ActiveList.Add(this);
        }

        private bool Holds(Renderer renderer)
        {
            for (int i = 0; i < sources.Count; i++)
                if (sources[i].renderer == renderer) return true;
            return false;
        }

        private static bool Named(string name, IReadOnlyList<string> names)
        {
            if (names == null) return false;
            for (int i = 0; i < names.Count; i++)
                if (!string.IsNullOrEmpty(names[i]) && name.Contains(names[i])) return true;
            return false;
        }

        /// <summary>Lets go of every model: the halo is off until something is added again.</summary>
        public void Clear()
        {
            sources.Clear();
            ActiveList.Remove(this);
        }

        /// <summary>
        /// The light at this moment of its breath: colour times brightness, and its reach in metres. The breath is a
        /// smooth swell (a raised cosine), brightest and widest together.
        /// </summary>
        public void Evaluate(float time, out Vector3 light, out float width)
        {
            float breath = 0.5f - 0.5f * Mathf.Cos((time * look.pulseSpeed + beat) * Mathf.PI * 2f);
            float brightness = Mathf.Max(0f, look.intensity) * (1f - Mathf.Clamp01(look.pulse) * (1f - breath));
            Color linear = look.color.linear;
            light = new Vector3(linear.r, linear.g, linear.b) * brightness;
            width = Mathf.Max(0f, look.width) * (1f + Mathf.Max(0f, look.pulseWidth) * breath);
        }

        /// <summary>
        /// A renderer's glow zone for the shader. Each box comes as the matrix that takes a world point into it,
        /// where the box is the unit cube about the origin. A zone that asks for a box it does not have glows all over.
        /// </summary>
        public void GetZone(int index, out GlowZoneMode mode, out Matrix4x4 worldToBox, out bool hasDark, out Matrix4x4 worldToDark)
        {
            Source source = sources[index];
            mode = source.zone.mode;
            worldToBox = worldToDark = Matrix4x4.identity;
            hasDark = false;
            if (mode == GlowZoneMode.None || source.space == null) return;

            Matrix4x4 modelToWorld = source.space.localToWorldMatrix;
            if (mode == GlowZoneMode.Only)
            {
                Vector3 size = source.zone.box.size;
                if (size.x > 0f && size.y > 0f && size.z > 0f)
                    worldToBox = (modelToWorld * Matrix4x4.TRS(source.zone.box.center, Quaternion.identity, size)).inverse;
                else
                    mode = GlowZoneMode.Whole;
            }
            hasDark = source.zone.HasDark;
            if (hasDark) worldToDark = (modelToWorld * Matrix4x4.TRS(source.zone.dark.center, Quaternion.identity, source.zone.dark.size)).inverse;
        }
    }
}
