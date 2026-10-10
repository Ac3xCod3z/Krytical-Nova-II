using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// How an evolved pet glows: a halo of light round its outline that breathes, and soft sparkles coming off it. Both
    /// take the stage's colour and both grow with <see cref="strength"/>, so a later stage is plainly brighter and
    /// busier than an earlier one. Strength 0 is no glow at all. Each pair of numbers is the value at the weakest
    /// glow (x) and at the strongest (y); they come from the pet settings.
    /// </summary>
    public struct PetGlowLook
    {
        /// <summary>Looping sparkle effect that rides on the pet, or null.</summary>
        public GameObject sparkles;
        public Color color;
        /// <summary>0 = none, 1 = the final stage.</summary>
        public float strength;
        /// <summary>True: no rim of light at this stage, whatever the strength (the sparkles and the stage's colour are still worn).</summary>
        public bool noRim;
        /// <summary>Brightness of the halo at the outline.</summary>
        public Vector2 rimIntensity;
        /// <summary>How far the halo reaches from the outline, in metres.</summary>
        public Vector2 rimWidth;
        /// <summary>How deep the halo breathes: the share of its brightness lost at the low point.</summary>
        public Vector2 rimPulse;
        /// <summary>Breaths per second.</summary>
        public Vector2 rimPulseSpeed;
        /// <summary>How much wider the halo is at the top of a breath, as a share of its width.</summary>
        public Vector2 rimPulseWidth;
        /// <summary>Sparkles per second.</summary>
        public Vector2 sparkleRate;
    }
}
