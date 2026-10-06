using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Factions;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Items
{
    /// <summary>
    /// An item lying on the ground. The server puts it there and takes it away; every machine shows it. Click it (or
    /// press the pickup key near it) to take it: the request goes through <see cref="PlayerLoot"/>, and the server
    /// checks range, who it is held for, and Pack space. It carries the whole <see cref="ItemStack"/>, so an item
    /// keeps its own state on the ground. Gone after a while if nobody takes it.
    /// Offline (no session) the local copy decides.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WorldDrop : NetworkBehaviour, IInstantInteractable
    {
        private const ulong Nobody = ulong.MaxValue;

        /// <summary>Who a drop is held for and until when, as every machine is told. <see cref="until"/> is server time.</summary>
        private struct Hold : INetworkSerializable, System.IEquatable<Hold>
        {
            public ulong holder;
            public double until;
            public float seconds;

            public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
            {
                serializer.SerializeValue(ref holder);
                serializer.SerializeValue(ref until);
                serializer.SerializeValue(ref seconds);
            }

            public bool Equals(Hold other) => holder == other.holder && until.Equals(other.until) && seconds.Equals(other.seconds);
        }
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Color[] RarityTint =
        {
            new Color(0.95f, 0.96f, 0.97f), new Color(0.33f, 0.59f, 1f), new Color(1f, 0.83f, 0.25f),
            new Color(0.73f, 0.41f, 1f), new Color(1f, 0.59f, 0.25f)
        };

        [SerializeField] private ItemCatalog catalog;
        [Tooltip("How close a player must be to take it, in metres.")]
        [SerializeField, Min(1f)] private float interactRange = 1.5f;
        [Tooltip("Seconds it lies there before it is cleared away.")]
        [SerializeField, Min(5f)] private float despawnSeconds = 180f;
        [Tooltip("The most drops a zone keeps; the oldest goes when another lands.")]
        [SerializeField, Min(1)] private int maxDropsPerZone = 200;
        [Tooltip("The part that spins and bobs, and is tinted by rarity.")]
        [SerializeField] private Transform visual;

        private readonly NetworkVariable<ItemStack> netStack = new NetworkVariable<ItemStack>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Hold> netHold = new NetworkVariable<Hold>(new Hold { holder = Nobody },
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private static readonly List<WorldDrop> ActiveDrops = new List<WorldDrop>(64);

        // Things dropped on one spot spread out so each can be seen and clicked.
        private const float ScatterSpacing = 0.6f;
        private const int ScatterAttempts = 24;
        // A parcel that was taken waits for the hand to come down, then goes to it.
        private const float TakenWaitSeconds = 0.25f;
        private const float TakenFlightSeconds = 0.2f;

        private ItemStack stack;
        private ulong reservedFor = Nobody;
        private FactionId faction;
        private float reservedUntil;      // local clock (Time.time): what the machine that decides goes by
        private double heldUntil;         // the session's clock: what everyone counts down against
        private float holdSeconds;        // how long the hold was to begin with
        private float removeAt;
        private Vector3 visualBase;
        private float phase;
        private MaterialPropertyBlock block;

        /// <summary>Every drop on this machine. Do not modify.</summary>
        public static IReadOnlyList<WorldDrop> Active => ActiveDrops;

        public ItemStack Stack => stack;
        public ItemDefinition Definition => catalog != null ? catalog.Get(stack.itemId) : null;
        public string DisplayName => LootRules.Label(Definition, stack.count);
        public ItemRarity Rarity => Definition != null ? Definition.rarity : ItemRarity.Common;
        /// <summary>The faction of the player who dropped it, or None. Known where the game is decided.</summary>
        public FactionId Faction => faction;
        public float InteractRange => interactRange;
        public Vector3 InteractPoint => transform.position;
        public Vector3 Position => transform.position;

        private bool HasAuthority => !IsSpawned || IsServer;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ActiveDrops.Clear();

        private void Awake()
        {
            if (visual != null) visualBase = visual.localPosition;
            phase = Random.value * 10f;
        }

        private void OnEnable() => ActiveDrops.Add(this);
        private void OnDisable() => ActiveDrops.Remove(this);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netStack.OnValueChanged += OnNetStackChanged;
            netHold.OnValueChanged += OnNetHoldChanged;
            if (IsServer)
            {
                netStack.Value = stack;
                if (reservedFor != Nobody) heldUntil = NetworkManager.ServerTime.Time + Mathf.Max(0f, reservedUntil - Time.time);
                netHold.Value = new Hold { holder = reservedFor, until = heldUntil, seconds = holdSeconds };
            }
            else
            {
                stack = netStack.Value;
                ApplyHold(netHold.Value);
            }
            ApplyLook();
        }

        public override void OnNetworkDespawn()
        {
            netStack.OnValueChanged -= OnNetStackChanged;
            netHold.OnValueChanged -= OnNetHoldChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetStackChanged(ItemStack previous, ItemStack current)
        {
            if (IsServer) return;
            stack = current;
            ApplyLook();
        }

        private void OnNetHoldChanged(Hold previous, Hold current)
        {
            if (!IsServer) ApplyHold(current);
        }

        private void ApplyHold(Hold hold)
        {
            reservedFor = hold.holder;
            heldUntil = hold.until;
            holdSeconds = hold.seconds;
        }

        // ------------------------------------------------------------------ server

        /// <summary>
        /// Server (or offline): puts a stack on the ground near a point. <paramref name="reservedFor"/> may hold it
        /// for one player for <paramref name="reserveSeconds"/>; <paramref name="dropFaction"/> limits who may take
        /// it afterwards (None = anyone).
        /// </summary>
        public static WorldDrop ServerSpawn(WorldDrop prefab, ItemStack stack, Vector3 position, NetworkObject reservedFor = null,
            float reserveSeconds = 0f, FactionId dropFaction = FactionId.None)
        {
            if (prefab == null || stack.IsEmpty) return null;
            var network = NetworkManager.Singleton;
            bool session = network != null && network.IsListening;
            if (session && !network.IsServer) return null;

            // A player's pivot floats above the ground (the agent's base offset): find the ground before looking
            // for room on it.
            Vector3 ground = GroundSnap.Snap(FreeSpot(GroundSnap.Snap(position)));
            WorldDrop drop = Instantiate(prefab, ground, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            drop.stack = stack;
            drop.faction = dropFaction;
            drop.reservedFor = reservedFor != null && reserveSeconds > 0f ? reservedFor.NetworkObjectId : Nobody;
            drop.reservedUntil = Time.time + Mathf.Max(0f, reserveSeconds);
            drop.holdSeconds = drop.reservedFor != Nobody ? reserveSeconds : 0f;
            drop.removeAt = Time.time + drop.despawnSeconds;
            if (session) drop.NetworkObject.Spawn(true);
            else drop.ApplyLook();

            // Oldest out when the zone holds too many.
            while (ActiveDrops.Count > drop.maxDropsPerZone)
            {
                WorldDrop oldest = ActiveDrops[0];
                if (oldest == drop) break;
                oldest.ServerRemove();
            }
            return drop;
        }

        /// <summary>
        /// The nearest spot to <paramref name="wanted"/> (a point on the ground) that no other drop lies on and a
        /// player can walk to.
        /// </summary>
        private static Vector3 FreeSpot(Vector3 wanted)
        {
            bool walkable = NavMesh.SamplePosition(wanted, out _, 0.5f, NavMesh.AllAreas); // no NavMesh here: don't ask for one
            for (int attempt = 0; attempt < ScatterAttempts; attempt++)
            {
                LootRules.ScatterOffset(attempt, ScatterSpacing, out float x, out float z);
                Vector3 candidate = wanted + new Vector3(x, 0f, z);
                if (IsTaken(candidate)) continue;
                if (walkable && attempt > 0 && !NavMesh.SamplePosition(candidate, out _, 0.5f, NavMesh.AllAreas)) continue;
                return candidate;
            }
            return wanted;
        }

        private static bool IsTaken(Vector3 point)
        {
            const float Clear = ScatterSpacing * 0.9f;
            for (int i = 0; i < ActiveDrops.Count; i++)
            {
                WorldDrop other = ActiveDrops[i];
                if (other == null) continue;
                Vector3 offset = other.transform.position - point;
                offset.y = 0f;
                if (offset.sqrMagnitude < Clear * Clear) return true;
            }
            return false;
        }

        /// <summary>True while the drop is held for a player other than this one.</summary>
        public bool IsReservedForAnother(NetworkObject player) =>
            reservedFor != Nobody && (player == null || !player.IsSpawned || player.NetworkObjectId != reservedFor);

        /// <summary>True while the drop is held for someone: nobody else can take it yet.</summary>
        public bool IsHeld => reservedFor != Nobody && HeldSecondsLeft > 0f;

        /// <summary>True while the drop is held for this player.</summary>
        public bool IsHeldFor(NetworkObject player) =>
            IsHeld && player != null && player.IsSpawned && player.NetworkObjectId == reservedFor;

        /// <summary>Seconds until anyone may take it; 0 when it is not held. The same on every machine.</summary>
        public float HeldSecondsLeft
        {
            get
            {
                if (reservedFor == Nobody) return 0f;
                double left = IsSpawned ? heldUntil - NetworkManager.ServerTime.Time : reservedUntil - Time.time;
                return (float)System.Math.Max(0.0, left);
            }
        }

        /// <summary>How long the hold lasted from the start, for a countdown bar. 0 when it was never held.</summary>
        public float HoldSeconds => holdSeconds;

        /// <summary>
        /// Server (or offline): takes <paramref name="count"/> off the stack; the drop goes when nothing is left,
        /// and its parcel is seen going to <paramref name="taker"/> on every machine.
        /// </summary>
        public void ServerTake(int count, Transform taker = null)
        {
            if (!HasAuthority || count <= 0) return;
            stack = stack.WithCount(stack.count - count);
            if (stack.IsEmpty)
            {
                if (taker != null)
                {
                    if (!IsSpawned) HandOver(taker);
                    else if (taker.TryGetComponent(out NetworkObject takerObject) && takerObject.IsSpawned) TakenRpc(takerObject);
                }
                ServerRemove();
                return;
            }
            if (IsSpawned) netStack.Value = stack;
            ApplyLook();
        }

        private void ServerRemove()
        {
            if (!HasAuthority) return;
            ActiveDrops.Remove(this);
            if (IsSpawned) NetworkObject.Despawn(true);
            else Destroy(gameObject);
        }

        private void Update()
        {
            if (HasAuthority)
            {
                if (reservedFor != Nobody && Time.time >= reservedUntil)
                {
                    reservedFor = Nobody;
                    if (IsSpawned) netHold.Value = new Hold { holder = Nobody, seconds = holdSeconds };
                }
                if (Time.time >= removeAt)
                {
                    ServerRemove();
                    return;
                }
            }
            if (visual == null) return;
            float t = Time.time + phase;
            visual.localPosition = visualBase + Vector3.up * (0.08f * Mathf.Sin(t * 2.2f));
            visual.Rotate(0f, 55f * Time.deltaTime, 0f, Space.Self);
        }

        // ------------------------------------------------------------------ presentation

        [Rpc(SendTo.Everyone)]
        private void TakenRpc(NetworkObjectReference takerReference)
        {
            if (takerReference.TryGet(out NetworkObject taker)) HandOver(taker.transform);
        }

        /// <summary>Leaves the parcel behind as a local effect that goes to the taker; the drop itself is removed.</summary>
        private void HandOver(Transform taker)
        {
            if (visual == null || taker == null) return;
            Transform parcel = visual;
            visual = null;
            parcel.SetParent(null, true);
            parcel.gameObject.AddComponent<TakenDropVisual>().Begin(taker, TakenWaitSeconds, TakenFlightSeconds);
        }

        private void ApplyLook()
        {
            name = stack.IsEmpty ? "Drop" : $"Drop ({DisplayName})";
            if (visual == null) return;
            block ??= new MaterialPropertyBlock();
            Color tint = RarityTint[Mathf.Clamp((int)Rarity, 0, RarityTint.Length - 1)];
            foreach (Renderer target in visual.GetComponentsInChildren<Renderer>(true))
            {
                target.GetPropertyBlock(block);
                block.SetColor(BaseColorId, tint);
                target.SetPropertyBlock(block);
            }
        }

        /// <summary>The local player clicked it and is in reach: ask the server for it.</summary>
        public void Interact(GameObject player)
        {
            if (player != null && player.TryGetComponent(out PlayerLoot loot)) loot.RequestPickup(this);
        }
    }
}
