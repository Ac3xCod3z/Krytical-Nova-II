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
    /// hold-to-charge pose and build-up for charged skills: at once for the player holding the key, and on the other
    /// peers through an RPC.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMeleeCombat), typeof(PlayerCombatSkills))]
    public sealed class PlayerSkillAttacks : NetworkBehaviour, ISkillAttackExecutor
    {
        private const int SkillComboStep = 3; // skills hit with the finisher's impact feel
        private const int MaxAreaColliders = 64;
        private const float ChargeHandoffSeconds = 0.75f;
        private const float ChannelCrossFade = 0.25f;
        private const float LiftSettleSpeed = 4f; // m/s the body returns to the ground if a cast ends off it

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
        private PlayerCadence cadence;
        private Coroutine executeRoutine;
        private Coroutine presentRoutine;
        private Transform model;        // the animated rig; raised and lowered by casts that leap
        private FootGroundingIK footIK;
        private Vector3 modelRest;
        private float bodyLift;
        private float castBusyUntil = float.NegativeInfinity;

        private float castCancelFrom = float.PositiveInfinity; // from here an attack may cut the current cast short

        public bool IsCasting => Time.time < castBusyUntil;

        public bool CanInterruptCast(SkillDefinitionSO next) => next != null && next.IsAttack && Time.time >= castCancelFrom;

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
            if (animancer != null)
            {
                model = animancer.transform;
                modelRest = model.localPosition;
                footIK = model.GetComponent<FootGroundingIK>();
            }
        }

        private void OnEnable()
        {
            if (hotbar == null) return;
            hotbar.ChargeStarted += OnChargeStarted;
            hotbar.ChargeEnded += OnChargeEnded;
        }

        private void OnDisable()
        {
            SetBodyLift(0f);
            if (hotbar == null) return;
            hotbar.ChargeStarted -= OnChargeStarted;
            hotbar.ChargeEnded -= OnChargeEnded;
        }

        // ------------------------------------------------------------------ ISkillAttackExecutor

        public Transform ResolveTarget(SkillDefinitionSO skill)
        {
            // An area attack hits whatever is round the caster: it has no target and never turns the body to aim (owner,
            // 23 Oct 2026, taking area skills back out of 22 Oct's "every strike faces its target").
            if (skill == null || skill.IsAreaAttack) return null;
            if (player != null && player.SelectedTarget != null) return player.SelectedTarget;
            return melee.CurrentTarget != null ? melee.CurrentTarget.Transform : null;
        }

        public bool CanExecute(SkillDefinitionSO skill, int rank, Transform target, out string reason, out bool outOfReach)
        {
            reason = string.Empty;
            outOfReach = false;
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
                outOfReach = true;
                return false;
            }
            // Up a wall or in a tree (Wallstride): a melee reach does not go up there; a ranged one does.
            if (!WithinHeight(damageable.Transform.position, skill.GetRange(rank)))
            {
                reason = "Too high above (or below) to reach.";
                return false;
            }
            return true;
        }

        public void Execute(SkillDefinitionSO skill, int rank, Transform target)
        {
            // Where the cast is decided the body turns to its target too (owner, 22 Oct 2026: every strike faces its
            // target). A dedicated server never runs Present, and its facing is the one every player is shown. Area attacks
            // do not aim (23 Oct 2026).
            if (target != null && !skill.IsAreaAttack) melee.FaceTowards(target.position);
            if (executeRoutine != null) StopCoroutine(executeRoutine);
            executeRoutine = StartCoroutine(ExecuteRoutine(skill, rank, target));
        }

        public void Present(SkillDefinitionSO skill, int rank, Transform target)
        {
            SpawnAtCaster(skill.castVfxPrefab, skill.vfxOffset);
            var feel = ClientPresentationRegistry.Feel;
            if (feel != null)
            {
                // Area attacks show their reach on the ground, timed to the moment the blow lands.
                float areaRadius = skill.IsAreaAttack ? skill.GetRange(rank) : 0f;
                feel.SkillCast(transform, skill.PresentationId, skill.IsAttack, IsLocalPlayerPresentation, areaRadius, skill.GetHitDelay(0));
                // The blade trails for as long as the cast keeps the caster busy.
                if (skill.IsAttack) feel.Swing(transform, SkillComboStep, IsLocalPlayerPresentation, skill.CastLockDuration);
            }

            // Buffs without a cast clip stay instant and usable on the move, as before.
            float castLock = skill.CastLockDuration;
            if (castLock <= 0f) return;
            castBusyUntil = Time.time + castLock;
            // A cast an attack may cut short holds the basic combo only until that point, so a strike can take over.
            float cancelDelay = skill.IsAttack ? castLock : skill.AttackCancelDelay;
            castCancelFrom = cancelDelay < castLock ? Time.time + cancelDelay : float.PositiveInfinity;
            melee.HoldForSkill(cancelDelay, target != null && !skill.IsAreaAttack ? target.position : (Vector3?)null);
            if (skill.IsAttack && IsLocalPlayerPresentation && animancerMotor != null && !animancerMotor.IsWeaponDrawn)
                animancerMotor.RequestSetWeaponDraw(true);

            if (skill.castClip == null || animancer == null) return;
            if (animancerMotor != null && animancerMotor.CurrentState == CharacterAnimState.Jumping) return;
            if (presentRoutine != null) StopCoroutine(presentRoutine);
            SetBodyLift(0f);
            bool holdsPose = skill.IsChanneled && skill.channelLoopClip != null;
            presentRoutine = StartCoroutine(holdsPose ? ChannelRoutine(skill) : PresentRoutine(skill));
        }

        public void CancelPresent()
        {
            castBusyUntil = float.NegativeInfinity;
            castCancelFrom = float.PositiveInfinity;
            melee.ReleaseSkillHold();
            if (presentRoutine != null)
            {
                StopCoroutine(presentRoutine);
                presentRoutine = null;
            }
            SetBodyLift(0f);
            ReleaseBody();
        }

        // ------------------------------------------------------------------ damage (server / offline)

        private IEnumerator ExecuteRoutine(SkillDefinitionSO skill, int rank, Transform target)
        {
            int hits = Mathf.Max(1, skill.hitCount);
            float perHit = skill.GetDamageMultiplier(rank) / hits;
            float range = skill.GetRange(rank);
            var single = target != null ? target.GetComponentInParent<IDamageable>() : null;
            float elapsed = 0f;
            // Cadence: a key pressed on the beat of the fight music makes this one cast hit a little harder.
            if (cadence == null) cadence = GetComponent<PlayerCadence>();
            bool pressedOnCadence = false;
            float onCadence = cadence != null ? cadence.ServerTake(skill, out pressedOnCadence) : 1f;
            // Redline (RedlineRules): spent on one hit, or held for all three of a three-hit attack pressed on the beat,
            // each of which then lands as a critical. Only against a target Cadence counts for. In the Cadence combo
            // (one-hit, two-hit, three-hit skill, each on the beat) steps 1 and 2 leave it alone: it waits for step 3.
            SkillKind kind = SkillKinds.Of(skill);
            bool charged = buffManager != null && buffManager.HasNextHitBuff;
            int comboStep = cadence != null && skill.IsAttack ? cadence.ServerCombo(kind, pressedOnCadence, charged) : 0;
            if (RedlineRules.SavedForTheChain(comboStep)) charged = false; // this cast does not touch it
            bool holding = RedlineRules.HoldsForEveryHit(kind, charged, pressedOnCadence && single != null && cadence.AppliesTo(single));

            for (int i = 0; i < hits; i++)
            {
                float delay = skill.GetHitDelay(i);
                while (elapsed < delay)
                {
                    elapsed += Time.deltaTime;
                    yield return null;
                }

                var outcome = HitOutcome.None;
                string label = hits > 1 ? $"{skill.displayName} ({i + 1}/{hits})" : skill.displayName;
                if (skill.IsAreaAttack)
                {
                    CollectAreaTargets(range);
                    foreach (var victim in areaTargets)
                    {
                        HitOutcome one = ResolveSkillHit(skill, victim, perHit, label, CadenceFor(victim, onCadence));
                        if (one > outcome) outcome = one;
                    }
                }
                else if (single != null && single.IsAlive
                         && FlatDistance(single.Transform.position) <= (range + rangeTolerance) * impactRangeLeniency
                         && WithinHeight(single.Transform.position, range))
                {
                    outcome = ResolveSkillHit(skill, single, perHit, label, CadenceFor(single, onCadence), forceCrit: holding);
                }

                if (charged && RedlineRules.AfterHit(kind, holding, outcome, i, hits) == ChargeStep.Spend)
                {
                    charged = false;
                    holding = false;
                    buffManager.ConsumeNextHitBuffs();
                }
            }
            executeRoutine = null;
        }

        private float CadenceFor(IDamageable victim, float onCadence) =>
            onCadence > 1f && cadence != null && cadence.AppliesTo(victim) ? onCadence : 1f;

        private HitOutcome ResolveSkillHit(SkillDefinitionSO skill, IDamageable victim, float multiplier, string label, float damageScale = 1f,
            bool forceCrit = false)
        {
            if (!melee.ResolveHit(victim, multiplier, SkillComboStep, label, out var result, skill.canCrit, fromSkill: true,
                    damageScale: damageScale, forceCrit: forceCrit))
                return HitOutcome.None;
            if (result.IsEvaded) return HitOutcome.Evaded;
            Vector3 position = victim.Transform.position + Vector3.up;
            if (IsSpawned) SkillHitVfxClientRpc(skill.skillId, position);
            else SpawnSkillHitVfxLocal(skill, position);
            return HitOutcome.Landed;
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

        /// <summary>A body this far above or below the reach of a skill is out of it (a player up a wall, in a tree).</summary>
        private const float HeightSlack = 1.5f;

        private bool WithinHeight(Vector3 point, float range) => Mathf.Abs(point.y - transform.position.y) <= range + HeightSlack;

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
            // A quick clip waits in the ready pose so its hits land on the timing every class shares (castClipDelay).
            if (skill.castClipDelay > 0f) yield return new WaitForSeconds(skill.castClipDelay);
            var state = animancer.Play(skill.castClip, castCrossFade);
            state.Speed = skill.castClipSpeed;
            state.Time = skill.castClipTrim; // a long wind-up starts part-way in
            state.ApplyFootIK = castFootIK;

            if (!skill.HasCastLift)
            {
                yield return new WaitForSeconds(Mathf.Max(0f, skill.CastLockDuration - skill.castClipDelay));
            }
            else
            {
                // The clip's leap is root motion, which the animator discards: put its height back on the body for
                // as long as this cast still owns it.
                float lockEnds = Time.time + Mathf.Max(0f, skill.CastLockDuration - skill.castClipDelay);
                while (Time.time < lockEnds && animancer.Layers[0].CurrentState == state)
                {
                    SetBodyLift(skill.castLiftCurve.Evaluate(state.Time));
                    yield return null;
                }
            }

            ReleaseBody();
            while (!Mathf.Approximately(bodyLift, 0f))
            {
                SetBodyLift(Mathf.MoveTowards(bodyLift, 0f, LiftSettleSpeed * Time.deltaTime));
                yield return null;
            }
            presentRoutine = null;
        }

        private void SetBodyLift(float metres)
        {
            if (model == null || Mathf.Approximately(metres, bodyLift)) return;
            bodyLift = metres;
            model.localPosition = modelRest + Vector3.up * metres;
            if (footIK != null) footIK.BodyLift = metres;
        }

        /// <summary>
        /// A channeled skill holds a pose of its own for as long as the channel lasts. The cast clip brings the body
        /// into it, the loop keeps it there until the buff is gone (run out, or cut short by damage or a move command),
        /// and the cast clip played backwards brings the body out again. A channel cut short by moving, jumping or
        /// attacking skips the way out: the body is already needed elsewhere.
        /// </summary>
        private IEnumerator ChannelRoutine(SkillDefinitionSO skill)
        {
            if (animancerMotor != null) animancerMotor.SetAttackingState(true);
            var enter = animancer.Play(skill.castClip, castCrossFade);
            enter.Speed = skill.castClipSpeed;
            enter.Time = 0f;
            enter.ApplyFootIK = castFootIK;

            float enterEnds = Time.time + skill.CastDuration;
            while (Time.time < enterEnds && IsChanneling(skill)) yield return null;

            if (IsChanneling(skill))
            {
                animancer.Play(skill.channelLoopClip, ChannelCrossFade).ApplyFootIK = castFootIK;
                while (IsChanneling(skill)) yield return null;
            }

            if (BodyIsFree)
            {
                var exit = animancer.Play(skill.castClip, ChannelCrossFade);
                exit.Speed = -skill.castClipSpeed;
                exit.NormalizedTime = 1f;
                exit.ApplyFootIK = castFootIK;
                float exitEnds = Time.time + skill.CastDuration;
                while (Time.time < exitEnds && BodyIsFree) yield return null;
            }

            presentRoutine = null;
            ReleaseBody();
        }

        private bool IsChanneling(SkillDefinitionSO skill) => buffManager != null && buffManager.HasBuff(skill.skillId);

        /// <summary>Nothing else has taken the body since the skill posed it: not a move, a jump or the basic combo.</summary>
        private bool BodyIsFree => !melee.IsAttacking
            && (animancerMotor == null || (animancerMotor.CurrentState == CharacterAnimState.Attacking && !animancerMotor.IsTravelling));

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
            ClientPresentationRegistry.Feel?.SkillHit(transform, skill.PresentationId, position, (int)skill.classSlot);
        }

        private GameObject SpawnAtCaster(GameObject prefab, Vector3 offset, float lifetime = 0f)
        {
            var pool = NetworkCombatVFXPool.Instance;
            if (pool == null || prefab == null) return null;
            Quaternion facing = melee.Facing;
            return pool.Spawn(prefab, transform.position + facing * offset, facing, lifetime);
        }

        // ------------------------------------------------------------------ hold-to-charge

        // The hotbar raises these on the machine holding the key; everyone else is told by RPC.
        private void OnChargeStarted(SkillDefinitionSO skill)
        {
            // A charged skill turns the body to the target it is aimed at as it starts (owner, 22 Oct 2026: "always turn
            // to face whoever or whatever you strike"), here and, through the call, on the server, whose facing everyone sees.
            // An area skill aims at nothing (23 Oct 2026).
            Transform target = !skill.IsAreaAttack && TryGetComponent(out PointAndClickPlayer click) ? click.SelectedTarget : null;
            PresentChargeStarted(skill, target != null ? target.position : (Vector3?)null);
            if (!IsSpawned) return;
            NetworkObject aimed = target != null ? target.GetComponentInParent<NetworkObject>() : null;
            if (aimed != null && aimed.IsSpawned) ChargeStartedRpc(skill.skillId, aimed, true);
            else ChargeStartedRpc(skill.skillId, default, false);
        }

        private void OnChargeEnded(SkillDefinitionSO skill, bool completed)
        {
            PresentChargeEnded(skill, completed);
            if (IsSpawned) ChargeEndedRpc(skill.skillId, completed);
        }

        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
        private void ChargeStartedRpc(string skillId, NetworkObjectReference aimedAt, bool aimed)
        {
            var skill = GetComponent<PlayerCombatSkills>().GetSkill(skillId);
            if (skill == null) return;
            Vector3? face = aimed && aimedAt.TryGet(out NetworkObject target) ? target.transform.position : (Vector3?)null;
            PresentChargeStarted(skill, face);
        }

        [Rpc(SendTo.NotOwner, InvokePermission = RpcInvokePermission.Owner)]
        private void ChargeEndedRpc(string skillId, bool completed)
        {
            var skill = GetComponent<PlayerCombatSkills>().GetSkill(skillId);
            if (skill != null) PresentChargeEnded(skill, completed);
        }

        private void PresentChargeStarted(SkillDefinitionSO skill, Vector3? facePoint = null)
        {
            melee.HoldForSkill(skill.chargeTime, facePoint);
            SpawnAtCaster(skill.chargeVfxPrefab, skill.vfxOffset, skill.chargeTime);
            ClientPresentationRegistry.Feel?.ChargeStarted(transform, skill.PresentationId, skill.chargeTime, IsLocalPlayerPresentation);
            if (skill.chargeLoopClip == null || animancer == null) return;
            if (animancerMotor != null && animancerMotor.CurrentState == CharacterAnimState.Jumping) return;
            if (presentRoutine != null) { StopCoroutine(presentRoutine); presentRoutine = null; }
            SetBodyLift(0f);
            if (animancerMotor != null) animancerMotor.SetAttackingState(true);
            animancer.Play(skill.chargeLoopClip, castCrossFade).ApplyFootIK = castFootIK;
        }

        private void PresentChargeEnded(SkillDefinitionSO skill, bool completed)
        {
            ClientPresentationRegistry.Feel?.ChargeEnded(transform, skill.PresentationId, completed);
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
