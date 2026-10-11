using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Persistence;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Economy
{
    public enum StorageKind : byte
    {
        /// <summary>One per character.</summary>
        Vault = 0,
        /// <summary>One per account: every character of the account opens the same one.</summary>
        DeepVault = 1
    }

    /// <summary>
    /// Items and Creds a player keeps out of the Pack: the Vault (<see cref="PlayerVault"/>) and the Deep Vault
    /// (<see cref="PlayerDeepVault"/>). Server-authoritative like the Pack: the owner asks, the server checks that
    /// the player stands by an NPC offering the service and runs <see cref="StorageRules"/> on scratch copies of
    /// both sides. Only the owner can read it. Offline (the unspawned scene avatar) the same rules run locally.
    /// </summary>
    [RequireComponent(typeof(PlayerInventory))]
    public abstract class PlayerStorage : NetworkBehaviour, ISaveParticipant
    {
        // A client sees itself a little ahead of where the server has it; allow for that when checking the NPC is near.
        private const float ReachPadding = 2.5f;
        private const string ReasonDefeated = "You can't use storage while defeated.";

        private NetworkList<ItemStack> netCells;
        private readonly NetworkVariable<long> netCreds = new NetworkVariable<long>(0,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        private readonly ItemStack[] cells = new ItemStack[StorageRules.Capacity];
        private readonly ItemStack[] scratch = new ItemStack[StorageRules.Capacity];
        private long creds;
        private bool pullPending;
        private PlayerInventory inventory;
        private PlayerWallet wallet;
        private PlayerResources resources;
        private PlayerSaveAgent saveAgent;

        /// <summary>GM (host): storage opens anywhere, with no NPC near. Off again when the session ends.</summary>
        public static bool GmAnywhere { get; set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => GmAnywhere = false;

        public abstract StorageKind Kind { get; }
        /// <summary>"Vault" or "Deep Vault".</summary>
        public abstract string DisplayName { get; }
        /// <summary>The NPC service that opens this storage.</summary>
        public abstract NpcOffer Offer { get; }
        /// <summary>Told to a player who asks for it with no such NPC near.</summary>
        protected abstract string WhereReason { get; }
        public abstract string SaveKey { get; }
        public abstract SaveScope SaveScope { get; }

        /// <summary>Raised where the contents are known (server, offline, owner) when they change.</summary>
        public event Action Changed;

        public ItemStack Get(int cell) => cell >= 0 && cell < cells.Length ? cells[cell] : ItemStack.Empty;
        public int UsedCells => StorageRules.Used(cells);
        /// <summary>Creds kept here. Known to the owner and where the game is decided.</summary>
        public long Creds => creds;
        public long CredsCap => wallet != null ? wallet.Cap : long.MaxValue;
        public long CredsRoom => Math.Max(0, CredsCap - creds);

        private bool HasAuthority => !IsSpawned || IsServer;

        /// <summary>This machine's own storage of a kind, or null.</summary>
        public static PlayerStorage Local(StorageKind kind)
        {
            PlayerInventory local = PlayerInventory.Local;
            if (local == null) return null;
            foreach (PlayerStorage storage in local.GetComponents<PlayerStorage>())
                if (storage.Kind == kind) return storage;
            return null;
        }

        protected virtual void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            wallet = GetComponent<PlayerWallet>();
            resources = GetComponent<PlayerResources>();
            saveAgent = GetComponent<PlayerSaveAgent>();
            netCells = new NetworkList<ItemStack>(readPerm: NetworkVariableReadPermission.Owner,
                writePerm: NetworkVariableWritePermission.Server);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netCells.OnListChanged += OnNetCellsChanged;
            netCreds.OnValueChanged += OnNetCredsChanged;
            if (IsServer)
            {
                WriteNetCells();
                netCreds.Value = creds;
            }
            else if (IsOwner)
            {
                creds = netCreds.Value;
                PullNetCells();
            }
        }

        public override void OnNetworkDespawn()
        {
            netCells.OnListChanged -= OnNetCellsChanged;
            netCreds.OnValueChanged -= OnNetCredsChanged;
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            netCells?.Dispose(); // guarded; covers the never-spawned offline avatar
        }

        // ------------------------------------------------------------------ owner

        /// <summary>Pack cell into storage: onto a chosen cell, or wherever it fits. <paramref name="count"/> 0 = the whole stack.</summary>
        public void RequestDeposit(int packCell, int toCell = -1, int count = 0)
        {
            if (IsSpawned) DepositRpc((short)packCell, (short)toCell, (ushort)Mathf.Clamp(count, 0, ushort.MaxValue));
            else ServerDeposit(packCell, toCell, count);
        }

        /// <summary>Storage cell into the Pack: onto a chosen cell, or wherever it fits. <paramref name="count"/> 0 = the whole stack.</summary>
        public void RequestWithdraw(int cell, int packCell = -1, int count = 0)
        {
            if (IsSpawned) WithdrawRpc((short)cell, (short)packCell, (ushort)Mathf.Clamp(count, 0, ushort.MaxValue));
            else ServerWithdraw(cell, packCell, count);
        }

        public void RequestMove(int from, int to)
        {
            if (IsSpawned) MoveRpc((short)from, (short)to); else ServerMove(from, to);
        }

        public void RequestSort()
        {
            if (IsSpawned) SortRpc(); else ServerSort();
        }

        public void RequestDepositCreds(long amount)
        {
            if (IsSpawned) DepositCredsRpc(amount); else ServerDepositCreds(amount);
        }

        public void RequestWithdrawCreds(long amount)
        {
            if (IsSpawned) WithdrawCredsRpc(amount); else ServerWithdrawCreds(amount);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DepositRpc(short packCell, short toCell, ushort count) => ServerDeposit(packCell, toCell, count);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void WithdrawRpc(short cell, short packCell, ushort count) => ServerWithdraw(cell, packCell, count);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void MoveRpc(short from, short to) => ServerMove(from, to);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SortRpc() => ServerSort();

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void DepositCredsRpc(long amount) => ServerDepositCreds(amount);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void WithdrawCredsRpc(long amount) => ServerWithdrawCreds(amount);

        // ------------------------------------------------------------------ server

        /// <summary>Why this player may not use the storage right now (shown to them), or null.</summary>
        private string BlockedReason()
        {
            if (resources != null && !resources.IsAlive) return ReasonDefeated;
            if (!GmAnywhere && !NpcInteractable.IsOffered(Offer, transform.position, ReachPadding)) return WhereReason;
            return null;
        }

        private bool Ready()
        {
            if (!HasAuthority || inventory == null || inventory.Catalog == null) return false;
            string blocked = BlockedReason();
            if (blocked == null) return true;
            Tell(blocked);
            return false;
        }

        private void ServerDeposit(int packCell, int toCell, int count)
        {
            if (!Ready()) return;
            Array.Copy(cells, scratch, cells.Length);
            string why = null;
            bool moved = inventory.ServerChangePack(pack => StorageRules.TryTransfer(pack, packCell, scratch, toCell, count,
                inventory.Catalog, $"The {DisplayName} is full.", out why));
            if (moved) Commit();
            else Tell(why);
        }

        private void ServerWithdraw(int cell, int packCell, int count)
        {
            if (!Ready()) return;
            Array.Copy(cells, scratch, cells.Length);
            string why = null;
            bool moved = inventory.ServerChangePack(pack => StorageRules.TryTransfer(scratch, cell, pack, packCell, count,
                inventory.Catalog, InventoryRules.ReasonBagFull, out why));
            if (moved) Commit();
            else Tell(why);
        }

        private void ServerMove(int from, int to)
        {
            if (!Ready()) return;
            Array.Copy(cells, scratch, cells.Length);
            string why = InventoryRules.ReasonInvalidLocation;
            if (to < 0 || !StorageRules.TryTransfer(scratch, from, scratch, to, 0, inventory.Catalog, null, out why))
            {
                Tell(why);
                return;
            }
            Commit();
        }

        private void ServerSort()
        {
            if (!Ready()) return;
            Array.Copy(cells, scratch, cells.Length);
            InventoryRules.Sort(scratch, inventory.Catalog);
            Commit();
        }

        private void ServerDepositCreds(long amount)
        {
            if (!Ready() || wallet == null || amount <= 0) return;
            if (!wallet.CanAfford(amount)) { Tell("You don't carry that many Creds."); return; }
            if (!WalletRules.Fits(creds, amount, CredsCap)) { Tell($"The {DisplayName} can't hold that many Creds."); return; }
            if (!wallet.ServerSpend(amount, CredsSink.VaultDeposit)) return;
            creds += amount;
            CommitCreds();
        }

        private void ServerWithdrawCreds(long amount)
        {
            if (!Ready() || wallet == null || amount <= 0) return;
            if (amount > creds) { Tell("There aren't that many Creds stored."); return; }
            if (!wallet.HasRoomFor(amount)) { Tell("Your wallet can't hold that many Creds."); return; }
            creds -= amount;
            wallet.ServerEarn(amount, CredsSource.VaultWithdraw);
            CommitCreds();
        }

        /// <summary>Keeps what the rules did to the scratch copy.</summary>
        private void Commit()
        {
            Array.Copy(scratch, cells, cells.Length);
            if (IsSpawned) WriteNetCells();
            Changed?.Invoke();
            if (saveAgent != null) saveAgent.SaveSoon();
        }

        /// <summary>
        /// Server only: a scratch copy of the cells for something that pays in items (<see cref="PlayerItemSources"/>).
        /// A cost is taken wherever the player stands: the NPC is only needed to put things in and take them out by hand.
        /// </summary>
        internal ItemStack[] ServerBeginCost()
        {
            if (!HasAuthority) return null;
            Array.Copy(cells, scratch, cells.Length);
            return scratch;
        }

        /// <summary>Server only: keeps what a cost took from the scratch copy, when it took anything.</summary>
        internal void ServerCommitCost()
        {
            if (!HasAuthority) return;
            for (int i = 0; i < cells.Length; i++)
            {
                if (scratch[i].Equals(cells[i])) continue;
                Commit();
                return;
            }
        }

        private void CommitCreds()
        {
            if (IsSpawned && IsServer) netCreds.Value = creds;
            Changed?.Invoke();
            if (saveAgent != null) saveAgent.SaveSoon();
        }

        // ------------------------------------------------------------------ telling the owner

        private void Tell(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (IsSpawned && !IsOwnedByServer) TellRpc(text);
            else if (!IsSpawned || IsOwner) ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ replication

        private void WriteNetCells()
        {
            if (netCells.Count != cells.Length)
            {
                netCells.Clear();
                for (int i = 0; i < cells.Length; i++) netCells.Add(cells[i]);
                return;
            }
            for (int i = 0; i < cells.Length; i++) netCells.Set(i, cells[i]); // Set is a no-op when unchanged
        }

        private void PullNetCells()
        {
            int n = Mathf.Min(netCells.Count, cells.Length);
            for (int i = 0; i < n; i++) cells[i] = netCells[i];
            for (int i = n; i < cells.Length; i++) cells[i] = ItemStack.Empty;
            Changed?.Invoke();
        }

        private void OnNetCellsChanged(NetworkListEvent<ItemStack> change)
        {
            // Initial sync arrives as one event per cell; coalesce into a single pull per frame.
            if (!IsServer && IsOwner) pullPending = true;
        }

        private void OnNetCredsChanged(long previous, long current)
        {
            if (IsServer) return;
            creds = current;
            Changed?.Invoke();
        }

        private void LateUpdate()
        {
            if (!pullPending) return;
            pullPending = false;
            PullNetCells();
        }

        // ------------------------------------------------------------------ saving

        [Serializable]
        private struct SavedItem
        {
            public int index;
            public ItemStack stack;
        }

        [Serializable]
        private sealed class StorageSave
        {
            public List<SavedItem> items = new List<SavedItem>();
            public long creds;
        }

        public string CaptureSave()
        {
            var save = new StorageSave { creds = creds };
            for (int i = 0; i < cells.Length; i++)
                if (!cells[i].IsEmpty) save.items.Add(new SavedItem { index = i, stack = cells[i] });
            return JsonUtility.ToJson(save);
        }

        /// <summary>Puts back what was stored. Items that no longer exist in the catalog are dropped.</summary>
        public void RestoreSave(string json)
        {
            if (!HasAuthority || string.IsNullOrEmpty(json)) return;
            var save = JsonUtility.FromJson<StorageSave>(json);
            if (save == null) return;

            ItemCatalog catalog = inventory != null ? inventory.Catalog : null;
            Array.Clear(cells, 0, cells.Length);
            if (save.items != null)
                foreach (SavedItem item in save.items)
                    if (item.index >= 0 && item.index < cells.Length && !item.stack.IsEmpty
                        && (catalog == null || catalog.Get(item.stack.itemId) != null))
                        cells[item.index] = item.stack;
            creds = Math.Min(CredsCap, Math.Max(0, save.creds));

            if (IsSpawned && IsServer)
            {
                WriteNetCells();
                netCreds.Value = creds;
            }
            Changed?.Invoke();
        }
    }
}
