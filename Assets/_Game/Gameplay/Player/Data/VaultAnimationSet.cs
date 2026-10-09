using System;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>The height classes of obstacle a vault is animated for. Append only: the map's assets store the order.</summary>
    public enum VaultHeight : byte
    {
        /// <summary>Crates, benches: shin to thigh.</summary>
        Low = 0,
        /// <summary>Barriers, bins, railings: waist to chest.</summary>
        Middle = 1,
        /// <summary>Dumpsters, hedges: chest to head.</summary>
        Tall = 2,
    }

    /// <summary>How one height class of obstacle is vaulted.</summary>
    [Serializable]
    public struct VaultStyle
    {
        public AnimationClip clip;
        [Tooltip("Seconds of the clip that are the vault itself (x = feet leave the ground, y = feet touch down).")]
        public Vector2 window;
        [Tooltip("How far above the ground the body's lowest point is while it crosses, with the root left on the ground. " +
                 "The root is only raised by what the obstacle needs beyond this.")]
        [Min(0f)] public float poseClearance;
        [Tooltip("Seconds in the air (x = shortest, y = longest). The vault takes distance / current speed, kept inside " +
                 "this range, so a run is not slowed down more than the move needs.")]
        public Vector2 airTime;
        [Tooltip("Feet to the obstacle's near face when the vault starts.")]
        [Min(0.3f)] public float takeoffDistance;
        [Tooltip("The obstacle's far face to the feet on landing.")]
        [Min(0.3f)] public float landingDistance;
    }

    /// <summary>
    /// One set of vault animations: a style for each height class of obstacle. A height may be left without a clip;
    /// whoever uses the set then gets that height from the default set (<see cref="VaultAnimationMap"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Player/Vault Animation Set", fileName = "VaultSet_New")]
    public sealed class VaultAnimationSet : ScriptableObject
    {
        [Tooltip("Low obstacles (crates, benches).")]
        public VaultStyle low;
        [Tooltip("Middling obstacles (barriers, bins, railings).")]
        public VaultStyle middle;
        [Tooltip("The tallest obstacles (dumpsters, hedges).")]
        public VaultStyle tall;

        public VaultStyle Get(VaultHeight height)
        {
            switch (height)
            {
                case VaultHeight.Tall: return tall;
                case VaultHeight.Middle: return middle;
                default: return low;
            }
        }

        public void Set(VaultHeight height, in VaultStyle style)
        {
            switch (height)
            {
                case VaultHeight.Tall: tall = style; break;
                case VaultHeight.Middle: middle = style; break;
                default: low = style; break;
            }
        }

        /// <summary>True when this set has an animation for that height.</summary>
        public bool Has(VaultHeight height) => Get(height).clip != null;

        /// <summary>True when every height has an animation: what the default set must be.</summary>
        public bool IsComplete => Has(VaultHeight.Low) && Has(VaultHeight.Middle) && Has(VaultHeight.Tall);
    }
}
