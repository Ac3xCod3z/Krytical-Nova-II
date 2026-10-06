using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Persistence;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// A player's Crew: founding one, inviting, answering, leaving, and (by rank) running it. Everything is a
    /// request; <see cref="CrewService"/> on the server decides and keeps the Crews. Every machine can read the
    /// player's <see cref="Tag"/> (Crew name, symbol, rank, title); only the owner is sent the roster.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCrew : NetworkBehaviour
    {
        private const string PromptId = "crew";

        [SerializeField] private CrewSettings settings;

        private readonly NetworkVariable<CrewTag> netTag = new NetworkVariable<CrewTag>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlayerIdentity identity;
        private PlayerCharacterProgression progression;
        private PlayerWeaponClass weaponClass;
        private PlayerChat chat;
        private PlayerWallet wallet;
        private PlayerSaveAgent saveAgent;
        private bool arrived;

        // Server only: the invitation waiting for this player's answer.
        private string invitedToCrew;
        private string invitedBy;
        private float inviteExpires;

        // Owner only: what the server sent last.
        private CrewHeader header;
        private CrewMemberView[] roster = Array.Empty<CrewMemberView>();

        /// <summary>Raised on the owner's machine when its Crew or roster changes.</summary>
        public event Action Changed;
        /// <summary>Raised on every machine when what this player shows of their Crew changes.</summary>
        public event Action TagChanged;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerCrew Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerCrew>() : null;

        public CrewSettings Settings => settings;
        public PlayerWallet Wallet => wallet;
        public string CharacterId => identity != null ? identity.CharacterId : string.Empty;
        public string MemberName => identity != null ? identity.CharacterName : name;
        public FactionId Faction => identity != null ? identity.Faction : FactionId.None;
        public int Level => progression != null ? progression.CurrentLevel : 1;
        public int WeaponClass => weaponClass != null ? (int)weaponClass.ActiveClass : 0;

        /// <summary>What everyone sees: the Crew's name and symbol, this player's rank and title. Empty name = no Crew.</summary>
        public CrewTag Tag => IsSpawned ? netTag.Value : default;

        /// <summary>The owner's Crew as last told by the server.</summary>
        public CrewHeader Header => header;
        public bool InCrew => header.exists;
        public IReadOnlyList<CrewMemberView> Roster => roster;

        /// <summary>The roster line of the member called <paramref name="memberName"/>, when there is one.</summary>
        public bool TryFindMember(string memberName, out CrewMemberView member)
        {
            foreach (CrewMemberView line in roster)
            {
                if (!string.Equals(line.name.ToString(), memberName, StringComparison.OrdinalIgnoreCase)) continue;
                member = line;
                return true;
            }
            member = default;
            return false;
        }

        private void Awake()
        {
            identity = GetComponent<PlayerIdentity>();
            progression = GetComponent<PlayerCharacterProgression>();
            weaponClass = GetComponent<PlayerWeaponClass>();
            chat = GetComponent<PlayerChat>();
            wallet = GetComponent<PlayerWallet>();
            saveAgent = GetComponent<PlayerSaveAgent>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netTag.OnValueChanged += OnTagChanged;
            if (!IsServer || saveAgent == null) return;
            saveAgent.Loaded += OnCharacterLoaded;
            if (saveAgent.IsLoaded) OnCharacterLoaded();
        }

        public override void OnNetworkDespawn()
        {
            netTag.OnValueChanged -= OnTagChanged;
            if (saveAgent != null) saveAgent.Loaded -= OnCharacterLoaded;
            if (IsServer && arrived)
            {
                arrived = false;
                CrewService.ServerLeft(this);
            }
            header = default;
            roster = Array.Empty<CrewMemberView>();
            base.OnNetworkDespawn();
        }

        private void OnCharacterLoaded()
        {
            if (arrived) return;
            arrived = true;
            CrewService.ServerArrived(this);
        }

        private void OnTagChanged(CrewTag previous, CrewTag current) => TagChanged?.Invoke();

        // ------------------------------------------------------------------ owner -> server

        /// <summary>Found a Crew (at a Vaultkeeper). <paramref name="symbol"/> is the index of one of the preset symbols.</summary>
        public void Found(string crewName, int symbol)
        {
            if (IsSpawned && IsOwner) FoundRpc(crewName ?? string.Empty, (byte)Mathf.Clamp(symbol, 0, 255));
        }

        public void Invite(PlayerCrew target)
        {
            if (IsSpawned && IsOwner && target != null && target.IsSpawned) InviteRpc(target.NetworkObject);
        }

        public void Invite(string playerName)
        {
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(playerName)) InviteByNameRpc(playerName.Trim());
        }

        /// <summary>Answer the Crew invitation this player was sent.</summary>
        public void Respond(bool accept)
        {
            if (IsSpawned && IsOwner) RespondRpc(accept);
        }

        public void Leave()
        {
            if (IsSpawned && IsOwner) LeaveRpc();
        }

        public void Expel(string memberId)
        {
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(memberId)) ExpelRpc(memberId);
        }

        public void SetOfficer(string memberId, bool officer)
        {
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(memberId)) OfficerRpc(memberId, officer);
        }

        public void HandOver(string memberId)
        {
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(memberId)) HandOverRpc(memberId);
        }

        public void SetTitle(string memberId, string title)
        {
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(memberId)) TitleRpc(memberId, title ?? string.Empty);
        }

        public void SetAnnouncement(string text)
        {
            if (IsSpawned && IsOwner) AnnouncementRpc(text ?? string.Empty);
        }

        public void SetSymbol(int symbol)
        {
            if (IsSpawned && IsOwner) SymbolRpc((byte)Mathf.Clamp(symbol, 0, 255));
        }

        public void Disband()
        {
            if (IsSpawned && IsOwner) DisbandRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void FoundRpc(string crewName, byte symbol) => CrewService.ServerFound(this, crewName, symbol);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void InviteRpc(NetworkObjectReference targetReference) =>
            CrewService.ServerInvite(this, targetReference.TryGet(out NetworkObject targetObject) ? targetObject.GetComponent<PlayerCrew>() : null);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void InviteByNameRpc(string playerName) => CrewService.ServerInviteByName(this, playerName);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RespondRpc(bool accept)
        {
            string crewId = invitedToCrew, inviter = invitedBy;
            bool live = !string.IsNullOrEmpty(crewId) && Time.time <= inviteExpires;
            invitedToCrew = null;
            invitedBy = null;
            ClosePromptRpc();
            if (!live) { Notify("There is no Crew invitation to answer."); return; }
            CrewService.ServerAnswerInvite(this, crewId, inviter, accept);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void LeaveRpc() => CrewService.ServerLeave(this);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ExpelRpc(string memberId) => CrewService.ServerExpel(this, memberId);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void OfficerRpc(string memberId, bool officer) => CrewService.ServerSetOfficer(this, memberId, officer);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void HandOverRpc(string memberId) => CrewService.ServerHandOver(this, memberId);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void TitleRpc(string memberId, string title) => CrewService.ServerSetTitle(this, memberId, title);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void AnnouncementRpc(string text) => CrewService.ServerSetAnnouncement(this, text);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SymbolRpc(byte symbol) => CrewService.ServerSetSymbol(this, symbol);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DisbandRpc() => CrewService.ServerDisband(this);

        // ------------------------------------------------------------------ server -> owner (used by CrewService)

        /// <summary>Server: a line in this player's chat log.</summary>
        public void Tell(string text)
        {
            if (chat != null && IsSpawned && IsServer && !string.IsNullOrEmpty(text)) chat.ServerSystem(text);
        }

        /// <summary>Server: a refusal, shown to this player as a notice.</summary>
        public void Notify(string text)
        {
            if (string.IsNullOrEmpty(text) || !IsSpawned || !IsServer) return;
            if (IsOwnedByServer) ClientPresentationRegistry.Messages?.ShowMessage(text);
            else NotifyRpc(text);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void NotifyRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        public void ServerSaveNow()
        {
            if (saveAgent != null) saveAgent.Save();
        }

        /// <summary>
        /// Server: this player's Crew as it stands (null for none): the tag everyone reads, the Crew chat channel,
        /// and the roster for the owner.
        /// </summary>
        public void ServerShow(Crew crew, CrewMemberView[] members)
        {
            if (!IsSpawned || !IsServer) return;
            var tag = new CrewTag();
            var head = new CrewHeader();
            if (crew != null)
            {
                CrewMember me = CrewRules.Find(crew, CharacterId);
                tag.crew.CopyFromTruncated(crew.name ?? string.Empty);
                tag.title.CopyFromTruncated(me != null ? me.title ?? string.Empty : string.Empty);
                tag.rank = me != null ? me.Rank : CrewRank.Member;
                tag.symbol = (byte)Mathf.Clamp(crew.symbol, 0, 255);
                head.exists = true;
                head.name = tag.crew;
                head.symbol = tag.symbol;
                head.announcement.CopyFromTruncated(crew.announcement ?? string.Empty);
                head.memberCap = (ushort)CrewRules.MemberCap(settings != null ? settings.memberCapByLevel : null, crew.level);
                head.myRank = tag.rank;
            }
            netTag.Value = tag;
            if (chat != null) chat.ServerSetGuild(crew != null ? crew.name : string.Empty);
            ViewRpc(head, members ?? Array.Empty<CrewMemberView>());
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ViewRpc(CrewHeader head, CrewMemberView[] members)
        {
            header = head;
            roster = members ?? Array.Empty<CrewMemberView>();
            Changed?.Invoke();
        }

        /// <summary>Server: asks this player to join a Crew. False while they are still answering another invitation.</summary>
        public bool ServerOfferInvite(Crew crew, string inviterName)
        {
            if (!IsSpawned || !IsServer) return false;
            if (!string.IsNullOrEmpty(invitedToCrew) && invitedToCrew != crew.id && Time.time <= inviteExpires) return false;
            float seconds = settings != null ? settings.inviteSeconds : 30f;
            invitedToCrew = crew.id;
            invitedBy = inviterName;
            inviteExpires = Time.time + seconds;
            PromptRpc(inviterName, crew.name, seconds);
            return true;
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PromptRpc(string inviterName, string crewName, float seconds) =>
            ClientPresentationRegistry.Prompts?.Show(PromptId, "CREW INVITATION", $"{inviterName} invites you to the Crew {crewName}.", seconds, Respond);

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ClosePromptRpc() => ClientPresentationRegistry.Prompts?.Hide(PromptId);
    }
}
