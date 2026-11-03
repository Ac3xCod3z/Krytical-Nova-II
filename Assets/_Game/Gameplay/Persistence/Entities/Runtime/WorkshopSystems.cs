using System.Collections.Generic;
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
    /// <summary>The owner works a piece at the Forge (NGO's five <c>PlayerGearWork</c> RPCs; M8 8.3.6).</summary>
    public struct GearWorkRpc : IRpcCommand
    {
        public byte Op;
        public short Cell, Cell2;
        public ushort Alloy, Protection;
    }

    /// <summary>The owner makes a recipe at the Fabricator (NGO's <c>PlayerCrafting.CraftRpc</c>; M8 8.3.7).</summary>
    public struct CraftRpc : IRpcCommand
    {
        public ushort Recipe, Times;
    }

    /// <summary>What became of the piece, to its owner (NGO's FinishedRpc).</summary>
    public struct GearWorkedRpc : IRpcCommand
    {
        public byte Kind, Outcome;
        public ushort BeforeId, BeforeCount, AfterId, AfterCount;
        public ulong BeforeData, AfterData;
    }

    /// <summary>The burst on a character that worked a piece, to every player (NGO's ShownRpc to everyone): whose by its NetworkId.</summary>
    public struct GearShownRpc : IRpcCommand
    {
        public int NetworkId;
        public byte Outcome;
    }

    /// <summary>What the Fabricator made, to its owner (NGO's CraftedRpc).</summary>
    public struct CraftedRpc : IRpcCommand
    {
        public ushort Recipe, Times;
    }

    /// <summary>
    /// Server: <see cref="IWorkshopOwnerLink"/> for the bodies of the entity netcode: the result to the player whose server body it is (by its
    /// <see cref="PlayerKey"/>, as <see cref="EntityTradeOwnerLink"/>); the Forge's burst to every player in the game.
    /// </summary>
    public sealed class EntityWorkshopOwnerLink : IWorkshopOwnerLink
    {
        public void GearWorked(Component part, byte kind, byte outcome, ItemStack before, ItemStack after)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return;
            Send(em, connection, new GearWorkedRpc
            {
                Kind = kind, Outcome = outcome, BeforeId = before.itemId, BeforeCount = before.count, BeforeData = before.data,
                AfterId = after.itemId, AfterCount = after.count, AfterData = after.data,
            });
            int actor = em.GetComponentData<NetworkId>(connection).Value;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            using var everyone = query.ToEntityArray(Allocator.Temp);
            foreach (Entity other in everyone)
                if (other != connection) Send(em, other, new GearShownRpc { NetworkId = actor, Outcome = outcome });
        }

        public void Crafted(Component part, ushort recipeId, int times)
        {
            if (EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection))
                Send(em, connection, new CraftedRpc { Recipe = recipeId, Times = (ushort)Mathf.Clamp(times, 0, ushort.MaxValue) });
        }

        static void Send<T>(EntityManager em, Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            Entity e = em.CreateEntity();
            em.AddComponentData(e, rpc);
            em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>Server: the Forge and Fabricator requests (NGO's RPCs at the Money limit) run on the server body by NGO's own rules.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WorkshopRequestServerSystem : SystemBase
    {
        EntityQuery m_Work, m_Crafts;

        /// <summary>Requests run (tests).</summary>
        public int Ran { get; private set; }

        protected override void OnCreate()
        {
            m_Work = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<GearWorkRpc>());
            m_Crafts = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<CraftRpc>());
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            if (!m_Work.IsEmpty)
            {
                using var received = m_Work.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Work.ToComponentDataArray<GearWorkRpc>(Allocator.Temp);
                em.DestroyEntity(m_Work);
                for (int i = 0; i < asks.Length; i++)
                    if (PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out IBodyGearWork forge))
                    {
                        forge.RunGearWork((GearWorkOpKind)asks[i].Op, asks[i].Cell, asks[i].Cell2, asks[i].Alloy, asks[i].Protection);
                        Ran++;
                    }
            }
            if (!m_Crafts.IsEmpty)
            {
                using var received = m_Crafts.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Crafts.ToComponentDataArray<CraftRpc>(Allocator.Temp);
                em.DestroyEntity(m_Crafts);
                for (int i = 0; i < asks.Length; i++)
                    if (PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out IBodyCrafting crafting))
                    {
                        crafting.RunCraft(asks[i].Recipe, asks[i].Times);
                        Ran++;
                    }
            }
        }
    }

    /// <summary>Client: the Forge's result and the Fabricator's on the local view body; another player's Forge burst on their body.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WorkshopClientSystem : SystemBase
    {
        EntityQuery m_Worked, m_Shown, m_Crafted;

        protected override void OnCreate()
        {
            m_Worked = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<GearWorkedRpc>());
            m_Shown = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<GearShownRpc>());
            m_Crafted = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<CraftedRpc>());
        }

        protected override void OnUpdate()
        {
            if (m_Worked.IsEmpty && m_Shown.IsEmpty && m_Crafted.IsEmpty) return;
            var em = EntityManager;
            using var worked = m_Worked.ToComponentDataArray<GearWorkedRpc>(Allocator.Temp);
            using var shown = m_Shown.ToComponentDataArray<GearShownRpc>(Allocator.Temp);
            using var crafted = m_Crafted.ToComponentDataArray<CraftedRpc>(Allocator.Temp);
            em.DestroyEntity(m_Worked);
            em.DestroyEntity(m_Shown);
            em.DestroyEntity(m_Crafted);
            GameObject local = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null) { local = entry.Body; break; }
            var forge = local != null ? local.GetComponent<IBodyGearWork>() : null;
            var crafting = local != null ? local.GetComponent<IBodyCrafting>() : null;
            foreach (GearWorkedRpc w in worked)
                forge?.MirrorGearWorked(w.Kind, w.Outcome, new ItemStack(w.BeforeId, w.BeforeCount, w.BeforeData), new ItemStack(w.AfterId, w.AfterCount, w.AfterData));
            foreach (CraftedRpc c in crafted) crafting?.MirrorCrafted(c.Recipe, c.Times);
            // another player's burst: their body here (remote views carry no Forge part, so the feel sink is told directly)
            foreach (GearShownRpc s in shown)
                if (GhostBodyRegistry.TryGetByNetworkId(World, s.NetworkId, out GhostBodyEntry entry) && entry.Body != null && !entry.IsLocal)
                    ClientPresentationRegistry.Feel?.GearWorked(entry.Body.transform, s.Outcome, false);
        }
    }
}
