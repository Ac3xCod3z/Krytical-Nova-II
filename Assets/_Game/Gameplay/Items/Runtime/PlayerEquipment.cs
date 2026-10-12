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
    /// It also carries what the character shows in place of what it wears: the Weave and the dye of each slot
    /// (<see cref="EquipmentLooks"/>), for everyone. Looks change how a character is drawn and nothing else: stats,
    /// reach and the Temper glow always come from the worn piece.
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
        private readonly NetworkVariable<EquipmentLooks> netLooks = new NetworkVariable<EquipmentLooks>(
            writePerm: NetworkVariableWritePermission.Server);
        private EquipmentData itemData;
        private bool hasItemData;
        private EquipmentLooks looks;

        private CharacterCombatStats combatStats;
        private EquipmentLoadout current;
        // One stable source object per slot so unequipping removes exactly that slot's modifiers.
        private readonly object[] slotSources = new object[EquipSlots.Count];
        private readonly List<StatModifier> resolved = new List<StatModifier>(8);

        /// <summary>(slot, previousItemId, newItemId). Raised on every peer, including the initial state on spawn.</summary>
        public event Action<EquipSlot, ushort, ushort> SlotChanged;
        /// <summary>The Temper % shown on a slot changed (also raised when the piece in it changes). On every peer.</summary>
        public event Action<EquipSlot> TemperChanged;
        /// <summary>The Weave or the dye of a slot changed, or the Morph of the pet slot. On every peer.</summary>
        public event Action<EquipSlot> LookChanged;

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

        /// <summary>The Weave and the dye of every slot, as everyone sees them.</summary>
        public EquipmentLooks Looks => looks;
        /// <summary>The colourways a dyed piece can wear.</summary>
        public GearThemeLibrary Colourways => catalog != null ? catalog.Themes : null;

        /// <summary>The piece a slot shows: the worn one, or the Weave over it. Null when nothing is worn.</summary>
        public ItemDefinition GetShownDefinition(EquipSlot slot)
        {
            ItemDefinition worn = GetDefinition(slot);
            ushort weave = looks.GetWeave(slot);
            return weave == 0 || catalog == null ? worn : WeaveRules.Shown(worn, catalog.Get(weave));
        }

        /// <summary>The colour grid of the colourway a slot is dyed in, or null (the piece's own colours).</summary>
        public Color32[] GetDyeCells(EquipSlot slot)
        {
            ushort colourway = looks.GetDye(slot);
            GearThemeLibrary colourways = Colourways;
            return colourway != 0 && colourways != null && colourways.TryGet(colourway, out var theme) ? theme.cells : null;
        }

        /// <summary>The colourway the piece worn in a slot is dyed in, as every peer knows it, or 0.</summary>
        public ushort GetDye(EquipSlot slot) => looks.GetDye(slot);

        /// <summary>Server (or offline) only: commit validated looks. The dyes are the worn pieces' own, whatever is passed.</summary>
        public void ServerSetLooks(EquipmentLooks next)
        {
            if (IsSpawned && !IsServer) return;
            next = WithWornDyes(next);
            if (next.Equals(looks)) return;
            ApplyLooks(next);
            if (IsSpawned) netLooks.Value = next;
        }

        /// <summary>The looks with each outfit slot's dye read off the piece worn in it.</summary>
        private EquipmentLooks WithWornDyes(EquipmentLooks next)
        {
            foreach (EquipSlot slot in WeaveRules.DyeSlots)
            {
                var def = GetDefinition(slot);
                next.SetDye(slot, hasItemData && def != null && def.IsLadderGear ? GearItemState.Unpack(itemData[slot]).Dye : (ushort)0);
            }
            return next;
        }

        private void ApplyLooks(EquipmentLooks next)
        {
            EquipmentLooks previous = looks;
            looks = next;
            foreach (EquipSlot slot in WeaveRules.WeaveSlots)
                if (previous.GetWeave(slot) != next.GetWeave(slot) || previous.GetDye(slot) != next.GetDye(slot))
                    LookChanged?.Invoke(slot);
            // The pet slot's look is a Morph: a pet and a stage of it.
            if (previous.pet != next.pet || previous.petStage != next.petStage) LookChanged?.Invoke(EquipSlot.Pet);
        }

        private void OnNetLooksChanged(EquipmentLooks previous, EquipmentLooks next)
        {
            if (IsServer) return; // already applied locally in ServerSetLooks
            ApplyLooks(next);
        }

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
            netLooks.OnValueChanged += OnNetLooksChanged;
            if (IsServer)
            {
                netLoadout.Value = current;
                netTemper.Value = TemperOf(itemData);
                looks = WithWornDyes(looks);
                netLooks.Value = looks;
            }
            else
            {
                looks = netLooks.Value; // first: each slot is then drawn once, with its look
                Apply(netLoadout.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            netLoadout.OnValueChanged -= OnNetLoadoutChanged;
            netTemper.OnValueChanged -= OnNetTemperChanged;
            netLooks.OnValueChanged -= OnNetLooksChanged;
            base.OnNetworkDespawn();
        }

        /// <summary>Server (or offline) only: commit a validated loadout.</summary>
        public void ServerSetLoadout(EquipmentLoadout loadout)
        {
            if (IsSpawned && !IsServer) return;
            Apply(loadout);
            ServerSetLooks(looks); // a new piece in a slot wears its own dye
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
            if (!IsSpawned || IsServer) ServerSetLooks(looks); // a piece's dye is part of its state
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
