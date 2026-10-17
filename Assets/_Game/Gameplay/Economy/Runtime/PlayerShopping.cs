using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Persistence;
using KryticalNova.Progression;
using KryticalNova.Social;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>
    /// A player's side of the NPC shops: buying from the vendor they stand by, and selling to any vendor what they
    /// carry. Everything is a request; the server checks that the player is by the vendor, what the thing costs
    /// (<see cref="VendorRules"/>, the same rules the window reads), the wallet and the room in the Pack, and only
    /// then moves Creds and items. A sale of something the rules say must be confirmed (Rare and better, a pet,
    /// worked gear) is refused unless it comes with the confirmation, so a stray click cannot sell it.
    /// A listing may have a weekly limit (the Armorer's forge materials, owner 17 Oct 2026: one of each per
    /// in-game week): what the character bought of it this week is kept here, saved with the character and told
    /// to its owner only, so the window can say how many are left.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerShopping : NetworkBehaviour, ISaveParticipant
    {
        // Beyond the vendor's own range: the player may have taken a step since the dialogue.
        private const float ReachPadding = 2.5f;
        /// <summary>The most of one item a single purchase may ask for.</summary>
        public const int MaxPerPurchase = 999;

        [SerializeField] private EconomySettings settings;
        [SerializeField] private VendorCatalog vendors;

        private PlayerInventory inventory;
        private PlayerWallet wallet;
        private PlayerResources resources;
        private IGearWearer wearer;
        private PlayerChat chat;
        private PlayerSaveAgent saveAgent;

        // This week's purchases of limited items, where the game is decided; the owner reads the copy below.
        private readonly Dictionary<ushort, int> bought = new Dictionary<ushort, int>();
        private long boughtWeek = long.MinValue;
        private NetworkList<VendorBought> netBought;
        private readonly NetworkVariable<long> netBoughtWeek = new NetworkVariable<long>(long.MinValue,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        /// <summary>Raised on the owner (and the host) when what was bought this week changes, or a new week starts.</summary>
        public event Action WeeklyChanged;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerShopping Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerShopping>() : null;

        public EconomySettings Settings => settings;
        public VendorCatalog Vendors => vendors;
        public long Creds => wallet != null ? wallet.Creds : 0;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            wallet = GetComponent<PlayerWallet>();
            resources = GetComponent<PlayerResources>();
            wearer = GetComponent<IGearWearer>();
            chat = GetComponent<PlayerChat>();
            saveAgent = GetComponent<PlayerSaveAgent>();
            netBought = new NetworkList<VendorBought>(null, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netBought.OnListChanged += OnNetBoughtChanged;
            netBoughtWeek.OnValueChanged += OnNetWeekChanged;
            if (IsServer) PushBought();
        }

        public override void OnNetworkDespawn()
        {
            netBought.OnListChanged -= OnNetBoughtChanged;
            netBoughtWeek.OnValueChanged -= OnNetWeekChanged;
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            netBought?.Dispose(); // guarded; covers the never-spawned offline avatar
        }

        private void OnNetBoughtChanged(NetworkListEvent<VendorBought> change) => WeeklyChanged?.Invoke();
        private void OnNetWeekChanged(long previous, long current) => WeeklyChanged?.Invoke();

        // ------------------------------------------------------------------ what the window reads

        /// <summary>What this NPC sells, or null when it has no shop.</summary>
        public VendorStock StockOf(NpcInteractable npc) => npc != null && vendors != null ? vendors.For(npc.Definition) : null;

        /// <summary>The vendor this player stands by (the nearest, when there are two), or null.</summary>
        public NpcInteractable VendorInReach() => NpcInteractable.NearestOffering(NpcOffer.Shop, transform.position, ReachPadding);

        /// <summary>True while the player stands by the NPC who keeps this stock.</summary>
        public bool InReachOf(VendorStock stock)
        {
            if (stock == null || stock.vendor == null) return false;
            IReadOnlyList<NpcInteractable> npcs = NpcInteractable.Active;
            for (int i = 0; i < npcs.Count; i++)
            {
                NpcInteractable npc = npcs[i];
                if (npc != null && npc.Definition == stock.vendor && (npc.Offers & NpcOffer.Shop) != 0 && npc.InReachOf(transform.position, ReachPadding)) return true;
            }
            return false;
        }

        /// <summary>True while any vendor is near enough to sell to.</summary>
        public bool CanSellHere => NpcInteractable.IsOffered(NpcOffer.Shop, transform.position, ReachPadding);

        /// <summary>May this character wear the piece (its faction and its body; its level is for it to grow into)?</summary>
        public bool CanWear(ItemDefinition item)
        {
            if (item == null) return false;
            if (inventory == null || inventory.Gear == null || wearer == null) return true;
            return inventory.Gear.CanUse(item.itemId, wearer.Faction, wearer.Body, out _);
        }

        /// <summary>A vendor's shelf as this player sees it.</summary>
        public void List(VendorStock stock, List<VendorOffer> results) =>
            VendorRules.List(stock, settings, inventory != null && inventory.Catalog != null ? inventory.Catalog.Items : null, CanWear, results,
                KryticalNova.World.WorldClock.Day);

        // ------------------------------------------------------------------ the weekly limit

        private int WeekDays => settings != null ? settings.vendorWeekDays : 7;

        /// <summary>The vendor week it is now.</summary>
        public long CurrentWeek => VendorRules.WeekOf(KryticalNova.World.WorldClock.Day, WeekDays);

        /// <summary>How many of an item this character bought this week. On the owner and where the game is decided.</summary>
        public int BoughtThisWeek(ushort itemId)
        {
            long week = CurrentWeek;
            if (!IsSpawned || IsServer)
                return VendorRules.BoughtIn(week, boughtWeek, bought.TryGetValue(itemId, out int count) ? count : 0);
            if (netBoughtWeek.Value != week) return 0;
            foreach (VendorBought entry in netBought)
                if (entry.itemId == itemId) return entry.count;
            return 0;
        }

        /// <summary>How many more of an offer this character may buy this week. int.MaxValue when it has no limit.</summary>
        public int RemainingThisWeek(in VendorOffer offer) =>
            offer.Item == null ? 0 : VendorRules.RemainingThisWeek(offer.WeeklyLimit, BoughtThisWeek(offer.Item.itemId));

        /// <summary>Real seconds until the next vendor week, -1 when the clock does not say.</summary>
        public double SecondsToNextWeek() =>
            KryticalNova.World.WorldClock.SecondsUntilDay(VendorRules.NextWeekStarts(KryticalNova.World.WorldClock.Day, WeekDays));

        // A new week forgets the last one's purchases.
        private void ServerTurnWeek()
        {
            long week = CurrentWeek;
            if (week == boughtWeek) return;
            boughtWeek = week;
            bought.Clear();
            PushBought();
        }

        private void PushBought()
        {
            if (IsSpawned && IsServer)
            {
                netBought.Clear();
                foreach (KeyValuePair<ushort, int> entry in bought)
                    netBought.Add(new VendorBought(entry.Key, (ushort)Mathf.Clamp(entry.Value, 0, ushort.MaxValue)));
                netBoughtWeek.Value = boughtWeek;
            }
            WeeklyChanged?.Invoke();
        }

        /// <summary>Server or offline (GM): what this character bought of limited items this week, as one line.</summary>
        public string DescribeWeek()
        {
            if (bought.Count == 0 || boughtWeek != CurrentWeek) return "Nothing limited bought this week.";
            var parts = new List<string>();
            foreach (KeyValuePair<ushort, int> entry in bought)
            {
                ItemDefinition item = inventory != null && inventory.Catalog != null ? inventory.Catalog.Get(entry.Key) : null;
                parts.Add($"{(item != null ? item.displayName : entry.Key.ToString())} x{entry.Value}");
            }
            return "Bought: " + string.Join(", ", parts) + ".";
        }

        /// <summary>Server or offline (GM): forget what this character bought this week.</summary>
        public void ServerResetWeek()
        {
            if (IsSpawned && !IsServer) return;
            boughtWeek = CurrentWeek;
            bought.Clear();
            PushBought();
        }

        /// <summary>What a vendor pays for one of an item. 0 = it will not buy it.</summary>
        public long SellValue(ItemDefinition item) => VendorRules.SellValue(vendors, settings, item);

        /// <summary>True when selling this must be confirmed first.</summary>
        public bool NeedsConfirm(ItemDefinition item, in ItemStack stack) => VendorRules.NeedsConfirm(settings, item, stack);

        // ------------------------------------------------------------------ owner -> server

        public void RequestBuy(VendorStock stock, ItemDefinition item, int count)
        {
            if (stock == null || stock.vendor == null || item == null) return;
            if (IsSpawned && IsOwner) BuyRpc(new FixedString64Bytes(stock.vendor.name), item.itemId, (ushort)Mathf.Clamp(count, 1, MaxPerPurchase));
            else if (!IsSpawned) ClientPresentationRegistry.Messages?.ShowMessage("Shops need an online session.");
        }

        /// <param name="confirmed">The player has read what is being sold and for how much, and agreed.</param>
        public void RequestSell(int packCell, int count, bool confirmed)
        {
            if (inventory == null) return;
            ItemStack held = inventory.Get(packCell);
            if (held.IsEmpty) return;
            if (IsSpawned && IsOwner) SellRpc((ushort)packCell, (ushort)Mathf.Clamp(count, 1, held.count), held, confirmed);
            else if (!IsSpawned) ClientPresentationRegistry.Messages?.ShowMessage("Shops need an online session.");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void BuyRpc(FixedString64Bytes vendor, ushort itemId, ushort count) => Answer(ServerBuy(vendor.ToString(), itemId, count));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SellRpc(ushort cell, ushort count, ItemStack expected, bool confirmed) => Answer(ServerSell(cell, count, expected, confirmed));

        // ------------------------------------------------------------------ the server decides

        /// <summary>Server: buys from a vendor. Returns why not (told to the player), or null when it was bought.</summary>
        public string ServerBuy(string vendorName, ushort itemId, int count)
        {
            if (!IsSpawned || !IsServer) return "Shops need an online session.";
            if (resources != null && !resources.IsAlive) return "You can't buy while defeated.";
            if (wallet == null || inventory == null || inventory.Catalog == null) return VendorRules.ReasonNotSold;
            VendorStock stock = vendors != null ? vendors.For(vendorName) : null;
            if (stock == null) return VendorRules.ReasonNotSold;
            if (!InReachOf(stock)) return VendorRules.ReasonTooFar;

            ItemDefinition item = inventory.Catalog.Get(itemId);
            if (item == null || !VendorRules.TryPrice(stock, settings, item, out long price, KryticalNova.World.WorldClock.Day)) return VendorRules.ReasonNotSold;
            // Gear off the rule is sold to those who can wear it, as the shelf shows it.
            if (!stock.TryGetListed(item, out _) && !CanWear(item)) return VendorRules.ReasonNotSold;

            count = Mathf.Clamp(count, 1, MaxPerPurchase);
            // This week's limit: what is left of it is all that is sold.
            int limit = stock.WeeklyLimitOf(item);
            if (limit > 0)
            {
                ServerTurnWeek();
                int left = VendorRules.RemainingThisWeek(limit, BoughtThisWeek(item.itemId));
                if (left <= 0) return VendorRules.SoldOutThisWeek(item.displayName, limit, SecondsToNextWeek());
                count = Mathf.Min(count, left);
            }
            if (!VendorRules.TryTotal(price, count, out long total)) return VendorRules.ReasonNotEnoughCreds;
            if (!wallet.CanAfford(total)) return VendorRules.ReasonNotEnoughCreds;

            // Room first: nothing is paid for that cannot be carried.
            var pack = new ItemStack[PlayerInventory.Capacity];
            inventory.CopyPack(pack);
            var wanted = new ItemStack(item.itemId, (ushort)count);
            if (StorageRules.RoomFor(pack, wanted, Mathf.Max(1, (int)item.maxStack)) < count) return InventoryRules.ReasonBagFull;

            if (!wallet.ServerSpend(total, CredsSink.Vendor)) return VendorRules.ReasonNotEnoughCreds;
            int given = inventory.ServerGive(item.itemId, count, earned: true);
            if (given < count)
            {
                // The room was there a moment ago: whatever did not fit is not paid for.
                if (VendorRules.TryTotal(price, count - given, out long back)) wallet.ServerEarn(back, CredsSource.Refund);
                total -= price * (count - given);
            }
            if (given > 0 && limit > 0)
            {
                bought[item.itemId] = BoughtThisWeek(item.itemId) + given;
                PushBought();
            }
            if (given > 0) Tell($"Bought {LootRules.Label(item, given)} from the {stock.Title} for {WalletRules.FormatCreds(total)}.");
            ServerSaveNow();
            return given > 0 ? null : InventoryRules.ReasonBagFull;
        }

        /// <summary>Server: sells from a Pack cell to the vendor the player stands by. Returns why not, or null when it was sold.</summary>
        public string ServerSell(int cell, int count, ItemStack expected, bool confirmed)
        {
            if (!IsSpawned || !IsServer) return "Shops need an online session.";
            if (resources != null && !resources.IsAlive) return "You can't sell while defeated.";
            if (wallet == null || inventory == null || inventory.Catalog == null) return VendorRules.ReasonNotBought;
            if (!CanSellHere) return VendorRules.ReasonTooFar;

            ItemStack held = inventory.Get(cell);
            // What the player saw when they agreed is what is sold: a cell that changed since is left alone.
            if (held.IsEmpty || !held.StacksWith(expected) || held.count < count || count <= 0) return InventoryRules.ReasonEmpty;
            ItemDefinition item = inventory.Catalog.Get(held.itemId);
            long unit = VendorRules.SellValue(vendors, settings, item);
            if (item == null || unit <= 0) return VendorRules.ReasonNotBought;
            if (VendorRules.NeedsConfirm(settings, item, held) && !confirmed) return VendorRules.ReasonNeedsConfirm;
            if (!VendorRules.TryTotal(unit, count, out long total) || !wallet.HasRoomFor(total)) return VendorRules.ReasonWalletFull;

            if (!inventory.ServerTakeFromCell(cell, count, out ItemStack taken)) return InventoryRules.ReasonEmpty;
            wallet.ServerEarn(total, CredsSource.VendorSale);
            Tell($"Sold {LootRules.Label(item, taken.count)} for {WalletRules.FormatCreds(total)}.");
            ServerSaveNow();
            return null;
        }

        // ------------------------------------------------------------------ telling the player

        private void Answer(string refusal)
        {
            if (string.IsNullOrEmpty(refusal)) return;
            if (IsOwnedByServer) ClientPresentationRegistry.Messages?.ShowMessage(refusal);
            else RefusedRpc(refusal);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void RefusedRpc(string reason) => ClientPresentationRegistry.Messages?.ShowMessage(reason);

        private void Tell(string text)
        {
            if (chat != null && IsSpawned && IsServer && !string.IsNullOrEmpty(text)) chat.ServerSystem(text);
        }

        // A purchase or a sale is never lost to a crash.
        private void ServerSaveNow()
        {
            if (saveAgent != null) saveAgent.Save();
        }

        // ------------------------------------------------------------------ saving (this week's limited purchases)

        [Serializable]
        private struct VendorWeekSave
        {
            public long week;
            public ushort[] items;
            public int[] counts;
        }

        public string SaveKey => "vendorWeek";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave()
        {
            var save = new VendorWeekSave { week = boughtWeek, items = new ushort[bought.Count], counts = new int[bought.Count] };
            int i = 0;
            foreach (KeyValuePair<ushort, int> entry in bought)
            {
                save.items[i] = entry.Key;
                save.counts[i++] = entry.Value;
            }
            return JsonUtility.ToJson(save);
        }

        public void RestoreSave(string json)
        {
            if ((IsSpawned && !IsServer) || string.IsNullOrEmpty(json)) return;
            VendorWeekSave save = JsonUtility.FromJson<VendorWeekSave>(json);
            boughtWeek = save.week;
            bought.Clear();
            if (save.items != null && save.counts != null)
                for (int i = 0; i < save.items.Length && i < save.counts.Length; i++)
                    if (save.counts[i] > 0) bought[save.items[i]] = save.counts[i];
            PushBought();
        }
    }
}
