using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using KryticalNova.Core;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities.Presentation;

namespace KryticalNova.Player.Entities.Bodies
{
    /// <summary>
    /// Server: brings every ServerBody to its ghost's pose, first thing in <see cref="KnRulesGroup"/> (after the ghost simulation has
    /// moved the ghosts, before any rule or RPC receive system of the same tick reads a body). Physics is synced once afterwards
    /// (<c>Physics.autoSyncTransforms</c> is off), so range and hit checks of the same tick see the new colliders (design R4).
    /// When the server world goes down, the bodies still standing are told their player is leaving and destroyed.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(KnRulesGroup), OrderFirst = true)]
    public partial class ServerBodySyncSystem : SystemBase
    {
        EntityQuery m_Ghosts;

        /// <summary>What syncs the physics scene after the bodies moved; tests count it.</summary>
        public Action SyncPhysics = Physics.SyncTransforms;

        /// <summary>Times the physics scene was synced (once per tick that moved any body).</summary>
        public int PhysicsSyncs { get; private set; }

        /// <summary>Bodies moved in the last update.</summary>
        public int LastSynced { get; private set; }

        protected override void OnCreate()
        {
            m_Ghosts = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, PlayerPose, PlayerGhostKey>()
                .WithNone<PlayerLeaving>());
            RequireForUpdate(m_Ghosts);
        }

        protected override void OnUpdate()
        {
            var poses = m_Ghosts.ToComponentDataArray<PlayerPose>(Allocator.Temp);
            var keys = m_Ghosts.ToComponentDataArray<PlayerGhostKey>(Allocator.Temp);
            int moved = 0;
            for (int i = 0; i < poses.Length; i++)
            {
                if (!GhostBodyRegistry.TryGetServerBody(keys[i].PlayerKey, out GameObject body)) continue;
                body.transform.SetPositionAndRotation(PoseMath.Metres(poses[i]), PoseMath.Rotation(poses[i].YawCdeg));
                moved++;
            }
            poses.Dispose();
            keys.Dispose();
            LastSynced = moved;
            if (moved > 0)
            {
                SyncPhysics?.Invoke();
                PhysicsSyncs++;
            }
        }

        protected override void OnDestroy()
        {
            // The world goes down with players still in it (server stop, a test): their saves were flushed by the stop path; tell
            // their rules once more (harmless twice) and take the bodies away so none outlives its world.
            foreach (GhostBodyEntry entry in GhostBodyRegistry.TakeWorld(World))
            {
                GhostBodyRegistry.SetServerBody(entry.Key, null);
                if (entry.Body == null) continue;
                try { ServerLeaving.Notify(entry.Body); }
                catch (Exception e) { Debug.LogError("[KnServer] a body's leaving rules threw while the world went down: " + e); }
                ServerBodies.DestroyBody(entry.Body);
            }
        }
    }

    /// <summary>Small helpers shared by the server body code.</summary>
    public static class ServerBodies
    {
        /// <summary>Destroys a body now in the Editor's edit mode (tests), at the end of the frame in Play.</summary>
        public static void DestroyBody(GameObject body)
        {
            if (body == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(body);
            else UnityEngine.Object.DestroyImmediate(body);
        }
    }
}
