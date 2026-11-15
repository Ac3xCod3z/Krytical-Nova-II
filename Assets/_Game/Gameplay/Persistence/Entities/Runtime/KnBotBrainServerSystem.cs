#if KN_DEV_BOTS || UNITY_EDITOR   // test lane only: a release build has no bot brain (KnBotBrainGuardTests)
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KryticalNova.Autopilot;
using KryticalNova.Hostiles;
using KryticalNova.Networking.Entities;
using KryticalNova.Player;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The test-lane bot brain (perf round 3, 15 Nov 2026): thin-client bots (<see cref="KnBotHost"/>) run no game code, and Autopilot is
    /// driven by the player's own client, so a bot on Autopilot stood still. On a server with development bots switched on
    /// (<see cref="KnDevBots.Enabled"/>: <c>-devbots</c> or server-settings.json, and only in a build with <c>KN_DEV_BOTS</c>), this
    /// system hunts for each bot whose Autopilot is on, through the requests a real client sends: the basic attack's start on the
    /// nearest Hostile of its zone (what the second click on a Hostile does: <see cref="MeleeRules.Begin"/>, then the server's own walk in
    /// and strikes), an attack skill every few seconds (a <see cref="SkillQueueRpc"/> from the bot's connection, as the hotbar sends),
    /// Autopilot switched on again after a death (an <see cref="AutopilotSetOnRpc"/>), a walk order back into the zone, and after a
    /// respawn in town the bot is put back in its zone. Real players are never touched: only accounts that joined with a bot token.
    /// Every 30 s it logs "[KnBotBrain]" totals and, every 60 s, a line per bot (metres walked, swings, strikes, casts, kills).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(PredictedSimulationSystemGroup))]
    public partial class KnBotBrainServerSystem : SystemBase
    {
        /// <summary>A pass every this many server ticks (0.2 s at 30 Hz).</summary>
        public const int PassTicks = 6;
        const float AutopilotAskSeconds = 3f;
        const float SummarySeconds = 30f;
        const float PerBotSeconds = 60f;

        sealed class Bot
        {
            public string Name;
            public int Number;
            public bool Enrolled;
            public bool HasHome;
            public int3 HomeMm;
            public int HomeRadiusMm;
            public bool HasLast;
            public int3 LastMm;
            public long WalkedMm;
            public int Swings, Strikes, Casts, SkillAsks, Kills, Teleports, AutopilotAsks, Attacks, WalksHome;
            public int LastTarget;
            public NetworkTick LastStepStart;
            public byte LastSwingHit;
            public float NextSkillAt;
            public float NextAutopilotAsk;
            public SkillDefinitionSO AskedSkill;
        }

        readonly Dictionary<Entity, Bot> m_Bots = new Dictionary<Entity, Bot>();
        readonly HashSet<Entity> m_NotBots = new HashSet<Entity>();
        readonly List<KnBotBrainRules.Candidate> m_Hostiles = new List<KnBotBrainRules.Candidate>();
        readonly Dictionary<int, int> m_HostileIndex = new Dictionary<int, int>();
        readonly Dictionary<int, int> m_Attackers = new Dictionary<int, int>();
        readonly Dictionary<int, Entity> m_Connections = new Dictionary<int, Entity>();
        readonly List<Entity> m_Gone = new List<Entity>();
        EntityQuery m_ConnectionQuery;
        NetworkTick m_LastPass;
        float m_NextSummary, m_NextPerBot;

        /// <summary>Totals over every bot this server has driven (tests and the soak's result file).</summary>
        public long WalkedMm { get; private set; }
        public int Attacks { get; private set; }
        public int SkillAsks { get; private set; }
        public int Teleports { get; private set; }
        public int BotsSeen => m_Bots.Count;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            m_ConnectionQuery = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
        }

        protected override void OnUpdate()
        {
            if (!KnDevBots.Enabled) return;
            NetworkTick now = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            if (!now.IsValid || (m_LastPass.IsValid && now.TicksSince(m_LastPass) < PassTicks)) return;
            m_LastPass = now;
            var em = EntityManager;
            float time = (float)SystemAPI.Time.ElapsedTime;

            if (!FindBots(em)) { Report(time); return; }
            GatherHostiles(em);
            GatherConnections();

            m_Attackers.Clear();
            foreach (var pair in m_Bots)
                if (em.Exists(pair.Key) && em.HasComponent<CombatState>(pair.Key))
                {
                    int t = em.GetComponentData<CombatState>(pair.Key).TargetGhostId;
                    if (t != 0) m_Attackers[t] = m_Attackers.TryGetValue(t, out int n) ? n + 1 : 1;
                }

            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !m_Bots.TryGetValue(entry.Ghost, out Bot bot)) continue;
                if (!em.Exists(entry.Ghost) || !em.HasComponent<CombatState>(entry.Ghost) || !em.HasComponent<PlayerPose>(entry.Ghost)) continue;
                Think(em, entry, bot, now, time);
            }
            Report(time);
        }

        /// <summary>Learns which bodies are bots (once each body has its character); false when there are none.</summary>
        bool FindBots(EntityManager em)
        {
            m_Gone.Clear();
            foreach (Entity e in m_Bots.Keys) if (!em.Exists(e)) m_Gone.Add(e);
            foreach (Entity e in m_Gone) m_Bots.Remove(e);
            m_NotBots.RemoveWhere(e => !em.Exists(e));
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || m_Bots.ContainsKey(entry.Ghost) || m_NotBots.Contains(entry.Ghost)) continue;
                if (!entry.Body.TryGetComponent(out PlayerIdentity identity) || !identity.HasIdentity) continue;   // asked again once admitted
                if (!KnDevBots.IsBotAccount(identity.AccountId)) { m_NotBots.Add(entry.Ghost); continue; }
                string name = identity.CharacterName;
                m_Bots[entry.Ghost] = new Bot { Name = name, Number = NumberOf(name), NextSkillAt = 0f };
            }
            return m_Bots.Count > 0;
        }

        void GatherHostiles(EntityManager em)
        {
            m_Hostiles.Clear();
            m_HostileIndex.Clear();
            foreach (var (ghost, target) in SystemAPI.Query<RefRO<GhostInstance>, RefRO<CombatTarget>>())
            {
                if (target.ValueRO.Kind != CombatTargetKinds.Hostile) continue;
                m_HostileIndex[ghost.ValueRO.ghostId] = m_Hostiles.Count;
                m_Hostiles.Add(new KnBotBrainRules.Candidate { GhostId = ghost.ValueRO.ghostId, FeetMm = target.ValueRO.FeetMm, Alive = target.ValueRO.Alive });
            }
        }

        void GatherConnections()
        {
            m_Connections.Clear();
            using var entities = m_ConnectionQuery.ToEntityArray(Allocator.Temp);
            using var ids = m_ConnectionQuery.ToComponentDataArray<NetworkId>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++) m_Connections[ids[i].Value] = entities[i];
        }

        void Think(EntityManager em, GhostBodyEntry entry, Bot bot, NetworkTick now, float time)
        {
            Entity ghost = entry.Ghost;
            int3 me = em.GetComponentData<PlayerPose>(ghost).Mm;
            if (bot.HasLast)
            {
                long walked = KnBotBrainRules.WalkedMm(bot.LastMm, me);
                bot.WalkedMm += walked;
                WalkedMm += walked;
            }
            bot.LastMm = me;
            bot.HasLast = true;

            CombatState combat = em.GetComponentData<CombatState>(ghost);
            CountSwings(bot, combat);

            bool alive = !em.HasComponent<PlayerVitals>(ghost) || em.GetComponentData<PlayerVitals>(ghost).MaxHealthMilli == 0
                         || em.GetComponentData<PlayerVitals>(ghost).HealthMilli > 0;
            var autopilot = entry.Body.GetComponent<PlayerAutopilot>();
            bool autopilotOn = autopilot != null && autopilot.IsOn;
            if (autopilotOn && !bot.Enrolled)
            {
                // the soak's equip switched Autopilot on inside a Hostile Zone: that zone is this bot's home from now on
                HostileZone zone = HostileZone.At(entry.Body.transform.position);
                if (zone != null)
                {
                    bot.Enrolled = true;
                    bot.HasHome = true;
                    bot.HomeMm = (int3)math.round((float3)zone.Centre * 1000f);
                    bot.HomeRadiusMm = math.max(1000, (int)math.round(zone.Radius * 1000f));
                }
            }

            // the Hostile this bot was on last pass died: a kill it took part in (the server's own melee check may already have let go)
            if (bot.LastTarget != 0 && m_HostileIndex.TryGetValue(bot.LastTarget, out int last) && !m_Hostiles[last].Alive) bot.Kills++;
            // the target died or left the zone: let go (as a client's Autopilot moves on)
            bool targetValid = false;
            if (combat.TargetGhostId != 0 && m_HostileIndex.TryGetValue(combat.TargetGhostId, out int index))
            {
                KnBotBrainRules.Candidate t = m_Hostiles[index];
                targetValid = t.Alive && bot.HasHome && KnBotBrainRules.FlatDistanceSq(t.FeetMm, bot.HomeMm) <= (long)bot.HomeRadiusMm * bot.HomeRadiusMm;
            }
            if (combat.TargetGhostId != 0 && !targetValid && bot.Enrolled)
            {
                MeleeRules.Cancel(ref combat);
                em.SetComponentData(ghost, combat);
            }
            bot.LastTarget = targetValid ? combat.TargetGhostId : 0;

            bool walking = (em.HasComponent<MoveState>(ghost) && (em.GetComponentData<MoveState>(ghost).Flags & MoveFlags.Moving) != 0)
                           || (em.HasComponent<ServerOrderRequest>(ghost) && em.IsComponentEnabled<ServerOrderRequest>(ghost));
            var view = new KnBotBrainRules.View
            {
                Alive = alive,
                Enrolled = bot.Enrolled,
                AutopilotOn = autopilotOn,
                HasHome = bot.HasHome,
                MeMm = me,
                HomeMm = bot.HomeMm,
                HomeRadiusMm = bot.HomeRadiusMm,
                CurrentTargetGhostId = targetValid ? combat.TargetGhostId : 0,
                CurrentTargetValid = targetValid,
                Walking = walking,
                MayAskAutopilot = time >= bot.NextAutopilotAsk,
            };
            KnBotBrainRules.Plan plan = KnBotBrainRules.Decide(view, m_Hostiles, m_Attackers);
            switch (plan.Act)
            {
                case KnBotBrainRules.Act.Attack:
                    MeleeRules.Begin(ref combat, plan.TargetGhostId);
                    em.SetComponentData(ghost, combat);
                    m_Attackers[plan.TargetGhostId] = m_Attackers.TryGetValue(plan.TargetGhostId, out int n) ? n + 1 : 1;
                    bot.LastTarget = plan.TargetGhostId;
                    bot.Attacks++;
                    Attacks++;
                    break;
                case KnBotBrainRules.Act.WalkHome:
                    PlayerEntitiesSetup.IssueOrder(em, ghost, bot.HomeMm);
                    bot.WalksHome++;
                    break;
                case KnBotBrainRules.Act.TeleportHome:
                    MeleeRules.Cancel(ref combat);
                    em.SetComponentData(ghost, combat);
                    PlayerEntitiesSetup.Teleport(em, ghost, bot.HomeMm);
                    bot.Teleports++;
                    Teleports++;
                    break;
                case KnBotBrainRules.Act.AutopilotOn:
                    if (SendFrom(em, entry.NetworkId, new AutopilotSetOnRpc { On = 1 })) bot.AutopilotAsks++;
                    bot.NextAutopilotAsk = time + AutopilotAskSeconds;
                    break;
            }

            // an attack skill now and then while fighting, by the hotbar's request (the server decides range, Flux and cooldown)
            if (bot.AskedSkill != null && entry.Body.TryGetComponent(out PlayerCombatSkills asked) && asked.IsOnCooldown(bot.AskedSkill))
            {
                bot.Casts++;
                bot.AskedSkill = null;
            }
            if (autopilotOn && alive && bot.LastTarget != 0 && time >= bot.NextSkillAt && entry.Body.TryGetComponent(out PlayerCombatSkills skills))
            {
                SkillDefinitionSO skill = ReadySkill(skills, bot);
                bot.NextSkillAt = time + KnBotBrainRules.SkillGapSeconds(bot.Number, bot.SkillAsks);
                if (skill != null && SendFrom(em, entry.NetworkId, new SkillQueueRpc
                    {
                        Op = SkillQueueRpc.Begin,
                        SkillId = new FixedString64Bytes(skill.skillId),
                        TargetGhostId = skill.IsAreaAttack ? 0 : bot.LastTarget,
                    }))
                {
                    bot.AskedSkill = skill;
                    bot.SkillAsks++;
                    SkillAsks++;
                }
            }
        }

        /// <summary>A swing is a new combo step; a strike is a step whose hit came due (one per swing, landed or not).</summary>
        static void CountSwings(Bot bot, in CombatState combat)
        {
            if (combat.Step > 0 && combat.StepStartTick.IsValid && !combat.StepStartTick.Equals(bot.LastStepStart))
            {
                bot.Swings++;
                bot.LastStepStart = combat.StepStartTick;
                bot.LastSwingHit = 0;
            }
            if (combat.SwingHit != 0 && bot.LastSwingHit == 0 && combat.Step > 0)
            {
                bot.Strikes++;
                bot.LastSwingHit = 1;
            }
        }

        static SkillDefinitionSO ReadySkill(PlayerCombatSkills skills, Bot bot)
        {
            IReadOnlyList<SkillDefinitionSO> all = skills.AvailableSkills;
            if (all == null || all.Count == 0) return null;
            // start the search at a different skill each time, so a bot uses all its attacks
            for (int k = 0; k < all.Count; k++)
            {
                SkillDefinitionSO s = all[(bot.SkillAsks + bot.Number + k) % all.Count];
                if (s != null && s.IsAttack && skills.IsLearned(s) && !skills.IsLocked(s) && !skills.IsOnCooldown(s)) return s;
            }
            return null;
        }

        /// <summary>Queues <paramref name="rpc"/> as if it had arrived from the bot's own connection (the request path a client uses).</summary>
        bool SendFrom<T>(EntityManager em, int networkId, T rpc) where T : unmanaged, IRpcCommand
        {
            if (!m_Connections.TryGetValue(networkId, out Entity connection) || !em.Exists(connection)) return false;
            Entity e = em.CreateEntity();
            em.AddComponentData(e, rpc);
            em.AddComponentData(e, new ReceiveRpcCommandRequest { SourceConnection = connection });
            return true;
        }

        static int NumberOf(string name)
        {
            int n = 0;
            if (string.IsNullOrEmpty(name)) return 0;
            foreach (char c in name) if (c >= '0' && c <= '9') n = n * 10 + (c - '0');
            return n;
        }

        void Report(float time)
        {
            if (m_Bots.Count == 0) return;
            if (time >= m_NextSummary)
            {
                m_NextSummary = time + SummarySeconds;
                int enrolled = 0, moved = 0, fought = 0;
                foreach (Bot b in m_Bots.Values)
                {
                    if (b.Enrolled) enrolled++;
                    if (b.WalkedMm >= 10_000) moved++;
                    if (b.Strikes > 0) fought++;
                }
                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "[KnBotBrain] t={0:0}s bots={1} hunting={2} walked10m={3} fought={4} walked={5:0} m attacks={6} skillAsks={7} teleports={8}",
                    time, m_Bots.Count, enrolled, moved, fought, WalkedMm / 1000.0, Attacks, SkillAsks, Teleports));
            }
            if (time >= m_NextPerBot)
            {
                m_NextPerBot = time + PerBotSeconds;
                var line = new StringBuilder();
                foreach (Bot b in m_Bots.Values)
                {
                    line.Clear();
                    line.AppendFormat(CultureInfo.InvariantCulture,
                        "[KnBotBrain] bot {0}: walked {1:0} m, swings {2}, strikes {3}, attacks {4}, skill asks {5}, casts {6}, kills {7}, teleports {8}, autopilot asks {9}, walks home {10}, hunting {11}",
                        b.Name, b.WalkedMm / 1000.0, b.Swings, b.Strikes, b.Attacks, b.SkillAsks, b.Casts, b.Kills, b.Teleports, b.AutopilotAsks, b.WalksHome, b.Enrolled);
                    Debug.Log(line.ToString());
                }
            }
        }
    }
}
#endif
