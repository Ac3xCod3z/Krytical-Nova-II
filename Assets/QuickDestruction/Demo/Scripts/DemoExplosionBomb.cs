using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Minimal bomb example for the Quick Destruction demo. Call Explode from gameplay code,
    /// a UnityEvent, an animation event or the component context menu.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Quick Destruction/Demo/Explosion Bomb")]
    public sealed class DemoExplosionBomb : MonoBehaviour
    {
        [Header("Explosion")]
        [Tooltip("World-space radius of the damage and Rigidbody shockwave.")]
        [Min(0.01f)] public float Radius = 6f;
        [Tooltip("Damage applied to every Quick Destruction object reached by the blast.")]
        [Min(0f)] public float Damage = 120f;
        [Tooltip("Radial impulse applied to broken fragments, surviving Dynamic Rigidbody " +
                 "destructibles and optional ordinary Rigidbodies.")]
        [Min(0f)] public float Impulse = 25f;
        [Tooltip("How damage fades with distance from the centre. Damage only — the impulse " +
                 "above always uses Unity's own linear attenuation.")]
        public ExplosionFalloff Falloff = ExplosionFalloff.Linear;
        [Tooltip("Also pushes ordinary non-Quick-Destruction Rigidbodies inside Radius.")]
        public bool AffectOtherRigidbodies = true;

        [Header("Blast Effect")]
        [Tooltip("Optional non-looping ParticleSystem prefab spawned once at the bomb position.")]
        public ParticleSystem ExplosionParticles;
        [Tooltip("Uniform scale applied on top of the particle prefab's authored scale.")]
        [Min(0.01f)] public float ParticleScale = 1f;
        [Tooltip("Seconds before the spawned effect is removed. 0 reads its duration and " +
                 "longest particle lifetime automatically.")]
        [Min(0f)] public float ParticleLifetime;

        [Header("Filtering")]
        public LayerMask AffectedLayers = ~0;
        public QueryTriggerInteraction TriggerInteraction = QueryTriggerInteraction.Ignore;

        [Header("Lifecycle")]
        [Tooltip("Destroys the GameObject carrying this component after it explodes. The spawned " +
                 "particle effect is independent and finishes normally.")]
        public bool DestroyAfterExplosion = true;

        bool m_hasExploded;

        /// <summary>
        /// Plays the optional blast effect and applies radial damage plus impulse once.
        /// Safe to wire directly to a parameterless UnityEvent.
        /// </summary>
        [ContextMenu("Explode")]
        public void Explode()
        {
            if (m_hasExploded || !Application.isPlaying) return;
            m_hasExploded = true;

            Vector3 position = transform.position;
            DestructionExplosion.SpawnBlastParticle(ExplosionParticles, position,
                ParticleScale, ParticleLifetime);
            DestructionExplosion.Detonate(position, Radius, Damage, Impulse,
                Falloff, AffectedLayers.value, TriggerInteraction,
                sourceId: ResolveSourceId(), affectOtherRigidbodies: AffectOtherRigidbodies);

            if (DestroyAfterExplosion)
                Destroy(gameObject);
        }

        /// <summary>Alias matching the runtime DestructionBomb API.</summary>
        public void Detonate()
        {
            Explode();
        }

        int ResolveSourceId()
        {
#if UNITY_6000_4_OR_NEWER
            return GetEntityId().GetHashCode();
#else
            return GetInstanceID();
#endif
        }
    }
}
