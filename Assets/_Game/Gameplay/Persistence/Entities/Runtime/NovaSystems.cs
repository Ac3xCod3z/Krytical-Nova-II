using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>The owner's Herald request (NGO's four <c>PlayerNova</c> RPCs; M8 8.3.5): the op and the track or tier.</summary>
    public struct NovaOpRpc : IRpcCommand
    {
        public byte Op, Arg;
    }

    /// <summary>A Herald result (NGO's <c>NovaWorkResult</c>): to its owner, and with <see cref="NetworkId"/> the actor to everyone else.</summary>
    public struct NovaResultRpc : IRpcCommand
    {
        public int NetworkId;
        public byte Mine, Kind, Outcome, Track, Before, After, NewTier;

        public NovaWorkResult Result => new NovaWorkResult
        {
            kind = (NovaWorkKind)Kind, outcome = (AscensionOutcome)Outcome, track = (StandingTrack)Track, before = Before, after = After, newTier = NewTier != 0,
        };
    }

    /// <summary>
    /// Server: <see cref="INovaLink"/> for the bodies of the entity netcode: the result to the player whose server body it is (by its
    /// <see cref="PlayerKey"/>), the moment to every other player (NGO's ShownRpc to everyone) with the actor's NetworkId.
    /// </summary>
    public sealed class EntityNovaLink : INovaLink
    {
        public void Finished(Component part, in NovaWorkResult result)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return;
            var rpc = new NovaResultRpc
            {
                NetworkId = em.GetComponentData<NetworkId>(connection).Value, Mine = 1, Kind = (byte)result.kind, Outcome = (byte)result.outcome,
                Track = (byte)result.track, Before = result.before, After = result.after, NewTier = (byte)(result.newTier ? 1 : 0),
            };
            Send(em, connection, rpc);
            rpc.Mine = 0;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            using var everyone = query.ToEntityArray(Allocator.Temp);
            foreach (Entity other in everyone) if (other != connection) Send(em, other, rpc);
        }

        static void Send(EntityManager em, Entity connection, NovaResultRpc rpc)
        {
            Entity e = em.CreateEntity();
            em.AddComponentData(e, rpc);
            em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>Server: the Herald requests (all at the Money limit, as NGO) run on the server body by NGO's own rules.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class NovaRequestServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        /// <summary>Requests run (tests).</summary>
        public int Ran { get; private set; }

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<NovaOpRpc>());

        protected override void OnUpdate()
        {
            if (m_Asks.IsEmpty) return;
            using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
            using var asks = m_Asks.ToComponentDataArray<NovaOpRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Asks);
            for (int i = 0; i < asks.Length; i++)
                if (PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out PlayerNova nova))
                {
                    nova.RunNovaOp((NovaOpKind)asks[i].Op, asks[i].Arg);
                    Ran++;
                }
        }
    }

    /// <summary>Client: a Herald result on the own view (window + moment), or the moment on another player's view.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class NovaClientSystem : SystemBase
    {
        EntityQuery m_Results;

        protected override void OnCreate() =>
            m_Results = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<NovaResultRpc>());

        protected override void OnUpdate()
        {
            if (m_Results.IsEmpty) return;
            using var results = m_Results.ToComponentDataArray<NovaResultRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Results);
            foreach (NovaResultRpc r in results)
            {
                if (r.Mine != 0)
                {
                    foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                        if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out PlayerNova own))
                        { own.MirrorFinished(r.Result); break; }
                }
                else if (GhostBodyRegistry.TryGetByNetworkId(World, r.NetworkId, out GhostBodyEntry entry) && entry.Body != null && !entry.IsLocal
                         && entry.Body.TryGetComponent(out PlayerNova other))
                    other.MirrorShown(r.Result);
            }
        }
    }
}
