using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities.Presentation;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Bodies
{
    /// <summary>
    /// Server: a player's connection closed (M3a-2b design 3.1). Registered in <see cref="ConnectionClosedSystem.Handlers"/>, which has
    /// already released the key (every async continuation sees the player as gone) and forgets the account after the handlers. In order:
    /// 1 the ghost loses its owner (NetworkId 0) and is marked <see cref="PlayerLeaving"/> FIRST: netcode hands the same NetworkId to the
    /// next connection, and a ghost still owned by it would become that player's; 2 the body's rules hear the player leave
    /// (<see cref="ServerLeaving.Notify"/>: the save is written and the account let go, a trade is cancelled); 3 the body is destroyed;
    /// 4 the ghost is destroyed (at once, never through the connection's LinkedEntityGroup); 5 the per-player budgets are forgotten.
    /// A second close of the same key finds nothing and does nothing.
    /// </summary>
    public sealed class PlayerLeaveHandler : IKnConnectionClosedHandler
    {
        readonly World m_World;

        /// <summary>Leave the ghost (ownerless, marked leaving) to <see cref="PlayerLeavingCleanupSystem"/> on the next update (the async-save variant of later phases).</summary>
        public bool DeferGhostDestroy;

        /// <summary>The per-player records of the server's own code (RPC budget, network health); set by the game.</summary>
        public Action<PlayerKey> Forget;

        /// <summary>One word per step: "invalidated", "left", "body destroyed", "ghost destroyed" (tests, the trace log).</summary>
        public Action<string> Trace;

        public PlayerLeaveHandler(World serverWorld) => m_World = serverWorld;

        /// <summary>Players whose body or ghost this handler took away.</summary>
        public int Left { get; private set; }

        public void OnConnectionClosed(PlayerKey key)
        {
            Entity ghost = FindGhost(key);
            GhostBodyRegistry.TryGetServerBody(key, out GameObject body);
            if (ghost == Entity.Null && body == null)
            {
                Forget?.Invoke(key); // refused before it had a body, or a second close: nothing of it is left
                return;
            }
            EntityManager em = m_World.EntityManager;

            // 1 nobody owns the ghost any more, before anything that could wait
            if (ghost != Entity.Null)
            {
                if (em.HasComponent<GhostOwner>(ghost)) em.SetComponentData(ghost, new GhostOwner { NetworkId = 0 });
                if (!em.HasComponent<PlayerLeaving>(ghost)) em.AddComponent<PlayerLeaving>(ghost);
                Trace?.Invoke("invalidated");
            }

            // 2 the rules on the body (save and release, trade cancel)
            if (body != null)
            {
                try { ServerLeaving.Notify(body); }
                catch (Exception e) { Debug.LogError("[KnServer] the leaving rules of " + key + " threw: " + e); }
                Trace?.Invoke("left");
            }

            // 3 the body
            GhostBodyRegistry.SetServerBody(key, null);
            if (ghost != Entity.Null) GhostBodyRegistry.Unregister(m_World, ghost, out _);
            if (body != null)
            {
                BodyRoleTag tag = body.GetComponent<BodyRoleTag>();
                if (tag != null) tag.Mover = null;
                ServerBodies.DestroyBody(body);
                Trace?.Invoke("body destroyed");
            }

            // 4 the ghost
            if (ghost != Entity.Null && !DeferGhostDestroy && em.Exists(ghost))
            {
                em.DestroyEntity(ghost);
                Trace?.Invoke("ghost destroyed");
            }

            // 5 the per-player records
            Forget?.Invoke(key);
            Left++;
        }

        Entity FindGhost(PlayerKey key)
        {
            if (m_World == null || !m_World.IsCreated || !key.IsValid) return Entity.Null;
            using var query = m_World.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<PlayerGhostKey>());
            var ghosts = query.ToEntityArray(Allocator.Temp);
            var keys = query.ToComponentDataArray<PlayerGhostKey>(Allocator.Temp);
            Entity found = Entity.Null;
            for (int i = 0; i < ghosts.Length; i++)
                if (keys[i].Key == key.Value) { found = ghosts[i]; break; }
            ghosts.Dispose();
            keys.Dispose();
            return found;
        }
    }

    /// <summary>Server: destroys the ghosts a <see cref="PlayerLeaveHandler"/> left marked <see cref="PlayerLeaving"/> (its deferred variant).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(KnRulesGroup), OrderLast = true)]
    public partial class PlayerLeavingCleanupSystem : SystemBase
    {
        EntityQuery m_Leaving;

        public int Destroyed { get; private set; }

        protected override void OnCreate()
        {
            m_Leaving = GetEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<PlayerLeaving>());
            RequireForUpdate(m_Leaving);
        }

        protected override void OnUpdate()
        {
            Destroyed += m_Leaving.CalculateEntityCount();
            EntityManager.DestroyEntity(m_Leaving);
        }
    }
}
