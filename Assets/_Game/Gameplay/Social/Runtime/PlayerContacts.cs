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

namespace KryticalNova.Social
{
    /// <summary>
    /// A player's Contacts: asking, answering, removing, blocking, favourites and notes. Everything is a request;
    /// <see cref="ContactsService"/> on the server decides and keeps the books. The owner is sent its own list
    /// whenever it changes (somebody came online, accepted, was removed), so the window only shows.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerContacts : NetworkBehaviour, IBodyContacts, IServerLeaving
    {
        private const string PromptPrefix = "contact:";
        private const float PromptSeconds = 30f;

        private PlayerIdentity identity;
        private PlayerCharacterProgression progression;
        private PlayerWeaponClass weaponClass;
        private PlayerChat chat;
        private PlayerSaveAgent saveAgent;
        private bool arrived;

        // Owner only: what the server sent last.
        private ContactView[] lines = Array.Empty<ContactView>();

        /// <summary>Raised on the owner's machine when its list changes.</summary>
        public event Action Changed;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerContacts Local => LocalPlayer.Get<PlayerContacts>();

        public string CharacterId => identity != null ? identity.CharacterId : string.Empty;
        public string ContactName => identity != null ? identity.CharacterName : name;
        public FactionId Faction => identity != null ? identity.Faction : FactionId.None;
        public int Level => progression != null ? progression.CurrentLevel : 1;
        public int WeaponClass => weaponClass != null ? (int)weaponClass.ActiveClass : 0;

        /// <summary>The owner's contacts, requests and blocked characters, as last told by the server.</summary>
        public IReadOnlyList<ContactView> Lines => lines;

        /// <summary>True when the character called <paramref name="characterName"/> is one of the owner's contacts.</summary>
        public bool IsContact(string characterName) => Has(characterName, ContactKind.Contact);
        public bool IsBlocked(string characterName) => Has(characterName, ContactKind.Blocked);

        private bool Has(string characterName, ContactKind kind)
        {
            foreach (ContactView line in lines)
                if (line.kind == kind && string.Equals(line.name.ToString(), characterName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // A client view of the entity netcode only asks and shows; its server body is where ContactsService is reached (M8 8.4.2).
        private bool EntityServer => BodyRoleTag.Of(this) is { Kind: NetRoleKind.ServerBody };
        private IBodyRequests Requests => BodyRoleTag.RequestsOf(this) is IBodyRequests requests ? requests : null;

        private bool Ask(ContactOpKind op, string a, string b = null, bool flag = false)
        {
            if (!(Requests is { } requests)) return false;
            requests.ContactOp(op, a ?? string.Empty, b ?? string.Empty, flag);
            return true;
        }

        private void Awake()
        {
            identity = GetComponent<PlayerIdentity>();
            progression = GetComponent<PlayerCharacterProgression>();
            weaponClass = GetComponent<PlayerWeaponClass>();
            chat = GetComponent<PlayerChat>();
            saveAgent = GetComponent<PlayerSaveAgent>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer || saveAgent == null) return;
            saveAgent.Loaded += OnCharacterLoaded;
            if (saveAgent.IsLoaded) OnCharacterLoaded();
        }

        public override void OnNetworkDespawn()
        {
            if (saveAgent != null) saveAgent.Loaded -= OnCharacterLoaded;
            if (IsServer && arrived)
            {
                arrived = false;
                // When the whole session shuts down there is nobody left to tell, but the book still notes the time.
                ContactsService.ServerLeft(this);
            }
            lines = Array.Empty<ContactView>();
            base.OnNetworkDespawn();
        }

        // The entity netcode's server body is never network-spawned: it arrives as NGO's server did on spawn, and leaves before its save.
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
            ContactsService.ServerLeft(this);
        }

        public override void OnDestroy()
        {
            if (saveAgent != null) saveAgent.Loaded -= OnCharacterLoaded;
            base.OnDestroy();
        }

        /// <summary>A server body of the entity netcode runs its owner's request (NGO's RPC bodies, after the Social limit).</summary>
        public void RunContactOp(ContactOpKind op, string a, string b, bool flag)
        {
            if (!EntityServer || string.IsNullOrEmpty(a)) return;
            switch (op)
            {
                case ContactOpKind.AddByName: ContactsService.ServerRequestByName(this, a); break;
                case ContactOpKind.Respond: ServerClosePrompt(a); ContactsService.ServerRespond(this, a, flag); break;
                case ContactOpKind.Cancel: ContactsService.ServerCancel(this, a); break;
                case ContactOpKind.Remove: ContactsService.ServerRemove(this, a); break;
                case ContactOpKind.RemoveByName: ContactsService.ServerRemoveByName(this, a); break;
                case ContactOpKind.Block: ContactsService.ServerBlock(this, a); break;
                case ContactOpKind.BlockByName: ContactsService.ServerBlockByName(this, a); break;
                case ContactOpKind.Unblock: ContactsService.ServerUnblock(this, a); break;
                case ContactOpKind.UnblockByName: ContactsService.ServerUnblockByName(this, a); break;
                case ContactOpKind.Favourite: ContactsService.ServerSetFavourite(this, a, flag); break;
                case ContactOpKind.Note: ContactsService.ServerSetNote(this, a, ContactRules.CleanNote(b)); break;
            }
        }

        /// <summary>The entity netcode's own view: the list the server sent (NGO's owner ViewRpc).</summary>
        public void MirrorView(string viewText)
        {
            if (!BodyRoleTag.IsLocalClientView(this) || !ContactViewText.TryDecode(viewText, out ContactView[] view)) return;
            lines = view;
            Changed?.Invoke();
        }

        public void MirrorPrompt(string fromId, string fromName)
        {
            if (BodyRoleTag.IsLocalClientView(this)) ShowPrompt(fromId, fromName);
        }

        public void MirrorClosePrompt(string fromId)
        {
            if (BodyRoleTag.IsLocalClientView(this)) ClientPresentationRegistry.Prompts?.Hide(PromptPrefix + fromId);
        }

        private void OnCharacterLoaded()
        {
            if (arrived) return;
            arrived = true;
            ContactsService.ServerArrived(this);
        }

        // ------------------------------------------------------------------ owner -> server

        /// <summary>Ask the character called <paramref name="characterName"/> to be a contact, online or not.</summary>
        public void Add(string characterName)
        {
            if (!string.IsNullOrWhiteSpace(characterName) && Ask(ContactOpKind.AddByName, characterName.Trim())) return;
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(characterName)) AddByNameRpc(characterName.Trim());
        }

        /// <summary>Ask the player <paramref name="target"/> (the one selected in the world).</summary>
        public void Add(PlayerContacts target)
        {
            if (target != null && Ask(ContactOpKind.AddByName, target.ContactName)) return;
            if (IsSpawned && IsOwner && target != null && target.IsSpawned) AddRpc(target.NetworkObject);
        }

        /// <summary>Answer a request from the character with this id.</summary>
        public void Respond(string characterId, bool accept)
        {
            if (!string.IsNullOrEmpty(characterId) && Ask(ContactOpKind.Respond, characterId, null, accept)) return;
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(characterId)) RespondRpc(characterId, accept);
        }

