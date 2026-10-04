using System;
using KryticalNova.Core;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// Server-authoritative bag. Clients only send intents; the server re-validates through <see cref="InventoryRules"/>
    /// against a scratch copy and writes back the result. The bag is owner-readable only, so other players never see it.
    /// Offline (unspawned scene avatar) the same rules run locally.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerEquipment))]
    public sealed class PlayerInventory : NetworkBehaviour, IHotbarItemSource
    {
        public const int PageSize = 48;
        public const int PageCount = 3;
        public const int Capacity = PageSize * PageCount;

        private const string ReasonDefeated = "You can't use items while defeated.";
        private const string ReasonUseCooldown = "You can't use another item yet.";
        private const string ReasonAlreadyFull = "Already at full strength.";

        private delegate bool Mutation(ItemStack[] bag, ref EquipmentLoadout loadout, out string reason);

        [SerializeField] private ItemCatalog catalog;
        [Tooltip("Who may wear which gear. Checked on every equip; also the source of the starter gear from the wearer's pack.")]
        [SerializeField] private GearDictionary gearDictionary;
        [Tooltip("Grant the catalog's starter loadout when this player is created (no save system yet).")]
        [SerializeField] private bool grantStarterLoadout = true;

        private NetworkList<ItemStack> netCells;
        private readonly ItemStack[] cells = new ItemStack[Capacity];
        private readonly ItemStack[] scratch = new ItemStack[Capacity];
        private PlayerEquipment equipment;
        private PlayerResources resources;
        private float nextUseTime;
        private bool starterGranted;
        private bool packGearGranted;
        private bool pullPending;
        private IGearWearer wearer;
        private InventoryRules.EquipGate equipGate;

        /// <summary>The inventory the local user controls: the owned networked player, else the offline scene avatar.</summary>
        public static PlayerInventory Local { get; private set; }
        public static event Action LocalChanged;

        public event Action Changed;
        /// <summary>Raised on the owner when the server rejects a request (reason is user-facing).</summary>
        public event Action<string> RequestFailed;

        public ItemCatalog Catalog => catalog;
        public PlayerEquipment Equipment => equipment;
        public ItemStack Get(int index) => index >= 0 && index < Capacity ? cells[index] : ItemStack.Empty;
        public ItemDefinition GetDefinition(int index) => catalog != null ? catalog.Get(Get(index).itemId) : null;

        public int UsedCells
        {
            get
            {
                int used = 0;
                foreach (var c in cells) if (!c.IsEmpty) used++;
                return used;
            }
        }

        private void Awake()
        {
            equipment = GetComponent<PlayerEquipment>();
            resources = GetComponent<PlayerResources>();
            wearer = GetComponent<IGearWearer>();
            equipGate = EquipBlockedReason;
            netCells = new NetworkList<ItemStack>(readPerm: NetworkVariableReadPermission.Owner,
                writePerm: NetworkVariableWritePermission.Server);
        }

        private void OnEnable()
        {
            // Only the offline scene avatar claims Local here. Networked players are enabled before they spawn, and a
            // remote player's proxy must never become Local, so they wait for OnNetworkSpawn's ownership check.
            var nm = NetworkManager.Singleton;
            if (!IsSpawned && (nm == null || !nm.IsListening) && Local == null) SetLocal(this);
        }

        private void Start()
        {
            // Offline avatar: nobody else will grant the starter kit.
            if (!IsSpawned) GrantStarterLoadout();
        }

        private void OnDisable()
        {
            if (Local == this) SetLocal(null);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netCells.OnListChanged += OnNetCellsChanged;
            if (IsServer)
            {
                GrantStarterLoadout();
                WriteNetCells();
            }
            else if (IsOwner)
            {
                PullNetCells();
            }
            if (IsOwner) SetLocal(this);
            else if (Local == this) SetLocal(null);
        }

        public override void OnNetworkDespawn()
        {
            netCells.OnListChanged -= OnNetCellsChanged;
            if (Local == this) SetLocal(null);
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            netCells?.Dispose(); // guarded; covers the never-spawned offline avatar
        }

        private static void SetLocal(PlayerInventory inventory)
        {
            if (Local == inventory) return;
            Local = inventory;
            LocalChanged?.Invoke();
        }

        // ---------------- Client API (called by UI) ----------------

        public void RequestMove(ItemLocation from, ItemLocation to)
        {
            if (IsSpawned) MoveRpc(from, to); else ServerMove(from, to);
        }

        public void RequestQuickEquip(int bagIndex)
        {
            if (IsSpawned) QuickEquipRpc((byte)bagIndex); else ServerQuickEquip((byte)bagIndex);
        }

        public void RequestQuickUnequip(EquipSlot slot)
        {
            if (IsSpawned) QuickUnequipRpc(slot); else ServerQuickUnequip(slot);
        }

        public void RequestDestroy(ItemLocation at)
        {
            if (IsSpawned) DestroyRpc(at); else ServerDestroy(at);
        }

        public void RequestSort()
        {
            if (IsSpawned) SortRpc(); else ServerSort();
        }

        /// <summary>Uses one consumable (pill) from a bag cell.</summary>
        public void RequestUse(int bagIndex)
        {
            if (IsSpawned) UseRpc((byte)bagIndex); else ServerUse((byte)bagIndex);
        }

        // ---------------- Hotbar items (IHotbarItemSource) ----------------

        public bool CanSlotItem(ushort itemId)
        {
            var def = catalog != null ? catalog.Get(itemId) : null;
            return def != null && def.IsHotbarUsable;
        }

        public int CountItem(ushort itemId)
        {
            int count = 0;
            foreach (var cell in cells) if (cell.itemId == itemId) count += cell.count;
            return count;
        }

        public string GetItemLabel(ushort itemId)
        {
            var def = catalog != null ? catalog.Get(itemId) : null;
            if (def == null) return string.Empty;
            return string.IsNullOrEmpty(def.placeholderGlyph) ? def.displayName : def.placeholderGlyph;
        }

        public Sprite GetItemIcon(ushort itemId)
        {
            var def = catalog != null ? catalog.Get(itemId) : null;
            return def != null ? def.icon : null;
        }

        /// <summary>Uses one of the item from the first bag cell that holds it.</summary>
        public bool UseItem(ushort itemId)
        {
            if (itemId == 0) return false;
            for (int i = 0; i < Capacity; i++)
            {
                if (cells[i].itemId != itemId) continue;
                RequestUse(i);
                return true;
            }
            NotifyFailed("You have none left.");
            return false;
        }

        // ---------------- RPCs ----------------

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void MoveRpc(ItemLocation from, ItemLocation to) => ServerMove(from, to);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void QuickEquipRpc(byte bagIndex) => ServerQuickEquip(bagIndex);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void QuickUnequipRpc(EquipSlot slot) => ServerQuickUnequip(slot);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DestroyRpc(ItemLocation at) => ServerDestroy(at);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SortRpc() => ServerSort();

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UseRpc(byte bagIndex) => ServerUse(bagIndex);

        [Rpc(SendTo.Owner)]
        private void RequestFailedRpc(string reason) => NotifyFailed(reason);

        // ---------------- Server logic ----------------

        private void ServerMove(ItemLocation from, ItemLocation to) =>
            Mutate((ItemStack[] b, ref EquipmentLoadout l, out string r) => InventoryRules.TryMove(b, ref l, catalog, from, to, out r, equipGate));

        private void ServerQuickEquip(byte bagIndex) =>
            Mutate((ItemStack[] b, ref EquipmentLoadout l, out string r) => InventoryRules.TryQuickEquip(b, ref l, catalog, bagIndex, out r, equipGate));

        /// <summary>The gear dictionary decides who may wear what: faction and body. Null means allowed.</summary>
        private string EquipBlockedReason(ItemDefinition item)
        {
            if (gearDictionary == null || wearer == null) return null;
            return gearDictionary.CanUse(item.itemId, wearer.Faction, wearer.Body, out string reason) ? null : reason;
        }

        /// <summary>The same check for UI (greying out gear this character cannot wear).</summary>
        public bool CanEquip(ItemDefinition item, out string reason)
        {
            reason = item != null ? EquipBlockedReason(item) : InventoryRules.ReasonUnknownItem;
            return reason == null;
        }

        /// <summary>
        /// Server/offline: puts some gear from the wearer's own pack in the bag, one piece per rarity for each slot.
        /// A stand-in until gear is earned in the world; called once the character's identity is known.
        /// </summary>
        public void ServerGrantPackGear(string pack)
        {
            if (packGearGranted || !CanMutate() || gearDictionary == null || string.IsNullOrEmpty(pack)) return;
            packGearGranted = true;
            Mutate((ItemStack[] b, ref EquipmentLoadout l, out string r) =>
            {
                r = null;
                // Rows are ordered by item ID, so the last row of a (slot, rarity) is its highest variant: the piece
                // that looks least like the basic outfit the character already wears.
                var picks = new System.Collections.Generic.Dictionary<int, ushort>();
                foreach (var entry in gearDictionary.Entries)
                    if (entry.pack == pack) picks[((int)entry.slot << 8) | (int)entry.rarity] = entry.itemId;
                foreach (ushort itemId in picks.Values) InventoryRules.TryAdd(b, catalog, itemId, 1);
                return true;
            });
        }

        private void ServerQuickUnequip(EquipSlot slot) =>
            Mutate((ItemStack[] b, ref EquipmentLoadout l, out string r) => InventoryRules.TryQuickUnequip(b, ref l, catalog, slot, out r));

        private void ServerDestroy(ItemLocation at) =>
            Mutate((ItemStack[] b, ref EquipmentLoadout l, out string r) => InventoryRules.TryDestroy(b, ref l, at, out r));

        private void ServerSort() =>
            Mutate((ItemStack[] b, ref EquipmentLoadout l, out string r) => { r = null; InventoryRules.Sort(b, catalog); return true; });

        private void ServerUse(byte bagIndex)
        {
            ItemDefinition used = null;
            Mutate((ItemStack[] b, ref EquipmentLoadout l, out string r) =>
            {
                if (!InventoryRules.TryConsume(b, catalog, bagIndex, out used, out r)) return false;
                r = UseBlockedReason(used);
                if (r == null) return true;
                used = null;
                return false;
            });
            if (used == null) return;

            nextUseTime = Time.time + used.useCooldown;
            if (used.restoreHealth > 0f) resources.Heal(used.restoreHealth);
            if (used.restoreChi > 0f) resources.RestoreChi(used.restoreChi);

            if (IsSpawned) ItemUsedRpc(used.restoreHealth > 0f, used.restoreChi > 0f);
            else PresentItemUsed(used.restoreHealth > 0f, used.restoreChi > 0f);
        }

        /// <summary>Everyone sees the pill take effect; the server alone knows it was actually consumed.</summary>
        [Rpc(SendTo.Everyone)]
        private void ItemUsedRpc(bool restoredHealth, bool restoredChi) => PresentItemUsed(restoredHealth, restoredChi);

        private void PresentItemUsed(bool restoredHealth, bool restoredChi) =>
            ClientPresentationRegistry.Feel?.Consumed(transform, restoredHealth, restoredChi);

        /// <summary>Why a consumable would be wasted right now, or null when it can be used.</summary>
        private string UseBlockedReason(ItemDefinition def)
        {
            if (resources == null || !resources.IsAlive) return ReasonDefeated;
            if (Time.time < nextUseTime) return ReasonUseCooldown;
            bool healthUseful = def.restoreHealth > 0f && resources.Health < resources.MaxHealth;
            bool chiUseful = def.restoreChi > 0f && resources.Chi < resources.MaxChi;
            return healthUseful || chiUseful ? null : ReasonAlreadyFull;
        }

        /// <summary>Server/dev only. Returns how many were added.</summary>
        public int ServerGive(ushort itemId, int count)
        {
            if (!CanMutate()) return 0;
            int added = 0;
            Mutate((ItemStack[] b, ref EquipmentLoadout l, out string r) =>
            {
                added = InventoryRules.TryAdd(b, catalog, itemId, count);
                r = added > 0 ? null : InventoryRules.ReasonBagFull;
                return added > 0;
            });
            return added;
        }

        /// <summary>Server/dev only: empties the bag (equipment untouched).</summary>
        public void ServerClear()
        {
            if (!CanMutate()) return;
            Mutate((ItemStack[] b, ref EquipmentLoadout l, out string r) => { r = null; Array.Clear(b, 0, b.Length); return true; });
        }

        private bool CanMutate() => !IsSpawned || IsServer;

        private void Mutate(Mutation mutation)
        {
            if (!CanMutate() || catalog == null) return;
            Array.Copy(cells, scratch, Capacity);
            var loadout = equipment.Loadout;
            if (!mutation(scratch, ref loadout, out string reason))
            {
                if (!string.IsNullOrEmpty(reason))
                {
                    if (IsSpawned && !IsOwnedByServer) RequestFailedRpc(reason);
                    else NotifyFailed(reason);
                }
                return;
            }
            Array.Copy(scratch, cells, Capacity);
            if (IsSpawned) WriteNetCells();
            equipment.ServerSetLoadout(loadout);
            Changed?.Invoke();
        }

        private void NotifyFailed(string reason)
        {
            RequestFailed?.Invoke(reason);
            ClientPresentationRegistry.Messages?.ShowMessage(reason);
        }

        private void GrantStarterLoadout()
        {
            if (starterGranted || !grantStarterLoadout || catalog == null) return;
            starterGranted = true;
            var loadout = equipment.Loadout;
            foreach (var def in catalog.StarterEquipped)
                if (def != null && def.IsEquippable) loadout[def.equipSlot] = def.itemId;
            foreach (var s in catalog.StarterBag)
                if (s.item != null) InventoryRules.TryAdd(cells, catalog, s.item.itemId, s.count);
            equipment.ServerSetLoadout(loadout);
            Changed?.Invoke();
        }

        private void WriteNetCells()
        {
            if (netCells.Count != Capacity)
            {
                netCells.Clear();
                for (int i = 0; i < Capacity; i++) netCells.Add(cells[i]);
                return;
            }
            for (int i = 0; i < Capacity; i++) netCells.Set(i, cells[i]); // Set is a no-op when unchanged
        }

        private void PullNetCells()
        {
            int n = Mathf.Min(netCells.Count, Capacity);
            for (int i = 0; i < n; i++) cells[i] = netCells[i];
            for (int i = n; i < Capacity; i++) cells[i] = ItemStack.Empty;
            Changed?.Invoke();
        }

        private void OnNetCellsChanged(NetworkListEvent<ItemStack> change)
        {
            // Initial sync arrives as one event per cell; coalesce into a single pull per frame.
            if (!IsServer && IsOwner) pullPending = true;
        }

        private void LateUpdate()
        {
            if (!pullPending) return;
            pullPending = false;
            PullNetCells();
        }
    }
}
