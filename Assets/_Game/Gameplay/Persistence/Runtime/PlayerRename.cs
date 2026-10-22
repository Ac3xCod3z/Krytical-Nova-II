using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Persistence
{
    /// <summary>
    /// The Name Change Pass (owner, 22 Oct 2026; sold in the Nova Store later, a GM item for now): the player types the new
    /// name, the server checks it against the rules and every other name in the game, spends the pass, renames the
    /// character and saves it with the name list in one change. The player is then told the server knows them by the new
    /// name once they log back in, and after a 5 second count is sent to character select.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRename : NetworkBehaviour
    {
        /// <summary>Seconds of the count before a renamed player goes back to character select.</summary>
        public const float BackToSelectSeconds = 5f;

        public const string ReasonSame = "That is already your name.";
        public const string ReasonNoPass = "There is no Name Change Pass in that Pack cell any more.";
        public const string ReasonNoSaves = "This game does not keep characters: names change on a server.";

        private PlayerInventory inventory;
        private PlayerIdentity identity;
        private PlayerSaveAgent saveAgent;

        /// <summary>This machine's own player's, while it is in a session.</summary>
        public static PlayerRename Local { get; private set; }

        /// <summary>The server took the name (the new name): the game counts down and goes to character select.</summary>
        public static event Action<string> Renamed;

        /// <summary>The server refused the name (why): the game asks again.</summary>
        public static event Action<string> Refused;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            Renamed = null;
            Refused = null;
        }

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            identity = GetComponent<PlayerIdentity>();
            saveAgent = GetComponent<PlayerSaveAgent>();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            base.OnNetworkDespawn();
        }

        /// <summary>Asks for the new name, spending the Name Change Pass in that Pack cell.</summary>
        public void RequestRename(int bagIndex, string name)
        {
            if (IsSpawned) RenameRpc((byte)bagIndex, new FixedString64Bytes(name ?? string.Empty));
            else Answer(ServerRename(bagIndex, name, out string clean), clean);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RenameRpc(byte bagIndex, FixedString64Bytes name)
        {
            if (!RpcBudget.Allow(this, CallKind.Account, nameof(RenameRpc))) return;
            string refusal = ServerRename(bagIndex, name.ToString(), out string clean);
            if (refusal == null) RenamedRpc(new FixedString64Bytes(clean));
            else RefusedRpc(new FixedString128Bytes(refusal));
        }

        [Rpc(SendTo.Owner)]
        private void RenamedRpc(FixedString64Bytes name) => Answer(null, name.ToString());

        [Rpc(SendTo.Owner)]
        private void RefusedRpc(FixedString128Bytes reason) => Answer(reason.ToString(), null);

        private static void Answer(string refusal, string name)
        {
            if (refusal == null) Renamed?.Invoke(name);
            else Refused?.Invoke(refusal);
        }

        // ------------------------------------------------------------------ deciding (server)

        /// <summary>Renames the character, spending the pass. Null when it is done (with the name as kept); otherwise why not.</summary>
        private string ServerRename(int bagIndex, string wanted, out string clean)
        {
            clean = null;
            if (inventory == null || identity == null || !identity.HasIdentity) return "Your character is not ready yet.";
            if (!ServerSave.HasAuthority) return ReasonNoSaves;
            if (!CharacterNameRules.Validate(wanted, out clean, out string reason)) return reason;
            string old = identity.CharacterName;
            if (string.Equals(clean, old, StringComparison.Ordinal)) return ReasonSame;

            string account = identity.AccountId, character = identity.CharacterId;
            NameIndex names = ServerRoster.Names();
            NameIndex.Entry holder = names.Holder(clean);
            // Only the capitals changing ("ace" to "Ace") is this character's own name.
            if (holder != null && holder.character != character) return CharacterNameRules.ReasonTakenInGame;

            ItemStack stack = inventory.Get(bagIndex);
            if (stack.IsEmpty || !(inventory.Catalog?.Get(stack.itemId) is ItemDefinition pass) || pass.useEffect != ItemUseEffect.NameChange)
                return ReasonNoPass;
            if (!inventory.ServerTakeFromCell(bagIndex, 1, out _)) return ReasonNoPass;

            identity.ServerRename(clean);
            if (SaveKeys.IsId(account) && SaveKeys.IsId(character))
            {
                names.Drop(old, account, character);
                names.Put(clean, account, character);
                ServerSave.Ledger(LedgerEntry.For(LedgerKinds.NameChanged, account, character, old + " > " + clean));
                if (saveAgent != null) ServerSave.SaveTogether(saveAgent, ServerRoster.NamesDocument);
            }
            ServerRecords.Write(RecordKind.Access, $"Character {old} ({character}) renamed {clean} with a {pass.displayName} (account {account})");
            return null;
        }
    }
}
