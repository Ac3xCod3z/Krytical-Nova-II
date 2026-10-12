using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Persistence;
using KryticalNova.Player;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>
    /// A character's Furnishing Crate: its own store of furnishings, apart from the Pack, counted by kind. A piece
    /// set down in a Hall comes out of it and a piece put away goes back into it. Furnishings move between the
    /// Crate and the Pack as ordinary items. The server holds it and only the owner is told what is in it; offline
    /// the local copy decides. It takes only the furnishings of its character's own faction.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerCrate : NetworkBehaviour, ISaveParticipant
    {
        public const string ReasonNotFurnishing = "Only furnishings go in the Furnishing Crate.";
        public const string ReasonCrateFull = "The Furnishing Crate holds no more of that.";
        public const string ReasonPackFull = "Your Pack is full.";

        [SerializeField] private HousingSettings settings;
        [SerializeField] private FurnishingCatalog catalog;

        private readonly List<CrateEntry> entries = new List<CrateEntry>();
        private PlayerInventory inventory;
        private PlayerIdentity identity;
        private PlayerSaveAgent saveAgent;

        /// <summary>Raised where the Crate is known (server, offline, owner) when what is in it changes.</summary>
        public event Action Changed;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerCrate Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerCrate>() : null;

        public HousingSettings Settings => settings;
        public FurnishingCatalog Catalog => catalog;
        public IReadOnlyList<CrateEntry> Entries => entries;
        public FactionId Faction => identity != null ? identity.Faction : FactionId.None;

        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            identity = GetComponent<PlayerIdentity>();
            saveAgent = GetComponent<PlayerSaveAgent>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsServer || saveAgent == null) return;
            saveAgent.Loaded += Push;
            if (saveAgent.IsLoaded) Push();
        }

        public override void OnNetworkDespawn()
        {
            if (saveAgent != null) saveAgent.Loaded -= Push;
            base.OnNetworkDespawn();
        }

        public int Count(ushort itemId)
        {
            for (int n = 0; n < entries.Count; n++)
                if (entries[n].itemId == itemId) return entries[n].count;
            return 0;
        }

        // ------------------------------------------------------------------ owner -> server

        /// <summary>Moves furnishings from a Pack cell into the Crate.</summary>
        public void RequestStore(int packCell, int count)
        {
            if (!IsSpawned) Answer(ServerStore(packCell, count));
            else if (IsOwner) StoreRpc((ushort)Mathf.Clamp(packCell, 0, ushort.MaxValue), (ushort)Mathf.Clamp(count, 1, ushort.MaxValue));
        }

        /// <summary>Moves furnishings from the Crate into the Pack, as many as fit.</summary>
        public void RequestWithdraw(ushort itemId, int count)
        {
            if (!IsSpawned) Answer(ServerWithdraw(itemId, count));
            else if (IsOwner) WithdrawRpc(itemId, (ushort)Mathf.Clamp(count, 1, ushort.MaxValue));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void StoreRpc(ushort packCell, ushort count) => Answer(ServerStore(packCell, count));

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void WithdrawRpc(ushort itemId, ushort count) => Answer(ServerWithdraw(itemId, count));

        private void Answer(string refusal)
        {
            if (string.IsNullOrEmpty(refusal)) return;
            if (!IsSpawned || IsOwnedByServer) ClientPresentationRegistry.Messages?.ShowMessage(refusal);
            else RefusedRpc(refusal);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void RefusedRpc(string reason) => ClientPresentationRegistry.Messages?.ShowMessage(reason);

        // ------------------------------------------------------------------ server

        /// <summary>Server only. Pack to Crate. Returns why not, or null.</summary>
        public string ServerStore(int packCell, int count)
        {
            if (!HasAuthority || inventory == null) return null;
            ItemStack stack = inventory.Get(packCell);
            if (stack.IsEmpty) return null;
            // Furnishings, and the Facades and Finishes a Hall is not wearing. Only a Facade is its faction's own.
            if (catalog == null || stack.data != 0 || !catalog.HeldInCrate(stack.itemId)) return ReasonNotFurnishing;
            if (!catalog.MayKeep(stack.itemId, Faction)) return HousingRules.ReasonOtherFaction;
            int room = Cap - Count(stack.itemId);
            count = Mathf.Min(Mathf.Min(count, stack.count), room);
            if (count <= 0) return ReasonCrateFull;
            if (!inventory.ServerTakeFromCell(packCell, count, out ItemStack taken) || taken.IsEmpty) return null;
            Add(taken.itemId, taken.count);
            Commit();
            return null;
        }

        /// <summary>Server only. Crate to Pack: what fits goes, the rest stays. Returns why nothing went, or null.</summary>
        public string ServerWithdraw(ushort itemId, int count)
        {
            if (!HasAuthority || inventory == null) return null;
            count = Mathf.Min(count, Count(itemId));
            if (count <= 0) return HousingRules.ReasonNoneLeft;
            int given = inventory.ServerGive(itemId, count);
            if (given <= 0) return ReasonPackFull;
            Add(itemId, -given);
            Commit();
            return null;
        }

        /// <summary>Server only. Puts furnishings straight into the Crate (a piece put away; a GM grant).</summary>
        public void ServerAdd(ushort itemId, int count)
        {
            if (!HasAuthority || itemId == 0 || count <= 0) return;
            Add(itemId, count);
            Commit();
        }

        /// <summary>Server only. Takes furnishings out of the Crate (a piece set down). All of them, or none.</summary>
        public bool ServerTake(ushort itemId, int count)
        {
            if (!HasAuthority || count <= 0 || Count(itemId) < count) return false;
            Add(itemId, -count);
            Commit();
            return true;
        }

        /// <summary>Server only (GM): empties the Crate.</summary>
        public void ServerClear()
        {
            if (!HasAuthority) return;
            entries.Clear();
            Commit();
        }

        private int Cap => settings != null ? settings.crateStackCap : int.MaxValue;

        private void Add(ushort itemId, int count)
        {
            for (int n = 0; n < entries.Count; n++)
            {
                if (entries[n].itemId != itemId) continue;
                int next = entries[n].count + count;
                if (next > 0) entries[n] = new CrateEntry { itemId = itemId, count = next };
                else entries.RemoveAt(n);
                return;
            }
            if (count > 0) entries.Add(new CrateEntry { itemId = itemId, count = count });
        }

        private void Commit()
        {
            Changed?.Invoke();
            Push();
            if (saveAgent != null && IsSpawned && IsServer) saveAgent.SaveSoon();
        }

        // The owner of a player that is not the host's own is told over the network; everyone else already knows.
        private void Push()
        {
            if (IsSpawned && IsServer && !IsOwnedByServer) ViewRpc(entries.ToArray());
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ViewRpc(CrateEntry[] items)
        {
            entries.Clear();
            if (items != null) entries.AddRange(items);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ saving

        [Serializable]
        private sealed class CrateSave
        {
            public int version = 1;
            public List<CrateEntry> items = new List<CrateEntry>();
        }

        public string SaveKey => "crate";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave() => JsonUtility.ToJson(new CrateSave { items = new List<CrateEntry>(entries) });

        public void RestoreSave(string json)
        {
            if (!HasAuthority || string.IsNullOrEmpty(json)) return;
            CrateSave saved = JsonUtility.FromJson<CrateSave>(json);
            entries.Clear();
            if (saved?.items != null)
                foreach (CrateEntry entry in saved.items)
                    if (entry.itemId != 0 && entry.count > 0) entries.Add(entry);
            Changed?.Invoke();
            Push();
        }
    }
}
