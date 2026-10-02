using UnityEngine;

namespace RipVertices.QuickDestruction
{
    /// <summary>
    /// Inspector-friendly source for a scene-wide Quick Destruction explosion. Call
    /// Detonate from gameplay code, an animation event, a UnityEvent, or the context menu.
    /// DetonateOn blows up one chosen object at its own position instead of searching an area.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Quick Destruction/Destruction Bomb")]
    public sealed class DestructionBomb : MonoBehaviour
    {
        [Header("Explosion")]
        [Min(0.01f)] public float Radius = 6f;
        [Min(0f)] public float Damage = 120f;
        [Tooltip("Radial physical force. Broken targets pass it to their fragments; surviving Dynamic Rigidbody targets are pushed intact.")]
        [Min(0f)] public float Force = 25f;
        [Tooltip("How damage fades with distance from the centre. It shapes damage only: the " +
                 "physical push always uses Unity's own linear attenuation, so Linear is the " +
                 "setting where damage and push fade together. None damages everything inside " +
                 "Radius equally, which is what a guaranteed demolition radius needs.")]
        public ExplosionFalloff Falloff = ExplosionFalloff.Linear;
        [Tooltip("Also applies a standard radial AddExplosionForce to ordinary non-Quick-Destruction Rigidbodies in range.")]
        public bool AffectOtherRigidbodies = true;

        [Header("Optional Blast Effect")]
        [Tooltip("Optional ParticleSystem played once at the blast position. Leave empty for a " +
                 "silent, invisible explosion — the destructibles it hits still raise their own " +
                 "break particles and sounds through their Break Effects component.")]
        public ParticleSystem ExplosionParticle;
        [Tooltip("Uniform scale applied to the spawned particle. Use it to match one prefab to " +
                 "blasts of different sizes.")]
        [Min(0.01f)] public float ParticleScale = 1f;
        [Tooltip("Scales the particle with Radius as well, taking the prefab as authored for a " +
                 "blast of Reference Radius. Off keeps Particle Scale alone.")]
        public bool ScaleParticleWithRadius;
        [Tooltip("The Radius the particle prefab was authored for. Only used by Scale Particle With Radius.")]
        [Min(0.01f)] public float ParticleReferenceRadius = 6f;
        [Tooltip("Seconds before the spawned particle object is destroyed. 0 reads the duration " +
                 "and longest lifetime from the ParticleSystem itself.")]
        [Min(0f)] public float ParticleLifetime;

        [Header("Filtering")]
        public LayerMask AffectedLayers = ~0;
        public QueryTriggerInteraction TriggerInteraction = QueryTriggerInteraction.Ignore;
        public int DamageTypeId;

        [Header("Lifecycle")]
        [Tooltip("Destroys this bomb object after Detonate. Leave off for pooled or reusable bombs.")]
        public bool DestroyAfterDetonation;

        /// <summary>
        /// Detonates at this Transform's world position and affects every destructible whose
        /// geometry the blast reaches.
        /// </summary>
        [ContextMenu("Detonate")]
        public void Detonate()
        {
            Detonate(transform.position);
        }

        /// <summary>Detonates at an explicit world position using this bomb's settings.</summary>
        public void Detonate(Vector3 position)
        {
            PlayBlastEffect(position);
            DestructionExplosion.Detonate(position, Radius, Damage, Force,
                Falloff, AffectedLayers.value, TriggerInteraction,
                DamageTypeId, ResolveSourceId(), AffectOtherRigidbodies);
            FinishDetonation();
        }

        /// <summary>
        /// Blows up one specific object at its own position, with the optional particle, without
        /// touching anything else nearby. Radius still decides how much of that object breaks and
        /// how the force falls off inside it; Damage and the area search are not involved, so a
        /// scripted "destroy this crate now" cannot be defeated by the crate's Health.
        ///
        /// Pass a DestructibleRoot, or any object with one on itself or a parent — a collider or
        /// a child prop reference both work, which makes this safe to wire to a UnityEvent.
        /// </summary>
        public void DetonateOn(GameObject target)
        {
            DetonateOn(target != null
                ? target.GetComponentInParent<DestructibleRoot>()
                : null);
        }

        /// <summary>Blows up one specific destructible at its own position.</summary>
        public void DetonateOn(DestructibleRoot target)
        {
            if (target == null || target.Profile == null) return;

            // The pivot is wherever the artist left it, so the blast is centred on the
            // geometry: a column whose pivot sits at its base must not be blown up from the
            // floor when the intent was "explode this object".
            Vector3 position = ResolveBlastCentre(target);
            PlayBlastEffect(position);
            target.BreakWithoutSpread(new BreakContext(position, Vector3.up, Radius,
                Force, ResolveSourceId()));
            FinishDetonation();
        }

        int ResolveSourceId()
        {
#if UNITY_6000_4_OR_NEWER
            return GetEntityId().GetHashCode();
#else
            return GetInstanceID();
#endif
        }

        void FinishDetonation()
        {
            if (DestroyAfterDetonation)
                Destroy(gameObject);
        }

        /// <summary>
        /// Centre of a destructible's visible geometry, falling back to its pivot when nothing
        /// is renderable yet.
        /// </summary>
        static Vector3 ResolveBlastCentre(DestructibleRoot root)
        {
            GameObject source = root.IntactRoot != null && root.IntactRoot.activeSelf
                ? root.IntactRoot
                : root.FracturedRoot != null ? root.FracturedRoot : root.gameObject;

            Renderer[] renderers = source.GetComponentsInChildren<Renderer>();
            Bounds bounds = default;
            bool any = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled) continue;
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return any ? bounds.center : root.transform.position;
        }

        void PlayBlastEffect(Vector3 position)
        {
            float scale = Mathf.Max(0.01f, ParticleScale);
            if (ScaleParticleWithRadius)
                scale *= Radius / Mathf.Max(0.01f, ParticleReferenceRadius);
            DestructionExplosion.SpawnBlastParticle(ExplosionParticle, position,
                scale, ParticleLifetime);
        }
    }
}
