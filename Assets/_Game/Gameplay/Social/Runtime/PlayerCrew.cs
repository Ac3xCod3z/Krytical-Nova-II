using KryticalNova.Networking;
using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Persistence;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using KryticalNova.Wallet;

namespace KryticalNova.Social
{
    /// <summary>
    /// A player's Crew: founding one, inviting, answering, leaving, and (by rank) running it. Everything is a
    /// request; <see cref="CrewService"/> on the server decides and keeps the Crews. Every machine can read the
    /// player's <see cref="Tag"/> (Crew name, symbol, rank, title); only the owner is sent the roster.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCrew : NetworkBehaviour, IBodyCrew, IServerLeaving
    {
        private const string PromptId = "crew";

        [SerializeField] private CrewSettings settings;

        private readonly NetworkVariable<CrewTag> netTag = new NetworkVariable<CrewTag>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlayerIdentity identity;
        private PlayerCharacterProgression progression;
        private PlayerWeaponClass weaponClass;
        private PlayerChat chat;
        private IWallet wallet;
        private PlayerSaveAgent saveAgent;
        private bool arrived;

        // Server only: the invitation waiting for this player's answer.
        private string invitedToCrew;
        private string invitedBy;
        private float inviteExpires;

        // Owner only: what the server sent last.
        private CrewHeader header;
        private CrewMemberView[] roster = Array.Empty<CrewMemberView>();

        // The entity netcode (M8 8.4.5): its bodies are never network-spawned. The server body keeps the tag in a field (NGO's netTag, read by
        // everyone, reaches every view through the PlayerCrewTag ghost field); a view shows what it was told.
        private CrewTag entityTag;
        private bool EntityView => BodyRoleTag.Of(this) is { Kind: NetRoleKind.ClientView };
        private bool EntityServer => BodyRoleTag.Of(this) is { Kind: NetRoleKind.ServerBody };
        private bool Decides => (IsSpawned && IsServer) || EntityServer;
        private IBodyRequests Requests => BodyRoleTag.RequestsOf(this) is IBodyRequests requests ? requests : null;

        private bool Ask(CrewOpKind op, string a = null, string b = null, byte b0 = 0, byte b1 = 0, byte b2 = 0, byte b3 = 0, bool flag = false)
        {
            if (!(Requests is { } requests)) return false;
            requests.CrewOp(op, a ?? string.Empty, b ?? string.Empty, b0, b1, b2, b3, flag);
            return true;
        }

        /// <summary>Raised on the owner's machine when its Crew or roster changes.</summary>
        public event Action Changed;
        /// <summary>Raised on every machine when what this player shows of their Crew changes.</summary>
        public event Action TagChanged;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerCrew Local => LocalPlayer.Get<PlayerCrew>();

        public CrewSettings Settings => settings;
        public IWallet Wallet => wallet;
        public string CharacterId => identity != null ? identity.CharacterId : string.Empty;
        public string MemberName => identity != null ? identity.CharacterName : name;
        public FactionId Faction => identity != null ? identity.Faction : FactionId.None;
        public int Level => progression != null ? progression.CurrentLevel : 1;
        public int WeaponClass => weaponClass != null ? (int)weaponClass.ActiveClass : 0;

        /// <summary>What everyone sees: the Crew's name and symbol, this player's rank and title. Empty name = no Crew.</summary>
        public CrewTag Tag => EntityServer || EntityView ? entityTag : IsSpawned ? netTag.Value : default;

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
            wallet = GetComponent<IWallet>();
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

        // ------------------------------------------------------------------ the entity netcode (M8 8.4.5)

        // The server body arrives on load and leaves before its save, as NGO's server did on spawn and despawn.
        private void Start()
        {
            if (!EntityServer || saveAgent == null) return;
            saveAgent.Loaded += OnCharacterLoaded;
            if (saveAgent.IsLoaded) OnCharacterLoaded();
        }

        public void OnServerLeaving()
        {
            if (!EntityServer || !arrived) return;
            arrived = false;
            CrewService.ServerLeft(this);
        }

        public override void OnDestroy()
        {
            if (saveAgent != null) saveAgent.Loaded -= OnCharacterLoaded;
            base.OnDestroy();
        }

        /// <summary>A server body of the entity netcode runs its owner's request (NGO's RPC bodies, after the Social limit).</summary>
        public void RunCrewOp(CrewOpKind op, string a, string b, byte b0, byte b1, byte b2, byte b3, bool flag)
        {
            if (!EntityServer) return;
            a ??= string.Empty;
            b ??= string.Empty;
            switch (op)
            {
                case CrewOpKind.Found: CrewService.ServerFound(this, a, b0, b1, tagColour: b2, tagBackground: b3); break;
                case CrewOpKind.InviteByName: if (a.Length > 0) CrewService.ServerInviteByName(this, a); break;
                case CrewOpKind.Respond: ServerRespond(flag); break;
                case CrewOpKind.Leave: CrewService.ServerLeave(this); break;
                case CrewOpKind.Expel: if (a.Length > 0) CrewService.ServerExpel(this, a); break;
                case CrewOpKind.Officer: if (a.Length > 0) CrewService.ServerSetOfficer(this, a, flag); break;
                case CrewOpKind.HandOver: if (a.Length > 0) CrewService.ServerHandOver(this, a); break;
                case CrewOpKind.Title: if (a.Length > 0) CrewService.ServerSetTitle(this, a, b); break;
                case CrewOpKind.Announcement: CrewService.ServerSetAnnouncement(this, b); break;
                case CrewOpKind.Symbol: CrewService.ServerSetSymbol(this, b0, b1); break;
                case CrewOpKind.Disband: CrewService.ServerDisband(this); break;
            }
        }

        public BodyCrewTag ShownTag
        {
            get
            {
                CrewTag tag = Tag;
                return new BodyCrewTag
                {
                    crew = tag.crew.ToString(), title = tag.title.ToString(), rank = (byte)tag.rank, symbol = tag.symbol, colour = tag.colour,
                    tagColour = tag.tagColour, tagBackground = tag.tagBackground,
                };
            }
        }

        /// <summary>Any view of the entity netcode: the tag the server gives this player (NGO's netTag, read by everyone).</summary>
        public void MirrorTag(in BodyCrewTag shown)
        {
            if (!EntityView) return;
            var tag = new CrewTag { rank = (CrewRank)shown.rank, symbol = shown.symbol, colour = shown.colour, tagColour = shown.tagColour, tagBackground = shown.tagBackground };
            tag.crew.CopyFromTruncated(shown.crew ?? string.Empty);
            tag.title.CopyFromTruncated(shown.title ?? string.Empty);
            if (tag.Equals(entityTag)) return;
            entityTag = tag;
            TagChanged?.Invoke();
        }

        /// <summary>The entity netcode's own view: the Crew window the server sent (NGO's owner ViewRpc).</summary>
        public void MirrorView(string viewText)
        {
            if (!BodyRoleTag.IsLocalClientView(this) || !CrewViewText.TryDecode(viewText, out CrewHeader head, out CrewMemberView[] members)) return;
            header = head;
            roster = members;
            Changed?.Invoke();
        }

        public void MirrorPrompt(string inviterName, string crewName, float seconds)
        {
            if (BodyRoleTag.IsLocalClientView(this)) ShowPrompt(inviterName, crewName, seconds);
        }

        public void MirrorClosePrompt()
        {
            if (BodyRoleTag.IsLocalClientView(this)) ClientPresentationRegistry.Prompts?.Hide(PromptId);
        }

        // ------------------------------------------------------------------ owner -> server

        /// <summary>Found a Crew (at a Vaultkeeper). <paramref name="symbol"/> is the index of one of the preset symbols.</summary>
        public void Found(string crewName, int symbol, int colour = 0, int tagColour = 0, int tagBackground = 0)
        {
            if (Ask(CrewOpKind.Found, crewName, null, (byte)Mathf.Clamp(symbol, 0, 255), (byte)Mathf.Clamp(colour, 0, 255), (byte)Mathf.Clamp(tagColour, 0, 255),
                (byte)Mathf.Clamp(tagBackground, 0, 255))) return;
            if (IsSpawned && IsOwner)
                FoundRpc(crewName ?? string.Empty, (byte)Mathf.Clamp(symbol, 0, 255), (byte)Mathf.Clamp(colour, 0, 255),
                    (byte)Mathf.Clamp(tagColour, 0, 255), (byte)Mathf.Clamp(tagBackground, 0, 255));
        }

        public void Invite(PlayerCrew target)
        {
            if (target != null && Ask(CrewOpKind.InviteByName, target.MemberName)) return;
            if (IsSpawned && IsOwner && target != null && target.IsSpawned) InviteRpc(target.NetworkObject);
        }

        public void Invite(string playerName)
        {
            if (!string.IsNullOrWhiteSpace(playerName) && Ask(CrewOpKind.InviteByName, playerName.Trim())) return;
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(playerName)) InviteByNameRpc(playerName.Trim());
        }

