using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>The owner's eight Autoward cells (NGO's <c>SetSlotsServerRpc</c>; M8 8.3.11): skill ids joined by '|'.</summary>
    public struct AutobuffSlotsRpc : IRpcCommand
    {
        public FixedString512Bytes Ids;

        public static string Join(string[] ids) => ids == null ? string.Empty : string.Join("|", ids);
        public static string[] Split(string text) => string.IsNullOrEmpty(text) ? new string[0] : text.Split('|');
    }

    /// <summary>The licence and the cells to their owner (NGO's netExpiresUtc and SlotsRpc).</summary>
    public struct AutobuffStateRpc : IRpcCommand
    {
        public long ExpiresUtc;
        public FixedString512Bytes Ids;
    }

    /// <summary>Server: the owner's cells (Account limit, as NGO) on the server body; the licence and cells to the owner on arrival and change.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AutobuffServerSystem : SystemBase
    {
        EntityQuery m_Asks, m_InGame;
        readonly Dictionary<Entity, (long expires, string ids)> m_Told = new Dictionary<Entity, (long, string)>();
        readonly List<Entity> m_Gone = new List<Entity>();

        /// <summary>Cells kept and states sent (tests).</summary>
        public int Kept { get; private set; }
        public int Sent { get; private set; }

        protected override void OnCreate()
        {
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<AutobuffSlotsRpc>());
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            if (!m_Asks.IsEmpty)
            {
                using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Asks.ToComponentDataArray<AutobuffSlotsRpc>(Allocator.Temp);
                em.DestroyEntity(m_Asks);
                for (int i = 0; i < asks.Length; i++)
                    if (PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Account, out GameObject body) && body.TryGetComponent(out IBodyAutobuff autobuff))
                    {
                        autobuff.RunSetSlots(AutobuffSlotsRpc.Split(asks[i].Ids.ToString()));
                        Kept++;
                    }
            }
            m_Gone.Clear();
            foreach (Entity ghost in m_Told.Keys) if (!em.Exists(ghost)) m_Gone.Add(ghost);
            foreach (Entity ghost in m_Gone) m_Told.Remove(ghost);
            using var connections = m_InGame.ToEntityArray(Allocator.Temp);
            using var ids = m_InGame.ToComponentDataArray<NetworkId>(Allocator.Temp);
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !entry.Body.TryGetComponent(out IBodyAutobuff state)) continue;
                string text = AutobuffSlotsRpc.Join(state.SkillIds);
                if (m_Told.TryGetValue(entry.Ghost, out var told) && told.expires == state.ExpiresUtc && told.ids == text) continue;
                Entity connection = Entity.Null;
                for (int i = 0; i < ids.Length; i++) if (ids[i].Value == entry.NetworkId) { connection = connections[i]; break; }
                if (connection == Entity.Null || System.Text.Encoding.UTF8.GetByteCount(text) > FixedString512Bytes.UTF8MaxLengthInBytes) continue;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new AutobuffStateRpc { ExpiresUtc = state.ExpiresUtc, Ids = new FixedString512Bytes(text) });
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                m_Told[entry.Ghost] = (state.ExpiresUtc, text);
                Sent++;
            }
        }
    }

    /// <summary>Client: the server's licence and cells on the own view (the Autoward window reads them).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AutobuffClientSystem : SystemBase
    {
        EntityQuery m_States;

        protected override void OnCreate() =>
            m_States = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<AutobuffStateRpc>());

        protected override void OnUpdate()
        {
            if (m_States.IsEmpty) return;
            using var states = m_States.ToComponentDataArray<AutobuffStateRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_States);
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out IBodyAutobuff own))
                {
                    foreach (AutobuffStateRpc s in states) own.MirrorState(s.ExpiresUtc, AutobuffSlotsRpc.Split(s.Ids.ToString()));
                    break;
                }
        }
    }
}
