using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The glow of one glowing thing (a tempered weapon, an evolved pet): a rim of light around its outline and the
    /// layers of fall-off behind it, each wider and fainter than the last, so the light reads as coming off the model
    /// whether or not the camera blooms. One material per layer, all copies of the shared glow material
    /// (KryticalNova/TemperGlow); the owner sets the look once and it reaches every layer.
    /// </summary>
    public sealed class GlowRim
    {
        // Width and brightness of each layer, relative to the rim itself. The rim is drawn first, then outward.
        private static readonly float[] Widen = { 1f, 1.9f, 3.2f, 5f };
        private static readonly float[] Dim = { 1f, 0.5f, 0.24f, 0.1f };
        private const int FirstQueue = 3010; // Transparent+10, the shader's own

        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        private static readonly int SparkleId = Shader.PropertyToID("_Sparkle");
        private static readonly int WidenId = Shader.PropertyToID("_Widen");
        private static readonly int DimId = Shader.PropertyToID("_Dim");

        private readonly Material[] layers;

        public GlowRim(Material template, string owner)
        {
            layers = new Material[Widen.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = new Material(template) { name = $"{template.name} ({owner} {i})" };
                layer.SetFloat(WidenId, Widen[i]);
                layer.SetFloat(DimId, Dim[i]);
                layer.renderQueue = FirstQueue + i; // inner layers first: each outer one only fills in around them
                layers[i] = layer;
            }
        }

        /// <param name="rimPower">How tight the rim hugs the outline: higher is thinner.</param>
        /// <param name="sparkle">Strength of the sparkles on the surface, 0 for none.</param>
        public void Set(Color color, float intensity, float rimPower, float pulse, float pulseSpeed, float sparkle)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                layer.SetColor(GlowColorId, color);
                layer.SetFloat(IntensityId, intensity);
                layer.SetFloat(RimPowerId, rimPower);
                layer.SetFloat(PulseId, pulse);
                layer.SetFloat(PulseSpeedId, pulseSpeed);
                layer.SetFloat(SparkleId, i == 0 ? sparkle : 0f); // once, not once per layer
            }
        }

        public void Attach(GameObject model) => GlowShell.Attach(model, layers);

        public static void Detach(GameObject model) => GlowShell.Detach(model);

        public void Destroy()
        {
            foreach (var layer in layers)
                if (layer != null) Object.Destroy(layer);
        }
    }
}
