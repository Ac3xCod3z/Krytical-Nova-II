using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Forge
{
    /// <summary>What was attempted at the forge.</summary>
    public enum GearWorkKind : byte { Temper, Fusion, Tuning, TuningReset, Reforge }

    /// <summary>How an attempt ended, as the owner is told: what the piece was and what it is now (empty when it broke).</summary>
    public struct GearWorkResult : INetworkSerializable
    {
        public GearWorkKind kind;
        public GearWorkOutcome outcome;
        public ItemStack before;
        public ItemStack after;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref kind);
            serializer.SerializeValue(ref outcome);
            serializer.SerializeValue(ref before);
            serializer.SerializeValue(ref after);
        }
    }

    /// <summary>
    /// The forge: Tempering, Fusion, Tuning and Reforge on gear in the player's Pack, done at an Armorer. The client
    /// only names the piece and the materials; the server checks everything again through
    /// <see cref="GearProgressionRules"/>, rolls, takes the materials, Creds and Renown, changes the piece and tells
    /// the owner how it went. Every number is in <see cref="GearProgressionSettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerGearWork : NetworkBehaviour
    {
        private const float ReachPadding = 2.5f;
        private const string ReasonDefeated = "You can't work on gear while defeated.";
        private const string ReasonWhere = "Visit an Armorer to work on gear.";
        private const string ReasonNoCreds = "You don't have enough Creds.";

        [SerializeField] private GearProgressionSettings settings;

        private PlayerInventory inventory;
        private PlayerWallet wallet;
        private PlayerCharacterProgression progression;
        private CharacterCombatStats stats;
        private PlayerResources resources;
        private IGearWearer wearer;
        private readonly List<ushort> reforgeResults = new List<ushort>(64);

        /// <summary>GM switch: the forge works anywhere until the session ends.</summary>
        public static bool GmAnywhere;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => GmAnywhere = false;

        /// <summary>The forge of the player this machine controls.</summary>
        public static PlayerGearWork Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerGearWork>() : null;

        public GearProgressionSettings Settings => settings;
        public PlayerInventory Inventory => inventory;
        /// <summary>The character's Luck as the forge reads it (gear, a tempered Amulet, Hot Streak).</summary>
        public float Luck => stats != null ? stats.GetStatValue(StatType.Luck) : 0f;
        public long Creds => wallet != null ? wallet.Creds : 0;
        public int Renown => progression != null ? progression.Renown : 0;
        /// <summary>True while an Armorer is in reach (or the GM switch is on): the window closes when it stops being so.</summary>
        public bool InReach => GmAnywhere || NpcInteractable.IsOffered(NpcOffer.GearWork, transform.position, ReachPadding);

        /// <summary>Raised on the owner when the server has finished an attempt.</summary>
        public event Action<GearWorkResult> Finished;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            wallet = GetComponent<PlayerWallet>();
            progression = GetComponent<PlayerCharacterProgression>();
            stats = GetComponent<CharacterCombatStats>();
            resources = GetComponent<PlayerResources>();
            wearer = GetComponent<IGearWearer>();
        }

        // ------------------------------------------------------------------ requests (owner)

        public void RequestTemper(int cell, ushort alloyItemId, ushort protectionItemId = 0)
        {
            if (IsSpawned) TemperRpc((short)cell, alloyItemId, protectionItemId);
            else ServerTemper(cell, alloyItemId, protectionItemId);
        }

        public void RequestFuse(int targetCell, int materialCell)
        {
            if (IsSpawned) FuseRpc((short)targetCell, (short)materialCell);
            else ServerFuse(targetCell, materialCell);
        }

        public void RequestTune(int cell)
        {
            if (IsSpawned) TuneRpc((short)cell);
            else ServerTune(cell);
        }

        public void RequestResetTuning(int cell)
        {
            if (IsSpawned) ResetTuningRpc((short)cell);
            else ServerResetTuning(cell);
        }

        public void RequestReforge(int cell)
        {
            if (IsSpawned) ReforgeRpc((short)cell);
            else ServerReforge(cell);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void TemperRpc(short cell, ushort alloyItemId, ushort protectionItemId) => ServerTemper(cell, alloyItemId, protectionItemId);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void FuseRpc(short targetCell, short materialCell) => ServerFuse(targetCell, materialCell);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void TuneRpc(short cell) => ServerTune(cell);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ResetTuningRpc(short cell) => ServerResetTuning(cell);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ReforgeRpc(short cell) => ServerReforge(cell);

        // ------------------------------------------------------------------ server

        private bool HasAuthority => !IsSpawned || IsServer;

        /// <summary>True when this player may use the forge now; otherwise they are told why not.</summary>
        private bool Ready()
        {
            if (!HasAuthority || inventory == null || inventory.Catalog == null) return false;
            string blocked = settings == null ? GearProgressionRules.ReasonNoSettings
                : resources != null && !resources.IsAlive ? ReasonDefeated
                : !InReach ? ReasonWhere : null;
            if (blocked == null) return true;
            Tell(blocked);
            return false;
        }

        /// <summary>The ladder gear in a Pack cell, or false (with the player told) when there is none.</summary>
        private bool TryGetGear(int cell, out ItemStack stack, out ItemDefinition item)
        {
            stack = inventory.Get(cell);
            item = stack.IsEmpty ? null : inventory.Catalog.Get(stack.itemId);
            if (item != null && item.IsLadderGear) return true;
            Tell(GearProgressionRules.ReasonNotGear);
            return false;
        }

        private void ServerTemper(int cell, ushort alloyItemId, ushort protectionItemId)
        {
            if (!Ready() || !TryGetGear(cell, out ItemStack stack, out ItemDefinition item)) return;
            TemperSettings temper = settings.temper;
            GearItemState state = GearItemState.Of(stack);
            GearProgressionRules.TryFindAlloy(temper, alloyItemId, out AlloyGrade alloy);
            TemperProtection protection = GearProgressionRules.ProtectionOf(temper, protectionItemId);
            if (protectionItemId != 0 && protection == TemperProtection.None) { Tell("That is not a Failsafe."); return; }

            string blocked = GearProgressionRules.TemperBlockedReason(settings, item, state, alloy, protection);
            if (blocked != null) { Tell(blocked); return; }
            if (inventory.CountItem(alloy.itemId) < 1) { Tell($"You have no {NameOf(alloy.itemId)}."); return; }
            if (protection != TemperProtection.None && inventory.CountItem(protectionItemId) < 1) { Tell($"You have no {NameOf(protectionItemId)}."); return; }
            long cost = GearProgressionRules.TemperCost(settings, item, state.Temper);
            if (wallet == null || !wallet.CanAfford(cost)) { Tell(ReasonNoCreds); return; }

            float chance = GearProgressionRules.TemperChance(temper, state.Temper, alloy.step, Luck, protection);
            GearItemState next = GearProgressionRules.ResolveTemper(temper, state, alloy, chance, protection,
                UnityEngine.Random.value, UnityEngine.Random.value, out GearWorkOutcome outcome, out bool protectionSpent);

            ItemStack after = outcome == GearWorkOutcome.Broken ? ItemStack.Empty : new ItemStack(stack.itemId, 1, next.PackInto(stack.data));
            bool done = inventory.ServerChangePack(pack =>
            {
                if (!pack[cell].Equals(stack) || !Remove(pack, alloy.itemId, 1)) return false;
                if (protectionSpent && !Remove(pack, protectionItemId, 1)) return false;
                pack[cell] = after;
                return true;
            });
            if (!done || !wallet.ServerSpend(cost, CredsSink.GearWork)) return;
            Finish(GearWorkKind.Temper, outcome, stack, after);
        }

        private void ServerFuse(int targetCell, int materialCell)
        {
            if (!Ready() || !TryGetGear(targetCell, out ItemStack target, out ItemDefinition targetItem)) return;
            FusionSettings fusion = settings.fusion;
            ItemStack material = targetCell == materialCell ? ItemStack.Empty : inventory.Get(materialCell);
            ItemDefinition materialItem = material.IsEmpty ? null : inventory.Catalog.Get(material.itemId);
            GearItemState state = GearItemState.Of(target);

            string blocked = GearProgressionRules.FusionBlockedReason(settings, targetItem, state, materialItem, GearItemState.Of(material));
            if (blocked != null) { Tell(blocked); return; }
            if (fusion.catalystItemId != 0 && inventory.CountItem(fusion.catalystItemId) < 1) { Tell($"You have no {NameOf(fusion.catalystItemId)}."); return; }
            int nextStage = state.Fusion + 1;
            long cost = GearProgressionRules.FusionCost(fusion, nextStage);
            if (wallet == null || !wallet.CanAfford(cost)) { Tell(ReasonNoCreds); return; }

            bool success = UnityEngine.Random.value * 100f < GearProgressionRules.FusionChance(fusion, nextStage, Luck);
            ItemStack after = success
                ? new ItemStack(target.itemId, 1, GearProgressionRules.ResolveFusion(fusion, state).PackInto(target.data))
                : target; // a failed Fusion never harms the target
            bool materialGoes = success || fusion.failConsumesMaterial;
            bool done = inventory.ServerChangePack(pack =>
            {
                if (!pack[targetCell].Equals(target) || !pack[materialCell].Equals(material)) return false;
                if (fusion.catalystItemId != 0 && !Remove(pack, fusion.catalystItemId, 1)) return false;
                if (materialGoes) pack[materialCell] = ItemStack.Empty;
                pack[targetCell] = after;
                return true;
            });
            if (!done || !wallet.ServerSpend(cost, CredsSink.GearWork)) return;
            Finish(GearWorkKind.Fusion, success ? GearWorkOutcome.Success : GearWorkOutcome.Fail, target, after);
        }

        private void ServerTune(int cell)
        {
            if (!Ready() || !TryGetGear(cell, out ItemStack stack, out ItemDefinition item)) return;
            TuningSettings tuning = settings.tuning;
            GearItemState state = GearItemState.Of(stack);
            string blocked = GearProgressionRules.TuneBlockedReason(settings, item, state);
            if (blocked != null) { Tell(blocked); return; }

            int nextStage = state.Tune + 1;
            GearProgressionRules.TryGetBracket(tuning, nextStage, out TuningBracket bracket);
            if (inventory.CountItem(bracket.chipItemId) < bracket.chips) { Tell($"You need {bracket.chips} {NameOf(bracket.chipItemId)}."); return; }
            if (progression == null || progression.Renown < bracket.renown) { Tell($"You need {bracket.renown} Renown."); return; }
            if (wallet == null || !wallet.CanAfford(bracket.creds)) { Tell(ReasonNoCreds); return; }

            bool success = UnityEngine.Random.value * 100f < GearProgressionRules.TuneChance(tuning, nextStage);
            GearItemState next = GearProgressionRules.ResolveTune(tuning, state, success);
            ItemStack after = new ItemStack(stack.itemId, 1, next.PackInto(stack.data));
            bool done = inventory.ServerChangePack(pack =>
            {
                if (!pack[cell].Equals(stack) || !Remove(pack, bracket.chipItemId, bracket.chips)) return false;
                pack[cell] = after;
                return true;
            });
            if (!done || !progression.TrySpendRenown(bracket.renown) || !wallet.ServerSpend(bracket.creds, CredsSink.GearWork)) return;
            Finish(GearWorkKind.Tuning, success ? GearWorkOutcome.Success : GearWorkOutcome.Fail, stack, after);
        }

        private void ServerResetTuning(int cell)
        {
            if (!Ready() || !TryGetGear(cell, out ItemStack stack, out _)) return;
            GearItemState state = GearItemState.Of(stack);
            ushort chip = settings.tuning.resetChipItemId;
            if (state.Tune == 0) { Tell("It has no Tuning to reset."); return; }
            if (inventory.CountItem(chip) < 1) { Tell($"You have no {NameOf(chip)}."); return; }

            ItemStack after = new ItemStack(stack.itemId, 1, state.WithTune(0).PackInto(stack.data));
            bool done = inventory.ServerChangePack(pack =>
            {
                if (!pack[cell].Equals(stack) || !Remove(pack, chip, 1)) return false;
                pack[cell] = after;
                return true;
            });
            if (done) Finish(GearWorkKind.TuningReset, GearWorkOutcome.Success, stack, after);
        }

        private void ServerReforge(int cell)
        {
            if (!Ready() || !TryGetGear(cell, out ItemStack stack, out ItemDefinition item)) return;
            ReforgeSettings reforge = settings.reforge;
            string blocked = GearProgressionRules.ReforgeBlockedReason(settings, item, GearItemState.Of(stack));
            if (blocked != null) { Tell(blocked); return; }
            long cost = GearProgressionRules.ReforgeCost(reforge, item.itemLevel);
            if (wallet == null || !wallet.CanAfford(cost)) { Tell(ReasonNoCreds); return; }

            GearProgressionRules.CollectReforgeResults(inventory.Gear, item, wearer != null ? wearer.Faction : Factions.FactionId.None,
                wearer != null ? wearer.Body : BodyFit.None, reforgeResults);
            if (reforgeResults.Count == 0) { Tell("There is nothing this could be reforged into."); return; }

            bool success = UnityEngine.Random.value * 100f < GearProgressionRules.ReforgeChance(reforge, Luck);
            ItemStack after = success
                ? new ItemStack(reforgeResults[UnityEngine.Random.Range(0, reforgeResults.Count)], 1)
                : stack; // a failed Reforge costs only the Creds
            bool done = inventory.ServerChangePack(pack =>
            {
                if (!pack[cell].Equals(stack)) return false;
                pack[cell] = after;
                return true;
            });
            if (!done || !wallet.ServerSpend(cost, CredsSink.GearWork)) return;
            Finish(GearWorkKind.Reforge, success ? GearWorkOutcome.Success : GearWorkOutcome.Fail, stack, after);
        }

        /// <summary>Takes <paramref name="count"/> of an item out of a Pack, from wherever it is stacked. False when there are not that many.</summary>
        private static bool Remove(ItemStack[] pack, ushort itemId, int count)
        {
            if (itemId == 0 || count <= 0) return true;
            int have = 0;
            foreach (ItemStack cell in pack) if (cell.itemId == itemId) have += cell.count;
            if (have < count) return false;
            for (int i = 0; i < pack.Length && count > 0; i++)
            {
                if (pack[i].itemId != itemId) continue;
                int take = Mathf.Min(count, pack[i].count);
                pack[i] = pack[i].WithCount(pack[i].count - take);
                count -= take;
            }
            return true;
        }

        private string NameOf(ushort itemId)
        {
            ItemDefinition item = inventory.Catalog.Get(itemId);
            return item != null ? item.displayName : "material";
        }

        // ------------------------------------------------------------------ telling the owner

        private void Finish(GearWorkKind kind, GearWorkOutcome outcome, ItemStack before, ItemStack after)
        {
            var result = new GearWorkResult { kind = kind, outcome = outcome, before = before, after = after };
            if (IsSpawned)
            {
                FinishedRpc(result);
                ShownRpc(outcome);
            }
            else
            {
                Finished?.Invoke(result);
                Show(outcome, true);
            }
        }

        /// <summary>The owner alone learns what became of the piece.</summary>
        [Rpc(SendTo.Owner)]
        private void FinishedRpc(GearWorkResult result) => Finished?.Invoke(result);

        /// <summary>Everyone sees that it worked, failed or broke: the burst on the character.</summary>
        [Rpc(SendTo.Everyone)]
        private void ShownRpc(GearWorkOutcome outcome) => Show(outcome, IsOwner);

        private void Show(GearWorkOutcome outcome, bool isLocalPlayer) =>
            ClientPresentationRegistry.Feel?.GearWorked(transform, (int)outcome, isLocalPlayer);

        private void Tell(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (IsSpawned && !IsOwnedByServer) TellRpc(text);
            else ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ GM

        /// <summary>GM: writes a state onto the gear in a Pack cell. Server only.</summary>
        public bool ServerSetState(int cell, GearItemState state)
        {
            if (!HasAuthority || inventory == null || inventory.Catalog == null) return false;
            ItemStack stack = inventory.Get(cell);
            ItemDefinition item = stack.IsEmpty ? null : inventory.Catalog.Get(stack.itemId);
            if (item == null || !item.IsLadderGear) return false;
            ItemStack after = new ItemStack(stack.itemId, 1, state.PackInto(stack.data));
            return inventory.ServerChangePack(pack => { pack[cell] = after; return true; });
        }
    }
}
