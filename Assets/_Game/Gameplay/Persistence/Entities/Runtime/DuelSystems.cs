using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    // =================================================================================================================
    // The Callout on the entity netcode (M4 step 8a): NGO's PlayerDuel decides on the server body (its RPC bodies), its owner
    // asks by these messages and is told by the owner link, as the Squad port (M8 8.4.3) does.
    // =================================================================================================================

    /// <summary>The owner's Callout request (NGO RequestRpc / RespondRpc / ForfeitRpc): the target by its ghost.</summary>
    public struct DuelOpRpc : IRpcCommand
    {
        public byte Op;
        public byte Accept;
        public int TargetGhostId;
    }

    /// <summary>What the server tells the owner (NGO PromptRpc, PromptClosedRpc, ResultRpc, CountdownRpc, CountdownCancelledRpc).</summary>
    public struct DuelShowRpc : IRpcCommand
    {
        public byte Kind;
        public byte Won;
        public float Seconds;
        public FixedString64Bytes Name;
    }

    /// <summary>The server body's way to its owner's game for the Callout.</summary>
    public sealed class EntityDuelOwnerLink : IDuelOwnerLink
    {
        public void Show(Component part, DuelShowKind kind, string name, float seconds, bool won)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return;
            Entity e = em.CreateEntity();
            em.AddComponentData(e, new DuelShowRpc { Kind = (byte)kind, Won = (byte)(won ? 1 : 0), Seconds = seconds, Name = ChatText.Name(name) });
            em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>Server: the owners' Callout requests at the Social limit (as NGO) on their server bodies.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class DuelServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<DuelOpRpc>());

        protected override void OnUpdate()
        {
            if (m_Asks.IsEmpty) return;
            EntityManager em = EntityManager;
            using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
            using var asks = m_Asks.ToComponentDataArray<DuelOpRpc>(Allocator.Temp);
            em.DestroyEntity(m_Asks);
            for (int i = 0; i < asks.Length; i++)
            {
                if (!PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Social, out GameObject body)
                    || !body.TryGetComponent(out IBodyDuel duel)) continue;
                duel.RunDuelOp((DuelOpKind)asks[i].Op, BodyOf(em, asks[i].TargetGhostId), asks[i].Accept != 0);
            }
        }

        Component BodyOf(EntityManager em, int ghostId)
        {
            if (ghostId == 0) return null;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
                    && em.GetComponentData<GhostInstance>(e.Ghost).ghostId == ghostId)
                    return e.Body.transform;
            return null;
        }
    }

    /// <summary>Client: the own view shows what its server told it about its Callout.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class DuelClientSystem : SystemBase
    {
        EntityQuery m_Shows;

        protected override void OnCreate() =>
            m_Shows = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<DuelShowRpc>());

        protected override void OnUpdate()
        {
            if (m_Shows.IsEmpty) return;
            using var shows = m_Shows.ToComponentDataArray<DuelShowRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Shows);
            IBodyDuel own = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out own)) break;
            if (own == null) return;
            foreach (DuelShowRpc show in shows) own.MirrorShow((DuelShowKind)show.Kind, show.Name.ToString(), show.Seconds, show.Won != 0);
        }
    }
}
