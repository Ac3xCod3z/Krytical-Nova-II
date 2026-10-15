using System;
using System.Collections.Generic;
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
    public sealed class PlayerInventory : NetworkBehaviour, IHotbarItemSource, ISaveParticipant
    {
        public const int PageSize = 48;
        public const int PageCount = 3;
        public const int Capacity = PageSize * PageCount;

        private const string ReasonDefeated = "You can't use items while defeated.";
        private const string ReasonUseCooldown = "You can't use another item yet.";
        private const string ReasonAlreadyFull = "Already at full strength.";

        private delegate bool Mutation(ItemStack[] bag, ref EquippedItems worn, out string reason);

        [SerializeField] private ItemCatalog catalog;
        [Tooltip("Who may wear which gear. Checked on every equip; also the source of the starter gear from the wearer's pack.")]
        [SerializeField] private GearDictionary gearDictionary;
        [Tooltip("Grant the catalog's starter loadout when this player is created (no save system yet).")]
        [SerializeField] private bool grantStarterLoadout = true;

        private NetworkList<ItemStack> netCells;
        // What each worn item carries of its own (a pet's growth). The ids are public on PlayerEquipment; this is not.
        private readonly NetworkVariable<EquipmentData> netEquippedData = new NetworkVariable<EquipmentData>(default,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        private EquipmentData equippedData;
        private readonly ItemStack[] cells = new ItemStack[Capacity];
        private readonly ItemStack[] scratch = new ItemStack[Capacity];
        private PlayerEquipment equipment;
        private PlayerResources resources;
        private float nextUseTime;
        private bool starterGranted;
        private bool starterKitGranted;
        // The weapon handed out before the character is known; the starter kit replaces it with the Discipline's.
        private ushort placeholderWeaponId;
        private bool pullPending;
        private IGearWearer wearer;
        private PlayerCharacterProgression progression;
        private InventoryRules.EquipGate equipGate;
        private IItemUseHandler[] useHandlers = Array.Empty<IItemUseHandler>();

        /// <summary>The inventory the local user controls: the owned networked player, else the offline scene avatar.</summary>
        public static PlayerInventory Local { get; private set; }
        public static event Action LocalChanged;

        public event Action Changed;
        /// <summary>Raised on the owner when the server rejects a request (reason is user-facing).</summary>
        public event Action<string> RequestFailed;
        /// <summary>
        /// Server (or offline): an item came to this player as its own (bought from an NPC, taken from a Hostile's
        /// drop, granted), as against handed on by another player. The Weave Library collects its look.
        /// </summary>
        public event Action<ushort> Earned;

        public ItemCatalog Catalog => catalog;
        public GearDictionary Gear => gearDictionary;
        public PlayerEquipment Equipment => equipment;
        public ItemStack Get(int index) => index >= 0 && index < Capacity ? cells[index] : ItemStack.Empty;

        /// <summary>The worn item in a slot with its own state, or empty. Known to the owner and the server only.</summary>
        public ItemStack GetEquipped(EquipSlot slot)
        {
            ushort id = equipment != null ? equipment.Get(slot) : (ushort)0;
            return id != 0 ? new ItemStack(id, 1, equippedData[slot]) : ItemStack.Empty;
        }

        /// <summary>The stack at a Pack cell or an equipment slot.</summary>
        public ItemStack GetStack(ItemLocation at) =>
            at.IsBag ? Get(at.index) : at.container == ItemContainer.Equipment ? GetEquipped(at.Slot) : ItemStack.Empty;
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
            progression = GetComponent<PlayerCharacterProgression>();
            equipGate = EquipBlockedReason;
            useHandlers = GetComponents<IItemUseHandler>();
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
            netEquippedData.OnValueChanged += OnNetEquippedDataChanged;
            if (IsServer)
            {
                GrantStarterLoadout();
                WriteNetCells();
                netEquippedData.Value = equippedData;
            }
            else if (IsOwner)
            {
                equippedData = netEquippedData.Value;
                equipment.SetItemData(equippedData);
                PullNetCells();
            }
            if (IsOwner) SetLocal(this);
            else if (Local == this) SetLocal(null);
        }

        public override void OnNetworkDespawn()
        {
            netCells.OnListChanged -= OnNetCellsChanged;
            netEquippedData.OnValueChanged -= OnNetEquippedDataChanged;
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
            Mutate((ItemStack[] b, ref EquippedItems l, out string r) => InventoryRules.TryMove(b, ref l, catalog, from, to, out r, equipGate));

        private void ServerQuickEquip(byte bagIndex) =>
            Mutate((ItemStack[] b, ref EquippedItems l, out string r) => InventoryRules.TryQuickEquip(b, ref l, catalog, bagIndex, out r, equipGate));

        /// <summary>
        /// The gear dictionary decides who may wear what (faction and body), then the piece's level must not be
        /// above the character's. Null means allowed.
        /// </summary>
        private string EquipBlockedReason(ItemDefinition item)
        {
            if (gearDictionary != null && wearer != null
                && !gearDictionary.CanUse(item.itemId, wearer.Faction, wearer.Body, out string reason)) return reason;
            if (progression != null && item.itemLevel > progression.CurrentLevel) return RequiresLevel(item.itemLevel);
            return null;
        }

        public static string RequiresLevel(int level) => $"Requires level {level}.";

        /// <summary>The same check for UI (greying out gear this character cannot wear).</summary>
        public bool CanEquip(ItemDefinition item, out string reason)
        {
            reason = item != null ? EquipBlockedReason(item) : InventoryRules.ReasonUnknownItem;
            return reason == null;
        }

        // No cape: capes come with Rare gear and up.
        private static readonly EquipSlot[] StarterSlots =
            { EquipSlot.Weapon, EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots };

        /// <summary>
        /// Server/offline: what a new character starts with, once per spawn, called when its identity is known. The
        /// level-1 Common outfit of its own pack and the level-1 Common weapon of its Discipline, worn. The outfit
        /// is the lineage's basic outfit, the look the creator showed; under it is the bare body, which is what a
        /// slot shows once its piece is taken off. Slots that already hold something are left alone.
        /// </summary>
        public void ServerGrantStarterKit(string pack, Factions.FactionId faction, BodyFit body, WeaponClassId discipline)
        {
            if (starterKitGranted || !CanMutate() || gearDictionary == null) return;
            starterKitGranted = true;
            Mutate((ItemStack[] b, ref EquippedItems l, out string r) =>
            {
                r = null;
                if (placeholderWeaponId != 0 && l.ids.weapon == placeholderWeaponId) l[EquipSlot.Weapon] = ItemStack.Empty;
                foreach (var slot in StarterSlots)
                {
                    if (!l.IsEmpty(slot)) continue;
                    if (slot != EquipSlot.Weapon && string.IsNullOrEmpty(pack)) continue; // no character: weapon only
                    if (gearDictionary.TryFindStarter(slot, faction, body, pack, discipline, out var entry)
                        && catalog.Get(entry.itemId) != null)
                        l[slot] = new ItemStack(entry.itemId, 1);
                }
                return true;
            });
        }

        /// <summary>
        /// GM: empties the Pack and re-equips the level-1 starter kit of a class (the wearer's own outfit pack).
        /// Returns false when that class has no starter weapon this wearer can use.
        /// </summary>
        public bool ServerResetToLoadout(WeaponClassId weaponClass)
        {
            if (!CanMutate() || gearDictionary == null || catalog == null) return false;
            var faction = wearer != null ? wearer.Faction : Factions.FactionId.None;
            var body = wearer != null ? wearer.Body : BodyFit.None;
            string pack = wearer != null ? wearer.GearPack : null;
            if (!gearDictionary.TryFindStarter(EquipSlot.Weapon, faction, body, pack, weaponClass, out _)) return false;

            Mutate((ItemStack[] b, ref EquippedItems l, out string r) =>
            {
                r = null;
                Array.Clear(b, 0, b.Length);
                l = default;
                foreach (var slot in StarterSlots)
                {
                    if (slot != EquipSlot.Weapon && string.IsNullOrEmpty(pack)) continue;
                    if (gearDictionary.TryFindStarter(slot, faction, body, pack, weaponClass, out var entry)
                        && catalog.Get(entry.itemId) != null)
                        l[slot] = new ItemStack(entry.itemId, 1);
                }
                foreach (var s in catalog.StarterBag)
                    if (s.item != null) InventoryRules.TryAdd(b, catalog, s.item.itemId, s.count);
                return true;
            });
            return true;
        }

        /// <summary>
        /// GM: puts pieces of gear on, each with the state it carries (its Temper). What was worn in a slot goes to
        /// the Pack. A piece this character may not wear (its level, faction or class), or whose slot cannot be freed
        /// because the Pack is full, goes to the Pack instead. Returns how many were put on.
        /// </summary>
        /// <param name="packed">How many went to the Pack instead.</param>
        /// <param name="lost">How many fitted nowhere.</param>
        public int ServerWear(IReadOnlyList<ItemStack> pieces, out int packed, out int lost)
        {
            int worn = 0, toPack = 0, dropped = 0;
            if (pieces != null && CanMutate() && catalog != null)
            {
                Mutate((ItemStack[] b, ref EquippedItems l, out string r) =>
                {
                    r = null;
                    foreach (ItemStack piece in pieces)
                    {
                        ItemDefinition def = piece.IsEmpty ? null : catalog.Get(piece.itemId);
                        if (def == null) continue;
                        if (def.IsEquippable && EquipBlockedReason(def) == null)
                        {
                            ItemStack old = l[def.equipSlot];
                            if (old.IsEmpty || InventoryRules.TryAdd(b, catalog, old) > 0)
                            {
                                l[def.equipSlot] = new ItemStack(piece.itemId, 1, piece.data);
                                worn++;
                                continue;
                            }
                        }
                        if (InventoryRules.TryAdd(b, catalog, piece) > 0) toPack++; else dropped++;
                    }
                    return true;
                });
            }
            packed = toPack;
            lost = dropped;
            return worn;
        }

        private void ServerQuickUnequip(EquipSlot slot) =>
            Mutate((ItemStack[] b, ref EquippedItems l, out string r) => InventoryRules.TryQuickUnequip(b, ref l, catalog, slot, out r));

        private void ServerDestroy(ItemLocation at) =>
            Mutate((ItemStack[] b, ref EquippedItems l, out string r) => InventoryRules.TryDestroy(b, ref l, at, out r));

        private void ServerSort() =>
            Mutate((ItemStack[] b, ref EquippedItems l, out string r) => { r = null; InventoryRules.Sort(b, catalog); return true; });

        private void ServerUse(byte bagIndex)
        {
            ItemDefinition used = null;
            Mutate((ItemStack[] b, ref EquippedItems l, out string r) =>
            {
                // A Flux Flask is kept: a drink uses one of its charges, which it carries in its data.
                if (bagIndex < b.Length && !b[bagIndex].IsEmpty && catalog.Get(b[bagIndex].itemId) is ItemDefinition kept
                    && kept.useEffect == ItemUseEffect.FluxFlask)
                {
                    int charges = TryGetComponent(out PlayerFlask flask) ? flask.Charges : 3;
                    ItemStack bottle = b[bagIndex];
                    if (!FlaskRules.HasDrink(bottle.data, charges)) { r = FlaskRules.ReasonEmpty; return false; }
                    r = UseBlockedReason(kept);
                    if (r != null) return false;
                    b[bagIndex] = new ItemStack(bottle.itemId, bottle.count, FlaskRules.AfterDrink(bottle.data, charges));
                    used = kept;
                    return true;
                }
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
            if (used.useEffect != ItemUseEffect.None) HandlerFor(used.useEffect)?.ServerUse(used);

            if (used.restoreHealth <= 0f && used.restoreChi <= 0f) return; // the effect's own system presents it
            if (IsSpawned) ItemUsedRpc(used.restoreHealth > 0f, used.restoreChi > 0f);
            else PresentItemUsed(used.restoreHealth > 0f, used.restoreChi > 0f);
        }

        private IItemUseHandler HandlerFor(ItemUseEffect effect)
        {
            foreach (IItemUseHandler handler in useHandlers)
                if (handler.Effect == effect) return handler;
            return null;
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
            if (def.useEffect != ItemUseEffect.None)
            {
                IItemUseHandler handler = HandlerFor(def.useEffect);
                return handler == null ? InventoryRules.ReasonNotUsable : handler.UseBlockedReason(def);
            }
            bool healthUseful = def.restoreHealth > 0f && resources.Health < resources.MaxHealth;
            bool chiUseful = def.restoreChi > 0f && resources.Chi < resources.MaxChi;
            return healthUseful || chiUseful ? null : ReasonAlreadyFull;
        }

        /// <summary>
        /// Server only. Adds up to <paramref name="count"/> of an item (with its own state, if any). Returns how
        /// many were added. <paramref name="earned"/> says the player came by it itself (see <see cref="Earned"/>);
        /// leave it off for what passes from one player to another.
        /// </summary>
        public int ServerGive(ushort itemId, int count, ulong data = 0, bool earned = false)
        {
            if (!CanMutate()) return 0;
            int added = 0;
            Mutate((ItemStack[] b, ref EquippedItems l, out string r) =>
            {
                added = InventoryRules.TryAdd(b, catalog, itemId, count, data);
                r = added > 0 ? null : InventoryRules.ReasonBagFull;
                return added > 0;
            });
            if (added > 0 && earned) Earned?.Invoke(itemId);
            return added;
        }

        /// <summary>Server only. Adds a whole stack, state included. Returns how many of it fitted.</summary>
        public int ServerGive(in ItemStack stack, bool earned = false) =>
            stack.IsEmpty ? 0 : ServerGive(stack.itemId, stack.count, stack.data, earned);

        /// <summary>
        /// Server only: rewrites what the worn item in <paramref name="slot"/> carries of its own (the system that
        /// owns that state keeps it current: a pet's growth and hunger). Ignored when the slot is empty.
        /// </summary>
        public void ServerSetEquippedData(EquipSlot slot, ulong data)
        {
            if (!CanMutate() || equipment == null || equipment.Get(slot) == 0 || equippedData[slot] == data) return;
            var next = equippedData;
            next[slot] = data;
            SetEquippedData(next);
            Changed?.Invoke();
        }

        /// <summary>
        /// Server only: takes whatever is at a Pack cell or equipment slot out of the inventory and hands it over
        /// (to be dropped, traded away, stored). False, with the player told why, when there is nothing there.
        /// </summary>
        public bool ServerTake(ItemLocation at, out ItemStack taken)
        {
            ItemStack result = ItemStack.Empty;
            if (CanMutate())
                Mutate((ItemStack[] b, ref EquippedItems l, out string r) => InventoryRules.TryTake(b, ref l, at, out result, out r));
            taken = result;
            return !taken.IsEmpty;
        }

        /// <summary>
        /// Server only: takes <paramref name="count"/> from the stack in a Pack cell (all of it when the count is
        /// the stack's). False when the cell does not hold that many.
        /// </summary>
        /// <summary>
        /// Server only: rewrites the state an item in a Pack cell carries (a Flask's drinks). Only when the cell still
        /// holds <paramref name="expected"/>; false when it changed or the cell is empty.
        /// </summary>
        public bool ServerSetCellData(int cell, ItemStack expected, ulong data)
        {
            if (!CanMutate() || cell < 0 || cell >= Capacity) return false;
            bool done = false;
            Mutate((ItemStack[] b, ref EquippedItems l, out string r) =>
            {
                r = null;
                ItemStack held = b[cell];
                if (held.IsEmpty || !held.Equals(expected)) { r = InventoryRules.ReasonEmpty; return false; }
                b[cell] = new ItemStack(held.itemId, held.count, data);
                done = true;
                return true;
            });
            return done;
        }

        public bool ServerTakeFromCell(int cell, int count, out ItemStack taken)
        {
            ItemStack result = ItemStack.Empty;
            if (CanMutate() && cell >= 0 && cell < Capacity && count > 0)
                Mutate((ItemStack[] b, ref EquippedItems l, out string r) =>
                {
                    r = null;
                    ItemStack held = b[cell];
                    if (held.IsEmpty || held.count < count) { r = InventoryRules.ReasonEmpty; return false; }
                    result = held.WithCount(count);
                    b[cell] = held.WithCount(held.count - count);
                    return true;
                });
            taken = result;
            return !taken.IsEmpty;
        }

        /// <summary>
        /// Server only: runs a change that touches the Pack and something outside it (a Vault, a trade partner's
        /// Pack) on a scratch copy and commits it only when <paramref name="change"/> returns true. The change gets
        /// the scratch Pack; whatever else it alters it must only alter on success.
        /// </summary>
        public bool ServerChangePack(Func<ItemStack[], bool> change, string failure = null)
        {
            bool done = false;
            if (CanMutate() && change != null)
                Mutate((ItemStack[] b, ref EquippedItems l, out string r) =>
                {
                    done = change(b);
                    r = done ? null : failure;
                    return done;
                });
            return done;
        }

        /// <summary>Server only: a copy of the Pack as it is now, for rules that plan a change before making it.</summary>
        public void CopyPack(ItemStack[] into)
        {
            if (into != null) Array.Copy(cells, into, Math.Min(into.Length, Capacity));
        }

        /// <summary>Server/dev only: empties the bag (equipment untouched).</summary>
        public void ServerClear()
        {
            if (!CanMutate()) return;
            Mutate((ItemStack[] b, ref EquippedItems l, out string r) => { r = null; Array.Clear(b, 0, b.Length); return true; });
        }

        private bool CanMutate() => !IsSpawned || IsServer;

        // ---------------------------------------------------------------- saving

        [Serializable]
        private struct SavedItem
        {
            public int index;
            public ItemStack stack;
        }

        [Serializable]
        private sealed class InventorySave
        {
            public List<SavedItem> pack = new List<SavedItem>();
            /// <summary>Worn items; index is the <see cref="EquipSlot"/>.</summary>
            public List<SavedItem> worn = new List<SavedItem>();
        }

        public string SaveKey => "inventory";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave()
        {
            var save = new InventorySave();
            for (int i = 0; i < Capacity; i++)
                if (!cells[i].IsEmpty) save.pack.Add(new SavedItem { index = i, stack = cells[i] });
            foreach (var slot in EquipSlots.All)
            {
                ItemStack worn = GetEquipped(slot);
                if (!worn.IsEmpty) save.worn.Add(new SavedItem { index = (int)slot, stack = worn });
            }
            return JsonUtility.ToJson(save);
        }

        /// <summary>
        /// A saved character gets exactly what it had: this replaces the starter loadout handed out at spawn. Items
        /// that no longer exist in the catalog are dropped. A new character (null) keeps the starter loadout.
        /// </summary>
        public void RestoreSave(string json)
        {
            if (!CanMutate() || catalog == null || string.IsNullOrEmpty(json)) return;
            var save = JsonUtility.FromJson<InventorySave>(json);
            if (save == null) return;

            starterGranted = true;
            starterKitGranted = true;
            placeholderWeaponId = 0;
            Array.Clear(cells, 0, Capacity);
            if (save.pack != null)
                foreach (SavedItem item in save.pack)
                    if (item.index >= 0 && item.index < Capacity && !item.stack.IsEmpty && catalog.Get(item.stack.itemId) != null)
                        cells[item.index] = item.stack;

            var worn = default(EquippedItems);
            if (save.worn != null)
                foreach (SavedItem item in save.worn)
                {
                    var slot = (EquipSlot)item.index;
                    var def = !item.stack.IsEmpty ? catalog.Get(item.stack.itemId) : null;
                    if (def != null && EquipSlots.IsValid(slot) && def.equipSlot == slot) worn[slot] = new ItemStack(item.stack.itemId, 1, item.stack.data);
                }

            if (IsSpawned) WriteNetCells();
            SetEquippedData(worn.data);
            equipment.ServerSetLoadout(worn.ids);
            Changed?.Invoke();
        }

        private void Mutate(Mutation mutation)
        {
            if (!CanMutate() || catalog == null) return;
            Array.Copy(cells, scratch, Capacity);
            var worn = new EquippedItems(equipment.Loadout, equippedData);
            if (!mutation(scratch, ref worn, out string reason))
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
            // Data first: whoever reacts to the new loadout (a pet coming out) reads the state that came with it.
            SetEquippedData(worn.data);
            equipment.ServerSetLoadout(worn.ids);
            Changed?.Invoke();
        }

        private void SetEquippedData(EquipmentData data)
        {
            equippedData = data;
            if (IsSpawned && IsServer) netEquippedData.Value = data;
            equipment.SetItemData(data);
        }

        private void OnNetEquippedDataChanged(EquipmentData previous, EquipmentData next)
        {
            if (IsServer) return; // already applied locally
            equippedData = next;
            equipment.SetItemData(next);
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
            // Nobody is ever unarmed: until a character identity arrives (it never does for a test avatar) the
            // player holds the plainest Monoblade. The starter kit swaps it for the Discipline's weapon.
            if (loadout.weapon == 0 && gearDictionary != null
                && gearDictionary.TryFindStarter(EquipSlot.Weapon, Factions.FactionId.None, BodyFit.None, null, WeaponClassId.Monoblade, out var placeholder)
                && catalog.Get(placeholder.itemId) != null)
            {
                placeholderWeaponId = placeholder.itemId;
                loadout.weapon = placeholder.itemId;
            }
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
