using KryticalNova.Networking;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>The own view's selection for its server body (NGO PlayerCombatState.SelectionRpc): the ghost selected, 0 = none.</summary>
    public struct CombatSelectionRpc : IRpcCommand
    {
        public int GhostId;
    }

    /// <summary>
    /// What <see cref="CombatFlag.InCombat"/> carries (build 17, item 3.1): bit 1 in a fight (as before), bit 2 the fight has a real target
    /// (<see cref="PlayerCombatState.InRealFight"/>), which the town's fight music waits for.
    /// </summary>
    public static class CombatFlagBits
    {
        public const byte InFight = 1, Real = 2;
        public static byte Of(bool fighting, bool real) => (byte)((fighting ? InFight : 0) | (fighting && real ? Real : 0));
        public static bool Fighting(byte bits) => (bits & InFight) != 0;
        public static bool RealFight(byte bits) => (bits & Real) != 0;
    }

    public static class CombatSelection
    {
        /// <summary>The ghost a selected body stands for in <paramref name="world"/>: a player's or an attackable target's, else 0.</summary>
        public static int GhostIdOf(World world, Transform selected)
        {
            if (selected == null) return 0;
            EntityManager em = world.EntityManager;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, world) && e.Body != null && (selected == e.Body.transform || selected.IsChildOf(e.Body.transform))
                    && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost))
                    return em.GetComponentData<GhostInstance>(e.Ghost).ghostId;
            return CombatTargetRegistry.GhostIdOf(selected);
        }

        /// <summary>The body of the ghost with <paramref name="ghostId"/> in <paramref name="world"/>, or null.</summary>
        public static Transform BodyOf(World world, int ghostId)
        {
            if (ghostId == 0) return null;
            EntityManager em = world.EntityManager;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, world) && e.Body != null && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
                    && em.GetComponentData<GhostInstance>(e.Ghost).ghostId == ghostId)
                    return e.Body.transform;
            return CombatTargetRegistry.BodyOf(world, ghostId);
        }
    }

    /// <summary>
    /// Server: each player's fight as its server body's PlayerCombatState decides it (Cadence step 1; NGO's netInCombat, server-written),
    /// onto the ghost's <see cref="CombatFlag"/>; written only when it changes.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CombatFlagServerSystem : SystemBase
    {
        EntityQuery m_Selections;

        protected override void OnCreate() =>
            m_Selections = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<CombatSelectionRpc>());

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (!m_Selections.IsEmpty)
            {
                using var received = m_Selections.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var selections = m_Selections.ToComponentDataArray<CombatSelectionRpc>(Allocator.Temp);
                em.DestroyEntity(m_Selections);
                for (int i = 0; i < selections.Length; i++)
                {
                    Entity connection = received[i].SourceConnection;
                    if (!em.Exists(connection) || !em.HasComponent<NetworkId>(connection)) continue;
                    if (GhostBodyRegistry.TryGetByNetworkId(World, em.GetComponentData<NetworkId>(connection).Value, out GhostBodyEntry entry)
                        && entry.Body != null && entry.Body.TryGetComponent(out PlayerCombatState who))
                        who.ServerSetSelected(CombatSelection.BodyOf(World, selections[i].GhostId));
                }
            }
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<CombatFlag>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out PlayerCombatState state)) continue;
                byte now = CombatFlagBits.Of(state.InCombat, state.InRealFight);
                if (em.GetComponentData<CombatFlag>(entry.Ghost).InCombat != now) em.SetComponentData(entry.Ghost, new CombatFlag { InCombat = now });
            }
        }
    }

    /// <summary>
    /// Client: every view takes its player's fight from the ghost (NGO netInCombat.OnValueChanged), so the own view's fight music starts
    /// with the server's fight (a struck Strike Dummy included) and Cadence has its beat.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CombatFlagClientSystem : SystemBase
    {
        PlayerCombatState m_Own;

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            PlayerCombatState own = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out own)) break;
            if (!ReferenceEquals(own, m_Own))
            {
                if (m_Own != null) m_Own.SelectionFromView -= SendSelection;
                m_Own = own;
                if (m_Own != null) m_Own.SelectionFromView += SendSelection;
            }
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<CombatFlag>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out PlayerCombatState state)) continue;
                byte bits = em.GetComponentData<CombatFlag>(entry.Ghost).InCombat;
                state.MirrorInCombat(CombatFlagBits.Fighting(bits), CombatFlagBits.RealFight(bits));
            }
        }

        protected override void OnDestroy()
        {
            if (m_Own != null) m_Own.SelectionFromView -= SendSelection;
        }

        /// <summary>Selections sent (tests).</summary>
        public int SelectionsSent { get; private set; }

        void SendSelection(Transform selected)
        {
            EntityManager em = EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new CombatSelectionRpc { GhostId = CombatSelection.GhostIdOf(World, selected) });
            em.AddComponent<SendRpcCommandRequest>(rpc);   // to the server
            SelectionsSent++;
        }
    }
}
