using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Persistence;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// Equipment loadouts (owner, 17 Oct 2026): ten slots, each a name and the gear that was worn when it was saved.
    /// Wearing one takes those very pieces out of the Pack (the same item in another state when it has been worked
    /// on since) and puts what was worn in their place; a piece that is not in the Pack is named and skipped. The
    /// owner asks, the server decides; the slots are told to the owner only and saved with the character.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerLoadouts : NetworkBehaviour, ISaveParticipant
    {
        private const int MaxTypedName = 64;

        private readonly LoadoutEntry[] entries = new LoadoutEntry[LoadoutRules.Slots];
        private readonly List<ItemStack> pieces = new List<ItemStack>(EquipSlots.All.Length);
        private readonly WearSetResult result = new WearSetResult();
        private NetworkList<LoadoutEntry> netEntries;
        private PlayerInventory inventory;
        private ISaveScheduler saveAgent;

        /// <summary>Raised on the owner (and where the game is decided) when a slot changes.</summary>
        public event Action Changed;

        public static PlayerLoadouts Local => LocalPlayer.Get<PlayerLoadouts>();

        public PlayerInventory Inventory => inventory;

        private bool Decides => !IsSpawned || IsServer;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            saveAgent = GetComponent<ISaveScheduler>();
            netEntries = new NetworkList<LoadoutEntry>(null, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netEntries.OnListChanged += OnNetChanged;
            if (IsServer) Push();
        }

        public override void OnNetworkDespawn()
        {
            netEntries.OnListChanged -= OnNetChanged;
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            netEntries?.Dispose(); // guarded; covers the never-spawned offline avatar
        }

        private void OnNetChanged(NetworkListEvent<LoadoutEntry> change) => Changed?.Invoke();

        // ------------------------------------------------------------------ what the windows read

        /// <summary>A slot as this machine knows it: the owner reads the server's copy.</summary>
        public LoadoutEntry Get(int index)
        {
            if (index < 0 || index >= LoadoutRules.Slots) return default;
            if (Decides) return entries[index];
            return index < netEntries.Count ? netEntries[index] : default;
        }

        public bool IsUsed(int index) => Get(index).used;

        /// <summary>The first slot with no loadout, or -1 when all ten hold one.</summary>
        public int FirstFree()
        {
            for (int i = 0; i < LoadoutRules.Slots; i++) if (!IsUsed(i)) return i;
            return -1;
        }

        /// <summary>The name a slot shows: its loadout's, or "Loadout N" when it is empty.</summary>
        public string NameOf(int index)
        {
            LoadoutEntry entry = Get(index);
            return entry.used && entry.name.Length > 0 ? entry.name.ToString() : LoadoutRules.DefaultName(index);
        }

        // ------------------------------------------------------------------ the owner asks

        /// <summary>Owner: save what is worn now into a slot, under a name (empty: "Loadout N"). Overwrites the slot.</summary>
        public void RequestSave(int index, string name)
        {
            if (IsSpawned && !IsOwner) return;
            if (IsSpawned && !IsServer) SaveRpc((byte)Mathf.Clamp(index, 0, 255), Trim(name));
            else ServerSave(index, name);
        }

        public void RequestRename(int index, string name)
        {
            if (IsSpawned && !IsOwner) return;
            if (IsSpawned && !IsServer) RenameRpc((byte)Mathf.Clamp(index, 0, 255), Trim(name));
            else ServerRename(index, name);
        }

        public void RequestDelete(int index)
        {
            if (IsSpawned && !IsOwner) return;
            if (IsSpawned && !IsServer) DeleteRpc((byte)Mathf.Clamp(index, 0, 255));
            else ServerDelete(index);
        }

        public void RequestWear(int index)
        {
            if (IsSpawned && !IsOwner) return;
            if (IsSpawned && !IsServer) WearRpc((byte)Mathf.Clamp(index, 0, 255));
            else ServerWear(index);
        }

        private static string Trim(string name) => name == null ? string.Empty : name.Length > MaxTypedName ? name.Substring(0, MaxTypedName) : name;

        // Every request is held to the Pack's speed limit (RpcBudget, online phase 2), and a name to its length.
        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SaveRpc(byte index, string name)
        {
            if (!RpcBudget.Allow(this, CallKind.Items, nameof(SaveRpc)) || !RpcBudget.TextFits(this, nameof(SaveRpc), name, MaxTypedName)) return;
            ServerSave(index, Trim(name));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RenameRpc(byte index, string name)
        {
            if (!RpcBudget.Allow(this, CallKind.Items, nameof(RenameRpc)) || !RpcBudget.TextFits(this, nameof(RenameRpc), name, MaxTypedName)) return;
            ServerRename(index, Trim(name));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DeleteRpc(byte index)
        {
            if (!RpcBudget.Allow(this, CallKind.Items, nameof(DeleteRpc))) return;
            ServerDelete(index);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void WearRpc(byte index)
        {
            if (!RpcBudget.Allow(this, CallKind.Items, nameof(WearRpc))) return;
            ServerWear(index);
        }

        // ------------------------------------------------------------------ the server decides

        /// <summary>Server or offline: the slot takes what is worn now (every slot; an empty one stays empty, and is left alone when worn).</summary>
        public void ServerSave(int index, string name)
        {
            if (!Decides || inventory == null || index < 0 || index >= LoadoutRules.Slots) return;
            var entry = new LoadoutEntry { used = true, name = new FixedString32Bytes(LoadoutRules.CleanName(name, index)) };
            bool any = false;
            foreach (EquipSlot slot in EquipSlots.All)
            {
                ItemStack worn = inventory.GetEquipped(slot);
                if (worn.IsEmpty) continue;
                entry.ids[slot] = worn.itemId;
                entry.data[slot] = worn.data;
                any = true;
            }
            if (!any) { Tell("Wear something first: a loadout keeps what you have on."); return; }
            entries[index] = entry;
            Push();
            SaveNow();
            Tell($"Saved {entry.name} (slot {index + 1}).");
        }

        public void ServerRename(int index, string name)
        {
            if (!Decides || index < 0 || index >= LoadoutRules.Slots || !entries[index].used) return;
            entries[index].name = new FixedString32Bytes(LoadoutRules.CleanName(name, index));
            Push();
            SaveNow();
        }

        public void ServerDelete(int index)
        {
            if (!Decides || index < 0 || index >= LoadoutRules.Slots || !entries[index].used) return;
            string gone = entries[index].name.ToString();
            entries[index] = default;
            Push();
            SaveNow();
            Tell($"Deleted {gone}.");
        }

        /// <summary>Server or offline: puts a slot's gear on from the Pack, and says what could not go on.</summary>
        public void ServerWear(int index)
        {
            if (!Decides || inventory == null || index < 0 || index >= LoadoutRules.Slots) return;
            LoadoutEntry entry = entries[index];
            if (!entry.used) { Tell("That loadout slot is empty."); return; }
            pieces.Clear();
            foreach (EquipSlot slot in EquipSlots.All) pieces.Add(entry.Piece(slot));
            if (!inventory.ServerWearSet(pieces, result)) return;
            if (result.Worn > 0) SaveNow();
            Tell(LoadoutRules.Describe(entry.name.ToString(), result, ItemName));
        }

        private string ItemName(ushort itemId)
        {
            ItemDefinition item = inventory != null && inventory.Catalog != null ? inventory.Catalog.Get(itemId) : null;
            return item != null ? item.displayName : $"item {itemId}";
        }

        private void Push()
        {
            if (IsSpawned && IsServer)
            {
                while (netEntries.Count > LoadoutRules.Slots) netEntries.RemoveAt(netEntries.Count - 1);
                for (int i = 0; i < LoadoutRules.Slots; i++)
                {
                    if (i >= netEntries.Count) netEntries.Add(entries[i]);
                    else if (!netEntries[i].Equals(entries[i])) netEntries[i] = entries[i];
                }
            }
            Changed?.Invoke();
        }

        private void SaveNow()
        {
            if (saveAgent != null && IsSpawned && IsServer) saveAgent.Save();
        }

        private void Tell(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (IsSpawned && !IsOwner) TellRpc(text);
            else ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ saving

        [Serializable]
        private struct SavedLoadout
        {
            public int slot;
            public string name;
            public EquipmentLoadout ids;
            public EquipmentData data;
        }

        [Serializable]
        private sealed class LoadoutSave
        {
            public List<SavedLoadout> loadouts = new List<SavedLoadout>();
        }

        public string SaveKey => "loadouts";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave()
        {
            var save = new LoadoutSave();
            for (int i = 0; i < LoadoutRules.Slots; i++)
                if (entries[i].used) save.loadouts.Add(new SavedLoadout { slot = i, name = entries[i].name.ToString(), ids = entries[i].ids, data = entries[i].data });
            return JsonUtility.ToJson(save);
        }

        public void RestoreSave(string json)
        {
            if (!Decides || string.IsNullOrEmpty(json)) return;
            LoadoutSave save = JsonUtility.FromJson<LoadoutSave>(json);
            Array.Clear(entries, 0, entries.Length);
            if (save?.loadouts != null)
                foreach (SavedLoadout saved in save.loadouts)
                    if (saved.slot >= 0 && saved.slot < LoadoutRules.Slots)
                        entries[saved.slot] = new LoadoutEntry
                        {
                            used = true, name = new FixedString32Bytes(LoadoutRules.CleanName(saved.name, saved.slot)), ids = saved.ids, data = saved.data,
                        };
            Push();
        }
    }
}
