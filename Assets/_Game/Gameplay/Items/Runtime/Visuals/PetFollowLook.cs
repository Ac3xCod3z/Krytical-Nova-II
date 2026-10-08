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
        /// <summary>The pet shrinks out of view while the camera is nearer to it than this, in metres. 0 = never.</summary>
        public float cameraClearance;

        /// <summary>How far the pet's nearest edge keeps from the owner's centre line.</summary>
        public float ClearDistance => ownerHalfWidth + gap > 0f ? ownerHalfWidth + gap : 0f;
    }
}
