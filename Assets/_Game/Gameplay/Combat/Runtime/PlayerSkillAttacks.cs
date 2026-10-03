using System.Collections;
using System.Collections.Generic;
using Animancer;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Combat
{
    /// <summary>
    /// Carries out attack skills for <see cref="PlayerCombatSkills"/>: single-target strikes, multi-hit strikes and
    /// area blasts around the caster. Damage is resolved where combat is authoritative (server or offline) through
    /// <see cref="PlayerMeleeCombat.ResolveHit"/>; the cast animation and VFX play on every peer. Also plays the
    /// hold-to-charge pose for charged skills on the local player.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMeleeCombat), typeof(PlayerCombatSkills))]
    public sealed class PlayerSkillAttacks : NetworkBehaviour, ISkillAttackExecutor
    {
        private const int SkillComboStep = 3; // skills hit with the finisher's impact feel
        private const int MaxAreaColliders = 64;
        private const float ChargeHandoffSeconds = 0.75f;

        [Header("Animation")]
        [SerializeField] private AnimancerComponent animancer;
        [SerializeField] private PlayerAnimancerMotor animancerMotor;
        [SerializeField, Min(0.01f)] private float castCrossFade = 0.1f;
        [Tooltip("Humanoid foot IK on skill clips. They come from the Grruzam set and sink/hover on this rig without it.")]
        [SerializeField] private bool castFootIK = true;

        [Header("Targeting")]
        [Tooltip("Added to a single-target skill's reach to allow for the target's body radius.")]
        [SerializeField, Min(0f)] private float rangeTolerance = 0.6f;
        [Tooltip("A hit still lands if the target drifted this much beyond reach between cast and impact.")]
        [SerializeField, Min(1f)] private float impactRangeLeniency = 1.5f;
        [SerializeField] private LayerMask areaTargetLayers = ~0;

        private static readonly Collider[] AreaColliders = new Collider[MaxAreaColliders];
        private readonly List<IDamageable> areaTargets = new List<IDamageable>();
        private readonly HashSet<Transform> areaSeen = new HashSet<Transform>();

        private PlayerMeleeCombat melee;
        private PointAndClickPlayer player;
        private PlayerSkillHotbar hotbar;
        private CharacterBuffManager buffManager;
        private Coroutine executeRoutine;
        private Coroutine presentRoutine;
        private float castBusyUntil = float.NegativeInfinity;

        public bool IsCasting => Time.time < castBusyUntil;

        private bool IsLocalPlayerPresentation => !IsSpawned || IsOwner;

        private void Awake()
        {
            melee = GetComponent<PlayerMeleeCombat>();
            player = GetComponent<PointAndClickPlayer>();
            hotbar = GetComponent<PlayerSkillHotbar>();
            buffManager = GetComponent<CharacterBuffManager>();
            if (animancerMotor == null) animancerMotor = GetComponent<PlayerAnimancerMotor>();
            // The player has two Animancers (the Visuals root and the humanoid rig under it). Skills must play on the
            // rig the locomotion motor drives, not on whichever one a child search finds first.
            if (animancer == null && animancerMotor != null) animancer = animancerMotor.Animancer;
            if (animancer == null) animancer = GetComponentInChildren<AnimancerComponent>();
        }

        private void OnEnable()
        {
            if (hotbar == null) return;
            hotbar.ChargeStarted += OnChargeStarted;
            hotbar.ChargeEnded += OnChargeEnded;
        }

        private void OnDisable()
        {
            if (hotbar == null) return;
            hotbar.ChargeStarted -= OnChargeStarted;
            hotbar.ChargeEnded -= OnChargeEnded;
        }

        // ------------------------------------------------------------------ ISkillAttackExecutor

        public Transform ResolveTarget(SkillDefinitionSO skill)
        {
            if (skill == null || skill.IsAreaAttack) return null;
            if (player != null && player.SelectedTarget != null) return player.SelectedTarget;
            return melee.CurrentTarget != null ? melee.CurrentTarget.Transform : null;
        }

        public bool CanExecute(SkillDefinitionSO skill, int rank, Transform target, out string reason)
        {
            reason = string.Empty;
            if (skill.IsAreaAttack) return true; // hits whatever is in range; an empty swing is allowed

            if (target == null) { reason = "Select a target first."; return false; }
            var damageable = target.GetComponentInParent<IDamageable>();
            if (damageable == null || !damageable.IsAlive) { reason = "That target can't be attacked."; return false; }
            if (!CombatHostility.IsHostile(transform, damageable)) { reason = "You can't attack that target."; return false; }

            float reach = skill.GetRange(rank) + rangeTolerance;
            float distance = FlatDistance(damageable.Transform.position);
            if (distance > reach)
            {
                reason = $"Out of range ({distance:0.0} m, reach {skill.GetRange(rank):0.0} m).";
                return false;
            }
            return true;
        }

        public void Execute(SkillDefinitionSO skill, int rank, Transform target)
        {
            if (executeRoutine != null) StopCoroutine(executeRoutine);
            executeRoutine = StartCoroutine(ExecuteRoutine(skill, rank, target));
        }

        public void Present(SkillDefinitionSO skill, int rank, Transform target)
        {
            SpawnAtCaster(skill.castVfxPrefab, skill.vfxOffset);
            var feel = ClientPresentationRegistry.Feel;
            if (feel != null)
            {
                feel.SkillCast(transform, skill.skillId, skill.IsAttack, IsLocalPlayerPresentation);
                // The blade trails for as long as the cast keeps the caster busy.
                if (skill.IsAttack) feel.Swing(transform, SkillComboStep, IsLocalPlayerPresentation, skill.CastLockDuration);
            }

            // Buffs without a cast clip stay instant and usable on the move, as before.
            float castLock = skill.CastLockDuration;
            if (castLock <= 0f) return;
            castBusyUntil = Time.time + castLock;
            melee.HoldForSkill(castLock, target != null ? target.position : (Vector3?)null);
            if (skill.IsAttack && IsLocalPlayerPresentation && animancerMotor != null && !animancerMotor.IsWeaponDrawn)
                animancerMotor.RequestSetWeaponDraw(true);

            if (skill.castClip == null || animancer == null) return;
            if (animancerMotor != null && animancerMotor.CurrentState == CharacterAnimState.Jumping) return;
            if (presentRoutine != null) StopCoroutine(presentRoutine);
            presentRoutine = StartCoroutine(PresentRoutine(skill));
        }

        // ------------------------------------------------------------------ damage (server / offline)

        private IEnumerator ExecuteRoutine(SkillDefinitionSO skill, int rank, Transform target)
        {
            int hits = Mathf.Max(1, skill.hitCount);
            float perHit = skill.GetDamageMultiplier(rank) / hits;
            float range = skill.GetRange(rank);
            var single = target != null ? target.GetComponentInParent<IDamageable>() : null;
            bool empowerSpent = false;
            float elapsed = 0f;

            for (int i = 0; i < hits; i++)
            {
                float delay = skill.GetHitDelay(i);
                while (elapsed < delay)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                bool landed = false;
                string label = hits > 1 ? $"{skill.displayName} ({i + 1}/{hits})" : skill.displayName;
                if (skill.IsAreaAttack)
                {
                    CollectAreaTargets(range);
                    foreach (var victim in areaTargets) landed |= ResolveSkillHit(skill, victim, perHit, label);
                }
                else if (single != null && single.IsAlive
                         && FlatDistance(single.Transform.position) <= (range + rangeTolerance) * impactRangeLeniency)
                {
                    landed = ResolveSkillHit(skill, single, perHit, label);
                }

                // One-hit buffs (Spiritual Rage) empower only the first hit that lands.
                if (landed && !empowerSpent)
                {
                    empowerSpent = true;
                    buffManager?.ConsumeNextHitBuffs();
                }
            }
            executeRoutine = null;
        }

        private bool ResolveSkillHit(SkillDefinitionSO skill, IDamageable victim, float multiplier, string label)
        {
            if (!melee.ResolveHit(victim, multiplier, SkillComboStep, label, out var result) || result.IsEvaded) return false;
            Vector3 position = victim.Transform.position + Vector3.up;
            if (IsSpawned) SkillHitVfxClientRpc(skill.skillId, position);
            else SpawnSkillHitVfxLocal(skill, position);
            return true;
        }

        private void CollectAreaTargets(float radius)
        {
            areaTargets.Clear();
            areaSeen.Clear();
            int count = Physics.OverlapSphereNonAlloc(transform.position, radius, AreaColliders, areaTargetLayers,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var victim = AreaColliders[i].GetComponentInParent<IDamageable>();
                if (victim == null || !victim.IsAlive || !areaSeen.Add(victim.Transform)) continue;
                if (CombatHostility.IsHostile(transform, victim)) areaTargets.Add(victim);
            }
        }

        private float FlatDistance(Vector3 point)
        {
            Vector3 offset = point - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        // ------------------------------------------------------------------ presentation (every peer)

        private IEnumerator PresentRoutine(SkillDefinitionSO skill)
        {
            if (animancerMotor != null) animancerMotor.SetAttackingState(true);
            var state = animancer.Play(skill.castClip, castCrossFade);
            state.Speed = skill.castClipSpeed;
            state.Time = 0f;
            state.ApplyFootIK = castFootIK;

            yield return new WaitForSeconds(skill.CastLockDuration);

            presentRoutine = null;
            ReleaseBody();
        }

        /// <summary>Hands the body back to locomotion unless the basic combo is about to take over again.</summary>
        private void ReleaseBody()
        {
            if (animancerMotor != null && !melee.IsAttacking) animancerMotor.SetAttackingState(false);
        }

        [ClientRpc]
        private void SkillHitVfxClientRpc(string skillId, Vector3 position)
        {
            var skill = GetComponent<PlayerCombatSkills>().GetSkill(skillId);
            if (skill != null) SpawnSkillHitVfxLocal(skill, position);
        }

        private void SpawnSkillHitVfxLocal(SkillDefinitionSO skill, Vector3 position)
        {
            var pool = NetworkCombatVFXPool.Instance;
            if (pool != null && skill.hitVfxPrefab != null) pool.Spawn(skill.hitVfxPrefab, position, Quaternion.identity);
            ClientPresentationRegistry.Feel?.SkillHit(transform, skill.skillId, position);
        }

        private GameObject SpawnAtCaster(GameObject prefab, Vector3 offset, float lifetime = 0f)
        {
            var pool = NetworkCombatVFXPool.Instance;
            if (pool == null || prefab == null) return null;
            Quaternion facing = melee.Facing;
            return pool.Spawn(prefab, transform.position + facing * offset, facing, lifetime);
        }

        // ------------------------------------------------------------------ hold-to-charge (local player only)

        private void OnChargeStarted(SkillDefinitionSO skill)
        {
            melee.HoldForSkill(skill.chargeTime);
            SpawnAtCaster(skill.chargeVfxPrefab, skill.vfxOffset, skill.chargeTime);
            if (skill.chargeLoopClip == null || animancer == null) return;
            if (animancerMotor != null && animancerMotor.CurrentState == CharacterAnimState.Jumping) return;
            if (presentRoutine != null) { StopCoroutine(presentRoutine); presentRoutine = null; }
            if (animancerMotor != null) animancerMotor.SetAttackingState(true);
            animancer.Play(skill.chargeLoopClip, castCrossFade).ApplyFootIK = castFootIK;
        }

        private void OnChargeEnded(SkillDefinitionSO skill, bool completed)
        {
            if (presentRoutine != null) return; // the cast's own presentation already owns the body
            if (!completed) ReleaseBody();
            else presentRoutine = StartCoroutine(ReleaseAfter(ChargeHandoffSeconds));
        }

        /// <summary>
        /// A completed charge hands over to the cast animation. If none starts (no cast clip, or a remote server
        /// rejected the cast), this returns the body to locomotion instead of leaving it in the charge pose.
        /// </summary>
        private IEnumerator ReleaseAfter(float seconds)
        {
            yield return new WaitForSeconds(seconds);
            presentRoutine = null;
            ReleaseBody();
        }
    }
}
