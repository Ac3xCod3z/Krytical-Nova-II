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
        [Tooltip("Humanoid foot IK on the strike clips. They come from the Grruzam set and sink/hover ~8cm on this rig without it.")]
        [SerializeField] private bool strikeFootIK = true;
        [SerializeField, Min(0f)] private float postComboDelay = 0.45f;

        [Header("Swing Hitbox Windows (Normalized Clip Time)")]
        [SerializeField, Range(0f, 1f)] private float swing1Start = 0.22f;
        [SerializeField, Range(0f, 1f)] private float swing1End = 0.65f;
        // Strike 2's forward slash lands early (~16-20% of the clip); the rest is recovery behind the body.
        [SerializeField, Range(0f, 1f)] private float swing2Start = 0.10f;
        [SerializeField, Range(0f, 1f)] private float swing2End = 0.30f;
        [SerializeField, Range(0f, 1f)] private float swing3Start = 0.20f;
        [SerializeField, Range(0f, 1f)] private float swing3End = 0.70f;

        [Header("Slash VFX Timing (Normalized Clip Time)")]
        [Tooltip("When each strike's slash VFX fires. Kept separate from the hitbox window so the trail can sit on the visible swing.")]
        [SerializeField, Range(0f, 1f)] private float slash1VfxTime = 0.22f;
        [SerializeField, Range(0f, 1f)] private float slash2VfxTime = 0.10f;
        [SerializeField, Range(0f, 1f)] private float slash3VfxTime = 0.38f;

        [Header("Strike Speeds")]
        [SerializeField, Range(1.0f, 2.5f)] private float strike1Speed = 1.30f;
        [SerializeField, Range(1.0f, 2.5f)] private float strike2Speed = 1.30f;
        [SerializeField, Range(1.0f, 2.5f)] private float strike3Speed = 1.00f;

        [Header("Combat Ranges")]
        [Tooltip("Horizontal distance at which the combo starts. Strike 2 only reaches ~1.8m, so keep this below that.")]
        [SerializeField, Min(0.5f)] private float meleeStrikingRange = 1.6f;
        [Tooltip("The approach stops this far inside the striking range, so the player halts on the ring instead of running into the target.")]
        [SerializeField, Min(0f)] private float approachInset = 0.15f;
        [SerializeField, Min(0.5f)] private float serverMaxValidationRange = 2.7f;
        [SerializeField] private float modelForwardOffset;

        [Header("Server Strike Validation")]
        [Tooltip("Fraction of a strike's clip duration that must elapse before the server accepts the next strike.")]
        [SerializeField, Range(0.3f, 1f)] private float minCadenceTolerance = 0.7f;
        [Tooltip("Extra seconds after a strike's clip ends during which its hit may still arrive (network latency).")]
        [SerializeField, Min(0f)] private float hitLatencyGrace = 0.35f;

        [Header("Slash VFX (every swing, cosmetic)")]
        [SerializeField] private GameObject slashVfxPrefab;
        [Tooltip("Slash origin relative to the character (x = right, y = up, z = forward).")]
        [SerializeField] private Vector3 slashVfxOffset = new Vector3(0f, 1.1f, 0.2f);
        [Tooltip("Per-strike rotation of the slash arc relative to the character's facing (Z tilts the arc).")]
        [SerializeField] private Vector3[] slashStepRotations =
        {
            new Vector3(0f, 0f, 15f),   // Strike 1: forehand sweep
            new Vector3(0f, 0f, 165f),  // Strike 2: mirrored backhand
            new Vector3(0f, 0f, 70f),   // Strike 3: steep diagonal finisher
        };
        [SerializeField, Min(0.1f)] private float slashVfxScale = 1f;

        [Header("Hit VFX (confirmed, non-evaded hits)")]
        [SerializeField] private GameObject hitVfxPrefab;
        [SerializeField] private float impactVfxHeight = 1.1f;
        [Tooltip("Pulls the impact out of the target's body toward the attacker so it isn't hidden inside the mesh.")]
        [SerializeField, Min(0f)] private float impactVfxTowardAttacker = 0.35f;

        private NavMeshAgent agent;
        private CharacterCombatStats combatStats;
        private CharacterBuffManager buffManager;
        private IDamageable currentTarget;
        private Coroutine comboRoutine;
        private bool isAttacking;
        private float skillHoldUntil = float.NegativeInfinity;

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
        /// <summary>Raised where a hit is resolved (server or offline), after the damage was applied. Feeds the combat log.</summary>
        public event Action<IDamageable, DamageResult> HitResolved;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            combatStats = GetComponent<CharacterCombatStats>();
            buffManager = GetComponent<CharacterBuffManager>();
            if (animancerMotor == null) animancerMotor = GetComponent<PlayerAnimancerMotor>();
            if (weaponHitbox == null) weaponHitbox = GetComponentInChildren<WeaponHitbox>(true);
            if (animancer == null) animancer = GetComponentInChildren<AnimancerComponent>();
        }

        // Hits are handled whether or not a network session is running: offline they apply locally,
        // online HandleHitboxRegisteredHit routes them through the server.
        private void OnEnable()
        {
            if (weaponHitbox != null)
            {
                weaponHitbox.OnHitRegistered += HandleHitboxRegisteredHit;
            }
        }

        private void OnDisable()
        {
            if (weaponHitbox != null)
            {
                weaponHitbox.OnHitRegistered -= HandleHitboxRegisteredHit;
            }
        }

        private void Start()
        {
            if (weaponHitbox == null)
            {
                weaponHitbox = GetComponentInChildren<WeaponHitbox>(true);
                if (weaponHitbox != null && isActiveAndEnabled)
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
                        agent.SetDestination(ApproachPoint(currentTarget.Transform.position));
                    }
                    yield return new WaitForSeconds(0.05f);
                }

                if (currentTarget == null || !currentTarget.IsAlive) break;

                // Stop movement dead (no coasting into the swing) and face target
                StopAgent();
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

            // A skill cast owns the body until its hold ends; the combo picks up again with this strike.
            if (Time.time < skillHoldUntil)
            {
                while (Time.time < skillHoldUntil) yield return null;
                if (currentTarget == null || !currentTarget.IsAlive) yield break;
                FaceTarget(currentTarget.Transform.position);
                if (animancerMotor != null) animancerMotor.SetAttackingState(true);
            }

            // Broadcast animation to proxies. A remote owner's server copy runs this same loop (started by
            // RequestAttackEntityServerRpc), so only the server sequences strikes; the client's loop is presentation.
            if (IsSpawned && IsServer)
            {
                if (ServerTryBeginSwing(step))
                    PlayComboStrikeClientRpc(step);
            }

            PlayStrikeAnimationLocal(step, clip, speed);

            float length = clip.length / speed;
            float swingStart = (clip.length * swingStartNorm) / speed;
            float swingEnd = (clip.length * swingEndNorm) / speed;
            float slashTime = (clip.length * GetSlashVfxTime(step)) / speed;
            float elapsed = 0f;
            bool swingActive = false;
            bool slashSpawned = false;

            while (elapsed < length)
            {
                if (Time.time < skillHoldUntil) break; // a skill interrupted this strike
                elapsed += Time.deltaTime;

                if (!slashSpawned && elapsed >= slashTime)
                {
                    slashSpawned = true;
                    SpawnSlashVfxLocal(step);
                }

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
            PlayStrikeState(clip, speed);
        }

        private void PlayStrikeState(AnimationClip clip, float speed)
        {
            if (animancer == null || clip == null) return;

            var state = animancer.Play(clip, comboCrossFade);
            state.Speed = speed;
            state.Time = 0f;
            state.ApplyFootIK = strikeFootIK;
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
            // A remote owner's local hitbox runs slightly out of phase with the server's swing; the server's own
            // hitbox lands the hit, so these mismatches are expected and only logged, not shown in the combat log.
            if (!serverSwingOpen || comboStep != serverSwingStep)
            {
                Debug.Log($"[{nameof(PlayerMeleeCombat)}:{name}] Hit ignored: no open swing for combo step {comboStep}.", this);
                return false;
            }

            float sinceSwing = Time.time - serverSwingTime;
            float maxWindow = GetStrikeDuration(comboStep) + hitLatencyGrace;
            if (sinceSwing > maxWindow)
            {
                Debug.Log($"[{nameof(PlayerMeleeCombat)}:{name}] Hit ignored: step {comboStep} arrived after its window ({sinceSwing:0.00}s > {maxWindow:0.00}s).", this);
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
                PlayStrikeState(clip, speed);

                // Proxies don't run the swing windows, so time the slash the same way the owner does.
                StartCoroutine(SpawnSlashVfxDelayed(step, clip.length * GetSlashVfxTime(step) / Mathf.Max(0.01f, speed)));
            }
        }

        private float GetSlashVfxTime(int step) => step == 1 ? slash1VfxTime : step == 2 ? slash2VfxTime : slash3VfxTime;

        private IEnumerator SpawnSlashVfxDelayed(int step, float delay)
        {
            if (delay > 0f) yield return new WaitForSeconds(delay);
            SpawnSlashVfxLocal(step);
        }

        private void SpawnSlashVfxLocal(int step)
        {
            var pool = NetworkCombatVFXPool.Instance;
            if (pool == null || slashVfxPrefab == null) return;

            Quaternion stepRotation = Quaternion.identity;
            if (slashStepRotations != null && step >= 1 && step <= slashStepRotations.Length)
                stepRotation = Quaternion.Euler(slashStepRotations[step - 1]);

            // Character rotation includes modelForwardOffset; undo it so the slash follows the true facing.
            Quaternion facing = transform.rotation * Quaternion.AngleAxis(-modelForwardOffset, Vector3.up);
            Vector3 position = transform.position + facing * slashVfxOffset;

            var instance = pool.Spawn(slashVfxPrefab, position, facing * stepRotation);
            if (instance != null) instance.transform.localScale = Vector3.one * slashVfxScale;
            ClientPresentationRegistry.Feel?.Swing(transform, step, IsLocalPlayerPresentation);
        }

        private bool IsWithinStrikingRange(IDamageable target)
        {
            if (target == null) return false;
            Vector3 offset = target.Transform.position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= meleeStrikingRange * meleeStrikingRange;
        }

        /// <summary>The point on the striking ring nearest the attacker, so arrival and autoBraking aim for the ring, not the target's centre.</summary>
        private Vector3 ApproachPoint(Vector3 targetPos)
        {
            Vector3 fromTarget = transform.position - targetPos;
            fromTarget.y = 0f;
            if (fromTarget.sqrMagnitude < 0.0001f) return targetPos;
            float ring = Mathf.Max(0.1f, meleeStrikingRange - approachInset);
            return targetPos + fromTarget.normalized * ring;
        }

        private void StopAgent()
        {
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
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
            // The client picks the target; the server decides whether it may be struck (a friend never is).
            if (!CombatHostility.IsHostile(transform, target)) return;

            // Server-authoritative combo order / cadence validation (networked sessions only)
            if (IsSpawned && !ServerValidateHit(target, comboStep)) return;

            // Server-authoritative distance anti-cheat validation
            float distance = Vector3.Distance(transform.position, target.Transform.position);
            if (distance > serverMaxValidationRange)
            {
                OnCombatMessage?.Invoke($"Melee strike rejected: target out of reach ({distance:0.1}m > {serverMaxValidationRange:0.1}m).");
                return;
            }

            // Combo step damage multiplier (1.0x, 1.15x, 1.35x for finisher)
            float stepMultiplier = comboStep == 1 ? 1.0f : comboStep == 2 ? 1.15f : 1.35f;
            string stepLabel = comboStep == 3 ? "Combo Finisher" : $"Strike {comboStep}";

            if (ResolveHit(target, stepMultiplier, comboStep, stepLabel, out var result) && !result.IsEvaded)
                buffManager?.ConsumeNextHitBuffs();
        }

        /// <summary>
        /// Resolves one authoritative hit (server or offline): damage roll, reflection / shield-shatter procs, damage
        /// numbers and impact effects. Shared by basic strikes and attack skills. Returns false when nothing was resolved.
        /// </summary>
        public bool ResolveHit(IDamageable target, float damageMultiplier, int comboStep, string stepLabel, out DamageResult result,
            bool canCrit = true)
        {
            result = default;
            if (target == null || !target.IsAlive) return false;

            var attackerStats = combatStats != null ? combatStats : GetComponent<CharacterCombatStats>();
            if (attackerStats == null || target.CombatStats == null) return false;

            if (buffManager == null) buffManager = GetComponent<CharacterBuffManager>();
            var defenderBuffs = target.Transform.GetComponent<CharacterBuffManager>();

            float refChance = 0f;
            float shatChance = 0f;
            float dispelChance = 0f;
            bool defenderReflect = defenderBuffs != null && defenderBuffs.HasReflection(out refChance, out _);
            bool attackerShatter = buffManager != null && buffManager.HasShieldShatter(out shatChance, out dispelChance);

            result = CombatCalculator.CalculateAttack(
                attackerStats,
                target.CombatStats,
                damageMultiplier,
                defenderReflect,
                refChance,
                attackerShatter,
                shatChance,
                canCrit);

            target.TakeDamage(result, attackerStats);

            if (attackerShatter && dispelChance > 0f && defenderBuffs != null)
            {
                if (UnityEngine.Random.Range(0f, 100f) <= dispelChance)
                {
                    defenderBuffs.DispelStanceBuffs();
                }
            }

            ShowDamageToAttacker(target.Transform.position, result);
            HitResolved?.Invoke(target, result);

            if (!result.IsEvaded)
            {
                Vector3 toAttacker = transform.position - target.Transform.position;
                toAttacker.y = 0f;
                toAttacker = toAttacker.sqrMagnitude > 0.0001f ? toAttacker.normalized : -target.Transform.forward;

                Vector3 impactPos = target.Transform.position + Vector3.up * impactVfxHeight + toAttacker * impactVfxTowardAttacker;
                var targetNetObj = target.Transform.GetComponent<NetworkObject>();
                ulong targetId = targetNetObj != null && targetNetObj.IsSpawned ? targetNetObj.NetworkObjectId : ulong.MaxValue;
                if (IsSpawned) SpawnImpactVfxClientRpc(impactPos, toAttacker, comboStep, result.IsCritical, targetId);
                else SpawnImpactVfxLocal(impactPos, toAttacker, comboStep, result.IsCritical, target.Transform);
            }

            if (result.IsEvaded)
                TellAttacker($"{stepLabel} EVADED by {target.EntityName}!");
            else if (result.IsReflected)
                TellAttacker($"{stepLabel} REFLECTED by {target.EntityName}'s Heavenly Repulsion!");
            else if (result.IsCritical)
                TellAttacker($"CRITICAL {stepLabel} on {target.EntityName} for {result.DamageDealt:0} dmg! (HP: {target.CurrentHealth:0}/{target.MaxHealth:0})");
            else
                TellAttacker($"{stepLabel} hit {target.EntityName} for {result.DamageDealt:0} dmg! (HP: {target.CurrentHealth:0}/{target.MaxHealth:0})");
            return true;
        }

        // ------------------------------------------------------------------ the attacker's own feedback
        // Hits resolve on the server, but the damage number and the combat line belong to the player who dealt the
        // blow and to nobody else. The chat window's Combat tab is fed from HitResolved (see PlayerChat), again for
        // the players involved only.

        private const byte CriticalBit = 1;
        private const byte EvadedBit = 2;
        private const byte ReflectedBit = 4;
        private const byte ShatteredBit = 8;

        private bool AttackerIsHere => !IsSpawned || IsOwner;

        private void ShowDamageToAttacker(Vector3 position, DamageResult result)
        {
            if (AttackerIsHere)
            {
                ClientPresentationRegistry.Feedback?.ShowCombatFeedback(position, result);
                return;
            }
            byte flags = (byte)((result.IsCritical ? CriticalBit : 0) | (result.IsEvaded ? EvadedBit : 0)
                | (result.IsReflected ? ReflectedBit : 0) | (result.ShieldShattered ? ShatteredBit : 0));
            DamageFeedbackRpc(position, result.DamageDealt, result.ReflectedDamage, flags);
        }

        [Rpc(SendTo.Owner)]
        private void DamageFeedbackRpc(Vector3 position, float damage, float reflected, byte flags)
        {
            ClientPresentationRegistry.Feedback?.ShowCombatFeedback(position, new DamageResult
            {
                DamageDealt = damage,
                ReflectedDamage = reflected,
                IsCritical = (flags & CriticalBit) != 0,
                IsEvaded = (flags & EvadedBit) != 0,
                IsReflected = (flags & ReflectedBit) != 0,
                ShieldShattered = (flags & ShatteredBit) != 0,
            });
        }

        private void TellAttacker(string message)
        {
            if (AttackerIsHere) OnCombatMessage?.Invoke(message);
            else CombatMessageRpc(message);
        }

        [Rpc(SendTo.Owner)]
        private void CombatMessageRpc(string message) => OnCombatMessage?.Invoke(message);

        /// <summary>
        /// Pauses the basic combo for a skill cast: closes the open swing, halts movement and optionally turns to
        /// face a point. The combo resumes on its next strike once the hold ends.
        /// </summary>
        public void HoldForSkill(float seconds, Vector3? facePoint = null)
        {
            skillHoldUntil = Mathf.Max(skillHoldUntil, Time.time + Mathf.Max(0f, seconds));
            if (weaponHitbox != null) weaponHitbox.EndSwing();
            StopAgent();
            if (facePoint.HasValue) FaceTarget(facePoint.Value);
        }

        /// <summary>The character's true facing (root rotation without the model's forward offset).</summary>
        public Quaternion Facing => transform.rotation * Quaternion.AngleAxis(-modelForwardOffset, Vector3.up);

        /// <summary>
        /// Plays this character's impact presentation on <paramref name="target"/> without resolving any damage (GM
        /// preview). It goes through the same call a real hit does, so the two cannot drift apart.
        /// </summary>
        public void PreviewImpact(Transform target, int comboStep, bool critical)
        {
            if (target == null) return;
            Vector3 toAttacker = transform.position - target.position;
            toAttacker.y = 0f;
            // Striking yourself: the blow comes from the front.
            toAttacker = toAttacker.sqrMagnitude > 0.0001f ? toAttacker.normalized : Facing * Vector3.forward;
            // An agent's root floats its base offset above the soles; the impact height is measured from the ground.
            float rootHeight = target.TryGetComponent(out NavMeshAgent targetAgent) ? targetAgent.baseOffset : 0f;
            Vector3 impactPos = target.position + Vector3.up * (impactVfxHeight - rootHeight) + toAttacker * impactVfxTowardAttacker;
            SpawnImpactVfxLocal(impactPos, toAttacker, comboStep, critical, target);
        }

        [ClientRpc]
        private void SpawnImpactVfxClientRpc(Vector3 position, Vector3 towardAttacker, int comboStep, bool isCritical, ulong targetNetworkObjectId)
        {
            Transform target = null;
            if (targetNetworkObjectId != ulong.MaxValue && NetworkManager != null &&
                NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out var targetNetObj))
                target = targetNetObj.transform;
            SpawnImpactVfxLocal(position, towardAttacker, comboStep, isCritical, target);
        }

        private void SpawnImpactVfxLocal(Vector3 position, Vector3 towardAttacker, int comboStep, bool isCritical, Transform target)
        {
            var pool = NetworkCombatVFXPool.Instance;
            // Burst faces back toward the attacker, i.e. sparks fly off the struck surface.
            if (pool != null && hitVfxPrefab != null)
                pool.Spawn(hitVfxPrefab, position, Quaternion.LookRotation(towardAttacker, Vector3.up));

            var feel = ClientPresentationRegistry.Feel;
            if (feel == null) return;
            var targetNet = target != null ? target.GetComponent<NetworkObject>() : null;
            feel.Hit(new HitFeel
            {
                Point = position,
                TowardAttacker = towardAttacker,
                Attacker = transform,
                Target = target,
                ComboStep = comboStep,
                IsCritical = isCritical,
                AttackerIsLocalPlayer = IsLocalPlayerPresentation,
                TargetIsLocalPlayer = targetNet != null && targetNet.IsSpawned && targetNet.IsLocalPlayer,
            });
        }

        /// <summary>True on the machine whose player this is (or offline), i.e. where camera-level feedback belongs.</summary>
        private bool IsLocalPlayerPresentation => !IsSpawned || IsOwner;
    }
}
