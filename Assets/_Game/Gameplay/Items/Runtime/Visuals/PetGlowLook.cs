using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// How an evolved pet glows: a rim of light on its own body that breathes, and soft sparkles coming off it. Both
    /// take the stage's colour and both grow with <see cref="strength"/>, so a later stage is plainly brighter and
    /// busier than an earlier one. Strength 0 is no glow at all.
    /// </summary>
    public struct PetGlowLook
    {
        /// <summary>Looping sparkle effect that rides on the pet, or null.</summary>
        public GameObject sparkles;
        /// <summary>The rim glow material every pet copies (the Temper glow), or null.</summary>
        public Material rim;
        public Color color;
        /// <summary>0 = none, 1 = the final stage.</summary>
        public float strength;
        /// <summary>Rim brightness at the weakest glow (x) and at the strongest (y).</summary>
        public Vector2 rimIntensity;
        /// <summary>Sparkles per second at the weakest glow (x) and at the strongest (y).</summary>
        public Vector2 sparkleRate;
    }
}
