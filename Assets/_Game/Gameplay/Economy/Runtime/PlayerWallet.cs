using System;
using KryticalNova.Core;
using KryticalNova.Items;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>
    /// A character's Creds. The server holds the balance and only the owner can read it. Nothing on a client can
    /// change it: every earn and spend is a server call made by the system that has a reason for it, tagged with
    /// where the Creds came from or went to (<see cref="CredsLedger"/>). Offline (the unspawned scene avatar) the
    /// local copy decides.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerWallet : NetworkBehaviour, ISaveParticipant
    {
        [SerializeField] private EconomySettings settings;

        private readonly NetworkVariable<long> netCreds = new NetworkVariable<long>(0,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        private long creds;

        /// <summary>Raised where the balance is known (server, offline, owner) when it changes.</summary>
        public event Action Changed;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerWallet Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerWallet>() : null;

        public EconomySettings Settings => settings;
        /// <summary>The balance. Known to the owner and where the game is decided; 0 on every other machine.</summary>
        public long Creds => creds;
        public long Cap => settings != null ? settings.walletCap : long.MaxValue;
        /// <summary>How many more Creds fit.</summary>
        public long Room => Math.Max(0, Cap - creds);

        private bool HasAuthority => !IsSpawned || IsServer;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netCreds.OnValueChanged += OnNetCredsChanged;
            if (IsServer) netCreds.Value = creds;
            else if (IsOwner) Apply(netCreds.Value);
        }

        public override void OnNetworkDespawn()
        {
            netCreds.OnValueChanged -= OnNetCredsChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetCredsChanged(long previous, long current)
        {
            if (!IsServer) Apply(current);
        }

        private void Apply(long value)
        {
            if (creds == value) return;
            creds = value;
            Changed?.Invoke();
        }

        private void Commit()
        {
            if (IsSpawned && IsServer) netCreds.Value = creds;
            Changed?.Invoke();
        }

        public bool CanAfford(long amount) => WalletRules.CanAfford(creds, amount);
        /// <summary>True when the whole amount fits under the wallet cap.</summary>
        public bool HasRoomFor(long amount) => WalletRules.Fits(creds, amount, Cap);

        /// <summary>Server only. Adds what fits under the cap and returns how much that was.</summary>
        public long ServerEarn(long amount, CredsSource source)
        {
            if (!HasAuthority || amount <= 0) return 0;
            long added = WalletRules.Add(ref creds, amount, Cap);
            if (added <= 0) return 0;
            CredsLedger.RecordEarned(source, added);
            Commit();
            return added;
        }

        /// <summary>Server only. Takes the whole amount, or nothing.</summary>
        public bool ServerSpend(long amount, CredsSink sink)
        {
            if (!HasAuthority || amount < 0 || !WalletRules.TrySpend(ref creds, amount)) return false;
            if (amount == 0) return true;
            CredsLedger.RecordSpent(sink, amount);
            Commit();
            return true;
        }

        /// <summary>Server only (GM): sets the balance outright, within the cap.</summary>
        public void ServerSet(long amount)
        {
            if (!HasAuthority) return;
            long next = Math.Min(Cap, Math.Max(0, amount));
            if (next > creds) CredsLedger.RecordEarned(CredsSource.Gm, next - creds);
            else if (next < creds) CredsLedger.RecordSpent(CredsSink.Gm, creds - next);
            creds = next;
            Commit();
        }

        // ------------------------------------------------------------------ saving

        [Serializable]
        private struct WalletSave
        {
            public long creds;
        }

        public string SaveKey => "wallet";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave() => JsonUtility.ToJson(new WalletSave { creds = creds });

        public void RestoreSave(string json)
        {
            if (!HasAuthority || string.IsNullOrEmpty(json)) return;
            creds = Math.Min(Cap, Math.Max(0, JsonUtility.FromJson<WalletSave>(json).creds));
            Commit();
        }
    }
}
