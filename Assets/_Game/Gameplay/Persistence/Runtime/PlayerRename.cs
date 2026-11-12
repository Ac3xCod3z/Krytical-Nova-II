using System;
using System.Collections.Generic;
using System.Threading;
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
        public const string ReasonSaveRefused = "Your new name could not be saved, so nothing changed and your pass is back. Try again.";
        public const string ReasonStillSaving = "Your last name change is still being saved. Try again in a moment.";

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
            LeftRenames.Clear();
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
            LeaveWithSaveOut();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            LeaveWithSaveOut();
            base.OnDestroy();
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
            else ServerRename(bagIndex, name, Answer);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RenameRpc(byte bagIndex, FixedString64Bytes name)
        {
            if (!RpcBudget.Allow(this, CallKind.Account, nameof(RenameRpc))) return;
            ServerRename(bagIndex, name.ToString(), (refusal, clean) =>
            {
                if (!IsSpawned) return;
                if (refusal == null) RenamedRpc(new FixedString64Bytes(clean));
                else RefusedRpc(new FixedString128Bytes(refusal));
            });
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
        /// server body decides; anything else refuses. <paramref name="answer"/> is told (refusal, name as kept) once: at once for a
        /// refusal, after the save for a rename (a refused save undoes it and answers <see cref="ReasonSaveRefused"/>).
        /// </summary>
        public void ServerRenameAsked(int bagIndex, string wanted, Action<string, string> answer)
        {
            if (!(BodyRoleTag.Of(this) is BodyRoleTag tag) || tag.Kind != NetRoleKind.ServerBody) { answer?.Invoke(ReasonNotReady, null); return; }
            ServerRename(bagIndex, wanted, answer);
        }

        /// <summary>
        /// The same, answered now when it can be (a refusal, or a store that has written by the time it returns). Null with the name
        /// in <paramref name="clean"/> also while the save is still out; its final word then comes only through the other form.
        /// </summary>
        public string ServerRenameAsked(int bagIndex, string wanted, out string clean)
        {
            string refusal = null, kept = null;
            bool told = false;
            ServerRenameAsked(bagIndex, wanted, (r, c) => { refusal = r; kept = c; told = true; });
            clean = told ? kept : pendingClean;
            return told ? refusal : null;
        }

        /// <summary>
        /// Puts a pass back after a refused save (owner, 12 Nov 2026): into its own cell when that is empty or holds its stack,
        /// else into the first empty cell. False when the Pack has no room.
        /// </summary>
        public static bool PutBack(ItemStack[] pack, int cell, ItemStack pass)
        {
            if (pack == null || pass.IsEmpty) return false;
            if (cell >= 0 && cell < pack.Length)
            {
                if (pack[cell].IsEmpty) { pack[cell] = pass; return true; }
                if (pack[cell].StacksWith(pass)) { pack[cell] = pack[cell].WithCount(pack[cell].count + pass.count); return true; }
            }
            for (int i = 0; i < pack.Length; i++)
                if (pack[i].IsEmpty) { pack[i] = pass; return true; }
            return false;
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

        // A rename whose save is out (owner, 12 Nov 2026: a refused save is undone). Told by the store, maybe on its own thread.
        private Action<string, string> pendingAnswer;
        private string pendingOld, pendingClean, pendingAccount, pendingCharacter;
        private ItemStack pendingPass;
        private int pendingCell;
        private LeftRename pendingWord;   // the save's word (0 waiting, 1 written, 2 refused); kept on if the player leaves

        private void Update()
        {
            if (pendingAnswer != null && pendingWord.Word != 0) FinishSave();
            if (LeftRenames.Count > 0) ServerFinishLeftRenames();
        }

        /// <summary>
        /// A rename whose save was still out when its player left (note a; lead and owner, 12 Nov 2026). It is never undone: when the
        /// answer comes back refused, the rename is finished forward (<see cref="FinishLeftRenames"/>); written, nothing more is done.
        /// </summary>
        public sealed class LeftRename
        {
            public readonly string Old, Clean, Account, Character;
            /// <summary>The name.change line the rename wrote (A2: kept, so an undo takes it out and a finish writes no second one).</summary>
            public readonly LedgerEntry Line;
            private int word;
            public LeftRename(string old, string clean, string account, string character, LedgerEntry line = null)
            { Old = old; Clean = clean; Account = account; Character = character; Line = line; }
            /// <summary>0 waiting, 1 written, 2 refused. Set by the store, maybe on its own thread.</summary>
            public int Word => Volatile.Read(ref word);
            public void Tell(bool written) => Volatile.Write(ref word, written ? 1 : 2);
        }

        private static readonly List<LeftRename> LeftRenames = new List<LeftRename>();   // main thread only

        /// <summary>Renames whose player left with the save still out (tests and the drain's callers).</summary>
        public static int LeftRenamesWaiting => LeftRenames.Count;

        /// <summary>Keeps a rename whose player left until its save's word comes (main thread).</summary>
        public static void KeepLeftRename(LeftRename rename) { if (rename != null) LeftRenames.Add(rename); }

        // The body goes while its rename's save is out: its word is then finished by the drain, not by this body.
        private void LeaveWithSaveOut()
        {
            if (pendingAnswer == null) return;
            pendingAnswer = null;   // the asker is gone: no answer
            KeepLeftRename(pendingWord);
            pendingWord = null;
        }

        /// <summary>Server, main thread: finishes the renames of players who left (RenameServerSystem and every PlayerRename call it).</summary>
        public static int ServerFinishLeftRenames()
        {
            ServerSave.ApplyRefusals();   // first: a refused line is then waiting again (or on its way again with its account)
            return FinishLeftRenames(ServerRoster.Names, () => ServerSave.SaveWorldWhole(ServerRoster.NamesDocument), ServerSave.Ledger, ServerSave.IsLedgerWaiting);
        }

        /// <summary>
        /// The drain (tests give their own names list, names save and ledger): a refused answer keeps the new name in the names list,
        /// saves the names document again, writes the name.change line again (the refused change carried the first) and one records
        /// line; a written answer needs nothing; no answer yet stays kept. The order of the player's own leave save does not matter.
        /// Returns how many were finished forward.
        /// </summary>
        public static int FinishLeftRenames(Func<NameIndex> names, Action saveNames, Action<LedgerEntry> ledger, Func<LedgerEntry, bool> waiting = null)
        {
            int forward = 0;
            for (int i = LeftRenames.Count - 1; i >= 0; i--)
            {
                LeftRename left = LeftRenames[i];
                int word = left.Word;
                if (word == 0) continue;
                LeftRenames.RemoveAt(i);
                if (word == 1)
                {
                    Debug.Log($"[Rename] {left.Old} > {left.Clean} ({left.Character}, account {left.Account}): saved after its player left.");
                    continue;
                }
                NameIndex index = names?.Invoke();
                if (index != null)
                {
                    index.Drop(left.Old, left.Account, left.Character);
                    index.Put(left.Clean, left.Account, left.Character);
                }
                saveNames?.Invoke();
                // One line only (A2): the first is written again with its refused change; a new one only when it is not on its way.
                LedgerEntry line = left.Line ?? LedgerEntry.For(LedgerKinds.NameChanged, left.Account, left.Character, left.Old + " > " + left.Clean);
                if (waiting == null || !waiting(line)) ledger?.Invoke(line);
                Debug.LogWarning($"[Rename] {left.Old} > {left.Clean} ({left.Character}, account {left.Account}): its save was refused after its player left: finished forward (names list saved again, ledger line again).");
                ServerRecords.Write(RecordKind.Access, $"Character {left.Old} ({left.Character}) rename to {left.Clean}: rename of a player who had left completed after a refused save (account {left.Account})");
                forward++;
            }
            return forward;
        }

        /// <summary>Renames the character, spending the pass; <paramref name="answer"/> gets (refusal, name as kept) once.</summary>
        private void ServerRename(int bagIndex, string wanted, Action<string, string> answer)
        {
            answer ??= (_, __) => { };
            if (inventory == null || identity == null || !identity.HasIdentity) { answer(ReasonNotReady, null); return; }
            if (!ServerSave.HasAuthority) { answer(ReasonNoSaves, null); return; }
            if (pendingAnswer != null) { answer(ReasonStillSaving, null); return; }
            string old = identity.CharacterName;
            string account = identity.AccountId, character = identity.CharacterId;
            NameIndex names = ServerRoster.Names();
            string refusal = CheckName(wanted, old, character, names, out string clean);
            if (refusal != null) { answer(refusal, null); return; }

            ItemStack stack = inventory.Get(bagIndex);
            var pass = stack.IsEmpty ? null : inventory.Catalog?.Get(stack.itemId) as ItemDefinition;
            refusal = CheckPassCell(bagIndex, stack, pass);
            if (refusal != null) { answer(refusal, null); return; }
            if (!inventory.ServerTakeFromCell(bagIndex, 1, out ItemStack taken)) { answer(ReasonNoPass, null); return; }

            identity.ServerRename(clean);
            ServerRecords.Write(RecordKind.Access, $"Character {old} ({character}) renamed {clean} with a {pass.displayName} (account {account})");
            if (SaveKeys.IsId(account) && SaveKeys.IsId(character))
            {
                names.Drop(old, account, character);
                names.Put(clean, account, character);
                LedgerEntry line = LedgerEntry.For(LedgerKinds.NameChanged, account, character, old + " > " + clean);
                ServerSave.Ledger(line);
                if (saveAgent != null)
                {
                    // The answer waits for the save's word: written, the name is taken; refused, everything is undone (FinishSave).
                    pendingAnswer = answer;
                    pendingOld = old; pendingClean = clean; pendingAccount = account; pendingCharacter = character;
                    pendingPass = taken; pendingCell = bagIndex;
                    var word = pendingWord = new LeftRename(old, clean, account, character, line);
                    ServerSave.SaveTogether(saveAgent, written => word.Tell(written), ServerRoster.NamesDocument);
                    if (word.Word != 0) FinishSave();   // a store that has written when it returns
                    return;
                }
                Debug.LogWarning($"[Rename] {old} > {clean} ({character}, account {account}): this body has no save agent: nothing is saved now; the names list holds the new name in memory only until a later change saves it.");
            }
            else Debug.LogWarning($"[Rename] {old} > {clean}: not a saved character (account or character id is not a real id): the name changed in memory only, nothing was saved.");
            answer(null, clean);
        }

        // Main thread: the save's word on the rename that waits for it.
        private void FinishSave()
        {
            Action<string, string> answer = pendingAnswer;
            pendingAnswer = null;
            int saveWord = pendingWord.Word;
            LedgerEntry pendingLine = pendingWord.Line;
            pendingWord = null;
            if (saveWord == 1)
            {
                Debug.Log($"[Rename] {pendingOld} > {pendingClean} ({pendingCharacter}, account {pendingAccount}): saved with the names list as one change.");
                answer(null, pendingClean);
                return;
            }
            // Refused whole (owner, 12 Nov 2026): undo it. The pass goes back, the old name comes back on the character and in the
            // names list, and the player is told to try again. The refused change carried the ledger line too, so none was kept.
            ItemStack pass = pendingPass;
            int cell = pendingCell;
            bool back = inventory != null && inventory.ServerChangePack(pack => PutBack(pack, cell, pass));
            if (!back) Debug.LogError($"[Rename] {pendingOld} ({pendingCharacter}): the pass could not be put back (the Pack is full): give it by hand.");
            if (identity != null) identity.ServerRename(pendingOld);
            NameIndex names = ServerRoster.Names();
            names.Drop(pendingClean, pendingAccount, pendingCharacter);
            names.Put(pendingOld, pendingAccount, pendingCharacter);
            Debug.LogWarning($"[Rename] {pendingOld} > {pendingClean} ({pendingCharacter}, account {pendingAccount}): the save was refused, so the rename is undone (pass back: {back}).");
            ServerRecords.Write(RecordKind.Access, $"Character {pendingOld} ({pendingCharacter}) rename to {pendingClean} undone: its save was refused (account {pendingAccount})");
            // A2: the refused change's line is waiting again after ApplyRefusals; an undone rename keeps none.
            ServerSave.ApplyRefusals();
            ServerSave.Unledger(pendingLine);
            if (saveAgent != null) ServerSave.SaveTogether(saveAgent, ServerRoster.NamesDocument);
            answer(ReasonSaveRefused, null);
        }
    }
}
