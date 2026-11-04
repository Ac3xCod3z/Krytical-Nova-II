using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>A trick the owner's pet took, to pass on (NGO's <c>PlayerPet</c> ShareTrickRpc; M8 8.4.7).</summary>
    public struct PetTrickRpc : IRpcCommand
    {
        public int NameHash;
    }

    /// <summary>A pet show for the player on connection <see cref="NetworkId"/> (NGO's OthersPlayTrickRpc / PlayAnimationRpc / EvolvedRpc).</summary>
    public struct PetShownRpc : IRpcCommand
    {
        public int NetworkId, Value;
        public byte Kind;
    }

    /// <summary>
    /// Server: <see cref="IPetShowLink"/> for the bodies of the entity netcode: the show goes to every in-game connection by the actor's NetworkId
    /// (its own player too when asked), as the Forge's burst (8.3.6).
    /// </summary>
    public sealed class EntityPetShowLink : IPetShowLink
    {
        /// <summary>Shows sent (tests).</summary>
        public static int Sent { get; private set; }

        public void Show(Component part, BodyPetShowKind kind, int value, bool toOwner)
        {
            BodyRoleTag tag = BodyRoleTag.Of(part);
            if (tag == null || tag.Kind != NetRoleKind.ServerBody || !GhostBodyRegistry.TryGetByKey(tag.Key, out GhostBodyEntry entry) || entry.World == null || !entry.World.IsCreated) return;
            var em = entry.World.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            using var connections = query.ToEntityArray(Allocator.Temp);
            using var ids = query.ToComponentDataArray<NetworkId>(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
            {
                if (!toOwner && ids[i].Value == entry.NetworkId) continue;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new PetShownRpc { NetworkId = entry.NetworkId, Kind = (byte)kind, Value = value });
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connections[i] });
                Sent++;
            }
        }
    }

    /// <summary>Server: the owners' tricks at the Cosmetic limit (as NGO) on their server bodies; every player ghost's pet growth and fed equal to its body's.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PetServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        /// <summary>Tricks run and ghosts rewritten (tests).</summary>
        public int Ran { get; private set; }
        public int Writes { get; private set; }

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<PetTrickRpc>());

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (!m_Asks.IsEmpty)
            {
                using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Asks.ToComponentDataArray<PetTrickRpc>(Allocator.Temp);
                em.DestroyEntity(m_Asks);
                for (int i = 0; i < asks.Length; i++)
                {
                    if (!PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Cosmetic, out GameObject body) || !body.TryGetComponent(out IBodyPet pet)) continue;
                    pet.RunTrick(asks[i].NameHash);
                    Ran++;
                }
            }
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<PlayerPetTag>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out IBodyPet pet)) continue;
                var now = new PlayerPetTag { Growth = pet.ShownGrowth, Fed = (byte)(pet.ShownFed ? 1 : 0) };
                PlayerPetTag had = em.GetComponentData<PlayerPetTag>(entry.Ghost);
                if (had.Growth == now.Growth && had.Fed == now.Fed) continue;
                em.SetComponentData(entry.Ghost, now);
                Writes++;
            }
        }
    }

    /// <summary>Client: every view shows its player's pet growth and fed from the ghost, and plays the shows the server passes on.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PetClientSystem : SystemBase
    {
        EntityQuery m_Shows;

        protected override void OnCreate() =>
            m_Shows = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<PetShownRpc>());

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<PlayerPetTag>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out IBodyPet pet)) continue;
                PlayerPetTag tag = em.GetComponentData<PlayerPetTag>(entry.Ghost);
                pet.MirrorShown(tag.Growth, tag.Fed != 0);
            }
            if (m_Shows.IsEmpty) return;
            using var shows = m_Shows.ToComponentDataArray<PetShownRpc>(Allocator.Temp);
            em.DestroyEntity(m_Shows);
            foreach (PetShownRpc show in shows)
                if (GhostBodyRegistry.TryGetByNetworkId(World, show.NetworkId, out GhostBodyEntry entry) && entry.Body != null && entry.Body.TryGetComponent(out IBodyPet pet))
                    pet.PlayShown((BodyPetShowKind)show.Kind, show.Value);
        }
    }
}
