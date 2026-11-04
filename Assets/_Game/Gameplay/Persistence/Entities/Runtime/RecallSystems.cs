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
    /// <summary>A Recall show for the player on connection <see cref="NetworkId"/> (NGO's ChannelRpc / ArrivedRpc; M8 8.4.10).</summary>
    public struct RecallShownRpc : IRpcCommand
    {
        public int NetworkId;
        public byte Kind;
        public float Seconds;
    }

    /// <summary>Server: <see cref="IRecallShowLink"/> for the bodies of the entity netcode: the show to every in-game connection, its own player's too.</summary>
    public sealed class EntityRecallShowLink : IRecallShowLink
    {
        /// <summary>Shows sent (tests).</summary>
        public static int Sent { get; private set; }

        public void Show(Component part, RecallShowKind kind, float seconds)
        {
            BodyRoleTag tag = BodyRoleTag.Of(part);
            if (tag == null || tag.Kind != NetRoleKind.ServerBody || !GhostBodyRegistry.TryGetByKey(tag.Key, out GhostBodyEntry entry) || entry.World == null || !entry.World.IsCreated) return;
            var em = entry.World.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            using var connections = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
            {
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new RecallShownRpc { NetworkId = entry.NetworkId, Kind = (byte)kind, Seconds = seconds });
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connections[i] });
                Sent++;
            }
        }
    }

    /// <summary>Client: plays the Recall shows the server passes on, on the body of the player they are about (aura, arrival).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RecallClientSystem : SystemBase
    {
        EntityQuery m_Shows;

        protected override void OnCreate() =>
            m_Shows = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<RecallShownRpc>());

        protected override void OnUpdate()
        {
            if (m_Shows.IsEmpty) return;
            using var shows = m_Shows.ToComponentDataArray<RecallShownRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Shows);
            foreach (RecallShownRpc show in shows)
                if (GhostBodyRegistry.TryGetByNetworkId(World, show.NetworkId, out GhostBodyEntry entry) && entry.Body != null)
                    RecallShowLink.Play(entry.Body.transform, (RecallShowKind)show.Kind, show.Seconds, entry.IsLocal);
        }
    }
}
