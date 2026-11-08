using System.Collections.Generic;
using KryticalNova.Brains;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// Server: gives every Hostile server body (a <see cref="HostileNet"/> with an <see cref="EcsHostileBrain"/>, spawned by its zone as
    /// in NGO, M5.5c table "M5.5d") its ghost with the brain on it, before the brain tick (M5.5d, plan s4): the ghost from
    /// <see cref="HostileGhostPrefab"/>, the brain's components (asleep until <c>HostileNet</c> wakes it), the zone's centre, radius and
    /// leash in <see cref="HostileHome"/>, then <see cref="EcsHostileBrain.Bind"/>; registered in <see cref="CombatTargetRegistry"/> so
    /// the players' strikes find it as they find the Strike Dummy. A body back in the pool loses its ghost. Managed bridge (bodies are
    /// GameObjects): it touches only new and gone bodies.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(BrainSimulationGroup))]
    public partial class HostileBodyLinkServerSystem : SystemBase
    {
        readonly Dictionary<EcsHostileBrain, Entity> m_Linked = new();
        readonly List<EcsHostileBrain> m_Gone = new();
        uint m_NextStableId = 1;

        /// <summary>Every linked body and its ghost (read by <see cref="HostileBodySyncServerSystem"/>).</summary>
        public IReadOnlyDictionary<EcsHostileBrain, Entity> Linked => m_Linked;

        /// <summary>Diagnostics (probe 6, 8 Nov 2026: bodies stood on the server, no Hostile target): updates run, links made, links undone.</summary>
        public int Updates { get; private set; }
        public int Links { get; private set; }
        public int Unlinks { get; private set; }

        protected override void OnCreate()
        {
            RequireForUpdate<HostileGhostPrefab>();
            RequireForUpdate<BrainConfig>();
        }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            Updates++;
            foreach (KeyValuePair<EcsHostileBrain, Entity> pair in m_Linked)
                if (pair.Key == null || !pair.Key.isActiveAndEnabled || !em.Exists(pair.Value)) m_Gone.Add(pair.Key);
            foreach (EcsHostileBrain body in m_Gone)
            {
                Entity ghost = m_Linked[body];
                CombatTargetRegistry.Unregister(World, ghost);
                if (em.Exists(ghost)) em.DestroyEntity(ghost);
                if (body != null) body.Unbind();
                m_Linked.Remove(body);
                Unlinks++;
            }
            m_Gone.Clear();

            Entity prefab = SystemAPI.GetSingleton<HostileGhostPrefab>().Prefab;
            BrainConfig config = SystemAPI.GetSingleton<BrainConfig>();
            foreach (HostileNet hostile in HostileNet.Active)
            {
                if (hostile == null || hostile.IsView || !hostile.TryGetComponent(out EcsHostileBrain brain) || m_Linked.ContainsKey(brain)) continue;
                m_Linked[brain] = Link(em, prefab, config, hostile, brain);
                Links++;
            }
        }

        Entity Link(EntityManager em, Entity prefab, in BrainConfig config, HostileNet hostile, EcsHostileBrain brain)
        {
            Entity e = em.Instantiate(prefab);
            Transform t = hostile.transform;
            em.AddComponent(e, new ComponentTypeSet(new ComponentType[]
            {
                typeof(BrainTag), typeof(BrainActive), typeof(BrainClock), typeof(BrainDice), typeof(BrainMover), typeof(BrainPath),
                typeof(HostileBrain), typeof(HostileTarget), typeof(HostileLife), typeof(HostileHome), typeof(HostileGhostSource), typeof(HostileSlot),
            }));
            em.AddBuffer<BrainEvent>(e);
            em.AddBuffer<BrainWaypoint>(e);
            em.AddBuffer<ThreatEntry>(e);
            em.AddBuffer<HostileAbilityRow>(e);
            em.SetComponentEnabled<BrainActive>(e, false);   // asleep until HostileNet wakes it (SetAwake)
            em.SetComponentData(e, LocalTransform.FromPositionRotation(t.position, t.rotation));
            uint id = m_NextStableId++;
            em.SetComponentData(e, new BrainClock { StableId = id });
            em.SetComponentData(e, new BrainDice { Rng = BrainDiceRules.Create(id, config.WorldSeed) });
            em.SetComponentData(e, new HostileGhostSource { LastPosition = t.position });
            HostileZone zone = hostile.Zone;
            em.SetComponentData(e, new HostileHome
            {
                Home = t.position,
                WanderRadius = 8f,   // HostileNet's SetHome (replayed on Bind) gives the settings' own
                ZoneCentre = zone != null ? new float2(zone.Centre.x, zone.Centre.z) : float2.zero,
                ZoneRadius = zone != null ? zone.Radius : 0f,
                LeashRadius = zone != null ? zone.LeashMetres : 0f,
            });
            em.SetComponentData(e, new HostileLife { BodyKey = (int)id });   // the brain's stable id stands for the body
            em.SetComponentData(e, new CombatTarget { Kind = CombatTargetKinds.Hostile, FeetMm = FeetMm(t.position) });
            CombatTargetRegistry.Register(World, e, hostile.gameObject);
            brain.Bind(World, e);
            return e;
        }

        public static int3 FeetMm(Vector3 p) => new int3((int)math.round(p.x * 1000f), (int)math.round(p.y * 1000f), (int)math.round(p.z * 1000f));
    }

    /// <summary>
    /// Server, after the brain tick: each linked body stands where its brain moved it, and its ghost shows the body's HP and size (the
    /// CombatEntity holds the HP, as with Emerald). Managed bridge: job later (perf phase) once bodies are no GameObjects.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BrainSimulationGroup))]
    public partial class HostileBodySyncServerSystem : SystemBase
    {
        HostileBodyLinkServerSystem m_Link;

        protected override void OnCreate() => m_Link = World.GetOrCreateSystemManaged<HostileBodyLinkServerSystem>();

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (KeyValuePair<EcsHostileBrain, Entity> pair in m_Link.Linked)
            {
                EcsHostileBrain body = pair.Key;
                Entity e = pair.Value;
                if (body == null || !em.Exists(e)) continue;
                LocalTransform t = em.GetComponentData<LocalTransform>(e);
                body.transform.SetPositionAndRotation(t.Position, t.Rotation);
                HostileLife life = em.GetComponentData<HostileLife>(e);
                CombatTarget target = em.GetComponentData<CombatTarget>(e);
                var entity = body.GetComponent<Progression.CombatEntity>();
                target.FeetMm = HostileBodyLinkServerSystem.FeetMm(t.Position);
                target.HealthMilli = entity != null ? Mathf.RoundToInt(entity.CurrentHealth * 1000f) : 0;
                target.MaxHealthMilli = entity != null ? Mathf.RoundToInt(entity.MaxHealth * 1000f) : 0;
                target.RadiusMm = life.RadiusMm;
                target.HeightMm = life.HeightMm;
                em.SetComponentData(e, target);
            }
        }
    }
}
