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
    /// Offline (unspawned) the local copy is authoritative.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEquipment : NetworkBehaviour
    {
        [SerializeField] private ItemCatalog catalog;

        private readonly NetworkVariable<EquipmentLoadout> netLoadout = new NetworkVariable<EquipmentLoadout>(
            writePerm: NetworkVariableWritePermission.Server);

        private CharacterCombatStats combatStats;
        private EquipmentLoadout current;
        // One stable source object per slot so unequipping removes exactly that slot's modifiers.
        private readonly object[] slotSources = new object[EquipSlots.Count];
        private readonly List<StatModifier> resolved = new List<StatModifier>(8);

        /// <summary>(slot, previousItemId, newItemId). Raised on every peer, including the initial state on spawn.</summary>
        public event Action<EquipSlot, ushort, ushort> SlotChanged;

        public ItemCatalog Catalog => catalog;
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
            if (IsServer) netLoadout.Value = current;
            else Apply(netLoadout.Value);
        }

        public override void OnNetworkDespawn()
        {
            netLoadout.OnValueChanged -= OnNetLoadoutChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>Server (or offline) only: commit a validated loadout.</summary>
        public void ServerSetLoadout(EquipmentLoadout loadout)
        {
            if (IsSpawned && !IsServer) return;
            Apply(loadout);
            if (IsSpawned) netLoadout.Value = loadout;
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
            }
        }

        private void ApplyStats(EquipSlot slot, ushort itemId)
        {
            if (combatStats == null) return;
            object source = slotSources[(int)slot];
            var def = catalog != null ? catalog.Get(itemId) : null;
            resolved.Clear();
            // The loadout carries item IDs only; when gear gains per-item state (Temper, Fusion...) the stack comes from it.
            if (def != null) GearStatResolver.Resolve(new ItemStack(itemId, 1), def, source, resolved);
            combatStats.SetSourceModifiers(source, resolved); // one recalculation per slot, also when emptied
        }
    }
}
