using System.Collections.Generic;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// Client (M5.5e, plan s4): the Hostiles a player sees. The nearest <see cref="ViewBudget"/> Hostile ghosts within
    /// <see cref="ViewRadius"/> of the camera each get a pooled body of their species (today's NGO prefab, from the NetworkManager's
    /// list as <see cref="HostileSpawner.Species"/>) made a view with <see cref="HostileNet.BindView"/>; it follows the ghost's
    /// interpolated place and plays what the ghost says, through the same view calls NGO's RPCs and network variables used
    /// (M5.0 H21): the lasting state, a new attack, a flinch, the level. A view is registered as the ghost's client copy in
    /// <see cref="CombatTargetRegistry"/> so the player can click and strike it. Managed bridge: job later (perf phase) for the pick.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class HostileViewClientSystem : SystemBase
    {
        public const int ViewBudget = 32;
        public const float ViewRadius = 45f;

        sealed class View
        {
            public HostileNet Body;
            public HostileGhost Shown;
        }

        readonly Dictionary<Entity, View> m_Views = new();
        readonly Dictionary<ushort, HostileNet> m_SpeciesById = new();
        readonly List<(float distSq, Entity ghost)> m_Near = new();
        readonly HashSet<Entity> m_Wanted = new();
        readonly List<Entity> m_Drop = new();
        EntityQuery m_Ghosts;

        protected override void OnCreate() =>
            m_Ghosts = GetEntityQuery(ComponentType.ReadOnly<HostileGhost>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<GhostInstance>());

        protected override void OnDestroy()
        {
            foreach (KeyValuePair<Entity, View> pair in m_Views) Release(pair.Key, pair.Value);
            m_Views.Clear();
        }

        protected override void OnUpdate()
        {
            Camera eye = Camera.main;
            if (eye == null) return;
            Vector3 from = eye.transform.position;
            EntityManager em = EntityManager;

            // the nearest ghosts within the radius, up to the budget
            m_Near.Clear();
            using (NativeArray<Entity> ghosts = m_Ghosts.ToEntityArray(Allocator.Temp))
                foreach (Entity g in ghosts)
                {
                    float d = math.distancesq(em.GetComponentData<LocalTransform>(g).Position, (float3)from);
                    if (d <= ViewRadius * ViewRadius) m_Near.Add((d, g));
                }
            m_Near.Sort((a, b) => a.distSq.CompareTo(b.distSq));
            m_Wanted.Clear();
            for (int i = 0; i < m_Near.Count && i < ViewBudget; i++) m_Wanted.Add(m_Near[i].ghost);

            // views out of range, over budget or gone go back to the pool
            m_Drop.Clear();
            foreach (KeyValuePair<Entity, View> pair in m_Views)
                if (!m_Wanted.Contains(pair.Key) || !em.Exists(pair.Key) || pair.Value.Body == null) m_Drop.Add(pair.Key);
            foreach (Entity g in m_Drop) { Release(g, m_Views[g]); m_Views.Remove(g); }

            foreach (Entity g in m_Wanted)
            {
                HostileGhost ghost = em.GetComponentData<HostileGhost>(g);
                LocalTransform place = em.GetComponentData<LocalTransform>(g);
                if (!m_Views.TryGetValue(g, out View view))
                {
                    HostileNet prefab = SpeciesOf(ghost.Species);
                    if (prefab == null) continue;
                    HostileNet body = HostilePool.TakeView(prefab, place.Position, place.Rotation);
                    body.BindView(ghost.Level);
                    body.ShowStateFromGhost(StateOf(ghost), joining: true);
                    view = new View { Body = body, Shown = ghost };
                    m_Views[g] = view;
                    CombatTargetRegistry.Register(World, g, body.gameObject);
                }
                view.Body.transform.SetPositionAndRotation(place.Position, place.Rotation);
                Show(view, ghost);
            }
        }

        static HostileBrainSettings s_Brain;
        static bool s_BrainLooked;

        /// <summary>
        /// How long before the hit the client shows attack <paramref name="attackIndex"/> of <paramref name="body"/> coming: the attack's own
        /// warning or the brain settings' minimum, plus the latency (owner, 7 Nov 2026); settings read once from the world assets.
        /// </summary>
        static float TellSeconds(HostileNet body, int attackIndex)
        {
            if (!s_BrainLooked)
            {
                s_BrainLooked = true;
                var assets = UnityEngine.Resources.Load<HostileWorldAssets>(HostileWorldAssets.ResourcePath);
                s_Brain = assets != null ? assets.hostiles : null;
            }
            HostileDefinition species = body != null ? body.Definition : null;
            if (s_Brain == null || species == null || attackIndex < 0 || attackIndex >= species.attacks.Length) return 0.5f;
            return s_Brain.WarningLeadSeconds(species.attacks[attackIndex]);
        }

        static void Show(View view, in HostileGhost now)
        {
            HostileGhost was = view.Shown;
            if (now.Level != was.Level) view.Body.ShowLevelFromGhost(now.Level);
            if (now.SpeedSteps != was.SpeedSteps || (now.Flags & 7) != (was.Flags & 7) || now.DeathIndex != was.DeathIndex)
                view.Body.ShowStateFromGhost(StateOf(now));
            // Q3 A: the tell starts the swing early on screen (HostileNet slows the clip so its hit frame lands on time)
            if ((now.Flags & HostileGhostFlags.Tell) != 0 && (was.Flags & HostileGhostFlags.Tell) == 0) view.Body.ShowTellFromGhost(now.AttackIndex, TellSeconds(view.Body, now.AttackIndex));
            // job later: start the clip at (interpolation tick - AttackStartTick) / tick rate into it (plan s4); today at once
            if (now.AttackStartTick != was.AttackStartTick && now.AttackStartTick != 0) view.Body.ShowAttackFromGhost(now.AttackIndex);
            if (now.HitCounter != was.HitCounter) view.Body.ShowHitFromGhost(1);
            view.Shown = now;
        }

        /// <summary>The ghost's lasting state as NGO's network variable carried it (the same bits and steps).</summary>
        public static HostileAnimState StateOf(in HostileGhost g) =>
            new HostileAnimState { SpeedSteps = g.SpeedSteps, Flags = (byte)(g.Flags & 7), DeathIndex = g.DeathIndex };

        HostileNet SpeciesOf(ushort id)
        {
            if (m_SpeciesById.Count == 0)
                foreach (HostileNet species in HostileSpawner.Species()) m_SpeciesById[species.Definition.id] = species;
            m_SpeciesById.TryGetValue(id, out HostileNet prefab);
            return prefab;
        }

        void Release(Entity ghost, View view)
        {
            CombatTargetRegistry.Unregister(World, ghost);
            if (view.Body != null) HostilePool.Return(view.Body);
        }
    }
}
