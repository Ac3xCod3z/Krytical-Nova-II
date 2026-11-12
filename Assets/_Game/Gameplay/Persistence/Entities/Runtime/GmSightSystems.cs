using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Player;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// One player of a GM's sight list, server to that GM's own game (NGO's <c>PlayerGmSight.SeenRpc</c> carried the whole array; DOTS wave 1,
    /// 12 Nov 2026, sends one per player so no rpc outgrows a packet). <see cref="Index"/> of <see cref="Total"/>: 0 starts a new list, the
    /// last one completes it. <see cref="You"/> marks the GM's own entry.
    /// </summary>
    public struct GmSeenRpc : IRpcCommand
    {
        public ushort Index;
        public ushort Total;
        public ulong Serial;
        public FixedString64Bytes Name;
        public byte Faction;
        public float3 Position;
        public byte You;
    }

    /// <summary>One player the server knows, as the sight list takes it.</summary>
    public struct GmSightPlayer
    {
        public ulong Serial;
        public string Name;
        public FactionId Faction;
        public UnityEngine.Vector3 Position;
    }

    /// <summary>The list's entries and their assembly, as pure functions (tests state them without a world).</summary>
    public static class GmSightList
    {
        /// <summary>The entries for the GM whose connection is <paramref name="gmSerial"/>: every player, in order, numbered. Empty when there are none.</summary>
        public static List<GmSeenRpc> Entries(IReadOnlyList<GmSightPlayer> players, ulong gmSerial)
        {
            var list = new List<GmSeenRpc>(players.Count);
            int total = players.Count > ushort.MaxValue ? ushort.MaxValue : players.Count;
            for (int i = 0; i < total; i++)
            {
                GmSightPlayer p = players[i];
                list.Add(new GmSeenRpc
                {
                    Index = (ushort)i, Total = (ushort)total, Serial = p.Serial, Name = GmSightRules.Fit(p.Name),
                    Faction = (byte)p.Faction, Position = new float3(p.Position.x, p.Position.y, p.Position.z), You = (byte)(p.Serial == gmSerial ? 1 : 0),
                });
            }
            return list;
        }

        /// <summary>Gathers entries as they arrive (reliable and ordered); true with the finished list when the last one came.</summary>
        public sealed class Collector
        {
            readonly List<GmSeenPlayer> m_Pending = new List<GmSeenPlayer>();
            ulong m_Self = ulong.MaxValue;

            public bool Add(GmSeenRpc entry, out List<GmSeenPlayer> finished, out ulong self)
            {
                if (entry.Index == 0) { m_Pending.Clear(); m_Self = ulong.MaxValue; }
                m_Pending.Add(new GmSeenPlayer
                {
                    ClientId = entry.Serial, Name = entry.Name, Faction = (FactionId)entry.Faction,
                    Position = new UnityEngine.Vector3(entry.Position.x, entry.Position.y, entry.Position.z),
                });
                if (entry.You != 0) m_Self = entry.Serial;
                self = m_Self;
                if (entry.Index + 1 < entry.Total) { finished = null; return false; }
                finished = new List<GmSeenPlayer>(m_Pending);
                m_Pending.Clear();
                return true;
            }
        }
    }

    /// <summary>
    /// Server: NGO's <c>PlayerGmSight.Update</c> for the entity netcode. Once a second, each GM whose sight is on (<see cref="GmRoster.IsGm"/>
    /// and <see cref="GmSightRules.SeeAll"/>, <c>gm.seeall</c>) is sent where every player on this map is (name, faction, place), to their own
    /// connection only. Nobody else is ever sent it. Positions come from the server's own bodies; nothing is saved, nothing capped.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class GmSightServerSystem : SystemBase
    {
        EntityQuery m_InGame;
        float m_NextSend;
        readonly List<GmSightPlayer> m_Players = new List<GmSightPlayer>(64);
        readonly HashSet<ulong> m_Known = new HashSet<ulong>();
        readonly List<ulong> m_Gone = new List<ulong>();

        /// <summary>Lists sent and entries sent (tests).</summary>
        public int ListsSent { get; private set; }
        public int EntriesSent { get; private set; }

        protected override void OnCreate() =>
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkStreamInGame>(), ComponentType.ReadOnly<ConnectionPlayerKey>());

        protected override void OnUpdate()
        {
            var em = EntityManager;
            using var connections = m_InGame.ToEntityArray(Allocator.Temp);
            using var keys = m_InGame.ToComponentDataArray<ConnectionPlayerKey>(Allocator.Temp);

            // a connection that left forgets its switch (NGO: the network variable went with the body)
            m_Gone.Clear();
            foreach (ulong serial in m_Known)
            {
                bool here = false;
                for (int i = 0; i < keys.Length; i++) if (keys[i].Serial == serial) { here = true; break; }
                if (!here) m_Gone.Add(serial);
            }
            foreach (ulong serial in m_Gone) { m_Known.Remove(serial); GmSightRules.Forget(serial); }
            for (int i = 0; i < keys.Length; i++) m_Known.Add(keys[i].Serial);

            float now = UnityEngine.Time.unscaledTime;
            if (now < m_NextSend) return;
            m_NextSend = now + GmSightRules.SendEvery;
            bool any = false;
            for (int i = 0; i < keys.Length; i++)
                if (GmSightRules.Sees(GmRoster.IsGm(keys[i].Serial), GmSightRules.SeeAll(keys[i].Serial))) { any = true; break; }
            if (!any) return;

            Collect(m_Players);
            if (m_Players.Count == 0) return;
            for (int i = 0; i < keys.Length; i++)
            {
                ulong serial = keys[i].Serial;
                if (!GmSightRules.Sees(GmRoster.IsGm(serial), GmSightRules.SeeAll(serial))) continue;
                List<GmSeenRpc> entries = GmSightList.Entries(m_Players, serial);
                foreach (GmSeenRpc entry in entries)
                {
                    Entity rpc = em.CreateEntity();
                    em.AddComponentData(rpc, entry);
                    em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connections[i] });
                }
                ListsSent++;
                EntriesSent += entries.Count;
            }
        }

        // every player on this map: the server's own bodies (NGO: PointAndClickPlayer.Active)
        static void Collect(List<GmSightPlayer> into)
        {
            into.Clear();
            foreach (BodyRoleTag tag in BodyRoleTag.ServerBodies)
            {
                if (tag == null || !tag.TryGetComponent(out PlayerIdentity identity)) continue;
                into.Add(new GmSightPlayer { Serial = tag.ConnectionSerial, Name = identity.CharacterName, Faction = identity.Faction, Position = tag.transform.position });
            }
        }
    }

    /// <summary>
    /// Client: hands the list the server sent a GM to <see cref="PlayerGmSight"/> (its maps and the GM window read it there, as they read NGO's).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class GmSightClientSystem : SystemBase
    {
        EntityQuery m_Entries;
        readonly GmSightList.Collector m_Collector = new GmSightList.Collector();

        /// <summary>Lists handed on (tests).</summary>
        public int ListsReceived { get; private set; }

        protected override void OnCreate() =>
            m_Entries = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<GmSeenRpc>());

        protected override void OnUpdate()
        {
            if (m_Entries.IsEmpty) return;
            var em = EntityManager;
            using var entries = m_Entries.ToComponentDataArray<GmSeenRpc>(Allocator.Temp);
            em.DestroyEntity(m_Entries);
            for (int i = 0; i < entries.Length; i++)
                if (m_Collector.Add(entries[i], out List<GmSeenPlayer> list, out ulong self))
                {
                    PlayerGmSight.FeedFromEntities(list, self);
                    ListsReceived++;
                }
        }
    }
}
