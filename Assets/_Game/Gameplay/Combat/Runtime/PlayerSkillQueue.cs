using KryticalNova.Networking;
using System;
using System.Collections;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Combat
{
    /// <summary>
    /// One attack skill kept for the player when it could not be cast at the press: the target was out of reach, or
    /// a swing or a cast was still playing. The player then walks into reach by itself and the skill is cast the
    /// moment it can be, as the basic attack already does. Single-hit and multi-hit skills alike.
    /// The owner asks; the server walks the player in and casts, so the walk and the cast are decided where the
    /// rest of combat is. The queue lets go only when the player gives another order (a click on the ground or on
    /// something else, Escape), asks for another skill, when either side dies, or when the target gets away.
    /// Offline (the unspawned scene avatar) the local copy does both halves.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCombatSkills))]
    public sealed class PlayerSkillQueue : NetworkBehaviour, ISkillQueue
    {
        // How often the way in is planned again: the target may be moving.
        private const float RepathSeconds = 0.15f;
        // A walk that ends on the very edge of a skill's reach may be a step short by the time the cast is checked.
        private const float LeastRing = 0.5f;
        // With no walk under way after this long there is no way in from here: the queue says so instead of waiting out its time.
        private const float NoWaySeconds = 1.5f;
        // The way in is planned again only when the place to stand has moved this far: a target standing still is walked to once.
        private const float RingMoved = 0.5f;
        private const float WalkingSpeed = 0.1f;

        [Tooltip("The furthest the target may be for a press to walk in and cast. Beyond it the press is refused as out of range.")]
        [SerializeField, Min(1f)] private float approachRange = 20f;
        [Tooltip("The walk in ends this far inside the skill's range, so the player stops on the ring instead of at its very edge.")]
        [SerializeField, Min(0f)] private float ringInset = 0.3f;
        [Tooltip("Seconds a queued skill waits before it is given up (no way in, a target that keeps its distance).")]
        [SerializeField, Min(1f)] private float timeoutSeconds = 10f;
        [Tooltip("A target that gets this many times the approach range away has got away: the queue lets go.")]
        [SerializeField, Min(1f)] private float giveUpFactor = 1.5f;

        private PlayerCombatSkills skills;
        private PointAndClickPlayer controls;
        private PlayerMeleeCombat melee;
        private PlayerSkillHotbar hotbar;
        private CombatEntity entity;
        private NavMeshAgent agent;

        // Owner side: what is waiting, and what was selected when it was asked for.
        private SkillDefinitionSO queued;
        private Transform selectionAtQueue;
        // Where the game is decided: the walk in and the cast, and whether the queue has set the player walking.
        private Coroutine run;
        private bool walkedIn;

        public SkillDefinitionSO Queued => queued;
        public event Action Changed;

        private bool Decides => !IsSpawned || IsServer;
        private bool IsMine => !IsSpawned || IsOwner;

        private void Awake()
        {
            skills = GetComponent<PlayerCombatSkills>();
            controls = GetComponent<PointAndClickPlayer>();
            melee = GetComponent<PlayerMeleeCombat>();
            hotbar = GetComponent<PlayerSkillHotbar>();
            entity = GetComponent<CombatEntity>();
            agent = GetComponent<NavMeshAgent>();
        }

        private void OnEnable()
        {
            if (hotbar != null) hotbar.CancelRequested += Clear;
            if (controls != null) controls.MoveOrdered += OnMoveOrdered;
        }

        private void OnDisable()
        {
            if (hotbar != null) hotbar.CancelRequested -= Clear;
            if (controls != null) controls.MoveOrdered -= OnMoveOrdered;
            ServerStop();
            SetQueued(null);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // Whatever the offline avatar was waiting on does not come along into a session.
            ServerStop();
            SetQueued(null);
        }

        // ------------------------------------------------------------------ the owner asks

        public bool Offer(SkillDefinitionSO skill, Transform target, bool outOfReach)
        {
            if (!IsMine || skill == null || !skill.IsAttack || skill.IsCharged) return false; // a charge is held, not queued
            if (!CanWaitFor(skill, target, outOfReach, approachRange)) return false;

            NetworkObjectReference reference = default;
            if (IsSpawned && target != null)
            {
                var targetObject = target.GetComponentInParent<NetworkObject>();
                if (targetObject == null || !targetObject.IsSpawned) return false;
                reference = targetObject;
            }

            SetQueued(skill);
            selectionAtQueue = controls != null ? controls.SelectedTarget : null;
            if (Decides) ServerBegin(skill, target);
            else QueueServerRpc(skill.skillId, reference);
            return true;
        }

        public void Clear()
        {
            if (!IsMine || queued == null) return;
            SetQueued(null);
            if (Decides) ServerStop(halt: true);
            else ClearServerRpc();
        }

        /// <summary>
        /// The rule for taking a press into the queue: a skill kept for being out of reach needs a target no further
        /// than <paramref name="range"/> away; one kept for a swing still playing needs nothing more.
        /// </summary>
        private bool CanWaitFor(SkillDefinitionSO skill, Transform target, bool outOfReach, float range)
        {
            if (!outOfReach) return true;
            return target != null && FlatDistance(target.position) <= range;
        }

        private void SetQueued(SkillDefinitionSO skill)
        {
            if (queued == skill) return;
            queued = skill;
            Changed?.Invoke();
        }

        private void Update()
        {
            // Owner: choosing something else, or nothing, is an order of its own. A click on the target itself is not.
            if (queued == null || !IsMine || controls == null) return;
            if (controls.SelectedTarget != selectionAtQueue) Clear();
        }

        // ------------------------------------------------------------------ the server walks in and casts

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void QueueServerRpc(string skillId, NetworkObjectReference targetReference)
        {
            if (!RpcBudget.Allow(this, CallKind.Combat, nameof(QueueServerRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(QueueServerRpc), skillId)) return;
            SkillDefinitionSO skill = skills.GetSkill(skillId);
            Transform target = targetReference.TryGet(out NetworkObject targetObject) ? targetObject.transform : null;
            if (skill == null || !skill.IsAttack || skill.IsCharged) { EndedRpc(string.Empty); return; }
            ServerBegin(skill, target);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ClearServerRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Combat, nameof(ClearServerRpc))) return;
            ServerStop(halt: true);
        }

        // The player's own move order (a click on the ground, a stop): the queue gives way to it.
        private void OnMoveOrdered()
        {
            if (!Decides || run == null) return;
            ServerStop();
            Ended(string.Empty);
        }

        private void ServerBegin(SkillDefinitionSO skill, Transform target)
        {
            ServerStop();
            run = StartCoroutine(ServerRun(skill, target));
        }

        /// <summary>
        /// Lets go of the walk in and the cast. With <paramref name="halt"/> the walk the queue started stops where
        /// the player stands (the player called it off); without, the player's own new order has already replaced it.
        /// </summary>
        private void ServerStop(bool halt = false)
        {
            if (run == null) return;
            StopCoroutine(run);
            run = null;
            if (halt && walkedIn && controls != null) controls.ServerStopMoving();
            walkedIn = false;
        }

        private IEnumerator ServerRun(SkillDefinitionSO skill, Transform target)
        {
            float until = Time.time + timeoutSeconds;
            float nextPath = 0f;
            float refusedSince = -1f; // since when no way in could be planned
            Vector3 orderedRing = default;
            bool paid = false, walked = false, cast = false;
            walkedIn = false;
            string said = string.Empty; // why it was let go, when the player should be told
            bool aimed = !skill.IsAreaAttack;

            while (true)
            {
                if (entity != null && !entity.IsAlive) break;
                if (aimed && target == null) break; // gone: defeated and cleared away, or out of sight
                if (Time.time > until) { said = $"{skill.displayName} was not cast: no way into range."; break; }

                CastCheck check = skills.CheckCast(skill, target, out string reason);
                if (check == CastCheck.Ready)
                {
                    if (walked && controls != null) controls.ServerStopMoving();
                    cast = true;
                    if (!skills.ServerTryCast(skill, target, out reason)) said = reason;
                    break;
                }
                if (check == CastCheck.Refused) { said = reason; break; }
                if (check == CastCheck.OutOfReach && target != null)
                {
                    if (FlatDistance(target.position) > approachRange * giveUpFactor)
                    {
                        said = $"{skill.displayName} was not cast: the target got away.";
                        break;
                    }
                    // The basic attack on the same target is already walking in, closer than any skill needs.
                    bool comboWalksIn = melee != null && melee.IsAttacking && melee.CurrentTarget != null && melee.CurrentTarget.Transform == target;
                    if (!comboWalksIn && controls != null && Time.time >= nextPath)
                    {
                        nextPath = Time.time + RepathSeconds;
                        Vector3 ring = RingPoint(target.position, skills.RangeOf(skill));
                        bool walking = IsWalking;
                        if (walking && walked && (ring - orderedRing).sqrMagnitude < RingMoved * RingMoved)
                        {
                            // On its way to where it should stand: nothing to plan again.
                        }
                        // The first step is the order and is paid for as one; the rest only steer it.
                        else if (controls.ServerMoveTo(ring, payRun: !paid))
                        {
                            paid = true;
                            walked = walkedIn = true;
                            orderedRing = ring;
                            refusedSince = -1f;
                        }
                        else if (!walking)
                        {
                            // Standing still with no route: across a gap, behind a wall, or no Flux for the run.
                            if (refusedSince < 0f) refusedSince = Time.time;
                            else if (Time.time - refusedSince > NoWaySeconds) { said = $"{skill.displayName} was not cast: no way into range."; break; }
                        }
                    }
                }
                yield return null; // Busy: the swing under way plays out
            }

            run = null;
            walkedIn = false;
            // Given up on the way in: the player does not walk on to where the target was.
            if (walked && !cast && controls != null) controls.ServerStopMoving();
            Ended(said);
        }

        /// <summary>The point of the skill's ring round the target that is nearest the player.</summary>
        private Vector3 RingPoint(Vector3 target, float range)
        {
            Vector3 toSelf = transform.position - target;
            toSelf.y = 0f;
            if (toSelf.sqrMagnitude < 0.0001f) return transform.position;
            return target + toSelf.normalized * Mathf.Max(LeastRing, range - ringInset);
        }

        private bool IsWalking => agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && agent.hasPath
                                  && agent.velocity.sqrMagnitude > WalkingSpeed * WalkingSpeed;

        private float FlatDistance(Vector3 point)
        {
            Vector3 offset = point - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        // ------------------------------------------------------------------ the owner is told

        private void Ended(string said)
        {
            if (IsSpawned) EndedRpc(said ?? string.Empty);
            else ShowEnded(said);
        }

        [Rpc(SendTo.Owner)]
        private void EndedRpc(string said) => ShowEnded(said);

        private void ShowEnded(string said)
        {
            SetQueued(null);
            if (!string.IsNullOrEmpty(said)) skills.Announce(said);
        }
    }
}
