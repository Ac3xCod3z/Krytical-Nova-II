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
    /// <para>Entity netcode (wave 2, 12 Nov 2026): the owner's view asks through <see cref="Link"/> (<c>RenameSystems</c>), the
    /// server body decides with the same <see cref="ServerRename"/> (<see cref="ServerRenameAsked"/>), and the answer comes back
    /// through <see cref="ApplyServerAnswer"/>. Only the Pack cell index and the wished name are taken from the ask.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerRename : NetworkBehaviour
    {
        /// <summary>Seconds of the count before a renamed player goes back to character select.</summary>
        public const float BackToSelectSeconds = 5f;

        public const string ReasonSame = "That is already your name.";
        public const string ReasonNoPass = "There is no Name Change Pass in that Pack cell any more.";
        public const string ReasonNoSaves = "This game does not keep characters: names change on a server.";
        public const string ReasonNotReady = "Your character is not ready yet.";

        private PlayerInventory inventory;
        private PlayerIdentity identity;
        private PlayerSaveAgent saveAgent;
        private static PlayerRename owned;

        /// <summary>This machine's own player's, while it is in a session (NGO: the owned one; entity netcode: the own view).</summary>
        public static PlayerRename Local => owned != null ? owned : LocalPlayer.Get<PlayerRename>();

        /// <summary>The entity netcode's way for the owner's view to ask the server; filled by the entity client, null elsewhere.</summary>
        public interface IOwnerLink
        {
            void Rename(byte bagIndex, string name);
        }

        /// <summary>Filled by the entity client (<c>RenameClientSystem</c>); null elsewhere.</summary>
        public static IOwnerLink Link;

        /// <summary>The server took the name (the new name): the game counts down and goes to character select.</summary>
        public static event Action<string> Renamed;

        /// <summary>The server refused the name (why): the game asks again.</summary>
        public static event Action<string> Refused;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            owned = null;
            Link = null;
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
            if (IsOwner) owned = this;
        }

        public override void OnNetworkDespawn()
        {
            if (owned == this) owned = null;
            base.OnNetworkDespawn();
        }

        /// <summary>Asks for the new name, spending the Name Change Pass in that Pack cell.</summary>
        public void RequestRename(int bagIndex, string name)
        {
            if (IsSpawned) RenameRpc((byte)bagIndex, new FixedString64Bytes(name ?? string.Empty));
            else if (BodyRoleTag.IsLocalClientView(this))
            {
                // the entity netcode's own view decides nothing: it asks the server and waits for its answer
                if (Link != null && bagIndex >= 0 && bagIndex <= byte.MaxValue) Link.Rename((byte)bagIndex, name ?? string.Empty);
                else Answer(ReasonNotReady, null);
            }
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

        /// <summary>Entity client: the server's answer to the own view's ask (taken: the name as kept; else why not).</summary>
        public static void ApplyServerAnswer(bool taken, string text)
        {
            if (taken) Answer(null, text ?? string.Empty);
            else Answer(string.IsNullOrEmpty(text) ? ReasonNotReady : text, null);
        }

        private static void Answer(string refusal, string name)
        {
            if (refusal == null) Renamed?.Invoke(name);
            else Refused?.Invoke(refusal);
        }

        // ------------------------------------------------------------------ deciding (server)

        /// <summary>
        /// Entity server: an ask from this body's owner (the server system has proven the connection and its call budget). Only a
        /// server body decides; anything else refuses. Null when it is done (with the name as kept); otherwise why not.
        /// </summary>
        public string ServerRenameAsked(int bagIndex, string wanted, out string clean)
        {
            clean = null;
            if (!(BodyRoleTag.Of(this) is BodyRoleTag tag) || tag.Kind != NetRoleKind.ServerBody) return ReasonNotReady;
            return ServerRename(bagIndex, wanted, out clean);
        }

        /// <summary>
        /// The name checks, in NGO's order: the rules (<see cref="CharacterNameRules"/>), not the name the character has, not a
        /// name another character holds (capitals ignored; changing only one's own capitals is allowed). Null when the name may be
        /// taken, with its kept form in <paramref name="clean"/>.
        /// </summary>
        public static string CheckName(string wanted, string old, string character, NameIndex names, out string clean)
        {
            if (!CharacterNameRules.Validate(wanted, out clean, out string reason)) return reason;
            if (string.Equals(clean, old, StringComparison.Ordinal)) return ReasonSame;
            NameIndex.Entry holder = names?.Holder(clean);
            if (holder != null && holder.character != character) return CharacterNameRules.ReasonTakenInGame;
            return null;
        }

        /// <summary>
        /// The Pack cell, as the server holds it at the moment of the take: inside the Pack, not empty, and holding an item whose
        /// effect is <see cref="ItemUseEffect.NameChange"/> (<paramref name="item"/> is the catalog's entry for that cell's item).
        /// Nothing about the item is taken from the ask. Null when the pass may be taken.
        /// </summary>
        public static string CheckPassCell(int bagIndex, ItemStack stack, ItemDefinition item)
        {
            if (bagIndex < 0 || bagIndex >= PlayerInventory.Capacity) return ReasonNoPass;
            if (stack.IsEmpty || item == null || item.useEffect != ItemUseEffect.NameChange) return ReasonNoPass;
            return null;
        }

        /// <summary>Renames the character, spending the pass. Null when it is done (with the name as kept); otherwise why not.</summary>
        private string ServerRename(int bagIndex, string wanted, out string clean)
        {
            clean = null;
            if (inventory == null || identity == null || !identity.HasIdentity) return ReasonNotReady;
            if (!ServerSave.HasAuthority) return ReasonNoSaves;
            string old = identity.CharacterName;
            string account = identity.AccountId, character = identity.CharacterId;
            NameIndex names = ServerRoster.Names();
            string refusal = CheckName(wanted, old, character, names, out clean);
            if (refusal != null) return refusal;

            ItemStack stack = inventory.Get(bagIndex);
            var pass = stack.IsEmpty ? null : inventory.Catalog?.Get(stack.itemId) as ItemDefinition;
            refusal = CheckPassCell(bagIndex, stack, pass);
            if (refusal != null) return refusal;
            if (!inventory.ServerTakeFromCell(bagIndex, 1, out _)) return ReasonNoPass;

            identity.ServerRename(clean);
            if (SaveKeys.IsId(account) && SaveKeys.IsId(character))
            {
                names.Drop(old, account, character);
                names.Put(clean, account, character);
                ServerSave.Ledger(LedgerEntry.For(LedgerKinds.NameChanged, account, character, old + " > " + clean));
                if (saveAgent != null) ServerSave.SaveTogether(saveAgent, ServerRoster.NamesDocument);
                // NGO's behaviour, kept (wave 2, 12 Nov 2026, lead's condition 1): the save is handed over as one change and its result
                // is not waited for; the pass stays spent and the name stays changed in memory whatever the save does (the save service
                // keeps a failed change and sends it again; a version conflict is refused and logged by the store). No rollback.
                Debug.Log(saveAgent != null
                    ? $"[Rename] {old} > {clean} ({character}, account {account}): the pass is spent and the name changed; saved with the names list as one change. If that save does not go through, nothing is undone."
                    : $"[Rename] {old} > {clean} ({character}, account {account}): the pass is spent and the name changed, but this body has no save agent: nothing is saved now (NGO's behaviour): the names list holds the new name in memory only until a later change saves it.");
            }
            else Debug.LogWarning($"[Rename] {old} > {clean}: not a saved character (account or character id is not a real id): the name changed in memory only, nothing was saved.");
            ServerRecords.Write(RecordKind.Access, $"Character {old} ({character}) renamed {clean} with a {pass.displayName} (account {account})");
            return null;
        }
    }
}
