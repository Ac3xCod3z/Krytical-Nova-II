using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>A Recall show for the player on connection <see cref="NetworkId"/> (NGO's ChannelRpc / ArrivedRpc; M8 8.4.10).</summary>
    public struct RecallShownRpc : IRpcCommand
    {
        public int NetworkId;
        public byte Kind;
        public float Seconds;
    }

    /// <summary>
    /// Server: who is channelling a Recall and until when (pure, tested). The aura is told once, by rpc, when the channel begins; with
    /// relevancy a client can get that player's ghost in the middle of the channel, and is then told again with the seconds left.
    /// </summary>
    public sealed class RecallChannels
    {
        readonly Dictionary<int, double> m_Ends = new Dictionary<int, double>();

        public int Count => m_Ends.Count;

        /// <summary>The player on connection <paramref name="networkId"/> began a channel of <paramref name="seconds"/> at <paramref name="now"/>.</summary>
        public void Begin(int networkId, double now, float seconds) => m_Ends[networkId] = now + seconds;

        /// <summary>The channel ended (finished, broken off, the player arrived or left).</summary>
        public void End(int networkId) => m_Ends.Remove(networkId);

        /// <summary>True, with the seconds left, while that player's channel still runs; a channel whose time is over is forgotten here.</summary>
        public bool TryLeft(int networkId, double now, out float seconds)
        {
            seconds = 0f;
            if (!m_Ends.TryGetValue(networkId, out double end)) return false;
            if (end <= now) { m_Ends.Remove(networkId); return false; }
            seconds = (float)(end - now);
            return true;
        }

        public void Clear() => m_Ends.Clear();
    }

    /// <summary>
    /// Client: the "channel begun" shows whose player has no body here yet (pure, tested). The rpc is quicker than the ghost and its
    /// body, so the show waits for the body, for as long as the channel runs; an end or an arrival of that player takes it off.
    /// </summary>
    public sealed class PendingRecallShows
    {
        readonly List<(int networkId, double end)> m_Waiting = new List<(int, double)>();

        public int Count => m_Waiting.Count;

        /// <summary>The player on <paramref name="networkId"/> channels until now + seconds; one entry a player (the newest counts).</summary>
        public void Hold(int networkId, double now, float seconds)
        {
            Drop(networkId);
            if (seconds > 0f) m_Waiting.Add((networkId, now + seconds));
        }

        public void Drop(int networkId) => m_Waiting.RemoveAll(w => w.networkId == networkId);

        /// <summary>
        /// Forgets the shows whose time is over, and hands every show whose body is here now (<paramref name="hasBody"/>) to
        /// <paramref name="play"/> with the seconds left.
        /// </summary>
        public void Step(double now, System.Func<int, bool> hasBody, System.Action<int, float> play)
        {
            for (int i = m_Waiting.Count - 1; i >= 0; i--)
            {
                (int networkId, double end) = m_Waiting[i];
                if (end <= now) { m_Waiting.RemoveAt(i); continue; }
                if (!hasBody(networkId)) continue;
                m_Waiting.RemoveAt(i);
                play(networkId, (float)(end - now));
            }
        }

        public void Clear() => m_Waiting.Clear();
    }

    /// <summary>Server: <see cref="IRecallShowLink"/> for the bodies of the entity netcode: the show to every in-game connection, its own player's too.</summary>
    public sealed class EntityRecallShowLink : IRecallShowLink
    {
        /// <summary>Shows sent (tests).</summary>
        public static int Sent { get; private set; }

        /// <summary>The channels running on this server (one server world a process), for the connections that get a channelling player's ghost later.</summary>
        public static readonly RecallChannels Channels = new RecallChannels();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Channels.Clear();

        public void Show(Component part, RecallShowKind kind, float seconds)
        {
            BodyRoleTag tag = BodyRoleTag.Of(part);
            if (tag == null || tag.Kind != NetRoleKind.ServerBody || !GhostBodyRegistry.TryGetByKey(tag.Key, out GhostBodyEntry entry) || entry.World == null || !entry.World.IsCreated) return;
            var em = entry.World.EntityManager;
            if (kind == RecallShowKind.Begun) Channels.Begin(entry.NetworkId, entry.World.Time.ElapsedTime, seconds);
            else Channels.End(entry.NetworkId);
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            using var connections = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++) Send(em, connections[i], entry.NetworkId, kind, seconds);
        }

        internal static void Send(EntityManager em, Entity connection, int networkId, RecallShowKind kind, float seconds)
        {
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new RecallShownRpc { NetworkId = networkId, Kind = (byte)kind, Seconds = seconds });
            em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            Sent++;
        }
    }

    /// <summary>
    /// Server (wave 4 relevancy): a connection that just got the ghost of a player in the middle of a Recall channel is told the
    /// channel now, with the seconds left (the others were told once, when it began). Reads the fill's "became relevant" lists;
    /// nothing runs while nobody channels or relevancy is off (no connection has the list).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RecallRelevantServerSystem : SystemBase
    {
        EntityQuery m_Lists, m_Owned;
        readonly List<(Entity connection, int networkId, float seconds)> m_Tell = new List<(Entity, int, float)>();

        protected override void OnCreate()
        {
            m_Lists = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<BecameRelevantGhost>());
            m_Owned = GetEntityQuery(ComponentType.ReadOnly<GhostInstance>(), ComponentType.ReadOnly<GhostOwner>());
        }

        protected override void OnUpdate()
        {
            if (EntityRecallShowLink.Channels.Count == 0 || m_Lists.IsEmpty) return;
            EntityManager em = EntityManager;
            double now = World.Time.ElapsedTime;
            using var connections = m_Lists.ToEntityArray(Allocator.Temp);
            using var ids = m_Lists.ToComponentDataArray<NetworkId>(Allocator.Temp);
            NativeHashMap<int, int> ownerOf = default;   // ghost id -> the connection that owns it, built only when somebody got a new ghost
            m_Tell.Clear();
            for (int i = 0; i < connections.Length; i++)
            {
                DynamicBuffer<BecameRelevantGhost> became = em.GetBuffer<BecameRelevantGhost>(connections[i], true);
                if (became.Length == 0) continue;
                if (!ownerOf.IsCreated) ownerOf = OwnerOfEveryGhost();
                for (int b = 0; b < became.Length; b++)
                    if (ownerOf.TryGetValue(became[b].GhostId, out int owner) && owner != ids[i].Value
                        && EntityRecallShowLink.Channels.TryLeft(owner, now, out float left))
                        m_Tell.Add((connections[i], owner, left));
            }
            if (ownerOf.IsCreated) ownerOf.Dispose();
            foreach ((Entity connection, int networkId, float seconds) in m_Tell)
                EntityRecallShowLink.Send(em, connection, networkId, RecallShowKind.Begun, seconds);
            m_Tell.Clear();
        }

        NativeHashMap<int, int> OwnerOfEveryGhost()
        {
            using var ghosts = m_Owned.ToComponentDataArray<GhostInstance>(Allocator.Temp);
            using var owners = m_Owned.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            var map = new NativeHashMap<int, int>(System.Math.Max(1, ghosts.Length), Allocator.Temp);
            for (int i = 0; i < ghosts.Length; i++)
                if (ghosts[i].ghostId != 0) map.TryAdd(ghosts[i].ghostId, owners[i].NetworkId);
            return map;
        }
    }

    /// <summary>Client: plays the Recall shows the server passes on, on the body of the player they are about (aura, arrival).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RecallClientSystem : SystemBase
    {
        EntityQuery m_Shows;
        readonly PendingRecallShows m_Pending = new PendingRecallShows();

        protected override void OnCreate() =>
            m_Shows = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<RecallShownRpc>());

        protected override void OnUpdate()
        {
            double now = World.Time.ElapsedTime;
            if (!m_Shows.IsEmpty)
            {
                using var shows = m_Shows.ToComponentDataArray<RecallShownRpc>(Allocator.Temp);
                EntityManager.DestroyEntity(m_Shows);
                foreach (RecallShownRpc show in shows)
                {
                    var kind = (RecallShowKind)show.Kind;
                    if (kind != RecallShowKind.Begun) m_Pending.Drop(show.NetworkId);
                    if (GhostBodyRegistry.TryGetByNetworkId(World, show.NetworkId, out GhostBodyEntry entry) && entry.Body != null)
                        RecallShowLink.Play(entry.Body.transform, kind, show.Seconds, entry.IsLocal);
                    else if (kind == RecallShowKind.Begun)
                        m_Pending.Hold(show.NetworkId, now, show.Seconds);   // the body is not here yet (it just came into range): the aura waits for it
                }
            }
            if (m_Pending.Count == 0) return;
            m_Pending.Step(now,
                id => GhostBodyRegistry.TryGetByNetworkId(World, id, out GhostBodyEntry entry) && entry.Body != null,
                (id, seconds) =>
                {
                    if (GhostBodyRegistry.TryGetByNetworkId(World, id, out GhostBodyEntry entry) && entry.Body != null)
                        RecallShowLink.Play(entry.Body.transform, RecallShowKind.Begun, seconds, entry.IsLocal);
                });
        }
    }
}
