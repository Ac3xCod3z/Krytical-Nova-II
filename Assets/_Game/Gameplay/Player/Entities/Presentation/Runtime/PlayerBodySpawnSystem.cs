using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using KryticalNova.Core;
using World = Unity.Entities.World;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>What the factory needs to make the body of one player ghost.</summary>
    public struct PlayerBodyRequest
    {
        public int NetworkId;
        /// <summary>None on a client.</summary>
        public PlayerKey Key;
        /// <summary>The ghost is the one this machine owns.</summary>
        public bool IsLocal;
        public Vector3 Position;
        public float YawDegrees;
    }

    /// <summary>
    /// Makes and unmakes presentation bodies. The game's implementation (a later slice) instantiates Player.prefab as
    /// NetRole.ClientView; tests use plain GameObjects. Injected with <see cref="PlayerPresentationSetup.Install"/>; there is no default.
    /// </summary>
    public interface IPlayerBodyFactory
    {
        /// <summary>A new body at the spot; null when it could not be made (the system reports it once and tries again later).</summary>
        GameObject Create(in PlayerBodyRequest request);

        /// <summary>Takes a body away (the ghost left relevancy or the world is going down).</summary>
        void Destroy(GameObject body);
    }

    /// <summary>Cleanup marker: this ghost has a presentation body. It outlives the ghost by one step, so the despawn is seen exactly once.</summary>
    public struct PlayerBodyLink : ICleanupComponentData
    {
        public byte Linked;
    }

    /// <summary>
    /// Client only. Gives every player ghost a presentation body when it appears (asks the injected <see cref="IPlayerBodyFactory"/>),
    /// registers it in <see cref="GhostBodyRegistry"/> (so the rest of the game finds it through <see cref="PlayerBodies"/>, and the owner's
    /// through <see cref="LocalPlayer"/>), and destroys it when the ghost is gone or the world goes down.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PlayerBodiesBeforeCameraGroup))]
    [UpdateBefore(typeof(PlayerPresentationFollowSystem))]
    public partial class PlayerBodySpawnSystem : SystemBase
    {
        EntityQuery m_New, m_Gone, m_Linked, m_Own;
        bool m_WarnedFactory;

        /// <summary>Set by <see cref="PlayerPresentationSetup.Install"/>. Without it no body is made (one error).</summary>
        public IPlayerBodyFactory Factory { get; set; }

        /// <summary>
        /// False keeps this world's owner out of <see cref="LocalPlayer"/> (tests with several client worlds in one process; the game has
        /// one client world).
        /// </summary>
        public bool RegisterLocalPlayer { get; set; } = true;

        protected override void OnCreate()
        {
            m_New = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerGhostTag, GhostOwner, LocalTransform, PlayerPose>()
                .WithNone<PlayerBodyLink>());
            m_Gone = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerBodyLink>()
                .WithNone<PlayerGhostTag>());
            m_Linked = GetEntityQuery(new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PlayerBodyLink, PlayerGhostTag>());
            m_Own = GetEntityQuery(ComponentType.ReadOnly<NetworkId>());
            RequireAnyForUpdate(m_New, m_Gone, m_Linked);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            if (!m_Gone.IsEmpty)
            {
                using var gone = m_Gone.ToEntityArray(Allocator.Temp);
                foreach (var e in gone)
                {
                    if (GhostBodyRegistry.Unregister(World, e, out var entry)) DestroyBody(entry);
                }
                em.RemoveComponent<PlayerBodyLink>(m_Gone);
            }

            if (!m_New.IsEmpty)
            {
                if (Factory == null)
                {
                    if (!m_WarnedFactory)
                    {
                        m_WarnedFactory = true;
                        Debug.LogWarning("PlayerBodySpawnSystem: no IPlayerBodyFactory installed (PlayerPresentationSetup.Install); player ghosts get no body.");
                    }
                }
                else
                {
                    using var fresh = m_New.ToEntityArray(Allocator.Temp);
                    foreach (var e in fresh) SpawnBody(em, e);
                }
            }

            if (!m_Linked.IsEmpty)
            {
                // ownership can settle a frame after the ghost appears: keep LocalPlayer in step
                using var linked = m_Linked.ToEntityArray(Allocator.Temp);
                foreach (var e in linked)
                    if (GhostBodyRegistry.TryGetByGhost(World, e, out var entry))
                    {
                        bool local = RegisterLocalPlayer && IsOwnedHere(em, e, m_Own);
                        if (local != entry.IsLocal) GhostBodyRegistry.SetLocal(entry, local);
                    }
            }
        }

        void SpawnBody(EntityManager em, Entity e)
        {
            var t = em.GetComponentData<LocalTransform>(e);
            var request = new PlayerBodyRequest
            {
                NetworkId = em.GetComponentData<GhostOwner>(e).NetworkId,
                Key = PlayerKey.None,
                IsLocal = RegisterLocalPlayer && IsOwnedHere(em, e, m_Own),
                Position = new Vector3(t.Position.x, t.Position.y, t.Position.z),
                YawDegrees = FollowMath.YawDegrees(t.Rotation),
            };
            var body = Factory.Create(in request);
            if (body == null)
            {
                if (!m_WarnedFactory)
                {
                    m_WarnedFactory = true;
                    Debug.LogWarning($"PlayerBodySpawnSystem: the body factory returned no body for the ghost of NetworkId {request.NetworkId}; it is asked again next frame.");
                }
                return;
            }

            var state = body.GetComponent<PlayerPresentationState>();
            if (state == null) state = body.AddComponent<PlayerPresentationState>();
            state.NetworkId = request.NetworkId;
            GhostBodyRegistry.Register(World, e, request.NetworkId, request.Key, body, request.IsLocal);
            em.AddComponentData(e, new PlayerBodyLink { Linked = 1 });
        }

        // GhostOwnerIsLocal settles a frame after the ghost appears, but the owner's NetworkId is already in the ghost: comparing it with
        // this world's own NetworkId decides at once, so the body is built in its right shape (a local ClientView keeps input and camera,
        // a remote one does not) instead of being rebuilt a frame later (M3a-2b step 7). Once settled both answers are the same.
        static bool IsOwnedHere(EntityManager em, Entity e, EntityQuery own)
        {
            if (em.HasComponent<GhostOwnerIsLocal>(e) && em.IsComponentEnabled<GhostOwnerIsLocal>(e)) return true;
            if (!em.HasComponent<GhostOwner>(e) || own.CalculateEntityCount() != 1) return false;
            int mine = own.GetSingleton<NetworkId>().Value;
            return mine != 0 && em.GetComponentData<GhostOwner>(e).NetworkId == mine;
        }

        void DestroyBody(GhostBodyEntry entry)
        {
            if (entry.Body == null) return;
            if (Factory != null) Factory.Destroy(entry.Body);
            else Object.Destroy(entry.Body);
        }

        protected override void OnDestroy()
        {
            // the world is going down with bodies still shown: take them away too
            foreach (var entry in GhostBodyRegistry.TakeWorld(World)) DestroyBody(entry);
        }
    }
}
