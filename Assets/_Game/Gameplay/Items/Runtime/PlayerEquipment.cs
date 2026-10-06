using System;
using System.Collections.Generic;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// The equipped loadout. Server-written and readable by everyone (other players must see your gear). Every peer
    /// derives stat modifiers and visuals from the replicated loadout, so neither needs its own sync.
    /// What each worn piece carries of its own (its Temper, Fusion and Tuning) is handed over by the inventory on the
    /// server and on the owner. Everyone else is told only the Temper % of each slot: enough to show the glow.
    /// Offline (unspawned) the local copy is authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEquipment : NetworkBehaviour
    {
        [SerializeField] private ItemCatalog catalog;
        [Tooltip("The numbers behind Temper, Fusion and Tuning. Without it worn gear gives its baked stats only.")]
        [SerializeField] private GearProgressionSettings progression;

        private readonly NetworkVariable<EquipmentLoadout> netLoadout = new NetworkVariable<EquipmentLoadout>(
            writePerm: NetworkVariableWritePermission.Server);
        // Temper % per slot, for everyone: what an observer needs to draw the glow.
        private readonly NetworkVariable<EquipmentTemper> netTemper = new NetworkVariable<EquipmentTemper>(
            writePerm: NetworkVariableWritePermission.Server);
        private EquipmentData itemData;
        private bool hasItemData;

        private CharacterCombatStats combatStats;
        private EquipmentLoadout current;
        // One stable source object per slot so unequipping removes exactly that slot's modifiers.
        private readonly object[] slotSources = new object[EquipSlots.Count];
        private readonly List<StatModifier> resolved = new List<StatModifier>(8);

        /// <summary>(slot, previousItemId, newItemId). Raised on every peer, including the initial state on spawn.</summary>
        public event Action<EquipSlot, ushort, ushort> SlotChanged;
        /// <summary>The Temper % shown on a slot changed (also raised when the piece in it changes). On every peer.</summary>
        public event Action<EquipSlot> TemperChanged;

        public ItemCatalog Catalog => catalog;
        public GearProgressionSettings Progression => progression;

        /// <summary>The Temper % of the piece worn in a slot, as every peer knows it.</summary>
        public int GetTemper(EquipSlot slot)
        {
            var def = GetDefinition(slot);
            if (def == null || !def.IsLadderGear) return 0;
            return hasItemData ? GearItemState.Unpack(itemData[slot]).Temper : netTemper.Value[slot];
        }
        public EquipmentLoadout Loadout => current;
        public ushort Get(EquipSlot slot) => current[slot];
        public ItemDefinition GetDefinition(EquipSlot slot) => catalog != null ? catalog.Get(current[slot]) : null;

        private void Awake()
        {
            combatStats = GetComponent<CharacterCombatStats>();
            for (int i = 0; i < slotSources.Length; i++) slotSources[i] = new object();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netLoadout.OnValueChanged += OnNetLoadoutChanged;
            netTemper.OnValueChanged += OnNetTemperChanged;
            if (IsServer)
            {
                netLoadout.Value = current;
                netTemper.Value = TemperOf(itemData);
            }
            else Apply(netLoadout.Value);
        }

        public override void OnNetworkDespawn()
        {
            netLoadout.OnValueChanged -= OnNetLoadoutChanged;
            netTemper.OnValueChanged -= OnNetTemperChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>Server (or offline) only: commit a validated loadout.</summary>
        public void ServerSetLoadout(EquipmentLoadout loadout)
        {
            if (IsSpawned && !IsServer) return;
            Apply(loadout);
            if (!IsSpawned) return;
            netLoadout.Value = loadout;
            // The Temper everyone sees is read per piece: a new piece in a slot means a new reading.
            netTemper.Value = TemperOf(itemData);
        }

        /// <summary>
        /// The state of the worn pieces, from the inventory: on the server (which then tells everyone the Temper %)
        /// and on the owner. Stats of slots whose state changed are worked out again.
        /// </summary>
        public void SetItemData(EquipmentData data)
        {
            EquipmentData previous = itemData;
            bool first = !hasItemData;
            itemData = data;
            hasItemData = true;
            if (IsSpawned && IsServer) netTemper.Value = TemperOf(data);
            foreach (var slot in EquipSlots.All)
            {
                if (!first && previous[slot] == data[slot]) continue;
                var def = GetDefinition(slot);
                if (def == null || !def.IsLadderGear) continue; // a pet's state is its own system's business
                ApplyStats(slot, current[slot]);
                TemperChanged?.Invoke(slot);
            }
        }

        private EquipmentTemper TemperOf(EquipmentData data)
        {
            var temper = default(EquipmentTemper);
            foreach (var slot in EquipSlots.All)
            {
                var def = GetDefinition(slot);
                if (def != null && def.IsLadderGear) temper[slot] = GearItemState.Unpack(data[slot]).Temper;
            }
            return temper;
        }

        private void OnNetTemperChanged(EquipmentTemper previous, EquipmentTemper next)
        {
            if (IsServer || hasItemData) return; // the server and the owner have the whole state already
            foreach (var slot in EquipSlots.All)
            {
                if (previous[slot] == next[slot]) continue;
                ApplyStats(slot, current[slot]);
                TemperChanged?.Invoke(slot);
            }
        }

        private void OnNetLoadoutChanged(EquipmentLoadout previous, EquipmentLoadout next)
        {
            if (IsServer) return; // already applied locally in ServerSetLoadout
            Apply(next);
        }

        private void Apply(EquipmentLoadout next)
        {
            var previous = current;
            current = next;
            foreach (var slot in EquipSlots.All)
            {
                ushort before = previous[slot];
                ushort after = next[slot];
                if (before == after) continue;
                ApplyStats(slot, after);
                SlotChanged?.Invoke(slot, before, after);
                TemperChanged?.Invoke(slot);
            }
        }

        private void ApplyStats(EquipSlot slot, ushort itemId)
        {
            if (combatStats == null) return;
            object source = slotSources[(int)slot];
            var def = catalog != null ? catalog.Get(itemId) : null;
            resolved.Clear();
            if (def != null)
            {
                // Server and owner know the piece's whole state; an observer knows its Temper %, which is close enough
                // for a mirror the server never reads.
                ulong data = !def.IsLadderGear ? 0
                    : hasItemData ? itemData[slot] : new GearItemState(netTemper.Value[slot], 0, 0).Pack();
                GearStatResolver.Resolve(new ItemStack(itemId, 1, data), def, source, resolved, progression);
            }
            combatStats.SetSourceModifiers(source, resolved); // one recalculation per slot, also when emptied
        }
    }
}
