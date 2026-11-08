using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>A mount request of the owner's view (NGO's <c>PlayerMounts</c> SelectRpc and RideRpc; M8 8.4.8).</summary>
    public struct MountOpRpc : IRpcCommand
    {
        public byte Op, Box;
    }

    /// <summary>The four boxes of the Mount window, for its own player only (NGO's netBook, read by the owner).</summary>
    public struct MountBookRpc : IRpcCommand
    {
        public byte Owned;
        public uint Xp1, Xp2, Xp3, Xp4, Hunger1, Hunger2, Hunger3, Hunger4;
    }

    /// <summary>The selected mount of the player on connection <see cref="NetworkId"/> evolved (NGO's EvolvedRpc to everyone).</summary>
    public struct MountEvolvedRpc : IRpcCommand
    {
        public int NetworkId;
        public byte Stage;
    }

    /// <summary>
    /// Server: <see cref="IMountOwnerLink"/> for the bodies of the entity netcode: the boxes go to the body's own connection, an evolution to
    /// every in-game connection by the actor's NetworkId (as the pet's, 8.4.7).
    /// </summary>
    public sealed class EntityMountOwnerLink : IMountOwnerLink
    {
        /// <summary>Messages sent (tests).</summary>
        public static int Sent { get; private set; }

        public bool Book(Component part, byte owned, uint xp1, uint xp2, uint xp3, uint xp4, uint hunger1, uint hunger2, uint hunger3, uint hunger4)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return false;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new MountBookRpc
            {
                Owned = owned, Xp1 = xp1, Xp2 = xp2, Xp3 = xp3, Xp4 = xp4, Hunger1 = hunger1, Hunger2 = hunger2, Hunger3 = hunger3, Hunger4 = hunger4,
            });
            em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            Sent++;
            return true;
        }

        public void Evolved(Component part, int stage)
        {
            BodyRoleTag tag = BodyRoleTag.Of(part);
            if (tag == null || tag.Kind != NetRoleKind.ServerBody || !GhostBodyRegistry.TryGetByKey(tag.Key, out GhostBodyEntry entry) || entry.World == null || !entry.World.IsCreated) return;
            var em = entry.World.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            using var connections = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
            {
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new MountEvolvedRpc { NetworkId = entry.NetworkId, Stage = (byte)stage });
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connections[i] });
                Sent++;
            }
        }
    }

    /// <summary>
    /// Server: the owners' mount requests at NGO's call kinds (a select is Cosmetic, the Ride key a Move) on their server bodies; every player
    /// ghost's mount equal to its body's.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MountServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        /// <summary>Requests run and ghosts rewritten (tests).</summary>
        public int Ran { get; private set; }
        public int Writes { get; private set; }

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<MountOpRpc>());

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (!m_Asks.IsEmpty)
            {
                using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Asks.ToComponentDataArray<MountOpRpc>(Allocator.Temp);
                em.DestroyEntity(m_Asks);
                for (int i = 0; i < asks.Length; i++)
                {
                    var op = (MountOpKind)asks[i].Op;
                    if (op != MountOpKind.Select && op != MountOpKind.Ride) continue;
                    CallKind kind = op == MountOpKind.Ride ? CallKind.Move : CallKind.Cosmetic;
                    if (!PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, kind, out GameObject body) || !body.TryGetComponent(out IBodyMounts mounts)) continue;
                    mounts.RunMountOp(op, asks[i].Box);
                    Ran++;
                }
            }
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<PlayerMountTag>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out IBodyMounts mounts)) continue;
                var now = new PlayerMountTag { Selected = mounts.ShownSelected, Phase = mounts.ShownPhase, Growth = mounts.ShownGrowth, Fed = (byte)(mounts.ShownFed ? 1 : 0),
                    Running = (byte)(mounts.ShownRunning ? 1 : 0), EndsRunning = (byte)(mounts.ShownEndsRunning ? 1 : 0) };
                PlayerMountTag had = em.GetComponentData<PlayerMountTag>(entry.Ghost);
                if (had.Selected == now.Selected && had.Phase == now.Phase && had.Growth == now.Growth && had.Fed == now.Fed && had.Running == now.Running && had.EndsRunning == now.EndsRunning) continue;
                em.SetComponentData(entry.Ghost, now);
                Writes++;
            }
        }
    }

    /// <summary>Client: every view shows its player's mount from the ghost; the own view gets its boxes; evolutions play where they are told.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MountClientSystem : SystemBase
    {
        EntityQuery m_Books, m_Evolved;

        protected override void OnCreate()
        {
            m_Books = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<MountBookRpc>());
            m_Evolved = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<MountEvolvedRpc>());
        }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            IBodyMounts own = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !entry.Body.TryGetComponent(out IBodyMounts mounts)) continue;
                if (entry.IsLocal) own = mounts;
                if (!em.Exists(entry.Ghost) || !em.HasComponent<PlayerMountTag>(entry.Ghost)) continue;
                PlayerMountTag tag = em.GetComponentData<PlayerMountTag>(entry.Ghost);
                mounts.MirrorShown(tag.Selected, tag.Phase, tag.Growth, tag.Fed != 0, tag.Running != 0, tag.EndsRunning != 0);
            }
            if (!m_Books.IsEmpty)
            {
                using var books = m_Books.ToComponentDataArray<MountBookRpc>(Allocator.Temp);
                em.DestroyEntity(m_Books);
                foreach (MountBookRpc b in books)
                    own?.MirrorBook(b.Owned, b.Xp1, b.Xp2, b.Xp3, b.Xp4, b.Hunger1, b.Hunger2, b.Hunger3, b.Hunger4);
            }
            if (m_Evolved.IsEmpty) return;
            using var evolved = m_Evolved.ToComponentDataArray<MountEvolvedRpc>(Allocator.Temp);
            em.DestroyEntity(m_Evolved);
            foreach (MountEvolvedRpc e in evolved)
                if (GhostBodyRegistry.TryGetByNetworkId(World, e.NetworkId, out GhostBodyEntry entry) && entry.Body != null && entry.Body.TryGetComponent(out IBodyMounts mounts))
                    mounts.PlayEvolved(e.Stage);
        }
    }
}
