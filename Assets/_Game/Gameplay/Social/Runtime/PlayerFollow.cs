using KryticalNova.Networking;
using System;
using KryticalNova.Combat;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.Social
{
    /// <summary>
    /// Following a Squad member: the player walks after the member by itself and stops a short way off
    /// (<see cref="FollowRules"/>, distances in <see cref="SquadSettings"/>). The owner asks; the server, which moves
    /// every player and knows where each one is however far apart they are, does the walking through the player's
    /// own move order (<see cref="PointAndClickPlayer.ServerMoveTo"/>), so the way taken, vaults and the cost of
    /// running are those of a click. The member's jumps are copied (owner, 15 Oct 2026): the server hears where the
    /// member took off and landed, the follower walks to the take-off and jumps to the landing with its own Jump
    /// (its rank, its Flux, its arc), so two can cross a river together. It ends when the follower moves by their own
    /// order, fights, or is defeated, when the two are no longer Squad mates, when the member leaves the map or gets
    /// too far ahead, or when a jump of the member's cannot be followed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerParty))]
    public sealed class PlayerFollow : NetworkBehaviour
    {
        [SerializeField] private SquadSettings settings;

        private PlayerParty party;
        private PlayerChat chat;
        private PointAndClickPlayer controls;
        private CombatEntity entity;
        private PlayerMeleeCombat melee;
        private PlayerCombatSkills skills;
        private PlayerDuel duel;
        private PlayerJumpMotor jump;

        // Server only.
        private PlayerParty target;
        private PlayerJumpMotor targetJump;
        private string targetName = string.Empty;
        private float nextLook;
        private bool moving;
        private bool ordered;
        private bool fought;
        private readonly System.Collections.Generic.List<FollowHop> hops = new System.Collections.Generic.List<FollowHop>(4);
        private bool jumpIsFollow; // the follower's jump in the air is a copy of the member's, not an order of its own

        // Owner only: who the server says this player follows, or empty.
        private string shownTarget = string.Empty;

        /// <summary>Raised on the owner's machine when following starts or ends.</summary>
        public event Action Changed;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerFollow Local => LocalPlayer.Get<PlayerFollow>();

        /// <summary>Owner's view: the name of the member being followed, or empty.</summary>
        public string Following => shownTarget;
        public bool IsFollowing => shownTarget.Length > 0;

        private void Awake()
        {
            party = GetComponent<PlayerParty>();
            chat = GetComponent<PlayerChat>();
            controls = GetComponent<PointAndClickPlayer>();
            entity = GetComponent<CombatEntity>();
            melee = GetComponent<PlayerMeleeCombat>();
            skills = GetComponent<PlayerCombatSkills>();
            duel = GetComponent<PlayerDuel>();
            jump = GetComponent<PlayerJumpMotor>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer) return;
            if (controls != null) controls.MoveOrdered += OnMoveOrdered;
            if (entity != null) entity.Damaged += OnDamaged;
            if (skills != null) skills.SkillCast += OnSkillCast;
        }

        public override void OnNetworkDespawn()
        {
            if (controls != null) controls.MoveOrdered -= OnMoveOrdered;
            if (entity != null) entity.Damaged -= OnDamaged;
            if (skills != null) skills.SkillCast -= OnSkillCast;
            Unwatch();
            target = null;
            shownTarget = string.Empty;
            base.OnNetworkDespawn();
        }

        // ------------------------------------------------------------------ owner -> server

        /// <summary>Follow the Squad member <paramref name="member"/> (in sight). Call on the owner's machine.</summary>
        public void Follow(PlayerParty member)
        {
            if (IsSpawned && IsOwner && member != null && member.IsSpawned) FollowRpc(member.NetworkObject);
        }

        /// <summary>Follow the Squad member called <paramref name="memberName"/>, wherever they are.</summary>
        public void Follow(string memberName)
        {
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(memberName)) FollowByNameRpc(memberName.Trim());
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void FollowRpc(NetworkObjectReference targetReference)
        {
            if (!RpcBudget.Allow(this, CallKind.Move, nameof(FollowRpc))) return;
            ServerFollow(targetReference.TryGet(out NetworkObject targetObject) ? targetObject.GetComponent<PlayerParty>() : null, null);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void FollowByNameRpc(string memberName)
        {
            if (!RpcBudget.Allow(this, CallKind.Move, nameof(FollowByNameRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(FollowByNameRpc), memberName)) return;
            PlayerParty found = null;
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent(out PlayerParty other)) continue;
                if (other == party || !string.Equals(other.MemberName, memberName, StringComparison.OrdinalIgnoreCase)) continue;
                // Two players may share a name: the one in this player's Squad is meant.
                if (found == null || party.IsInPartyWith(other)) found = other;
            }
            ServerFollow(found, ChatRouting.Clean(memberName));
        }

        // ------------------------------------------------------------------ server

        private void ServerFollow(PlayerParty member, string askedName)
        {
            if (settings == null || controls == null) return;
            bool present = member != null && member.IsSpawned;
            string name = present ? member.MemberName : askedName ?? string.Empty;
            string refusal = FollowRules.StartRefusal(present, member == party, present && party.IsInPartyWith(member),
                entity == null || entity.IsAlive, present ? DistanceTo(member) : 0f, settings.followMaxDistance, name);
            if (refusal != null) { Tell(refusal); return; }

            Unwatch();
            target = member;
            targetJump = member.GetComponent<PlayerJumpMotor>();
            if (targetJump != null) targetJump.JumpStarted += OnLeaderJump;
            targetName = name;
            moving = false;
            ordered = false;
            fought = false;
            jumpIsFollow = false;
            nextLook = 0f;
            Tell($"You are following {targetName}. Move to stop.");
            FollowingRpc(targetName);
        }

        private void OnMoveOrdered() => ordered = true;

        private void OnLeaderJump(Vector3 takeoff, Vector3 landing) =>
            FollowRules.Enqueue(hops, new FollowHop { takeoff = takeoff, landing = landing, at = Time.time }, settings != null ? settings.followHopQueue : 4);

        private void Unwatch()
        {
            if (targetJump != null) targetJump.JumpStarted -= OnLeaderJump;
            targetJump = null;
            hops.Clear();
            jumpIsFollow = false;
        }
        private void OnDamaged(DamageResult result, CharacterCombatStats attacker) => fought = true;
        private void OnSkillCast(SkillDefinitionSO skill) => fought = true;

        private void Update()
        {
            if (target == null && targetName.Length == 0) return;
            if (!IsSpawned || !IsServer || Time.time < nextLook) return;
            nextLook = Time.time + settings.followRepathSeconds;

            // A copied jump in the air: wait for the landing (an abort leaves the follower at the take-off; the next look re-walks).
            if (jumpIsFollow)
            {
                if (jump != null && jump.IsBusy) return;
                jumpIsFollow = false;
            }

            bool present = target != null && target.IsSpawned && target.gameObject.scene == gameObject.scene;
            var facts = new FollowFacts
            {
                targetPresent = present,
                sameSquad = present && party.IsInPartyWith(target),
                followerAlive = entity == null || entity.IsAlive,
                // A jump of the player's own is their move as much as a click is; a copied one is not.
                followerOrdered = ordered || (jump != null && jump.IsBusy && !jumpIsFollow),
                followerInCombat = fought || (melee != null && melee.IsAttacking) || (duel != null && duel.IsDuelling),
                // While a jump of the member's waits to be copied, the member may be far across a river: not too far.
                distance = present && hops.Count == 0 ? DistanceTo(target) : 0f,
                moving = moving,
            };

            FollowStep step = FollowRules.Evaluate(facts, settings.followKeepDistance, settings.followResumeDistance, settings.followMaxDistance, out FollowEnd end);
            // A jump of the member's to copy comes before the ordinary steering; a hop that is dropped lets it go on.
            if (step != FollowStep.End && hops.Count > 0 && FollowHop()) return;
            switch (step)
            {
                case FollowStep.End:
                    ServerEnd(end);
                    break;
                case FollowStep.Hold:
                    if (moving) controls.ServerStopMoving();
                    moving = false;
                    break;
                case FollowStep.Move:
                    // Setting off is an order like any other and is paid for; steering it after the member is not.
                    bool started = controls.ServerMoveTo(StandPoint(), payRun: !moving);
                    moving = moving || started;
                    break;
            }
        }

        /// <summary>
        /// The next jump of the member's: walk to its take-off, then jump to its landing with this player's own Jump.
        /// True when that took this look (the ordinary steering waits); false when the hop was dropped and the
        /// ordinary steering should go on.
        /// </summary>
        private bool FollowHop()
        {
            if (hops.Count == 0) return false;
            FollowHop hop = hops[0];
            var facts = new HopFacts
            {
                age = Time.time - hop.at,
                distanceToTakeoff = GroundDistance(hop.takeoff),
                distanceToLanding = GroundDistance(hop.landing),
                jumpReady = jump != null && jump.IsReady,
                jumping = jump != null && jump.IsBusy,
            };
            switch (FollowRules.NextHop(facts, settings.followHopReach, settings.followHopMaxAge))
            {
                case HopStep.Drop:
                    hops.RemoveAt(0);
                    return false;
                case HopStep.WalkToTakeoff:
                    bool started = controls.ServerMoveTo(hop.takeoff, payRun: !moving);
                    moving = moving || started;
                    return true;
                case HopStep.Jump:
                    if (moving) controls.ServerStopMoving();
                    moving = false;
                    if (controls.ServerJumpTo(hop.landing, out string why))
                    {
                        jumpIsFollow = true;
                        hops.RemoveAt(0);
                    }
                    else
                    {
                        hops.Clear();
                        ServerEnd(FollowEnd.JumpRefused, why);
                    }
                    return true;
                default:
                    return true; // in the air, or the landing is settling
            }
        }

        private float GroundDistance(Vector3 point)
        {
            Vector3 offset = point - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        private void ServerEnd(FollowEnd end, string detail = null)
        {
            // An order of the player's own already decides where they go; anything else leaves them standing.
            if (moving && end != FollowEnd.Ordered && end != FollowEnd.Defeated) controls.ServerStopMoving();
            Tell(detail != null ? FollowRules.Describe(end, targetName, detail) : FollowRules.Describe(end, targetName));
            Unwatch();
            target = null;
            targetName = string.Empty;
            moving = false;
            FollowingRpc(string.Empty);
        }

        private float DistanceTo(PlayerParty member)
        {
            Vector3 offset = member.transform.position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>The spot the keep distance behind the member, on this side of it.</summary>
        private Vector3 StandPoint() => FollowRules.StandPoint(transform.position, target.transform.position, settings.followKeepDistance);

        private void Tell(string text)
        {
            if (chat != null && !string.IsNullOrEmpty(text)) chat.ServerSystem(text);
        }

        // ------------------------------------------------------------------ server -> owner

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void FollowingRpc(string memberName)
        {
            shownTarget = memberName ?? string.Empty;
            Changed?.Invoke();
        }
    }
}
