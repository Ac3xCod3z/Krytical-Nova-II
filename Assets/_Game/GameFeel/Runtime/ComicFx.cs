using System;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The four colours a comic effect is drawn in. One ramp belongs to one kind of hit (see
    /// <see cref="HitLookSettings"/>), so a colour always means the same thing in a fight.
    /// </summary>
    [Serializable]
    public struct ComicRamp
    {
        [Tooltip("The brightest part: the flash at the middle of an impact.")]
        public Color core;
        [Tooltip("The main colour: a word's letters, the body of an impact.")]
        public Color fill;
        [Tooltip("The second colour: the burst behind a word, the fringe of an impact.")]
        public Color back;
        [Tooltip("Outlines, shadows and dots.")]
        public Color ink;

        public ComicRamp(Color core, Color fill, Color back, Color ink)
        {
            this.core = core; this.fill = fill; this.back = back; this.ink = ink;
        }

        public Color Of(ComicRole role)
        {
            switch (role)
            {
                case ComicRole.Core: return core;
                case ComicRole.Fill: return fill;
                case ComicRole.Back: return back;
                case ComicRole.Ink: return ink;
                default: return Color.white;
            }
        }
    }

    /// <summary>Which colour of a <see cref="ComicRamp"/> a part of a comic effect takes. Keep = its own, untouched.</summary>
    public enum ComicRole : byte { Keep = 0, Core = 1, Fill = 2, Back = 3, Ink = 4 }

    /// <summary>
    /// On a comic effect made from the Cartoon FX pack (an impact, a word): which of its particle systems is drawn
    /// in which colour of the ramp, so one pooled effect can be shown in the colours of any kind of hit. The pack's
    /// shaders take a particle's colour from the system's custom data, which is what <see cref="Apply"/> sets; it
    /// costs nothing to look up at run time because the parts are listed when the effect is built (ComicFxBuilder).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ComicFx : MonoBehaviour
    {
        [Serializable]
        public struct Part
        {
            public ParticleSystem system;
            public ComicRole role;
            [Tooltip("A letter: the pack's letter shader blends two colours, and both are set.")]
            public bool twoColours;
            [Tooltip("How see-through this part is (the ramp's colour keeps its hue; a soft dot pattern stays soft).")]
            [Range(0f, 1f)] public float alpha;
            [Tooltip("A letter with a coloured outline: the outline is drawn by the letter's material, and takes this colour of the ramp. Keep = the material's own (ink, or white).")]
            public ComicRole outline;
            public ParticleSystemRenderer renderer;
        }

        private static readonly int OutlineColor = Shader.PropertyToID("_OutlineColor");
        private static MaterialPropertyBlock block;

        [SerializeField] private Part[] parts = Array.Empty<Part>();
        [Tooltip("The colours the effect was built in, shown when no ramp is asked for.")]
        [SerializeField] private ComicRamp authored;

        public int PartCount => parts.Length;
        public ComicRamp Authored => authored;

        /// <summary>Editor: the parts and the colours they were built in.</summary>
        public void Set(Part[] list, ComicRamp builtIn)
        {
            parts = list ?? Array.Empty<Part>();
            authored = builtIn;
        }

        /// <summary>Draws the effect in this ramp from its next particle on. Call it as the effect is spawned.</summary>
        public void Apply(in ComicRamp ramp)
        {
            // Custom data reaches the shader as it is, with none of the conversion a colour property gets: in a
            // linear project a colour handed over as picked would come out pale.
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (int i = 0; i < parts.Length; i++)
            {
                Part part = parts[i];
                if (part.system == null || part.role == ComicRole.Keep) continue;
                Color colour = ramp.Of(part.role);
                if (linear) colour = colour.linear;
                colour.a = part.alpha;
                ParticleSystem.CustomDataModule data = part.system.customData;
                data.SetColor(ParticleSystemCustomData.Custom1, colour);
                if (part.twoColours) data.SetColor(ParticleSystemCustomData.Custom2, colour);

                if (part.outline == ComicRole.Keep || part.renderer == null) continue;
                // Per renderer, so the material (shared by every copy of the effect in the pool) is left alone. Handed
                // over as a plain vector: the shader wants the linear value, and a vector is never converted.
                Color edge = ramp.Of(part.outline);
                if (linear) edge = edge.linear;
                block ??= new MaterialPropertyBlock();
                part.renderer.GetPropertyBlock(block);
                block.SetVector(OutlineColor, new Vector4(edge.r, edge.g, edge.b, 1f));
                part.renderer.SetPropertyBlock(block);
            }
        }

        public void ApplyAuthored() => Apply(authored);
    }
}