        /// <summary>Take back a request sent to the character with this id.</summary>
        public void CancelRequest(string characterId)
        {
            if (!string.IsNullOrEmpty(characterId) && Ask(ContactOpKind.Cancel, characterId)) return;
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(characterId)) CancelRpc(characterId);
        }

        public void Remove(string characterId)
        {
            if (!string.IsNullOrEmpty(characterId) && Ask(ContactOpKind.Remove, characterId)) return;
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(characterId)) RemoveRpc(characterId);
        }

        public void RemoveByName(string characterName)
        {
            if (!string.IsNullOrWhiteSpace(characterName) && Ask(ContactOpKind.RemoveByName, characterName.Trim())) return;
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(characterName)) RemoveByNameRpc(characterName.Trim());
        }

        public void Block(string characterId)
        {
            if (!string.IsNullOrEmpty(characterId) && Ask(ContactOpKind.Block, characterId)) return;
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(characterId)) BlockRpc(characterId);
        }

        public void BlockByName(string characterName)
        {
            if (!string.IsNullOrWhiteSpace(characterName) && Ask(ContactOpKind.BlockByName, characterName.Trim())) return;
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(characterName)) BlockByNameRpc(characterName.Trim());
        }

        public void Unblock(string characterId)
        {
            if (!string.IsNullOrEmpty(characterId) && Ask(ContactOpKind.Unblock, characterId)) return;
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(characterId)) UnblockRpc(characterId);
        }

        public void UnblockByName(string characterName)
        {
            if (!string.IsNullOrWhiteSpace(characterName) && Ask(ContactOpKind.UnblockByName, characterName.Trim())) return;
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(characterName)) UnblockByNameRpc(characterName.Trim());
        }

        public void SetFavourite(string characterId, bool favourite)
        {
            if (!string.IsNullOrEmpty(characterId) && Ask(ContactOpKind.Favourite, characterId, null, favourite)) return;
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(characterId)) FavouriteRpc(characterId, favourite);
        }

        public void SetNote(string characterId, string note)
        {
            if (!string.IsNullOrEmpty(characterId) && Ask(ContactOpKind.Note, characterId, ContactRules.CleanNote(note))) return;
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(characterId)) NoteRpc(characterId, ContactRules.CleanNote(note));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void AddByNameRpc(string characterName)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(AddByNameRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(AddByNameRpc), characterName)) return;
            ContactsService.ServerRequestByName(this, characterName);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void AddRpc(NetworkObjectReference targetReference)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(AddRpc))) return;
            PlayerContacts target = targetReference.TryGet(out NetworkObject targetObject) ? targetObject.GetComponent<PlayerContacts>() : null;
            ContactBook book = target != null ? ContactsService.Book(target.CharacterId) : null;
            if (book == null) Notify("That player has no saved character to add.");
            else ContactsService.ServerRequest(this, book);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RespondRpc(string characterId, bool accept)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(RespondRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(RespondRpc), characterId)) return;
            ClosePromptRpc(characterId);
            ContactsService.ServerRespond(this, characterId, accept);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CancelRpc(string characterId)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(CancelRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(CancelRpc), characterId)) return;
            ContactsService.ServerCancel(this, characterId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RemoveRpc(string characterId)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(RemoveRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(RemoveRpc), characterId)) return;
            ContactsService.ServerRemove(this, characterId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RemoveByNameRpc(string characterName)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(RemoveByNameRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(RemoveByNameRpc), characterName)) return;
            ContactsService.ServerRemoveByName(this, characterName);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void BlockRpc(string characterId)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(BlockRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(BlockRpc), characterId)) return;
            ContactsService.ServerBlock(this, characterId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void BlockByNameRpc(string characterName)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(BlockByNameRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(BlockByNameRpc), characterName)) return;
            ContactsService.ServerBlockByName(this, characterName);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UnblockRpc(string characterId)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(UnblockRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(UnblockRpc), characterId)) return;
            ContactsService.ServerUnblock(this, characterId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UnblockByNameRpc(string characterName)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(UnblockByNameRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(UnblockByNameRpc), characterName)) return;
            ContactsService.ServerUnblockByName(this, characterName);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void FavouriteRpc(string characterId, bool favourite)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(FavouriteRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(FavouriteRpc), characterId)) return;
            ContactsService.ServerSetFavourite(this, characterId, favourite);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void NoteRpc(string characterId, string note)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(NoteRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(NoteRpc), characterId)) return;
            if (!RpcBudget.TextFits(this, nameof(NoteRpc), note)) return;
            ContactsService.ServerSetNote(this, characterId, note);
        }

        // ------------------------------------------------------------------ server -> owner (used by ContactsService)

        /// <summary>Server: a line in this player's chat log.</summary>
        public void Tell(string text)
        {
            // a server body of the entity netcode too: its player's chat log (M8 8.4.1)
            if (chat != null && ((IsSpawned && IsServer) || EntityServer) && !string.IsNullOrEmpty(text)) chat.ServerSystem(text);
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

        /// <summary>Server: sends the owner its list as it stands now.</summary>
        public void ServerPushView()
        {
            if (EntityServer) { ContactsOwnerLink.Current?.View(this, ContactViewText.Encode(ContactsService.ServerView(CharacterId))); return; }
            if (IsSpawned && IsServer) ViewRpc(ContactsService.ServerView(CharacterId));
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ViewRpc(ContactView[] view)
        {
            lines = view ?? Array.Empty<ContactView>();
            Changed?.Invoke();
        }

        /// <summary>Server: asks the owner, on screen, to answer a contact request.</summary>
        public void ServerPrompt(string fromId, string fromName)
        {
            if (EntityServer) { ContactsOwnerLink.Current?.Prompt(this, fromId, fromName); return; }
            if (IsSpawned && IsServer) PromptRpc(fromId, fromName);
        }

        public void ServerClosePrompt(string fromId)
        {
            if (EntityServer) { ContactsOwnerLink.Current?.ClosePrompt(this, fromId); return; }
            if (IsSpawned && IsServer) ClosePromptRpc(fromId);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PromptRpc(string fromId, string fromName) => ShowPrompt(fromId, fromName);

        private void ShowPrompt(string fromId, string fromName) =>
            ClientPresentationRegistry.Prompts?.Show(PromptPrefix + fromId, "CONTACT REQUEST", $"{fromName} wants to add you as a contact.",
                PromptSeconds, accept => Respond(fromId, accept));

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ClosePromptRpc(string fromId) => ClientPresentationRegistry.Prompts?.Hide(PromptPrefix + fromId);
    }
}
