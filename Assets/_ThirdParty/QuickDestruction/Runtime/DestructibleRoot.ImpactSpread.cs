using System.Collections.Generic;
using UnityEngine;

namespace RipVertices.QuickDestruction
{
    public partial class DestructibleRoot
    {
        [Tooltip("Optional common ancestor containing this destructible and the other modules allowed to receive its impact wave. Empty limits spread to this Destructible Root and its children, including a Whole Building bake. Unrelated scene objects are excluded. The assigned Transform must be an ancestor of this object.")]
        public Transform ImpactSpreadParent;
        public Transform ResolvedImpactSpreadParent => ImpactSpreadParent != null &&
            transform.IsChildOf(ImpactSpreadParent) ? ImpactSpreadParent : transform;

        // Shared Health is zero after a local break. Remaining shell still needs collateral
        // resistance, otherwise even a 1% wave would detach every piece in its radius.
        readonly Dictionary<int, float> m_impactSpreadFragmentHealth = new Dictionary<int, float>();

        bool IsDirectImpactFragment(FragmentRecord fragment, in DamageContext hit) =>
            (hit.TargetStructuralGroup < 0 || fragment.StructuralGroup == hit.TargetStructuralGroup) &&
            (hit.Radius <= 0f || Vector3.Distance(GetFragmentClosestPoint(fragment, hit.Point), hit.Point) <= hit.Radius);

        internal bool IsDirectImpactBody(Rigidbody body, in DamageContext hit)
        {
            if (body == IntactBody) return true;
            foreach (var fragment in Fragments)
                if (fragment != null && fragment.Body == body) return IsDirectImpactFragment(fragment, hit);
            return false;
        }

        internal bool ReceiveImpactSpread(in DamageContext hit, float radius, float damage,
            float impulse, ImpactSpreadFalloff falloff, LayerMask layers, bool directRoot, Transform ignored)
        {
            if (damage <= 0f) return false;
            bool damageFades = DestructionImpactSpread.DamageFades(falloff);
            var wave = new BreakContext(hit.Point, hit.Normal, radius, impulse,
                hit.SourceId, Vector3.zero, 0f, -1, DestructionImpactSpread.ImpulseFades(falloff), true);
            if (m_state < DestructibleState.Fractured)
            {
                if (directRoot) return false; // The direct hit already consumed this Health.
                float distance = float.PositiveInfinity;
                foreach (var collider in GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled || collider.isTrigger ||
                        collider.GetComponentInParent<DestructibleRoot>() != this ||
                        (layers.value & (1 << collider.gameObject.layer)) == 0 ||
                        DestructionImpactSpread.Excluded(collider.transform, hit.SourceId, ignored)) continue;
                    distance = Mathf.Min(distance, Vector3.Distance(hit.Point, collider.ClosestPoint(hit.Point)));
                }
                float amount = damage * DestructionImpactSpread.Influence(distance, radius, damageFades);
                if (amount <= 0f) return false;
                var received = new DamageContext(amount, hit.Point, hit.Normal, radius,
                    hit.DamageTypeId, hit.SourceId, 0f, Vector3.zero, 0f);
                HitReceived?.Invoke(received);
                if (!Profile.AcceptsHitDamage(amount) ||
                    Time.time - m_lastDamageTime < Mathf.Max(0.05f, Profile.DamageCooldown)) return false;
                m_lastDamageTime = Time.time;
                m_lastDamageSourceId = hit.SourceId;
                float consumed = Mathf.Min(m_health, amount);
                m_health -= consumed;
                int activated = 0;
                if (m_health <= 0f)
                {
                    // DoBreak honours the explicit radius; the direct-hit All Pieces
                    // override must never turn collateral damage into a whole bake.
                    activated = DoBreak(wave);
                }
                else m_state = DestructibleState.Damaged;
                Damaged?.Invoke(received, new DamageResult(activated < 0 ? DamageOutcome.DelayedByBudget :
                    m_health <= 0f ? DamageOutcome.Broken : DamageOutcome.Damaged, consumed, Mathf.Max(0, activated)));
                return true;
            }

            var eligible = new List<int>();
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < Fragments.Count; i++)
            {
                var f = Fragments[i];
                if (f == null || f.Transform == null || f.Activated || f.Detached ||
                    m_pendingActivationIndices.Contains(i) ||
                    (layers.value & (1 << f.Transform.gameObject.layer)) == 0 ||
                    (f.Collider != null && f.Collider.isTrigger) ||
                    DestructionImpactSpread.Excluded(f.Transform, hit.SourceId, ignored) ||
                    (directRoot && IsDirectImpactFragment(f, hit))) continue;
                float distance = Vector3.Distance(GetFragmentClosestPoint(f, hit.Point), hit.Point);
                float amount = damage * DestructionImpactSpread.Influence(distance, radius, damageFades);
                if (amount <= 0f || !Profile.AcceptsHitDamage(amount)) continue;
                nearest = Mathf.Min(nearest, distance);
                if (!m_impactSpreadFragmentHealth.TryGetValue(i, out float health)) health = Profile.Health;
                health = Mathf.Max(0f, health - amount);
                m_impactSpreadFragmentHealth[i] = health;
                if (health <= 0f) eligible.Add(i);
            }
            if (!directRoot && nearest < radius)
                HitReceived?.Invoke(new DamageContext(damage * DestructionImpactSpread.Influence(nearest, radius, damageFades),
                    hit.Point, hit.Normal, radius, hit.DamageTypeId, hit.SourceId, impulse, Vector3.zero, 0f));
            if (eligible.Count == 0) return nearest < radius;
            int physicalCount = 0;
            foreach (int index in eligible)
                if (HasActivatablePhysics(Fragments[index])) physicalCount++;
            int granted = physicalCount;
            if (UsesActiveBodyBudget && !ActivateAllOnFirstBreak)
                granted = DestructionBudgetManager.Instance.RequestActivation(Profile.BudgetGroup,
                    Profile.GroupMaxActiveBodies, Mathf.Min(granted,
                        Mathf.Max(0, Profile.MaxActiveFragments - m_activeCount - m_pendingActivationIndices.Count)));
            int changed = 0;
            int acceptedBodies = 0;
            for (int n = 0; n < eligible.Count; n++)
            {
                int index = eligible[n];
                var fragment = Fragments[index];
                if (!HasActivatablePhysics(fragment)) DetachFragmentRecord(fragment);
                else if (acceptedBodies >= granted) continue;
                else if (acceptedBodies++ < LargeActivationBatchSize)
                    ActivateFragment(index, wave, Vector3.zero, Vector3.zero);
                else
                {
                    m_pendingActivationIndices.Add(index);
                    m_pendingActivations.Enqueue(new PendingFragmentActivation { Index = index, Context = wave });
                }
                changed++;
            }
            if (changed > 0)
            {
                m_state = DestructibleState.Fractured;
                // Support loss can release pieces beyond the sphere. Only the entries
                // directly selected by this wave must stay inside its authored radius.
                ReleaseUnsupportedAfterFragmentChange(wave, -1, true, out _);
                Broken?.Invoke(wave);
                OnBrokenEvent?.Invoke();
            }
            return changed > 0 || nearest < radius;
        }
    }
}
