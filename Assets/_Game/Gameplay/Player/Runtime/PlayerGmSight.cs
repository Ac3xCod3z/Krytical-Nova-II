using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Networking;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>One player on the map, as a GM's game is told it.</summary>
    public struct GmSeenPlayer : INetworkSerializable
    {
        public ulong ClientId;
        public FixedString64Bytes Name;
        public FactionId Faction;
        public Vector3 Position;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Faction);
            serializer.SerializeValue(ref Position);
        }
    }

    /// <summary>
    /// A GM sees everyone on the map (owner, 22 Oct 2026: "A GM can see everyone on the map at any time"). A game only
    /// knows the players near it (the server sends each game the objects within its reach), so the server sends a GM's
    /// own game, once a second, where every player on this map is: their name, faction and place. The minimap and the
    /// world map draw them. Only a GM's game is ever sent it; <c>gm.seeall off</c> stops it (on by default).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGmSight : NetworkBehaviour
    {
        private const float SendEvery = 1f;
        private const float FreshFor = 3.5f;

        private readonly NetworkVariable<bool> seeAll = new NetworkVariable<bool>(
            true, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        private static readonly List<GmSeenPlayer> seen = new List<GmSeenPlayer>(64);
        private static float receivedAt = -100f;
        private float nextSend;
        private PlayerGm gm;

        /// <summary>This machine's own player's sight, while it is in a session.</summary>
        public static PlayerGmSight Local { get; private set; }

        /// <summary>Everyone on the map, as last sent to this machine (empty unless this player is a GM who sees all).</summary>
        public static IReadOnlyList<GmSeenPlayer> Seen => seen;

        /// <summary>This machine's player is a GM seeing everyone, and the list is fresh.</summary>
        public static bool Active => Local != null && Local.seeAll.Value && Time.unscaledTime - receivedAt < FreshFor;

        /// <summary>Whether this GM is sent everyone on the map (server: set by <c>gm.seeall</c>).</summary>
        public bool SeeAll
        {
            get => seeAll.Value;
            set { if (IsServer) seeAll.Value = value; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            seen.Clear();
            receivedAt = -100f;
        }

        private void Awake() => gm = GetComponent<PlayerGm>();

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
        }

        public override void OnNetworkDespawn()
        {
            if (Local != this) return;
            Local = null;
            seen.Clear();
        }

        // The server: once a second, everyone on this map to a GM's own game.
        private void Update()
        {
            if (!IsSpawned || !IsServer || !seeAll.Value || Time.unscaledTime < nextSend) return;
            nextSend = Time.unscaledTime + SendEvery;
            if (OwnerClientId == NetworkManager.ServerClientId) return; // the host's own game has every player already
            if (!GmRoster.IsGm(OwnerClientId) && (gm == null || !gm.IsGm)) return;
            IReadOnlyList<PointAndClickPlayer> players = PointAndClickPlayer.Active;
            var list = new GmSeenPlayer[players.Count];
            int count = 0;
            for (int i = 0; i < players.Count; i++)
            {
                PointAndClickPlayer p = players[i];
                if (p == null || !p.IsSpawned || !p.TryGetComponent(out PlayerIdentity identity)) continue;
                list[count++] = new GmSeenPlayer
                {
                    ClientId = p.OwnerClientId,
                    Name = Fit(identity.CharacterName),
                    Faction = identity.Faction,
                    Position = p.transform.position,
                };
            }
            if (count < list.Length) Array.Resize(ref list, count);
            SeenRpc(list);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void SeenRpc(GmSeenPlayer[] players)
        {
            seen.Clear();
            if (players != null) seen.AddRange(players);
            receivedAt = Time.unscaledTime;
        }

        private static FixedString64Bytes Fit(string name)
        {
            name ??= string.Empty;
            if (name.Length > 30) name = name.Substring(0, 30);
            return new FixedString64Bytes(name);
        }
    }
}
