using System;
using KryticalNova.Combat;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// Following a Squad member: the player walks after the member by itself and stops a short way off
    /// (<see cref="FollowRules"/>, distances in <see cref="SquadSettings"/>). The owner asks; the server, which moves
    /// every player and knows where each one is however far apart they are, does the walking through the player's
    /// own move order (<see cref="PointAndClickPlayer.ServerMoveTo"/>), so the way taken, vaults and the cost of
    /// running are those of a click. It ends when the follower moves by their own order, fights, or is defeated,
    /// when the two are no longer Squad mates, when the member leaves the map or gets too far ahead.
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
        private string targetName = string.Empty;
        private float nextLook;
        private bool moving;
        private bool ordered;
        private bool fought;

        // Owner only: who the server says this player follows, or empty.
        private string shownTarget = string.Empty;

        /// <summary>Raised on the owner's machine when following starts or ends.</summary>
        public event Action Changed;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerFollow Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerFollow>() : null;

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
        private void FollowRpc(NetworkObjectReference targetReference) =>
            ServerFollow(targetReference.TryGet(out NetworkObject targetObject) ? targetObject.GetComponent<PlayerParty>() : null, null);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void FollowByNameRpc(string memberName)
        {
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

            target = member;
            targetName = name;
            moving = false;
            ordered = false;
            fought = false;
            nextLook = 0f;
            Tell($"You are following {targetName}. Move to stop.");
            FollowingRpc(targetName);
        }

        private void OnMoveOrdered() => ordered = true;
        private void OnDamaged(DamageResult result, CharacterCombatStats attacker) => fought = true;
        private void OnSkillCast(SkillDefinitionSO skill) => fought = true;

        private void Update()
        {
            if (target == null && targetName.Length == 0) return;
            if (!IsSpawned || !IsServer || Time.time < nextLook) return;
            nextLook = Time.time + settings.followRepathSeconds;

            bool present = target != null && target.IsSpawned && target.gameObject.scene == gameObject.scene;
            var facts = new FollowFacts
            {
                targetPresent = present,
                sameSquad = present && party.IsInPartyWith(target),
                followerAlive = entity == null || entity.IsAlive,
                // A jump is the player's own move as much as a click is.
                followerOrdered = ordered || (jump != null && jump.IsBusy),
                followerInCombat = fought || (melee != null && melee.IsAttacking) || (duel != null && duel.IsDuelling),
                distance = present ? DistanceTo(target) : 0f,
                moving = moving,
            };

            switch (FollowRules.Evaluate(facts, settings.followKeepDistance, settings.followResumeDistance, settings.followMaxDistance, out FollowEnd end))
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

        private void ServerEnd(FollowEnd end)
        {
            // An order of the player's own already decides where they go; anything else leaves them standing.
            if (moving && end != FollowEnd.Ordered && end != FollowEnd.Defeated) controls.ServerStopMoving();
            Tell(FollowRules.Describe(end, targetName));
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

        /// <summary>The spot just inside the keep distance, on this side of the member.</summary>
        private Vector3 StandPoint()
        {
            Vector3 back = transform.position - target.transform.position;
            back.y = 0f;
            float distance = back.magnitude;
            float standOff = settings.followKeepDistance * 0.8f;
            return distance <= standOff ? transform.position : target.transform.position + back * (standOff / distance);
        }

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
