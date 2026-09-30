using System;
using System.Collections;
using System.Collections.Generic;
using Animancer;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Combat
{
    /// <summary>
    /// Manages the 3-part Katana strike sequence with physical weapon hitbox windows,
    /// target auto-approach, looping delay, and server-authoritative multiplayer validation.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class PlayerMeleeCombat : NetworkBehaviour
    {
        [Header("Animation & Hitbox")]
        [SerializeField] private AnimancerComponent animancer;
        [SerializeField] private PlayerAnimancerMotor animancerMotor;
        [SerializeField] private WeaponHitbox weaponHitbox;

        [Header("3-Part Strike Clips")]
        [SerializeField] private AnimationClip combo1Clip;
        [SerializeField] private AnimationClip combo2Clip;
        [SerializeField] private AnimationClip combo3Clip;
        [SerializeField, Min(0.01f)] private float comboCrossFade = 0.12f;
        [SerializeField, Min(0f)] private float postComboDelay = 0.45f;

        [Header("Swing Hitbox Windows (Normalized Clip Time)")]
        [SerializeField, Range(0f, 1f)] private float swing1Start = 0.22f;
        [SerializeField, Range(0f, 1f)] private float swing1End = 0.65f;
        [SerializeField, Range(0f, 1f)] private float swing2Start = 0.22f;
        [SerializeField, Range(0f, 1f)] private float swing2End = 0.65f;
        [SerializeField, Range(0f, 1f)] private float swing3Start = 0.20f;
        [SerializeField, Range(0f, 1f)] private float swing3End = 0.70f;

        [Header("Strike Speeds")]
        [SerializeField, Range(1.0f, 2.5f)] private float strike1Speed = 1.30f;
        [SerializeField, Range(1.0f, 2.5f)] private float strike2Speed = 1.30f;
        [SerializeField, Range(1.0f, 2.5f)] private float strike3Speed = 1.00f;

        [Header("Combat Ranges")]
        [SerializeField, Min(0.5f)] private float meleeStrikingRange = 2.2f;
        [SerializeField, Min(0.5f)] private float serverMaxValidationRange = 2.7f;
        [SerializeField] private float modelForwardOffset;

        [Header("Server Strike Validation")]
        [Tooltip("Fraction of a strike's clip duration that must elapse before the server accepts the next strike.")]
        [SerializeField, Range(0.3f, 1f)] private float minCadenceTolerance = 0.7f;
        [Tooltip("Extra seconds after a strike's clip ends during which its hit may still arrive (network latency).")]
        [SerializeField, Min(0f)] private float hitLatencyGrace = 0.35f;

        [Header("Impact VFX (NetworkCombatVFXPool)")]
        [SerializeField] private string impactVfxPoolKey = "hit_impact";
        [SerializeField, Min(0.1f)] private float impactVfxLifetime = 3f;
        [SerializeField] private float impactVfxHeight = 1.1f;

        private NavMeshAgent agent;
        private CharacterCombatStats combatStats;
        private CharacterBuffManager buffManager;
        private IDamageable currentTarget;
        private Coroutine comboRoutine;
        private bool isAttacking;

        // Server-only combo state: the server, not the client, decides which strike step is currently live.
        private int serverSwingStep;
        private bool serverSwingOpen;
        private float serverSwingTime = float.NegativeInfinity;
        private float serverSwingMinInterval;
        private readonly HashSet<Transform> serverSwingHitTargets = new HashSet<Transform>();

        public bool IsAttacking => isAttacking;
        public IDamageable CurrentTarget => currentTarget;
        public float MeleeStrikingRange => meleeStrikingRange;

        public event Action<string> OnCombatMessage;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            combatStats = GetComponent<CharacterCombatStats>();
            buffManager = GetComponent<CharacterBuffManager>();
            if (animancerMotor == null) animancerMotor = GetComponent<PlayerAnimancerMotor>();
            if (weaponHitbox == null) weaponHitbox = GetComponentInChildren<WeaponHitbox>(true);
            if (animancer == null) animancer = GetComponentInChildren<AnimancerComponent>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (weaponHitbox != null)
            {
                weaponHitbox.OnHitRegistered += HandleHitboxRegisteredHit;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (weaponHitbox != null)
            {
                weaponHitbox.OnHitRegistered -= HandleHitboxRegisteredHit;
            }
            base.OnNetworkDespawn();
        }

        private void Start()
        {
            if (weaponHitbox == null)
            {
                weaponHitbox = GetComponentInChildren<WeaponHitbox>(true);
                if (weaponHitbox != null)
                {
                    weaponHitbox.OnHitRegistered += HandleHitboxRegisteredHit;
                }
            }
        }

        /// <summary>
        /// Begins or updates attack focus on a target entity.
        /// Automatically draws the Katana if sheathed.
        /// </summary>
        public void StartAttackTarget(IDamageable target)
        {
            if (target == null || !target.IsAlive)
            {
                CancelAttack();
                return;
            }

            currentTarget = target;

            // Ensure Katana is drawn for combat
            if (animancerMotor != null && !animancerMotor.IsWeaponDrawn)
            {
                animancerMotor.RequestSetWeaponDraw(true);
            }

            if (comboRoutine == null)
            {
                comboRoutine = StartCoroutine(ComboLoopRoutine());
            }
        }

        /// <summary>
        /// Cancels ongoing melee attacks and returns character to locomotion.
        /// </summary>
        public void CancelAttack()
        {
            if (comboRoutine != null)
            {
                StopCoroutine(comboRoutine);
                comboRoutine = null;
            }

            isAttacking = false;
            if (weaponHitbox != null)
            {
                weaponHitbox.EndSwing();
            }

            if (animancerMotor != null)
            {
                animancerMotor.SetAttackingState(false);
            }

            currentTarget = null;
        }

        private IEnumerator ComboLoopRoutine()
        {
            isAttacking = true;

            while (currentTarget != null && currentTarget.IsAlive)
            {
                // 1. Approach into striking range if needed
                while (currentTarget != null && currentTarget.IsAlive && !IsWithinStrikingRange(currentTarget))
                {
                    if (animancerMotor != null) animancerMotor.SetAttackingState(false);
                    if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                    {
                        agent.isStopped = false;
                        agent.SetDestination(currentTarget.Transform.position);
                    }
                    yield return new WaitForSeconds(0.05f);
                }

                if (currentTarget == null || !currentTarget.IsAlive) break;

                // Stop movement and face target
                if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.ResetPath();
                }
                FaceTarget(currentTarget.Transform.position);

                // Wait for any active draw/sheath animation to finish before swinging
                while (animancerMotor != null && animancerMotor.IsActionBusy &&
                       animancerMotor.CurrentState != CharacterAnimState.Attacking)
                {
                    yield return null;
                }

                if (animancerMotor != null) animancerMotor.SetAttackingState(true);

                // Strike 1
                yield return ExecuteStrikeStep(1, combo1Clip, swing1Start, swing1End, strike1Speed);
                if (currentTarget == null || !currentTarget.IsAlive) break;

                // Strike 2
                FaceTarget(currentTarget.Transform.position);
                yield return ExecuteStrikeStep(2, combo2Clip, swing2Start, swing2End, strike2Speed);
                if (currentTarget == null || !currentTarget.IsAlive) break;

                // Strike 3
                FaceTarget(currentTarget.Transform.position);
                yield return ExecuteStrikeStep(3, combo3Clip, swing3Start, swing3End, strike3Speed);
                if (currentTarget == null || !currentTarget.IsAlive) break;

                // Slight delay after the third hit before the combo restarts
                if (postComboDelay > 0f)
                {
                    yield return new WaitForSeconds(postComboDelay);
                }
            }

            CancelAttack();
        }

        private IEnumerator ExecuteStrikeStep(int step, AnimationClip clip, float swingStartNorm, float swingEndNorm, float speed = 1f)
        {
            if (clip == null) yield break;

            // Broadcast animation to proxies
            if (IsSpawned)
            {
                if (IsServer)
                {
                    if (ServerTryBeginSwing(step))
                        PlayComboStrikeClientRpc(step);
                }
                else
                {
                    RequestPlayComboStrikeServerRpc(step);
                }
            }

            PlayStrikeAnimationLocal(step, clip, speed);

            float length = clip.length / speed;
            float swingStart = (clip.length * swingStartNorm) / speed;
            float swingEnd = (clip.length * swingEndNorm) / speed;
            float elapsed = 0f;
            bool swingActive = false;

            while (elapsed < length)
            {
                elapsed += Time.deltaTime;

                if (!swingActive && elapsed >= swingStart && elapsed < swingEnd)
                {
                    swingActive = true;
                    if (weaponHitbox != null)
                    {
                        weaponHitbox.BeginSwing(step, IsSpawned ? NetworkObjectId : 0);
                    }
                }
                else if (swingActive && elapsed >= swingEnd)
                {
                    swingActive = false;
                    if (weaponHitbox != null)
                    {
                        weaponHitbox.EndSwing();
                    }
                }

                yield return null;
            }

            if (swingActive && weaponHitbox != null)
            {
                weaponHitbox.EndSwing();
            }
        }

        private void PlayStrikeAnimationLocal(int step, AnimationClip clip, float speed = 1f)
        {
            if (animancer != null && clip != null)
            {
                var state = animancer.Play(clip, comboCrossFade);
                state.Speed = speed;
                state.Time = 0f;
            }
        }

        [ServerRpc]
        private void RequestPlayComboStrikeServerRpc(int step)
        {
            if (ServerTryBeginSwing(step))
                PlayComboStrikeClientRpc(step);
        }

        private float GetStrikeDuration(int step)
        {
            AnimationClip clip = step == 1 ? combo1Clip : step == 2 ? combo2Clip : combo3Clip;
            float speed = step == 1 ? strike1Speed : step == 2 ? strike2Speed : strike3Speed;
            return clip != null ? clip.length / Mathf.Max(0.01f, speed) : 0f;
        }

        /// <summary>Server-side combo sequencing: validates order (1→2→3, or restart at 1) and cadence before opening a strike.</summary>
        private bool ServerTryBeginSwing(int step)
        {
            float now = Time.time;

            if (step < 1 || step > 3)
            {
                serverSwingOpen = false;
                RejectStrike($"Melee strike rejected: invalid combo step {step}.");
                return false;
            }

            if (step != 1 && step != serverSwingStep + 1)
            {
                serverSwingOpen = false;
                RejectStrike($"Melee strike rejected: combo step {step} out of order (last accepted step {serverSwingStep}).");
                return false;
            }

            float sinceLast = now - serverSwingTime;
            if (serverSwingStep != 0 && sinceLast < serverSwingMinInterval)
            {
                serverSwingOpen = false;
                RejectStrike($"Melee strike rejected: step {step} too fast ({sinceLast:0.00}s < {serverSwingMinInterval:0.00}s).");
                return false;
            }

            serverSwingStep = step;
            serverSwingOpen = true;
            serverSwingTime = now;
            serverSwingMinInterval = GetStrikeDuration(step) * minCadenceTolerance;
            serverSwingHitTargets.Clear();
            return true;
        }

        /// <summary>Validates a damage request against the currently open server-side swing.</summary>
        private bool ServerValidateHit(IDamageable target, int comboStep)
        {
            if (!serverSwingOpen || comboStep != serverSwingStep)
            {
                RejectStrike($"Melee strike rejected: no open swing for combo step {comboStep}.");
                return false;
            }

            float sinceSwing = Time.time - serverSwingTime;
            float maxWindow = GetStrikeDuration(comboStep) + hitLatencyGrace;
            if (sinceSwing > maxWindow)
            {
                RejectStrike($"Melee strike rejected: step {comboStep} hit arrived after its window ({sinceSwing:0.00}s > {maxWindow:0.00}s).");
                return false;
            }

            // One damage application per target per swing.
            return serverSwingHitTargets.Add(target.Transform);
        }

        private void RejectStrike(string message)
        {
            Debug.LogWarning($"[{nameof(PlayerMeleeCombat)}:{name}] {message}", this);
            OnCombatMessage?.Invoke(message);
        }

        [ClientRpc]
        private void PlayComboStrikeClientRpc(int step)
        {
            if (IsOwner) return; // Owner already played animation locally

            AnimationClip clip = step == 1 ? combo1Clip : step == 2 ? combo2Clip : combo3Clip;
            float speed = step == 1 ? strike1Speed : step == 2 ? strike2Speed : strike3Speed;
            if (clip != null && animancer != null)
            {
                var state = animancer.Play(clip, comboCrossFade);
                state.Speed = speed;
                state.Time = 0f;
            }
        }

        private bool IsWithinStrikingRange(IDamageable target)
        {
            if (target == null) return false;
            float dist = Vector3.Distance(transform.position, target.Transform.position);
            return dist <= meleeStrikingRange;
        }

        private void FaceTarget(Vector3 targetPos)
        {
            Vector3 dir = targetPos - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.LookRotation(dir, Vector3.up) *
                                     Quaternion.AngleAxis(modelForwardOffset, Vector3.up);
            }
        }

        private void HandleHitboxRegisteredHit(IDamageable target, int comboStep)
        {
            if (target == null || !target.IsAlive) return;

            if (IsSpawned && !IsServer)
            {
                var targetNetObj = target.Transform.GetComponent<NetworkObject>();
                if (targetNetObj != null)
                {
                    RequestMeleeStrikeDamageServerRpc(targetNetObj.NetworkObjectId, comboStep);
                    return;
                }
            }

            ExecuteAuthoritativeStrike(target, comboStep);
        }

        [ServerRpc]
        private void RequestMeleeStrikeDamageServerRpc(ulong targetNetworkObjectId, int comboStep)
        {
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var targetNetObj))
                return;

            var damageable = targetNetObj.GetComponent<IDamageable>();
            if (damageable == null) return;

            ExecuteAuthoritativeStrike(damageable, comboStep);
        }

        private void ExecuteAuthoritativeStrike(IDamageable target, int comboStep)
        {
            if (target == null || !target.IsAlive) return;

            // Server-authoritative combo order / cadence validation (networked sessions only)
            if (IsSpawned && !ServerValidateHit(target, comboStep)) return;

            // Server-authoritative distance anti-cheat validation
            float distance = Vector3.Distance(transform.position, target.Transform.position);
            if (distance > serverMaxValidationRange)
            {
                OnCombatMessage?.Invoke($"Melee strike rejected: target out of reach ({distance:0.1}m > {serverMaxValidationRange:0.1}m).");
                return;
            }

            var attackerStats = combatStats != null ? combatStats : GetComponent<CharacterCombatStats>();
            if (attackerStats == null || target.CombatStats == null) return;

            if (buffManager == null) buffManager = GetComponent<CharacterBuffManager>();
            var defenderBuffs = target.Transform.GetComponent<CharacterBuffManager>();

            float refChance = 0f;
            float shatChance = 0f;
            float dispelChance = 0f;
            bool defenderReflect = defenderBuffs != null && defenderBuffs.HasReflection(out refChance, out _);
            bool attackerShatter = buffManager != null && buffManager.HasShieldShatter(out shatChance, out dispelChance);

            // Combo step damage multiplier (1.0x, 1.15x, 1.35x for finisher)
            float stepMultiplier = comboStep == 1 ? 1.0f : comboStep == 2 ? 1.15f : 1.35f;

            var result = CombatCalculator.CalculateAttack(
                attackerStats,
                target.CombatStats,
                stepMultiplier,
                defenderReflect,
                refChance,
                attackerShatter,
                shatChance);

            target.TakeDamage(result, attackerStats);

            if (attackerShatter && dispelChance > 0f && defenderBuffs != null)
            {
                if (UnityEngine.Random.Range(0f, 100f) <= dispelChance)
                {
                    defenderBuffs.DispelStanceBuffs();
                }
            }

            ClientPresentationRegistry.Feedback?.ShowCombatFeedback(target.Transform.position, result);

            if (!result.IsEvaded)
            {
                Vector3 impactPos = target.Transform.position + Vector3.up * impactVfxHeight;
                if (IsSpawned) SpawnImpactVfxClientRpc(impactPos);
                else SpawnImpactVfxLocal(impactPos);
            }

            string stepLabel = comboStep == 3 ? "Combo Finisher" : $"Strike {comboStep}";
            if (result.IsEvaded)
                OnCombatMessage?.Invoke($"{stepLabel} EVADED by {target.EntityName}!");
            else if (result.IsReflected)
                OnCombatMessage?.Invoke($"{stepLabel} REFLECTED by {target.EntityName}'s Heavenly Repulsion!");
            else if (result.IsCritical)
                OnCombatMessage?.Invoke($"CRITICAL {stepLabel} on {target.EntityName} for {result.DamageDealt:0} dmg! (HP: {target.CurrentHealth:0}/{target.MaxHealth:0})");
            else
                OnCombatMessage?.Invoke($"{stepLabel} hit {target.EntityName} for {result.DamageDealt:0} dmg! (HP: {target.CurrentHealth:0}/{target.MaxHealth:0})");
        }

        [ClientRpc]
        private void SpawnImpactVfxClientRpc(Vector3 position)
        {
            SpawnImpactVfxLocal(position);
        }

        private void SpawnImpactVfxLocal(Vector3 position)
        {
            var pool = NetworkCombatVFXPool.Instance;
            if (pool == null || string.IsNullOrEmpty(impactVfxPoolKey)) return;
            pool.Spawn(impactVfxPoolKey, position, Quaternion.identity, impactVfxLifetime);
        }
    }
}