        /// <summary>Answer the Crew invitation this player was sent.</summary>
        public void Respond(bool accept)
        {
            if (Ask(CrewOpKind.Respond, flag: accept)) return;
            if (IsSpawned && IsOwner) RespondRpc(accept);
        }

        public void Leave()
        {
            if (Ask(CrewOpKind.Leave)) return;
            if (IsSpawned && IsOwner) LeaveRpc();
        }

        public void Expel(string memberId)
        {
            if (!string.IsNullOrEmpty(memberId) && Ask(CrewOpKind.Expel, memberId)) return;
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(memberId)) ExpelRpc(memberId);
        }

        public void SetOfficer(string memberId, bool officer)
        {
            if (!string.IsNullOrEmpty(memberId) && Ask(CrewOpKind.Officer, memberId, flag: officer)) return;
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(memberId)) OfficerRpc(memberId, officer);
        }

        public void HandOver(string memberId)
        {
            if (!string.IsNullOrEmpty(memberId) && Ask(CrewOpKind.HandOver, memberId)) return;
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(memberId)) HandOverRpc(memberId);
        }

        public void SetTitle(string memberId, string title)
        {
            if (!string.IsNullOrEmpty(memberId) && Ask(CrewOpKind.Title, memberId, title ?? string.Empty)) return;
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(memberId)) TitleRpc(memberId, title ?? string.Empty);
        }

        public void SetAnnouncement(string text)
        {
            if (Ask(CrewOpKind.Announcement, null, text ?? string.Empty)) return;
            if (IsSpawned && IsOwner) AnnouncementRpc(text ?? string.Empty);
        }

        /// <summary>The Leader's choice of the Crew's symbol and of the colour it is drawn in.</summary>
        public void SetSymbol(int symbol, int colour)
        {
            if (Ask(CrewOpKind.Symbol, null, null, (byte)Mathf.Clamp(symbol, 0, 255), (byte)Mathf.Clamp(colour, 0, 255))) return;
            if (IsSpawned && IsOwner) SymbolRpc((byte)Mathf.Clamp(symbol, 0, 255), (byte)Mathf.Clamp(colour, 0, 255));
        }

        public void Disband()
        {
            if (Ask(CrewOpKind.Disband)) return;
            if (IsSpawned && IsOwner) DisbandRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void FoundRpc(string crewName, byte symbol, byte colour, byte tagColour, byte tagBackground)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(FoundRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(FoundRpc), crewName)) return;
            CrewService.ServerFound(this, crewName, symbol, colour, tagColour: tagColour, tagBackground: tagBackground);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void InviteRpc(NetworkObjectReference targetReference)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(InviteRpc))) return;
            CrewService.ServerInvite(this, targetReference.TryGet(out NetworkObject targetObject) ? targetObject.GetComponent<PlayerCrew>() : null);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void InviteByNameRpc(string playerName)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(InviteByNameRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(InviteByNameRpc), playerName)) return;
            CrewService.ServerInviteByName(this, playerName);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RespondRpc(bool accept)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(RespondRpc))) return;
            ServerRespond(accept);
        }

        private void ServerRespond(bool accept)
        {
            string crewId = invitedToCrew, inviter = invitedBy;
            bool live = !string.IsNullOrEmpty(crewId) && Time.time <= inviteExpires;
            invitedToCrew = null;
            invitedBy = null;
            if (EntityServer) CrewOwnerLink.Current?.ClosePrompt(this);   // its player's prompt (M8 8.4.5)
            else ClosePromptRpc();
            if (!live) { Notify("There is no Crew invitation to answer."); return; }
            CrewService.ServerAnswerInvite(this, crewId, inviter, accept);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void LeaveRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(LeaveRpc))) return;
            CrewService.ServerLeave(this);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ExpelRpc(string memberId)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(ExpelRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(ExpelRpc), memberId)) return;
            CrewService.ServerExpel(this, memberId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void OfficerRpc(string memberId, bool officer)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(OfficerRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(OfficerRpc), memberId)) return;
            CrewService.ServerSetOfficer(this, memberId, officer);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void HandOverRpc(string memberId)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(HandOverRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(HandOverRpc), memberId)) return;
            CrewService.ServerHandOver(this, memberId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void TitleRpc(string memberId, string title)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(TitleRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(TitleRpc), memberId)) return;
            if (!RpcBudget.TextFits(this, nameof(TitleRpc), title)) return;
            CrewService.ServerSetTitle(this, memberId, title);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void AnnouncementRpc(string text)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(AnnouncementRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(AnnouncementRpc), text)) return;
            CrewService.ServerSetAnnouncement(this, text);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SymbolRpc(byte symbol, byte colour)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(SymbolRpc))) return;
            CrewService.ServerSetSymbol(this, symbol, colour);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DisbandRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(DisbandRpc))) return;
            CrewService.ServerDisband(this);
        }

        // ------------------------------------------------------------------ server -> owner (used by CrewService)

        /// <summary>Server: a line in this player's chat log.</summary>
        public void Tell(string text)
        {
            if (chat != null && Decides && !string.IsNullOrEmpty(text)) chat.ServerSystem(text);
        }

        /// <summary>Server: a refusal, shown to this player as a notice.</summary>
        public void Notify(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (EntityServer) { OwnerMessages.TryTell(this, text); return; }   // its player's message line, as NGO's NotifyRpc
            if (!IsSpawned || !IsServer) return;
            if (IsOwnedByServer) ClientPresentationRegistry.Messages?.ShowMessage(text);
            else NotifyRpc(text);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void NotifyRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        public void ServerSaveNow()
        {
            if (saveAgent != null) saveAgent.Save();
        }

        /// <summary>Server: writes this character and the world document <paramref name="world"/> (the Crews) as one change.</summary>
        public void ServerSaveWith(string world) => ServerSave.SaveTogether(saveAgent, world);

        /// <summary>
        /// Server: this player's Crew as it stands (null for none): the tag everyone reads, the Crew chat channel,
        /// and the roster for the owner.
        /// </summary>
        public void ServerShow(Crew crew, CrewMemberView[] members)
        {
            if (!Decides) return;
            var tag = new CrewTag();
            var head = new CrewHeader();
            if (crew != null)
            {
                CrewMember me = CrewRules.Find(crew, CharacterId);
                tag.crew.CopyFromTruncated(crew.name ?? string.Empty);
                tag.title.CopyFromTruncated(me != null ? me.title ?? string.Empty : string.Empty);
                tag.rank = me != null ? me.Rank : CrewRank.Member;
                tag.symbol = (byte)Mathf.Clamp(crew.symbol, 0, 255);
                tag.colour = (byte)Mathf.Clamp(crew.symbolColour, 0, 255);
                tag.tagColour = (byte)Mathf.Clamp(crew.tagColour, 0, 255);
                tag.tagBackground = (byte)Mathf.Clamp(crew.tagBackground, 0, 255);
                head.exists = true;
                head.name = tag.crew;
                head.symbol = tag.symbol;
                head.colour = tag.colour;
                head.announcement.CopyFromTruncated(crew.announcement ?? string.Empty);
                head.memberCap = (ushort)CrewRules.MemberCap(settings != null ? settings.memberCapByLevel : null, crew.level);
                head.myRank = tag.rank;
            }
            if (chat != null) chat.ServerSetGuild(crew != null ? crew.name : string.Empty);
            if (EntityServer)
            {
                // the tag reaches everyone through the ghost (CrewSystems), the window its owner by PlayerKey
                if (!tag.Equals(entityTag)) { entityTag = tag; TagChanged?.Invoke(); }
                CrewOwnerLink.Current?.View(this, CrewViewText.Encode(head, members ?? Array.Empty<CrewMemberView>()));
                return;
            }
            netTag.Value = tag;
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
            if (!Decides) return false;
            if (!string.IsNullOrEmpty(invitedToCrew) && invitedToCrew != crew.id && Time.time <= inviteExpires) return false;
            float seconds = settings != null ? settings.inviteSeconds : 30f;
            invitedToCrew = crew.id;
            invitedBy = inviterName;
            inviteExpires = Time.time + seconds;
            if (EntityServer) CrewOwnerLink.Current?.Prompt(this, inviterName, crew.name, seconds);
            else PromptRpc(inviterName, crew.name, seconds);
            return true;
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PromptRpc(string inviterName, string crewName, float seconds) => ShowPrompt(inviterName, crewName, seconds);

        private void ShowPrompt(string inviterName, string crewName, float seconds) =>
            ClientPresentationRegistry.Prompts?.Show(PromptId, "CREW INVITATION", $"{inviterName} invites you to the Crew {crewName}.", seconds, Respond);

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ClosePromptRpc() => ClientPresentationRegistry.Prompts?.Hide(PromptId);
    }
}
