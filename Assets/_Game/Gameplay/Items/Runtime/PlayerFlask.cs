using KryticalNova.Networking;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.NPC;
using KryticalNova.Persistence;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;
using KryticalNova.Wallet;

namespace KryticalNova.Items
{
    /// <summary>
    /// The Flux Flask (Twelve Sky's Calabash): a bottle kept in the Pack and drunk from the Pack or the Hotbar. Each
    /// drink gives back a share of Max Flux over a few seconds and uses one of its charges; the bottle stays. An
    /// Apothecary refills every Flask in the Pack for Creds (a button in its dialogue). The server decides; the
    /// drinks used travel with the Flask in its stack's data (<see cref="FlaskRules"/>). Numbers: <see cref="FlaskSettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerFlask : NetworkBehaviour, IItemUseHandler
    {
        private const float ReachPadding = 2.5f;

        [SerializeField] private FlaskSettings settings;

        private PlayerInventory inventory;
        private PlayerResources resources;
        private IWallet wallet;
        private PlayerSaveAgent saveAgent;

        private float flowLeft;      // Flux still to come from the drink taken
        private float flowPerSecond;

        public static PlayerFlask Local => LocalPlayer.Get<PlayerFlask>();

        public FlaskSettings Settings => settings;
        /// <summary>Drinks a full Flask holds.</summary>
        public int Charges => settings != null ? settings.charges : 3;
        private bool Decides => !IsSpawned || IsServer;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            resources = GetComponent<PlayerResources>();
            wallet = GetComponent<IWallet>();
            saveAgent = GetComponent<PlayerSaveAgent>();
        }

        // ------------------------------------------------------------------ a drink (server)

        public ItemUseEffect Effect => ItemUseEffect.FluxFlask;

        /// <summary>Why a drink would be wasted now. An empty Flask is refused by the inventory, which sees its data.</summary>
        public string UseBlockedReason(UsedItem item)
        {
            if (flowLeft > 0f) return FlaskRules.ReasonFlowing;
            if (resources != null && resources.Chi >= resources.MaxChi) return FlaskRules.ReasonFull;
            return null;
        }

        public void ServerUse(UsedItem item)
        {
            if (!Decides || resources == null) return;
            float share = settings != null ? settings.fluxShare : 0.45f, seconds = settings != null ? settings.seconds : 8f;
            flowLeft = resources.MaxChi * share;
            flowPerSecond = FlaskRules.FluxPerSecond(resources.MaxChi, share, seconds);
            if (IsSpawned) DrankRpc(); else ClientPresentationRegistry.Feel?.Consumed(transform, false, true);
        }

        [Rpc(SendTo.Everyone)]
        private void DrankRpc() => ClientPresentationRegistry.Feel?.Consumed(transform, false, true);

        private void Update()
        {
            if (flowLeft <= 0f || !Decides) return;
            if (resources == null || !resources.IsAlive) { flowLeft = 0f; return; }
            float amount = Mathf.Min(flowLeft, flowPerSecond * Time.deltaTime);
            flowLeft -= amount;
            resources.RestoreChi(amount);
        }

        // ------------------------------------------------------------------ refilling at an Apothecary

        /// <summary>Owner: asks the server to refill every Flask in the Pack at the Apothecary in reach.</summary>
        public void RequestRefill()
        {
            if (IsSpawned && IsOwner) RefillRpc();
            else if (!IsSpawned) Tell(ServerRefill());
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RefillRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Items, nameof(RefillRpc))) return;
            Tell(ServerRefill());
        }

        /// <summary>Server: refills every Flask in the Pack for Creds. Returns what the player is told.</summary>
        public string ServerRefill()
        {
            if (!Decides) return null;
            if (resources != null && !resources.IsAlive) return "You can't do that while defeated.";
            if (NpcInteractable.NearestOffering(NpcOffer.FlaskRefill, transform.position, ReachPadding) == null) return FlaskRules.ReasonNoApothecary;
            if (inventory == null || inventory.Catalog == null || wallet == null) return FlaskRules.ReasonNoApothecary;

            int charges = Charges, flasks = 0, drinks = 0;
            long cost = 0, price = settings != null ? settings.refillPricePerCharge : 60;
            for (int cell = 0; cell < PlayerInventory.Capacity; cell++)
            {
                ItemStack held = inventory.Get(cell);
                if (held.IsEmpty || !IsFlask(held.itemId) || FlaskRules.Used(held.data, charges) == 0) continue;
                flasks++;
                drinks += FlaskRules.Used(held.data, charges);
                cost += FlaskRules.RefillCost(held.data, charges, price);
            }
            if (flasks == 0) return FlaskRules.ReasonNoneToRefill;
            if (!wallet.CanAfford(cost)) return $"Refilling costs {WalletRules.FormatCreds(cost)}. You don't have enough Creds.";
            if (cost > 0 && !wallet.ServerSpend(cost, CredsSink.Vendor)) return "You don't have enough Creds.";
            for (int cell = 0; cell < PlayerInventory.Capacity; cell++)
            {
                ItemStack held = inventory.Get(cell);
                if (held.IsEmpty || !IsFlask(held.itemId) || FlaskRules.Used(held.data, charges) == 0) continue;
                inventory.ServerSetCellData(cell, held, FlaskRules.Refilled(held.data));
            }
            if (saveAgent != null) saveAgent.Save();
            return $"Refilled {drinks} drink{(drinks == 1 ? "" : "s")} for {WalletRules.FormatCreds(cost)}.";
        }

        private bool IsFlask(ushort itemId)
        {
            ItemDefinition def = inventory.Catalog.Get(itemId);
            return def != null && def.useEffect == ItemUseEffect.FluxFlask;
        }

        /// <summary>What a Flask's tooltip adds: its drinks left and what a refill costs.</summary>
        public string TooltipLine(in ItemStack stack)
        {
            int charges = Charges;
            ulong data = stack.IsEmpty ? 0UL : stack.data;
            long price = settings != null ? settings.refillPricePerCharge : 60;
            return $"Drinks left: {FlaskRules.Left(data, charges)} of {charges}. An Apothecary refills it for {WalletRules.FormatCreds(price)} a drink.";
        }

        private void Tell(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (IsSpawned && !IsOwner) TellRpc(text);
            else ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);
    }
}
