using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Optional per-projectile overrides consumed by Quick Destruction's normal Rigidbody
    /// collision path. This does not deal damage itself, so adding it cannot duplicate a hit.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Quick Destruction/Destruction Impact Source")]
    public sealed class DestructionImpactSource : MonoBehaviour
    {
        [Header("Directional Launch")]
        [Tooltip("Directional impulse in N s along this projectile's travel direction. -1 uses the target profile, 0 disables directional push, and positive values override the target. Heavier bodies gain less speed. Max Fragment Launch Speed remains the safety cap.")]
        [Min(-1f)] public float DirectionalForce = -1f;

        [Header("Local Hit Area")]
        [Tooltip("Override the target profile's direct-impact selection radius for this projectile.")]
        public bool OverrideImpactRadius;
        [Tooltip("Radius around the contact point whose baked or loose fragments may react. Nearby objects are affected separately only when Spread Impact To Nearby Objects is enabled on the struck profile.")]
        [Min(0.01f)] public float ImpactRadius = 0.75f;
    }
}
