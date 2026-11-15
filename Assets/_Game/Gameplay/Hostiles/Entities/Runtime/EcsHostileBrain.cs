using System;
using KryticalNova.Brains;
using KryticalNova.Core;
using KryticalNova.Progression;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    using World = Unity.Entities.World;   // inside the namespace: KryticalNova.World (a namespace) would win otherwise

    /// <summary>
    /// The entity brain behind a Hostile's hidden server body (M5.5c): <see cref="HostileNet"/> drives it through
    /// <see cref="IHostileBrain"/> exactly as it drives Emerald through HostileBrainLink. Every call maps to the brain entity's
    /// components or a <see cref="BrainEvent"/>; the table with NGO's file:line is <c>m5-rules-tables/M5.5c-ecs-hostile-brain.md</c>.
    /// The body link (M5.5d) calls <see cref="Bind"/>. Managed bridge: it runs only on the rare calls and on hits, never per tick.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EcsHostileBrain : MonoBehaviour, IHostileBrain
    {
        /// <summary>The server body of a player ghost (filled at server install, M5.5d), for <see cref="Target"/>.</summary>
        public static Func<Entity, Transform> BodyOfGhost;
        /// <summary>The ghost id of a player's server body (filled at server install), for hits and <see cref="Aggro"/>.</summary>
        public static Func<Transform, int> GhostIdOfBody;

        World m_World;
        Entity m_Brain;
        CombatEntity m_Entity;
        bool m_Awake, m_Frozen, m_Listening;
        // HostileNet configures its brain in Start, which can come before the link binds it: kept and given on Bind
        HostileDefinition m_Species;
        HostileStats m_Stats;
        bool m_HasHome;
        Vector3 m_Home;
        int m_Wander;

        public Entity Brain => m_Brain;
        bool Bound => m_World != null && m_World.IsCreated && m_World.EntityManager.Exists(m_Brain);

        /// <summary>Ties this body to its brain entity in the server world (M5.5d's link).</summary>
        public void Bind(World world, Entity brain)
        {
            m_World = world;
            m_Brain = brain;
            m_Entity = GetComponent<CombatEntity>();
            if (m_Species != null) Configure(m_Species, m_Stats);
            if (m_HasHome) SetHome(m_Home, m_Wander);
            Apply();
        }

        /// <summary>Lets go of the brain entity (the body went back to the pool).</summary>
        public void Unbind()
        {
            Listen(false);
            m_World = null;
            m_Brain = Entity.Null;
        }

        void OnDisable() => Listen(false);

        // ------------------------------------------------------------------ IHostileBrain

        public void SetAwake(bool awake)
        {
            bool sleeps = m_Awake && !awake;
            m_Awake = awake;
            if (sleeps) EndFight();
            Apply();
        }

        /// <summary>
        /// Falling asleep ends the fight at once (M6.2, H15): NGO's HostileNet.SetAsleep calls Calm before it stops Emerald, and Emerald's
        /// Calm clears at once; the entity brain's Calm is an event a sleeping brain would never read. It wakes fresh where it stands.
        /// </summary>
        void EndFight()
        {
            if (!Bound) return;
            EntityManager em = m_World.EntityManager;
            em.SetComponentData(m_Brain, default(HostileTarget));
            em.GetBuffer<ThreatEntry>(m_Brain).Clear();
            HostileBrain b = em.GetComponentData<HostileBrain>(m_Brain);
            if (b.Mode == HostileMode.Dead) return;
            b.Mode = HostileMode.Idle;
            b.Phase = HostilePhase.Approach;
            b.Saw = 0;
            b.HealOnHome = 0;
            b.UnreachableSinceTick = 0;
            em.SetComponentData(m_Brain, b);
        }

        public void SetHome(Vector3 home, int wanderRadius)
        {
            m_HasHome = true; m_Home = home; m_Wander = wanderRadius;
            if (!Bound) return;
            EntityManager em = m_World.EntityManager;
            HostileHome h = em.GetComponentData<HostileHome>(m_Brain);
            h.Home = home;
            h.WanderRadius = Mathf.Max(1, wanderRadius);
            em.SetComponentData(m_Brain, h);
        }

        public void Configure(HostileDefinition species, in HostileStats stats)
        {
            m_Species = species; m_Stats = stats;
            if (!Bound || species == null) return;
            EntityManager em = m_World.EntityManager;
            int tickRate = TickRate(em);
            float noticeDefault = TryConfig(em, out HostileBrainConfig config) ? config.NoticeSecondsDefault : 0.1f;
            float scale = Mathf.Max(0.1f, species.scale);
            em.SetComponentData(m_Brain, new HostileLife
            {
                BodyKey = em.GetComponentData<HostileLife>(m_Brain).BodyKey,
                Species = species.id,
                Level = (short)stats.Level,
                Tier = (byte)stats.Tier,
                AggroRadius = Mathf.Max(1, Mathf.CeilToInt(stats.AggroRadius)),   // H01: Emerald's detection radius (HostileBrainLink.cs:109)
                Walk = stats.WalkSpeed,
                Run = stats.RunSpeed,
                AttackRange = species.attackRange,
                ReachAbove = species.reachAbove,
                RadiusMm = Mathf.RoundToInt(species.bodySize.x * scale * 1000f),
                HeightMm = Mathf.RoundToInt(species.bodySize.y * scale * 1000f),
                // the GM's Prowler stand-in (M5.5): a FightsBack species made to attack on sight (HostileNet.GmAttacksOnSight)
                Behaviour = species.behaviour == HostileBehaviour.AttacksOnSight || (TryGetComponent(out HostileNet net) && net.GmAttacksOnSight)
                    ? HostileBehaviourKind.AttacksOnSight : HostileBehaviourKind.FightsBack,
                Poise = (byte)Mathf.Clamp(species.poise, 0, 100),   // M6.1: the species' own (normal 30, Bruisers 70)
                NoticeTicks = HostileRules.NoticeTicks(species.noticeSeconds, noticeDefault, tickRate),
                Profile = (byte)species.scoreProfile,
                Assist = (byte)(species.assist ? 1 : 0),
                Flees = (byte)(species.flees ? 1 : 0),
            });
            // the species bake (M5.3b2d): one row per attack of the species
            DynamicBuffer<HostileAbilityRow> rows = em.GetBuffer<HostileAbilityRow>(m_Brain);
            rows.Clear();
            foreach (HostileDefinition.Attack attack in species.attacks)
            {
                float length = attack.clip != null ? attack.clip.length : attack.windupSeconds;
                rows.Add(new HostileAbilityRow
                {
                    WindupTicks = (uint)math.max(1, (int)math.ceil(attack.windupSeconds * tickRate)),
                    RecoverTicks = (uint)math.max(0, (int)math.ceil((length - attack.windupSeconds) * tickRate)),
                    Range = species.attackRange,
                    Weight = attack.weight,
                    Heavy = (byte)(attack.heavy ? 1 : 0),
                    Warning = (byte)attack.warning,
                    WarningTicks = (uint)math.max(0, (int)math.ceil(attack.warningSeconds * tickRate - 1e-4f)),
                });
            }
        }

        public bool Frozen
        {
            get => m_Frozen;
            set { m_Frozen = value; Apply(); }
        }

        public Transform Target
        {
            get
            {
                if (!Bound || BodyOfGhost == null) return null;
                HostileTarget t = m_World.EntityManager.GetComponentData<HostileTarget>(m_Brain);
                return t.GhostId != 0 && t.Ghost != Entity.Null ? BodyOfGhost(t.Ghost) : null;
            }
        }

        public void Aggro(Transform target)
        {
            if (!m_Awake || target == null || (m_Entity != null && !m_Entity.IsAlive)) return;
            int ghostId = GhostIdOfBody != null ? GhostIdOfBody(target) : 0;
            if (ghostId != 0) Event(BrainEventKind.Aggro, ghostId, 0f);
        }

        public void Calm()
        {
            if (m_Awake) Event(BrainEventKind.Calm, 0, 0f);
        }

        public string Describe()
        {
            if (!Bound) return "no brain";
            if (!m_Awake) return "asleep";
            if (m_Entity != null && !m_Entity.IsAlive) return "dead";
            byte mode = m_World.EntityManager.GetComponentData<HostileBrain>(m_Brain).Mode;
            string state = m_Frozen ? "frozen" : mode == HostileMode.Engage ? "in combat" : "wandering";
            Transform target = Target;
            return target != null ? $"{state}, target {target.name}" : state;
        }

        // ------------------------------------------------------------------ the game tells the brain

        void Apply()
        {
            if (!Bound) return;
            m_World.EntityManager.SetComponentEnabled<BrainActive>(m_Brain, m_Awake && !m_Frozen);
            Listen(m_Awake);
        }

        void Listen(bool on)
        {
            if (on == m_Listening || m_Entity == null) return;
            m_Listening = on;
            if (on) { m_Entity.Damaged += OnDamaged; m_Entity.Killed += OnKilled; }
            else { m_Entity.Damaged -= OnDamaged; m_Entity.Killed -= OnKilled; }
        }

        void OnDamaged(DamageResult result, CharacterCombatStats attacker)
        {
            // as HostileBrainLink.OnDamaged: an evaded or reflected swing, no damage, or a dead Hostile tells the brain nothing
            if (!m_Awake || result.IsEvaded || result.IsReflected || m_Entity == null || !m_Entity.IsAlive) return;
            float amount = Mathf.Round(result.DamageDealt);
            if (amount <= 0f) return;
            if (result.NoThreat) return;   // a Mount Companion's strike (item 19 phase 3): no threat, so it never draws aggro
            int ghostId = attacker != null && GhostIdOfBody != null ? GhostIdOfBody(attacker.transform) : 0;
            Event(BrainEventKind.Damaged, ghostId, amount);
        }

        void OnKilled(KillingBlow blow) => Event(BrainEventKind.Killed, 0, 0f);

        void Event(byte kind, int ghostId, float amount)
        {
            if (!Bound) return;
            EntityManager em = m_World.EntityManager;
            // the brains read events stamped with the tick they run on: the next brain tick
            uint next = TryTick(em, out BrainTick tick) ? tick.Tick + 1 : 0;
            em.GetBuffer<BrainEvent>(m_Brain).Add(new BrainEvent { Kind = kind, GhostId = ghostId, Amount = amount, Tick = next });
        }

        static int TickRate(EntityManager em)
        {
            using var q = em.CreateEntityQuery(typeof(BrainConfig));
            return q.TryGetSingleton(out BrainConfig c) && c.TickSeconds > 0f ? Mathf.RoundToInt(1f / c.TickSeconds) : 30;
        }

        static bool TryConfig(EntityManager em, out HostileBrainConfig config)
        {
            using var q = em.CreateEntityQuery(typeof(HostileBrainConfig));
            return q.TryGetSingleton(out config);
        }

        static bool TryTick(EntityManager em, out BrainTick tick)
        {
            using var q = em.CreateEntityQuery(typeof(BrainTick));
            return q.TryGetSingleton(out tick);
        }
    }
}
