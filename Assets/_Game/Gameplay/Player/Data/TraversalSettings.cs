using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// The heights that sort an obstacle into a vault's height class, and how far from where a vault may start and land.
    /// (The motor's probe steps and tolerances are engineering limits and stay in code.)
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Player/Traversal Settings", fileName = "TraversalSettings")]
    public sealed class TraversalSettings : ScriptableObject
    {
        [Tooltip("Obstacles this tall and taller (metres above the feet) are vaulted in the middle style; lower ones in the low style.")]
        [Min(0f)] public float middleFromHeight = 0.75f;
        [Tooltip("Obstacles this tall and taller are vaulted in the tall style. 1.35 m puts the dumpsters (1.38 to 1.41 m) in it with the hedges.")]
        [Min(0f)] public float tallFromHeight = 1.35f;

        // The starting values of the vault's reach below; the asset holds the ones in use.
        public const float DefaultMaximumRunUp = 3f;
        public const float DefaultLandingSearch = 2.4f;
        public const float DefaultStandingStartAside = 0.5f;
        public const float DefaultStepHeight = 0.3f;
        public const float DefaultLeadSeconds = 1f / 60f;
        public const float DefaultLeadMaximum = 1.2f;

        [Header("What can be vaulted from where")]
        [Tooltip("Farthest (metres) a vault may start from the obstacle when the ground in front of it cannot be walked on.")]
        [Min(0f)] public float maximumRunUp = DefaultMaximumRunUp;
        [Tooltip("How far past the usual landing distance the landing may move to find walkable ground.")]
        [Min(0f)] public float landingSearch = DefaultLandingSearch;
        [Tooltip("How far to the side of the crossing point the character may stand and still vault from where it is.")]
        [Min(0f)] public float standingStartAside = DefaultStandingStartAside;
        [Tooltip("Tallest thing the body may come down over between the obstacle and the landing.")]
        [Min(0f)] public float stepHeight = DefaultStepHeight;

        [Header("When a vault starts")]
        [Tooltip("The vault starts this many seconds of running before the take-off point (two frames at the least, at any " +
                 "frame rate), so the run flows into it instead of reaching the point first (owner, 22 Oct 2026: about a frame).")]
        [Range(0f, 0.2f)] public float leadSeconds = DefaultLeadSeconds;
        [Tooltip("On a server, the vault also starts earlier by half the player's round trip, so their own game, which is " +
                 "that far ahead, is not left standing at the take-off point waiting for it. Never more than this (metres).")]
        [Range(0f, 3f)] public float leadMaximum = DefaultLeadMaximum;

        /// <summary>The height class of an obstacle whose top is <paramref name="top"/> metres above the feet.</summary>
        public VaultHeight HeightOf(float top) =>
            top >= tallFromHeight ? VaultHeight.Tall : top >= middleFromHeight ? VaultHeight.Middle : VaultHeight.Low;

        private void OnValidate() => tallFromHeight = Mathf.Max(tallFromHeight, middleFromHeight);
    }
}
