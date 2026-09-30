using System;
using System.Collections.Generic;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// Physical trigger hitbox attached to melee weapons (e.g. Katana).
    /// Active strictly during attack swing windows to prevent distant or unintended hits.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WeaponHitbox : MonoBehaviour
    {
        [Header("Hitbox Configuration")]
        [SerializeField] private BoxCollider hitboxCollider;
        [SerializeField] private Transform ownerRoot;
        [SerializeField] private bool autoConfigureCollider = true;

        [Header("Swept Detection")]
        [Tooltip("Layers checked by the per-frame sweep between the blade's previous and current pose.")]
        [SerializeField] private LayerMask sweepLayers = ~0;
        [Tooltip("Max distance the blade tip may travel between two sweep samples. Smaller = more precise, more queries.")]
        [SerializeField, Min(0.02f)] private float sweepStepDistance = 0.15f;
        [SerializeField, Range(1, 32)] private int maxSweepSamples = 12;

        private readonly HashSet<IDamageable> hitEntitiesThisSwing = new HashSet<IDamageable>();
        private readonly Collider[] sweepBuffer = new Collider[16];
        private ulong currentAttackerNetworkId;
        private int currentComboStep;
        private bool hasLastSweepPose;
        private Vector3 lastSweepPosition;
        private Quaternion lastSweepRotation;

        public event Action<IDamageable, int> OnHitRegistered;
        public bool IsActive => hitboxCollider != null && hitboxCollider.enabled;
        public int CurrentComboStep => currentComboStep;

        private void Awake()
        {
            if (hitboxCollider == null)
            {
                hitboxCollider = GetComponent<BoxCollider>();
            }

            if (hitboxCollider != null)
            {
                hitboxCollider.isTrigger = true;
                if (autoConfigureCollider)
                {
                    // Center and extend along the Katana blade (local +Z)
                    hitboxCollider.center = new Vector3(0f, 0.01f, 0.55f);
                    hitboxCollider.size = new Vector3(0.18f, 0.18f, 1.15f);
                }
                hitboxCollider.enabled = false; // Disabled until an active attack swing window begins
            }

            if (ownerRoot == null)
            {
                ownerRoot = transform.root;
            }
        }

        public void SetOwnerRoot(Transform owner)
        {
            ownerRoot = owner;
        }

        /// <summary>
        /// Opens the active physical strike window for this combo step.
        /// </summary>
        public void BeginSwing(int comboStep = 1, ulong attackerNetworkId = 0)
        {
            currentComboStep = comboStep;
            currentAttackerNetworkId = attackerNetworkId;
            hitEntitiesThisSwing.Clear();
            hasLastSweepPose = false;

            if (hitboxCollider == null)
                hitboxCollider = GetComponent<BoxCollider>();

            if (hitboxCollider != null)
            {
                hitboxCollider.enabled = true;
            }
        }

        /// <summary>
        /// Closes the strike window. Hits will no longer register.
        /// </summary>
        public void EndSwing()
        {
            if (hitboxCollider == null)
                hitboxCollider = GetComponent<BoxCollider>();

            if (hitboxCollider != null)
            {
                hitboxCollider.enabled = false;
            }
            hitEntitiesThisSwing.Clear();
            hasLastSweepPose = false;
        }

        /// <summary>
        /// Fast swings can pass through a target between physics steps without a trigger contact,
        /// so while active, sweep the blade box from last frame's pose to this frame's (after animation).
        /// </summary>
        private void LateUpdate()
        {
            if (!IsActive) return;

            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;

            if (!hasLastSweepPose)
            {
                OverlapAtPose(position, rotation);
            }
            else
            {
                Vector3 tipOffset = Vector3.Scale(hitboxCollider.center + Vector3.forward * hitboxCollider.size.z * 0.5f, transform.lossyScale);
                float tipTravel = Vector3.Distance(lastSweepPosition + lastSweepRotation * tipOffset, position + rotation * tipOffset);
                int samples = Mathf.Clamp(Mathf.CeilToInt(tipTravel / sweepStepDistance), 1, maxSweepSamples);

                for (int i = 1; i <= samples; i++)
                {
                    float t = i / (float)samples;
                    OverlapAtPose(Vector3.Lerp(lastSweepPosition, position, t), Quaternion.Slerp(lastSweepRotation, rotation, t));
                }
            }

            lastSweepPosition = position;
            lastSweepRotation = rotation;
            hasLastSweepPose = true;
        }

        private void OverlapAtPose(Vector3 position, Quaternion rotation)
        {
            Vector3 scale = transform.lossyScale;
            Vector3 center = position + rotation * Vector3.Scale(hitboxCollider.center, scale);
            Vector3 halfExtents = Vector3.Scale(hitboxCollider.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))) * 0.5f;

            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, sweepBuffer, rotation, sweepLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                TryRegisterHit(sweepBuffer[i]);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            TryRegisterHit(other);
        }

        private void TryRegisterHit(Collider other)
        {
            if (!IsActive) return;
            if (other == null) return;

            // Never hit owner or self
            if (ownerRoot != null && (other.transform == ownerRoot || other.transform.IsChildOf(ownerRoot)))
                return;

            var damageable = other.GetComponentInParent<IDamageable>();
            if (damageable == null) return;

            if (hitEntitiesThisSwing.Contains(damageable)) return; // Hit once per swing
            if (!damageable.IsAlive) return;

            hitEntitiesThisSwing.Add(damageable);
            OnHitRegistered?.Invoke(damageable, currentComboStep);
        }

        private void OnDrawGizmosSelected()
        {
            if (hitboxCollider == null)
                hitboxCollider = GetComponent<BoxCollider>();

            if (hitboxCollider == null) return;

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = hitboxCollider.enabled ? new Color(1f, 0.2f, 0.2f, 0.6f) : new Color(0.2f, 1f, 0.2f, 0.3f);
            Gizmos.DrawWireCube(hitboxCollider.center, hitboxCollider.size);
        }
    }
}
