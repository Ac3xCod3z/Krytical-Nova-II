using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>The scene objects that are NGO's Strike Dummies (CombatEntity.IsStrikeDummy, never a player body), pure finding rules.</summary>
    public static class StrikeDummies
    {
        /// <summary>A Strike Dummy in the scene: standalone, not a Hostile, and not on any player body (a server body or a view).</summary>
        public static bool IsSceneDummy(CombatEntity entity) =>
            entity != null && entity.enabled && entity.gameObject.activeInHierarchy && entity.IsStrikeDummy && entity.GetComponentInParent<BodyRoleTag>(true) == null;

        /// <summary>Its capsule as the hit test sees it, scale included (NGO: the scene's CapsuleCollider; none = a player's 0.5 / 2 m).</summary>
        public static void CapsuleOf(CombatEntity entity, out int radiusMm, out int heightMm)
        {
            radiusMm = 500;
            heightMm = 2000;
            if (!entity.TryGetComponent(out CapsuleCollider capsule)) return;
            Vector3 s = entity.transform.lossyScale;
            float across = capsule.direction == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))
                : capsule.direction == 2 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y)) : Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            float along = capsule.direction == 0 ? Mathf.Abs(s.x) : capsule.direction == 2 ? Mathf.Abs(s.z) : Mathf.Abs(s.y);
            radiusMm = Mathf.RoundToInt(capsule.radius * across * 1000f);
            heightMm = Mathf.RoundToInt(capsule.height * along * 1000f);
        }

        public static int3 FeetMm(Vector3 position) =>
            new int3((int)math.round(position.x * 1000f), (int)math.round(position.y * 1000f), (int)math.round(position.z * 1000f));

        public static int Milli(float value) => Mathf.RoundToInt(value * 1000f);
    }

    /// <summary>
    /// Server: one Strike Dummy ghost for every Strike Dummy in the loaded scenes (M4.2b; ghosts-design.md:543-544), linked to the
    /// server's scene copy, which keeps deciding everything as NGO's server did (infinite HP refill, no death, its hits through
    /// TakeDamage). Each tick the ghost carries the copy's HP and place. A dummy whose scene
    /// goes away takes its ghost with it.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class StrikeDummyServerSystem : SystemBase
    {
        const int ScanEveryUpdates = 30;
        int m_Wait;

        /// <summary>Dummy ghosts spawned (tests).</summary>
        public int Spawned { get; private set; }

        protected override void OnCreate() => RequireForUpdate<StrikeDummyGhostPrefab>();

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (--m_Wait <= 0)
            {
                m_Wait = ScanEveryUpdates;
                Entity prefab = SystemAPI.GetSingleton<StrikeDummyGhostPrefab>().Prefab;
                foreach (CombatEntity dummy in Object.FindObjectsByType<CombatEntity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                    if (StrikeDummies.IsSceneDummy(dummy) && CombatTargetRegistry.Of(World, dummy.gameObject) == null) Spawn(em, prefab, dummy);
            }

            // Only the Strike Dummies' own entries and ghosts. The registry is shared with the Hostiles (HostileBodyLinkServerSystem): until
            // 8 Nov 2026 this system took every Hostile entry for a lost dummy and destroyed its ghost each frame (probe 7: 9,122 links,
            // 9,104 undone, no Hostile target), so monsters could not be clicked, struck by the basic attack or fight back.
            var gone = new List<Entity>();
            foreach (CombatTargetEntry entry in CombatTargetRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World)) continue;
                if (em.Exists(entry.Ghost) && em.HasComponent<CombatTarget>(entry.Ghost) &&
                    em.GetComponentData<CombatTarget>(entry.Ghost).Kind != CombatTargetKinds.StrikeDummy) continue;   // not ours
                if (!em.Exists(entry.Ghost) || !em.HasComponent<CombatTarget>(entry.Ghost)) { gone.Add(entry.Ghost); continue; }
                if (!entry.Body.TryGetComponent(out CombatEntity dummy) || !StrikeDummies.IsSceneDummy(dummy)) { gone.Add(entry.Ghost); continue; }
                CombatTarget target = em.GetComponentData<CombatTarget>(entry.Ghost);
                target.FeetMm = StrikeDummies.FeetMm(dummy.transform.position);
                target.HealthMilli = StrikeDummies.Milli(dummy.CurrentHealth);
                target.MaxHealthMilli = StrikeDummies.Milli(dummy.MaxHealth);
                em.SetComponentData(entry.Ghost, target);   // its place goes into the hit-test ring at the tick's end (PositionHistoryWriteSystem)
            }
            foreach (Entity ghost in gone)
            {
                CombatTargetRegistry.Unregister(World, ghost);
                if (em.Exists(ghost)) em.DestroyEntity(ghost);
            }
            // the registry drops the entries of an object destroyed with its scene: their ghosts go too
            using var all = GetEntityQuery(ComponentType.ReadOnly<CombatTarget>(), ComponentType.ReadOnly<GhostInstance>()).ToEntityArray(Allocator.Temp);
            foreach (Entity ghost in all)
                if (em.GetComponentData<CombatTarget>(ghost).Kind == CombatTargetKinds.StrikeDummy && !Linked(ghost)) em.DestroyEntity(ghost);
        }

        bool Linked(Entity ghost)
        {
            foreach (CombatTargetEntry entry in CombatTargetRegistry.Entries)
                if (ReferenceEquals(entry.World, World) && entry.Ghost == ghost) return true;
            return false;
        }

        void Spawn(EntityManager em, Entity prefab, CombatEntity dummy)
        {
            Entity ghost = em.Instantiate(prefab);
            Transform t = dummy.transform;
            if (em.HasComponent<LocalTransform>(ghost))
                em.SetComponentData(ghost, LocalTransform.FromPositionRotation(t.position, t.rotation));
            StrikeDummies.CapsuleOf(dummy, out int radius, out int height);
            em.SetComponentData(ghost, new CombatTarget
            {
                FeetMm = StrikeDummies.FeetMm(t.position),
                HealthMilli = StrikeDummies.Milli(dummy.CurrentHealth),
                MaxHealthMilli = StrikeDummies.Milli(dummy.MaxHealth),
                RadiusMm = radius,
                HeightMm = height,
                Kind = CombatTargetKinds.StrikeDummy,
            });
            CombatTargetRegistry.Register(World, ghost, dummy.gameObject);
            Spawned++;
        }
    }

    /// <summary>
    /// Client: each Strike Dummy ghost is linked to this machine's own copy of that dummy (the one standing where the ghost says, within
    /// 0.5 m), so a click on it attacks the ghost and hits on it show there; the copy takes the server's HP as NGO's clients took
    /// netStandaloneHealth (never the server's own copy in "Play DOTS here": the server decides that one).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class StrikeDummyClientSystem : SystemBase
    {
        const float LinkReach = 0.5f;
        EntityQuery m_Targets;

        /// <summary>Dummy ghosts linked to a scene copy (tests).</summary>
        public int Linked { get; private set; }

        protected override void OnCreate() =>
            m_Targets = GetEntityQuery(ComponentType.ReadOnly<CombatTarget>(), ComponentType.ReadOnly<GhostInstance>());

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (CombatTargetEntry entry in CombatTargetRegistry.Entries)
                if (ReferenceEquals(entry.World, World) && !em.Exists(entry.Ghost)) { CombatTargetRegistry.Unregister(World, entry.Ghost); break; }
            if (m_Targets.IsEmpty) return;
            using var ghosts = m_Targets.ToEntityArray(Allocator.Temp);
            using var targets = m_Targets.ToComponentDataArray<CombatTarget>(Allocator.Temp);
            CombatEntity[] scene = null;
            for (int i = 0; i < ghosts.Length; i++)
            {
                CombatTarget target = targets[i];
                if (target.Kind != CombatTargetKinds.StrikeDummy) continue;
                CombatEntity copy = CopyOf(ghosts[i]);
                if (copy == null)
                {
                    scene ??= Object.FindObjectsByType<CombatEntity>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    copy = Nearest(scene, (float3)target.FeetMm * 0.001f);
                    if (copy == null) continue;   // its scene is not loaded here yet: try again
                    CombatTargetRegistry.Register(World, ghosts[i], copy.gameObject);
                    Linked++;
                }
                if (target.MaxHealthMilli > 0 && !CombatTargetRegistry.IsServerCopy(copy.gameObject))
                    copy.MirrorSceneHealth(target.HealthMilli / 1000f, target.MaxHealthMilli / 1000f);
            }
        }

        CombatEntity CopyOf(Entity ghost)
        {
            foreach (CombatTargetEntry entry in CombatTargetRegistry.Entries)
                if (ReferenceEquals(entry.World, World) && entry.Ghost == ghost) return entry.Body.GetComponent<CombatEntity>();
            return null;
        }

        CombatEntity Nearest(CombatEntity[] scene, Vector3 feet)
        {
            CombatEntity best = null;
            float bestDistance = LinkReach;
            foreach (CombatEntity c in scene)
            {
                if (!StrikeDummies.IsSceneDummy(c) || CombatTargetRegistry.Of(World, c.gameObject) != null) continue;
                float d = Vector3.Distance(c.transform.position, feet);
                if (d <= bestDistance) { best = c; bestDistance = d; }
            }
            return best;
        }
    }
}
