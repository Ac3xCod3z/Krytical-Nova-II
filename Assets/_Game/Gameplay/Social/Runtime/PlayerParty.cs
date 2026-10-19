using KryticalNova.Networking;
using System;
using System.Collections.Generic;
using System.Text;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// Parties: a small group of players of one faction, close in level (<see cref="PartyRules"/>), with a chat
    /// channel of their own. One player invites, the other accepts; the first to invite leads, and only the leader
    /// invites, removes and promotes. A party of one is disbanded. The server decides all of it and sends each member
    /// the roster; the party id is replicated so every machine can tell who is grouped with whom.
    /// Parties last for the session: nothing is saved.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerParty : NetworkBehaviour, ISquadBuffTargets
    {
        private const ulong NoParty = 0;

        /// <summary>Server only. The first member leads.</summary>
        private sealed class Group
        {
            public ulong Id;
            public readonly List<PlayerParty> Members = new List<PlayerParty>(PartyRules.MaxSize);
        }

        [Tooltip("Seconds an invitation waits for an answer.")]
        [SerializeField, Min(5f)] private float inviteSeconds = 30f;
        [SerializeField] private SquadSettings settings;

        private readonly NetworkVariable<ulong> netPartyId = new NetworkVariable<ulong>(NoParty,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private static ulong lastPartyId;
        // Reused on the server for every request: they are handled one at a time on the main thread.
        private static readonly List<ChatParticipant> Online = new List<ChatParticipant>(16);
        private static readonly List<PlayerParty> OnlineParty = new List<PlayerParty>(16);
        private static readonly List<int> Found = new List<int>(4);
        private static readonly List<int> Levels = new List<int>(PartyRules.MaxSize);
        private static readonly List<bool> LevelFree = new List<bool>(PartyRules.MaxSize);

        private PlayerIdentity identity;
        private PlayerCharacterProgression progression;
        private PlayerChat chat;
        private PlayerResources resources;

        // Server only.
        private Group group;
        private PlayerParty invitedBy;      // who asked this player, until answered or expired
        private float inviteExpires;
        private bool leaving;               // on its way out of the game: no longer written to

        // Owner only: the roster the server sent last, leader first. Empty when in no party.
        private readonly List<PartyMember> roster = new List<PartyMember>(PartyRules.MaxSize);

        /// <summary>Raised on the owner's machine when its roster changes.</summary>
        public event Action Changed;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerParty Local => LocalPlayer.Get<PlayerParty>();

        public string MemberName => identity != null ? identity.CharacterName : name;
        public int Level => progression != null ? progression.CurrentLevel : 1;

        /// <summary>0 when in no party. The same on every machine.</summary>
        public ulong PartyId => IsSpawned ? netPartyId.Value : NoParty;
        public bool InParty => PartyId != NoParty;

        /// <summary>The owner's view of its party, leader first. Empty on other machines and when in no party.</summary>
        public IReadOnlyList<PartyMember> Roster => roster;
        public bool IsLeader => roster.Count > 0 && IsSpawned && roster[0].ObjectId == NetworkObjectId;

        /// <summary>True on every peer while this player and <paramref name="other"/> are in the same party.</summary>
        public bool IsInPartyWith(PlayerParty other) => other != null && other != this && InParty && other.PartyId == PartyId;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => lastPartyId = NoParty;

        private void Awake()
        {
            identity = GetComponent<PlayerIdentity>();
            progression = GetComponent<PlayerCharacterProgression>();
            chat = GetComponent<PlayerChat>();
            resources = GetComponent<PlayerResources>();
        }

        public override void OnNetworkDespawn()
        {
            // When the whole session shuts down there is nobody left to tell.
            if (IsServer && !NetworkManager.ShutdownInProgress)
            {
                leaving = true;
                invitedBy = null;
                Remove(this, $"{MemberName} left the game.", null);
            }
            roster.Clear();
            base.OnNetworkDespawn();
        }

        // ------------------------------------------------------------------ owner -> server

        /// <summary>Invite the player called <paramref name="playerName"/>, wherever they are. Call on the owner's machine.</summary>
        public void Invite(string playerName)
        {
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(playerName)) InviteByNameRpc(playerName.Trim());
        }

        /// <summary>Invite the player <paramref name="target"/> (the one selected in the world).</summary>
        public void Invite(PlayerParty target)
        {
            if (IsSpawned && IsOwner && target != null && target.IsSpawned) InviteRpc(target.NetworkObject);
        }

        /// <summary>Answer the invitation this player was sent.</summary>
        public void Respond(bool accept)
        {
            if (IsSpawned && IsOwner) RespondRpc(accept);
        }

        public void Leave()
        {
            if (IsSpawned && IsOwner) LeaveRpc();
        }

        /// <summary>Leader only: remove the member called <paramref name="playerName"/>.</summary>
        public void Kick(string playerName)
        {
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(playerName)) KickRpc(playerName.Trim());
        }

        /// <summary>Leader only: hand the party to the member called <paramref name="playerName"/>.</summary>
        public void Promote(string playerName)
        {
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(playerName)) PromoteRpc(playerName.Trim());
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void InviteByNameRpc(string playerName)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(InviteByNameRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(InviteByNameRpc), playerName)) return;
            Online.Clear();
            OnlineParty.Clear();
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent(out PlayerParty other)) continue;
                Online.Add(new ChatParticipant { ClientId = client.ClientId, Name = other.MemberName });
                OnlineParty.Add(other);
            }

            ChatRouting.FindByName(playerName, Online, Found);
            if (Found.Count == 0) { Tell($"No player named '{ChatRouting.Clean(playerName)}' is online."); return; }
            if (Found.Count > 1)
            {
                var names = new StringBuilder("Several players share that name. Invite one of: ");
                for (int i = 0; i < Found.Count; i++)
                {
                    if (i > 0) names.Append(", ");
                    names.Append(Online[Found[i]].Name).Append(ChatRouting.IdSeparator).Append(Online[Found[i]].ClientId);
                }
                Tell(names.ToString());
                return;
            }
            ServerInvite(OnlineParty[Found[0]]);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void InviteRpc(NetworkObjectReference targetReference)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(InviteRpc))) return;
            ServerInvite(targetReference.TryGet(out NetworkObject targetObject) ? targetObject.GetComponent<PlayerParty>() : null);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RespondRpc(bool accept)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(RespondRpc))) return;
            PlayerParty from = invitedBy;
            bool live = from != null && from.IsSpawned && !from.leaving && Time.time <= inviteExpires;
            invitedBy = null;
            InviteClosedRpc();
            if (!live) { Tell("There is no squad invitation to answer."); return; }

            if (!accept)
            {
                Tell($"You declined {from.MemberName}'s squad invitation.");
                from.Tell($"{MemberName} declined your squad invitation.");
                return;
            }
            if (group != null) { Tell("You are already in a squad."); return; }
            // The inviter may have joined somebody else's party, or the party may have filled, in the meantime.
            string refusal = from.group != null && from.group.Members[0] != from ? $"{from.MemberName} no longer leads a squad." : from.Refusal(this);
            if (refusal != null)
            {
                Tell("You can no longer join that squad.");
                from.Tell(refusal);
                return;
            }

            if (from.group == null)
            {
                from.group = new Group { Id = ++lastPartyId };
                from.group.Members.Add(from);
            }
            group = from.group;
            group.Members.Add(this);
            Tell($"You joined {from.MemberName}'s squad. Talk to it with /p.");
            foreach (PlayerParty member in group.Members)
                if (member != this) member.Tell($"{MemberName} joined the squad.");
            Publish(group);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void LeaveRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(LeaveRpc))) return;
            if (group == null) { Tell("You are not in a squad."); return; }
            Remove(this, $"{MemberName} left the squad.", "You left the squad.");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void KickRpc(string playerName)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(KickRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(KickRpc), playerName)) return;
            if (!TryLeadMember(playerName, out PlayerParty member)) return;
            Remove(member, $"{member.MemberName} was removed from the squad.", "You were removed from the squad.");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PromoteRpc(string playerName)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(PromoteRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(PromoteRpc), playerName)) return;
            if (!TryLeadMember(playerName, out PlayerParty member)) return;
            group.Members.Remove(member);
            group.Members.Insert(0, member);
            foreach (PlayerParty other in group.Members) other.Tell($"{member.MemberName} now leads the squad.");
            Publish(group);
        }

        // ------------------------------------------------------------------ the party (server)

        private void ServerInvite(PlayerParty target)
        {
            if (target == null || target == this) { Tell("Invite another player."); return; }
            if (group != null && group.Members[0] != this) { Tell("Only the squad leader can invite."); return; }
            if (target.group != null)
            {
                Tell(target.group == group ? $"{target.MemberName} is already in your squad." : $"{target.MemberName} is already in a squad.");
                return;
            }
            if (target.invitedBy != null && target.invitedBy != this && Time.time <= target.inviteExpires)
            { Tell($"{target.MemberName} is answering another invitation."); return; }
            string refusal = Refusal(target) ?? SocialGate.Refusal(this, target);
            if (refusal != null) { Tell(refusal); return; }

            target.invitedBy = this;
            target.inviteExpires = Time.time + inviteSeconds;
            Tell($"You invited {target.MemberName} to your squad.");
            target.Tell($"{MemberName} invites you to a squad. Join with /join, refuse with /refuse.");
            target.InvitePromptRpc(MemberName, inviteSeconds);
        }

        /// <summary>Why <paramref name="candidate"/> cannot join this player's party (or the one they would found), or null.</summary>
        private string Refusal(PlayerParty candidate)
        {
            // A Sponsor may hunt with its Recruits whatever its level: the level rule leaves it out, both ways.
            Levels.Clear();
            LevelFree.Clear();
            bool candidateFree;
            if (group == null)
            {
                Levels.Add(Level);
                LevelFree.Add(Sponsors(this, candidate));
                candidateFree = Sponsors(candidate, this);
            }
            else
            {
                foreach (PlayerParty member in group.Members)
                {
                    Levels.Add(member.Level);
                    LevelFree.Add(SponsorsOneOf(member, group.Members, candidate));
                }
                candidateFree = SponsorsOneOf(candidate, group.Members);
            }

            switch (PartyRules.Check(FactionOf(this), Levels, FactionOf(candidate), candidate.Level, LevelFree, candidateFree))
            {
                case PartyCheck.OtherFaction:
                    return $"{candidate.MemberName} is not of your faction.";
                case PartyCheck.LevelTooFar:
                    return $"{candidate.MemberName} is level {candidate.Level}: squad members must be within {PartyRules.MaxLevelGap} levels of each other.";
                case PartyCheck.Full:
                    return $"Your squad is full ({PartyRules.MaxSize} players).";
                default:
                    return null;
            }
        }

        private static FactionId FactionOf(PlayerParty player) => player.identity != null ? player.identity.Faction : default;

        /// <summary>Finds a member of the party this player leads by name; tells this player why not otherwise.</summary>
        private bool TryLeadMember(string playerName, out PlayerParty member)
        {
            member = null;
            if (group == null) { Tell("You are not in a squad."); return false; }
            if (group.Members[0] != this) { Tell("Only the squad leader can do that."); return false; }
            foreach (PlayerParty other in group.Members)
            {
                if (other == this || !string.Equals(other.MemberName, playerName, StringComparison.OrdinalIgnoreCase)) continue;
                member = other;
                return true;
            }
            Tell($"No squad member is named '{ChatRouting.Clean(playerName)}'.");
            return false;
        }

        private static void Remove(PlayerParty member, string toOthers, string toMember)
        {
            Group left = member.group;
            if (left == null) return;
            bool led = left.Members[0] == member;
            left.Members.Remove(member);
            member.group = null;
            member.PublishNone();
            member.Tell(toMember);
            foreach (PlayerParty other in left.Members) other.Tell(toOthers);

            if (left.Members.Count < 2)
            {
                foreach (PlayerParty other in left.Members)
                {
                    other.group = null;
                    other.PublishNone();
                    other.Tell("The squad was disbanded.");
                }
                left.Members.Clear();
                return;
            }
            if (led)
                foreach (PlayerParty other in left.Members) other.Tell($"{left.Members[0].MemberName} now leads the squad.");
            Publish(left);
        }

        private static void Publish(Group party)
        {
            var members = new PartyMember[party.Members.Count];
            for (int i = 0; i < members.Length; i++)
            {
                PlayerParty member = party.Members[i];
                members[i] = new PartyMember { ObjectId = member.NetworkObjectId, Name = member.MemberName, Level = member.Level };
            }
            foreach (PlayerParty member in party.Members)
            {
                member.netPartyId.Value = party.Id;
                member.RosterRpc(members);
            }
        }

        private void PublishNone()
        {
            if (leaving || !IsSpawned) return;
            netPartyId.Value = NoParty;
            RosterRpc(Array.Empty<PartyMember>());
        }

        private void Tell(string text)
        {
            if (!leaving && chat != null && !string.IsNullOrEmpty(text)) chat.ServerSystem(text);
        }

        // ------------------------------------------------------------------ server -> owner

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void RosterRpc(PartyMember[] members)
        {
            roster.Clear();
            if (members != null) roster.AddRange(members);
            Changed?.Invoke();
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void InvitePromptRpc(string inviterName, float seconds) => ClientPresentationRegistry.Party?.ShowInvite(inviterName, seconds);

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void InviteClosedRpc() => ClientPresentationRegistry.Party?.HideInvite();

        // ------------------------------------------------------------------ experience boost

        private static bool Sponsors(PlayerParty sponsor, PlayerParty recruit) =>
            sponsor != null && recruit != null && sponsor != recruit
            && sponsor.TryGetComponent(out PlayerSponsor a) && recruit.TryGetComponent(out PlayerSponsor b) && a.ServerSponsors(b);

        /// <summary>
        /// Server only: true when <paramref name="sponsor"/> is the Sponsor of one of <paramref name="members"/>
        /// (or of <paramref name="also"/>): it is in that Squad to help its Recruit, not to hunt.
        /// </summary>
        public static bool SponsorsOneOf(PlayerParty sponsor, List<PlayerParty> members, PlayerParty also = null)
        {
            foreach (PlayerParty member in members)
                if (Sponsors(sponsor, member)) return true;
            return Sponsors(sponsor, also);
        }

        /// <summary>
        /// Server only: the extra share of experience this player earns for being in a party right now
        /// (<see cref="PartyRules.ExperienceBoost"/>). Hostile kills are paid with it (HostileKillRewards): the
        /// amount is multiplied by 1 + this value. A member's map is the scene their player is in; once zones run as
        /// separate sessions, the party itself must first outlive a member travelling to another one.
        /// </summary>
        public float ServerExperienceBoost()
        {
            if (!IsSpawned || !IsServer || group == null) return 0f;
            int together = 0;
            foreach (PlayerParty member in group.Members)
                if (member.gameObject.scene == gameObject.scene) together++;
            return PartyRules.ExperienceBoost(group.Members.Count, together, settings != null ? settings.maxExperienceBoost : PartyRules.MaxExperienceBoost);
        }

        /// <summary>
        /// Server (or offline) only: this player's Squad, this player included, into <paramref name="into"/>. A
        /// player in no Squad is a Squad of one.
        /// </summary>
        public void ServerGetMembers(List<PlayerParty> into)
        {
            into.Clear();
            if (IsSpawned && IsServer && group != null) into.AddRange(group.Members);
            else into.Add(this);
        }

        // ------------------------------------------------------------------ a Stance shared with the Squad

        public int BuffShareRank => settings != null ? settings.buffShareRank : 20;
        public float BuffShareRadius => settings != null ? settings.buffShareRadius : 20f;
        public string BuffGiverName => MemberName;

        /// <summary>Server: the other living members in this map within the share radius (SquadBuffRules).</summary>
        public void ServerGetBuffTargets(List<PlayerCombatSkills> into)
        {
            into.Clear();
            if (!IsSpawned || !IsServer || group == null) return;
            if (resources != null && !resources.IsAlive) return;
            float radius = BuffShareRadius;
            foreach (PlayerParty member in group.Members)
            {
                if (member == null || member == this || member.gameObject.scene != gameObject.scene) continue;
                if (member.resources != null && !member.resources.IsAlive) continue;
                if (!SquadBuffRules.InReach(transform.position, member.transform.position, radius)) continue;
                if (member.TryGetComponent(out PlayerCombatSkills skills)) into.Add(skills);
            }
        }

        // ------------------------------------------------------------------ presentation helpers

        /// <summary>
        /// A member's health (0..1) and current level, read from their player object. False when that player is not
        /// on this machine (out of sight): the roster's level is then the last one known.
        /// </summary>
        public bool TryGetVitals(in PartyMember member, out float health, out int level)
        {
            health = 0f;
            level = member.Level;
            if (!IsSpawned || !NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(member.ObjectId, out NetworkObject other)) return false;
            if (other.TryGetComponent(out PlayerCharacterProgression theirs)) level = theirs.CurrentLevel;
            if (!other.TryGetComponent(out CombatEntity entity)) return false;
            float max = entity.MaxHealth;
            health = max > 0f ? Mathf.Clamp01(entity.CurrentHealth / max) : 0f;
            return true;
        }
    }
}
