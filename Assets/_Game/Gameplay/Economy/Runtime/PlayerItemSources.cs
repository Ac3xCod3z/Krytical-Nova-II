using System;
using System.Collections.Generic;
using KryticalNova.Items;
using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.Economy
{
    /// <summary>Where a player keeps items that can pay for something, in the order they are spent.</summary>
    public enum ItemSourceKind : byte { Pack = 0, Vault = 1, DeepVault = 2 }

    /// <summary>
    /// Everything a player can pay with in items: the Pack, the Vault and the Deep Vault, as one. Whatever costs
    /// items (the forge, a Kiosk Permit, crafting when it exists) counts and spends through this, so what is
    /// stored counts wherever the player stands. Counting works on the owner too; spending is the server's.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerItemSources : MonoBehaviour
    {
        private PlayerInventory inventory;
        private PlayerStorage vault;
        private PlayerStorage deepVault;
        private readonly List<ItemStack[]> spendFrom = new List<ItemStack[]>(3);

        /// <summary>The sources of the player this machine controls.</summary>
        public static PlayerItemSources Local => LocalPlayer.Get<PlayerItemSources>();

        /// <summary>Raised where the contents are known (server, offline, owner) when any of the three changes.</summary>
        public event Action Changed;

        private void Awake() => Resolve();

        // Asked for by windows the moment a player becomes the local one, which can be before this component's own
        // Awake has run (the Pack's runs first): the three are looked up on first need.
        private void Resolve()
        {
            if (inventory != null) return;
            inventory = GetComponent<PlayerInventory>();
            foreach (PlayerStorage storage in GetComponents<PlayerStorage>())
            {
                if (storage.Kind == StorageKind.Vault) vault = storage;
                else if (storage.Kind == StorageKind.DeepVault) deepVault = storage;
            }
        }

        private void OnEnable()
        {
            Resolve();
            inventory.Changed += OnChanged;
            if (vault != null) vault.Changed += OnChanged;
            if (deepVault != null) deepVault.Changed += OnChanged;
        }

        private void OnDisable()
        {
            inventory.Changed -= OnChanged;
            if (vault != null) vault.Changed -= OnChanged;
            if (deepVault != null) deepVault.Changed -= OnChanged;
        }

        private void OnChanged() => Changed?.Invoke();

        /// <summary>How many plain <paramref name="itemId"/> the player has in all three.</summary>
        public int Count(ushort itemId) =>
            CountIn(ItemSourceKind.Pack, itemId) + CountIn(ItemSourceKind.Vault, itemId) + CountIn(ItemSourceKind.DeepVault, itemId);

        /// <summary>How many plain <paramref name="itemId"/> one of the three holds.</summary>
        public int CountIn(ItemSourceKind kind, ushort itemId)
        {
            if (itemId == 0) return 0;
            Resolve();
            int count = 0;
            if (kind == ItemSourceKind.Pack)
            {
                for (int i = 0; i < PlayerInventory.Capacity; i++) count += Plain(inventory.Get(i), itemId);
                return count;
            }
            PlayerStorage storage = kind == ItemSourceKind.Vault ? vault : deepVault;
            if (storage == null) return 0;
            for (int i = 0; i < StorageRules.Capacity; i++) count += Plain(storage.Get(i), itemId);
            return count;
        }

        /// <summary>"Pack 3 · Vault 12": where the player's <paramref name="itemId"/> are, leaving out the empty places.</summary>
        public string Describe(ushort itemId)
        {
            int pack = CountIn(ItemSourceKind.Pack, itemId);
            int stored = CountIn(ItemSourceKind.Vault, itemId);
            int deep = CountIn(ItemSourceKind.DeepVault, itemId);
            string text = $"Pack {pack}";
            if (stored > 0) text += $" · Vault {stored}";
            if (deep > 0) text += $" · Deep Vault {deep}";
            return text;
        }

        /// <summary>
        /// Server only: makes <paramref name="packChange"/> to the Pack and takes <paramref name="costs"/> out of
        /// the Pack, then the Vault, then the Deep Vault, as one change: all of it happens or none of it does.
        /// The Pack change runs first, so what it moved out of the Pack no longer counts towards the cost.
        /// <paramref name="afterSpend"/> runs on the Pack once the cost has left it (what a craft makes goes into
        /// the room its materials freed).
        /// </summary>
        public bool ServerSpend(IReadOnlyList<ItemCost> costs, Func<ItemStack[], bool> packChange = null,
            Func<ItemStack[], bool> afterSpend = null)
        {
            Resolve();
            ItemStack[] vaultCells = vault != null ? vault.ServerBeginCost() : null;
            ItemStack[] deepCells = deepVault != null ? deepVault.ServerBeginCost() : null;
            bool done = inventory.ServerChangePack(pack =>
            {
                if (packChange != null && !packChange(pack)) return false;
                spendFrom.Clear();
                spendFrom.Add(pack);
                if (vaultCells != null) spendFrom.Add(vaultCells);
                if (deepCells != null) spendFrom.Add(deepCells);
                if (!ItemCostRules.TrySpend(spendFrom, costs)) return false;
                return afterSpend == null || afterSpend(pack);
            });
            if (!done) return false;
            if (vaultCells != null) vault.ServerCommitCost();
            if (deepCells != null) deepVault.ServerCommitCost();
            return true;
        }

        private static int Plain(in ItemStack stack, ushort itemId) => stack.itemId == itemId && stack.data == 0 ? stack.count : 0;
    }
}
