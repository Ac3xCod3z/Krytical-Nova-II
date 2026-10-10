namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// How a pet keeps its place beside its owner: how wide the owner is where the pet hovers, how much clear air
    /// the pet leaves, and how near the camera may come. The numbers live in the pet settings; this carries them to
    /// the <see cref="PetFollower"/>. The default (<see cref="spaced"/> false) is the item's authored hover point,
    /// whatever the pet's size.
    /// </summary>
    public struct PetFollowLook
    {
        /// <summary>False = hover at the item's authored point and nothing more.</summary>
        public bool spaced;
        /// <summary>Half the owner's width at the height pets hover, in metres from the owner's centre line.</summary>
        public float ownerHalfWidth;
        /// <summary>Clear air between the owner and the pet's nearest edge, in metres. The same at every size.</summary>
        public float gap;
        /// <summary>How far the pet's glow reaches beyond its body at this stage, in metres. The light counts as part of the pet.</summary>
        public float glowReach;
        /// <summary>Extra clear air for each unit the pet has grown beyond its base size: a pet half as big again gets half of this.</summary>
        public float gapGrowth;
        /// <summary>The pet shrinks out of view while the camera is nearer to it than this, in metres. 0 = never.</summary>
        public float cameraClearance;
        /// <summary>The furthest the pet falls behind its place while its owner travels, in metres. 0 = no limit.</summary>
        public float maxTrail;

        /// <summary>
        /// How far the pet's nearest edge keeps from the owner's centre line at a growth stage's size (1 = base). The
        /// air between the owner and what is seen of the pet, glow included, never shrinks as it grows; a big pet gets more.
        /// </summary>
        public float ClearDistanceAt(float stageScale)
        {
            float distance = ownerHalfWidth + gap + glowReach + gapGrowth * (stageScale > 1f ? stageScale - 1f : 0f);
            return distance > 0f ? distance : 0f;
        }
    }
}
