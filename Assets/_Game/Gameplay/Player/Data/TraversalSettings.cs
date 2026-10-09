using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// The heights that sort an obstacle into a vault's height class. (What can be vaulted at all, and how, are the
    /// vault motor's own limits on the Player prefab.)
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Player/Traversal Settings", fileName = "TraversalSettings")]
    public sealed class TraversalSettings : ScriptableObject
    {
        [Tooltip("Obstacles this tall and taller (metres above the feet) are vaulted in the middle style; lower ones in the low style.")]
        [Min(0f)] public float middleFromHeight = 0.75f;
        [Tooltip("Obstacles this tall and taller are vaulted in the tall style. 1.35 m puts the dumpsters (1.38 to 1.41 m) in it with the hedges.")]
        [Min(0f)] public float tallFromHeight = 1.35f;

        /// <summary>The height class of an obstacle whose top is <paramref name="top"/> metres above the feet.</summary>
        public VaultHeight HeightOf(float top) =>
            top >= tallFromHeight ? VaultHeight.Tall : top >= middleFromHeight ? VaultHeight.Middle : VaultHeight.Low;

        private void OnValidate() => tallFromHeight = Mathf.Max(tallFromHeight, middleFromHeight);
    }
}
