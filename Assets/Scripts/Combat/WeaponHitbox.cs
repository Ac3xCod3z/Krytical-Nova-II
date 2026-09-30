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

        private readonly HashSet<IDamageable> hitEntitiesThisSwing = new HashSet<IDamageable>();
        private ulong currentAttackerNetworkId;
        private int currentComboStep;

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
        }

        private void OnTriggerEnter(Collider other)
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
