using System;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Factions;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>
    /// A Kiosk as it stands in the world: a stand-in that looks like its owner and sells for them, whether the
    /// owner is online or not. The server spawns it from the Kiosk's record (<see cref="KioskService"/>) and keeps
    /// its shelf current; every machine shows it. Click it to browse: buying is a request through the buyer's
    /// <see cref="PlayerKiosk"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class KioskStand : NetworkBehaviour, IInstantInteractable
    {
        private const float ExpiryCheckSeconds = 5f;
        private const float PlateHeight = 2.35f;

        /// <summary>What every machine knows about the shop besides its shelf.</summary>
        private struct Sign : INetworkSerializable, IEquatable<Sign>
        {
            public FixedString64Bytes shopName;
            public FixedString64Bytes ownerName;
            public FactionId faction;
            public long expiresUtc;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref shopName);
                serializer.SerializeValue(ref ownerName);
                serializer.SerializeValue(ref faction);
                serializer.SerializeValue(ref expiresUtc);
            }

            public bool Equals(Sign other) =>
                shopName.Equals(other.shopName) && ownerName.Equals(other.ownerName) && faction == other.faction && expiresUtc == other.expiresUtc;
        }

        [Tooltip("The player's character model: the stand-in is a doll of it wearing the owner's look.")]
        [SerializeField] private GameObject dollModel;
        [SerializeField] private AnimationClip dollIdle;
        [SerializeField] private CharacterCreationDatabase database;
        [Tooltip("Where the stand-in stands.")]
        [SerializeField] private Transform dollAnchor;
        [Tooltip("How close a player must be to browse and buy, in metres.")]
        [SerializeField, Min(1f)] private float interactRange = 3f;

        private readonly NetworkVariable<Sign> netSign = new NetworkVariable<Sign>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<CharacterNetState> netLook = new NetworkVariable<CharacterNetState>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private NetworkList<KioskListing> netListings;

        private static readonly List<KioskStand> ActiveStands = new List<KioskStand>(16);

        private CharacterDoll doll;
        private float nextExpiryCheck;

        /// <summary>Every Kiosk stand on this machine. Do not modify.</summary>
        public static IReadOnlyList<KioskStand> Active => ActiveStands;

        /// <summary>Raised on this machine when the local player clicks a stand and is close enough to browse it.</summary>
        public static event Action<KioskStand> Browsed;

        /// <summary>Server only: the record this stand sells for.</summary>
        public KioskRecord Record { get; private set; }

        /// <summary>Raised on every machine when the shelf or the sign changes.</summary>
        public event Action Changed;

        public string ShopName => netSign.Value.shopName.ToString();
        public string OwnerName => netSign.Value.ownerName.ToString();
        public FactionId Faction => netSign.Value.faction;
        public long ExpiresUtc => netSign.Value.expiresUtc;
        public int ListingCount => netListings != null ? netListings.Count : 0;
        public KioskListing GetListing(int index) => index >= 0 && index < ListingCount ? netListings[index] : default;

        public string DisplayName => ShopName;
        public float InteractRange => interactRange;
        public Vector3 InteractPoint => transform.position + transform.forward * 1.6f;
        public Vector3 Position => transform.position;
        public Vector3 PlatePosition => transform.position + Vector3.up * PlateHeight;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ActiveStands.Clear();
            Browsed = null;
        }

        private void Awake()
        {
            netListings = new NetworkList<KioskListing>(readPerm: NetworkVariableReadPermission.Everyone,
                writePerm: NetworkVariableWritePermission.Server);
        }

        private void OnEnable() => ActiveStands.Add(this);
        private void OnDisable() => ActiveStands.Remove(this);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netSign.OnValueChanged += OnSignChanged;
            netLook.OnValueChanged += OnLookChanged;
            netListings.OnListChanged += OnListingsChanged;
            if (IsServer && Record != null) Publish();
            ShowLook();
        }

        public override void OnNetworkDespawn()
        {
            netSign.OnValueChanged -= OnSignChanged;
            netLook.OnValueChanged -= OnLookChanged;
            netListings.OnListChanged -= OnListingsChanged;
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            netListings?.Dispose();
        }

        // ------------------------------------------------------------------ server

        /// <summary>Server only, before the stand is spawned: what it sells for.</summary>
        public void ServerBind(KioskRecord record) => Record = record;

        /// <summary>Server only: puts the record's sign, look and shelf where every machine reads them.</summary>
        public void Publish()
        {
            if (!IsSpawned || !IsServer || Record == null) return;
            var sign = new Sign { faction = (FactionId)Record.faction, expiresUtc = Record.expiresUtc };
            sign.shopName.CopyFromTruncated(Record.shopName ?? string.Empty);
            sign.ownerName.CopyFromTruncated(Record.ownerName ?? string.Empty);
            netSign.Value = sign;
            netLook.Value = Record.look.IsEmpty ? default : CharacterNetState.From(Record.ownerName ?? string.Empty, Record.look);

            // Rewritten in place so a client browsing the shelf keeps its indexes for as long as they hold.
            int kept = Mathf.Min(netListings.Count, Record.listings.Count);
            for (int i = 0; i < kept; i++)
                if (!netListings[i].Equals(Record.listings[i])) netListings[i] = Record.listings[i];
            for (int i = netListings.Count - 1; i >= Record.listings.Count; i--) netListings.RemoveAt(i);
            for (int i = kept; i < Record.listings.Count; i++) netListings.Add(Record.listings[i]);
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || Record == null || Time.unscaledTime < nextExpiryCheck) return;
            nextExpiryCheck = Time.unscaledTime + ExpiryCheckSeconds;
            if (KioskRules.IsExpired(DateTime.UtcNow.Ticks, Record.expiresUtc)) KioskService.ServerExpire(Record);
        }

        // ------------------------------------------------------------------ every machine

        private void OnSignChanged(Sign previous, Sign current) => Changed?.Invoke();

        private void OnListingsChanged(NetworkListEvent<KioskListing> change) => Changed?.Invoke();

        private void OnLookChanged(CharacterNetState previous, CharacterNetState current) => ShowLook();

        /// <summary>The stand-in: a doll of the player model wearing the owner's look. Nobody watches on a headless server.</summary>
        private void ShowLook()
        {
            if (Application.isBatchMode || dollModel == null) return;
            CharacterNetState look = netLook.Value;
            if (doll == null)
            {
                doll = CharacterDoll.Create(dollModel, dollAnchor != null ? dollAnchor : transform, dollIdle);
                if (doll == null) return;
                foreach (Transform part in doll.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = gameObject.layer;
            }
            if (!look.IsEmpty && database != null) doll.Apply(look.ToRecipe(), database);
        }

        /// <summary>The local player clicked the stand and is in reach: browse it.</summary>
        public void Interact(GameObject player) => Browsed?.Invoke(this);
    }
}
