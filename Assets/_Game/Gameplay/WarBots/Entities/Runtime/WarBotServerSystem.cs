using System;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.Crossfire;
using KryticalNova.Crossfire.Entities;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Mounts;
using KryticalNova.Networking.Entities;
using KryticalNova.Persistence.Entities;
using KryticalNova.Player;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Bodies;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using KryticalNova.Social;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.WarBots.Entities
{
    /// <summary>
    /// The War Bots (owner, 16 Nov 2026; CROSSFIRE-WARBOTS-BRIEF Part B): players the server runs itself to fill a short side of a
    /// Crossfire war after its vote. A bot is a whole player inside the server: the same body, ghost, combat, Stims, buffs, squad,
    /// chat, emotes and mount as a person, admitted through the game's own admission with a server-minted key
    /// (<see cref="PlayerKeys.MintServerBot"/>) and no connection, account or save; nothing it does is written anywhere. Crossfire
    /// decides when and how many (<see cref="IWarBotFill"/>); this system decides how each one plays (<see cref="WarBotRules"/>,
    /// numbers in <see cref="WarBotSettings"/>). Each bot thinks every <see cref="WarBotSettings.thinkSeconds"/>, the bots spread over
    /// the ticks, so a full war costs the same each frame.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(PredictedSimulationSystemGroup))]
    public partial class WarBotServerSystem : SystemBase, IWarBotFill
    {
        sealed class Bot
        {
            public uint Number;
            public PlayerKey Key;
            public string Name;
            public FactionId Side;
            public Entity Ghost;
            public int GhostId;
            public GameObject Body;
            public WarBotProfile Profile;
            public System.Random Rng;
            public Vector3 Rally;
            public float RallyYaw;
            // parts of the body, found once
            public PlayerCombatSkills Skills;
            public CharacterBuffManager Buffs;
            public PlayerInventory Inventory;
            public PlayerParty Party;
            public PlayerChat Chat;
            public PlayerEmotes Emotes;
            public PlayerMounts Mounts;
            public PlayerCadence Cadence;
            public Action<BodyChatLine> Heard;
            // the fight
            public double NextStimAt;
            public double NoBuffUntil;
            public double NextThink, ReactAt, NextSkillAt, LastSaid = double.MinValue, LastEmote = double.MinValue, NextPartyTry;
            public WarBotAct PendingAct;
            public int PendingTarget;
            public int LastHits;
            public int Target;
            public bool Retreating;
            public bool HasGoal;
            public Vector3 Goal;
            public Vector3 LastSpot;
            public double StillSince;
            public bool WasDead;
            // the squad
            public WarBotOrder Order;
            public Vector3 HoldAt;
            public PlayerParty Leader;
            public PlayerParty Inviter;
            public double AnswerAt;
            public bool LowSaid;
        }

        readonly List<Bot> m_Bots = new List<Bot>();
        readonly Dictionary<Entity, Bot> m_ByGhost = new Dictionary<Entity, Bot>();
        readonly Dictionary<PlayerParty, Bot> m_ByParty = new Dictionary<PlayerParty, Bot>();
        readonly List<WarBotCombatant> m_Seen = new List<WarBotCombatant>(128);
        readonly Dictionary<int, int> m_SeenIndex = new Dictionary<int, int>(128);
        readonly List<LineageDefinition> m_Lineages = new List<LineageDefinition>(8);
        readonly List<int> m_Hits = new List<int>(8);
        readonly List<SkillDefinitionSO> m_Ready = new List<SkillDefinitionSO>(8);
        readonly HashSet<string> m_Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<Bot> m_Gone = new List<Bot>();
        /// <summary>Degrees between two bots coming in round a Rally Point: the golden angle, so no two stand on one line (geometry, not a tunable).</summary>
        const float GoldenAngle = 137.5f;
        uint m_Next;
        double m_Now;
        int m_SeenFrame = -1;
        int m_Frame;

        /// <summary>The settings (tests put their own); the Resources asset otherwise.</summary>
        public WarBotSettings Settings { get; set; }
        WarBotSettings Config => Settings != null ? Settings : WarBotSettings.Instance;

        /// <summary>Bots in, bots that came in and went (tests and the server log).</summary>
        public int Live => m_Bots.Count;
        public int Admitted { get; private set; }
        public int Removed { get; private set; }
        /// <summary>The last refusal of an admission (null none).</summary>
        public string LastRefusal { get; private set; }
        /// <summary>Times every bot went home because no real player was left in the war (tests and the server log).</summary>
        public int WentHomeAlone { get; private set; }
        double m_NoRealSince = double.NaN;

        protected override void OnCreate()
        {
            RequireForUpdate<NetworkTime>();
            KryticalNova.Crossfire.Entities.WarBots.Fill = this;
        }

        protected override void OnDestroy()
        {
            RemoveAll();
            if (ReferenceEquals(KryticalNova.Crossfire.Entities.WarBots.Fill, this)) KryticalNova.Crossfire.Entities.WarBots.Fill = null;
        }

        // ================================================================== IWarBotFill (Crossfire asks)

        public int Fill(in WarBotRequest request, IReadOnlyList<Entity> realPlayers)
        {
            if (request.Count <= 0 || request.Faction == FactionId.None) return 0;
            WarBotSettings s = Config;
            var levels = new List<int>(realPlayers?.Count ?? 0);
            var gear = new List<int>(realPlayers?.Count ?? 0);
            if (realPlayers != null)
                foreach (Entity ghost in realPlayers)
                {
                    if (!GhostBodyRegistry.TryGetByGhost(World, ghost, out GhostBodyEntry entry) || entry.Body == null) continue;
                    if (entry.Body.TryGetComponent(out PlayerCharacterProgression p)) levels.Add(p.CurrentLevel);
                    if (entry.Body.TryGetComponent(out PlayerInventory inv)) gear.Add(GearScoreOf(inv, s));
                }
            BracketLevels(request.Bracket, out int bracketMin, out int bracketMax);
            WarBotRules.RangeOf(levels, bracketMin, bracketMax, out int levelMin, out int levelMax);
            WarBotRules.RangeOf(gear, 0, 0, out int gearMin, out int gearMax);
            int came = 0;
            for (int i = 0; i < request.Count; i++)
                if (Spawn(request.Faction, request.RallyAt, request.RallyYaw, levelMin, levelMax, gearMin, gearMax, i)) came++;
            Debug.Log($"[WarBots] {came} of {request.Count} bots in for the {request.Faction} (levels {levelMin}-{levelMax}, gear {gearMin}-{gearMax})"
                      + (LastRefusal != null && came < request.Count ? $"; refused: {LastRefusal}" : "."));
            return came;
        }

        public int Release(FactionId faction, int count)
        {
            if (count <= 0) return 0;
            var side = new List<Bot>();
            var facts = new List<(bool inFight, bool inPartyWithRealPlayer)>();
            foreach (Bot b in m_Bots)
            {
                if (b.Side != faction) continue;
                side.Add(b);
                facts.Add((b.Target != 0, InPartyWithRealPlayer(b)));
            }
            int left = 0;
            foreach (int i in WarBotRules.PickReleases(facts, count))
            {
                Remove(side[i]);
                left++;
            }
            return left;
        }

        public int Count(FactionId faction)
        {
            int n = 0;
            foreach (Bot b in m_Bots) if (b.Side == faction) n++;
            return n;
        }

        public bool IsWarBot(Entity ghost) => SideOf(ghost) != FactionId.None;

        public FactionId SideOf(Entity ghost) => m_ByGhost.TryGetValue(ghost, out Bot b) ? b.Side : FactionId.None;

        public void EndWar()
        {
            WarBotSettings s = Config;
            int was = m_Bots.Count;
            RemoveAll();
            if (was > 0) Debug.Log($"[WarBots] The war is over: {was} bots left.");
        }

        // ================================================================== coming in and going

        bool Spawn(FactionId side, Vector3 rally, float rallyYaw, int levelMin, int levelMax, int gearMin, int gearMax, int index)
        {
            WarBotSettings s = Config;
            var admission = World.GetExistingSystemManaged<KnAdmissionSystem>()?.Admission;
            PlayerWorldAssets assets = PlayerWorldAssets.Instance;
            PlayerIdentity prefabIdentity = assets != null && assets.playerPrefab != null ? assets.playerPrefab.GetComponent<PlayerIdentity>() : null;
            CharacterCreationDatabase database = prefabIdentity != null ? prefabIdentity.Database : null;
            if (admission == null || database == null) { LastRefusal = "the server has no admission or character database"; return false; }

            uint number = ++m_Next;
            var rng = new System.Random(unchecked((int)(number * 2654435761u) ^ Environment.TickCount));
            m_Lineages.Clear();
            database.GetLineages(side, m_Lineages);
            if (m_Lineages.Count == 0) { LastRefusal = "no lineage for " + side; return false; }
            LineageDefinition lineage = m_Lineages[rng.Next(m_Lineages.Count)];
            BodyType bodyType = rng.Next(2) == 0 ? BodyType.Masculine : BodyType.Feminine;
            WeaponClassRole classRole = (WeaponClassRole)rng.Next(3);
            WeaponClassId weaponClass = WeaponClassRules.ClassOf(side, classRole);
            CharacterRecipe recipe = RecipeEdit.Randomize(lineage, bodyType, database, rng);
            recipe.weaponClass = weaponClass;
            string name = UniqueName(lineage.id, bodyType, rng, s);

            PlayerKey key = PlayerKeys.MintServerBot(number);
            string refusal;
            try
            {
                refusal = admission.Admit(new KnAdmissionRequest
                {
                    Connection = Entity.Null,
                    Key = key,
                    Serial = PlayerKeys.ServerBotBase + number,
                    // a ghost owner no connection has (negative), one per bot: observers fetch its look by it, nobody predicts it
                    NetworkId = -(int)number,
                    CharacterId = string.Empty,
                    Name = name,
                    RecipeText = RecipeText.Encode(recipe),
                });
            }
            catch (Exception e)
            {
                refusal = e.Message;
                Debug.LogException(e);
            }
            if (refusal != null || !GhostBodyRegistry.TryGetByKey(key, out GhostBodyEntry entry) || entry.Body == null)
            {
                PlayerKeys.ReleaseServerBot(number);
                LastRefusal = refusal ?? "no body after the admission";
                return false;
            }

            var bot = new Bot
            {
                Number = number, Key = key, Name = name, Side = side, Ghost = entry.Ghost, Body = entry.Body, Rng = rng,
                Rally = rally, RallyYaw = rallyYaw,
                Profile = WarBotRules.RollProfile(s, RoleOf(classRole), levelMin, levelMax, gearMin, gearMax, rng),
            };
            bot.GhostId = EntityManager.HasComponent<GhostInstance>(bot.Ghost) ? EntityManager.GetComponentData<GhostInstance>(bot.Ghost).ghostId : 0;
            Dress(bot, weaponClass, s);
            // into the Rally Point, spread out a little so a side does not stand in one spot
            Vector3 at = rally + Quaternion.Euler(0f, index * GoldenAngle, 0f) * Vector3.forward
                * (s.rallySpreadMinMeters + (index % Mathf.Max(1, s.rallySpreadRings)) * s.rallySpreadStepMeters);
            PlayerEntitiesSetup.Teleport(EntityManager, bot.Ghost, Mm(at), (int)(rallyYaw * 100f));
            bot.NextThink = m_Now + rng.NextDouble() * s.thinkSeconds;   // spread over the ticks
            bot.NextSkillAt = m_Now + s.firstSkillAfterSeconds;
            bot.StillSince = m_Now;
            bot.LastSpot = at;
            m_Bots.Add(bot);
            m_ByGhost[bot.Ghost] = bot;
            if (bot.Party != null) m_ByParty[bot.Party] = bot;
            m_Names.Add(name);
            Admitted++;
            return true;
        }

        /// <summary>The bot's level, attributes, skills, gear, Stims and mount: what a player of its level and gear would have.</summary>
        void Dress(Bot bot, WeaponClassId weaponClass, WarBotSettings s)
        {
            GameObject body = bot.Body;
            bot.Skills = body.GetComponent<PlayerCombatSkills>();
            bot.Buffs = body.GetComponent<CharacterBuffManager>();
            bot.Inventory = body.GetComponent<PlayerInventory>();
            bot.Party = body.GetComponent<PlayerParty>();
            bot.Chat = body.GetComponent<PlayerChat>();
            bot.Emotes = body.GetComponent<PlayerEmotes>();
            bot.Mounts = body.GetComponent<PlayerMounts>();
            bot.Cadence = body.GetComponent<PlayerCadence>();

            if (body.TryGetComponent(out PlayerCharacterProgression progression))
            {
                progression.SetLevel(bot.Profile.Level);
                int[] split = WarBotRules.SplitPoints(progression.StatPoints, s.AttributesOf(bot.Profile.Role));
                for (int i = 0; i < split.Length; i++)
                    if (split[i] > 0) progression.TryAllocateMultiple((CharacterAttribute)i, split[i]);
            }
            // Run first, as every player learns it (1 point a rank): a War Bot's long walks (the hunt across the field) are paid runs
            // (PlayerServerPlanSystem: "Learn this skill first."), so without it every one was refused without a word and no bot left
            // its yard on The Crucible (17 Nov 2026); fights close by still worked, the melee approach charges no Run
            if (body.TryGetComponent(out PlayerMovementSkills moves)) moves.TryUpgradeMultiple(MovementSkillId.Run, byte.MaxValue);
            // ranks bought with the level's own skill points, round the class's skills (no skill books: a bot finds none)
            if (bot.Skills != null && bot.Skills.AvailableSkills != null)
            {
                bool bought = true;
                for (int round = 0; bought && round < 64; round++)
                {
                    bought = false;
                    foreach (SkillDefinitionSO skill in bot.Skills.AvailableSkills)
                        if (skill != null && !skill.requiresSkillBook && bot.Skills.RunUpgrade(skill.skillId, 1) > 0) bought = true;
                }
            }
            if (bot.Inventory != null)
            {
                WearGear(bot, weaponClass, s);
                if (s.stimItems != null)
                    foreach (ushort stim in s.stimItems)
                        if (stim != 0 && s.stimsEach > 0) bot.Inventory.ServerGive(stim, s.stimsEach);
            }
            if (bot.Profile.HasMount && bot.Mounts != null && bot.Mounts.Catalog != null)
            {
                var built = new List<MountDefinition>();
                foreach (MountDefinition mount in bot.Mounts.Catalog.Mounts)
                    if (mount != null && mount.IsBuilt) built.Add(mount);
                if (built.Count > 0) bot.Mounts.ServerRegister(built[bot.Rng.Next(built.Count)]);
            }
            if (bot.Chat != null)
            {
                bot.Heard = line => OnHeard(bot, line);
                bot.Chat.ServerHeard += bot.Heard;
            }
        }

        /// <summary>A gear set at the bot's level tier, in the rarity and Temper its gear score says (as gm.gearset wear does).</summary>
        void WearGear(Bot bot, WeaponClassId weaponClass, WarBotSettings s)
        {
            PlayerInventory inv = bot.Inventory;
            if (inv.Gear == null || inv.Catalog == null) return;
            var work = bot.Body.GetComponent<KryticalNova.Forge.PlayerGearWork>();
            int cap = work != null && work.Settings != null ? work.Settings.temper.hardCap : GearItemState.MaxTemper;
            WarBotRules.SplitGearScore(bot.Profile.GearScore, s.scorePerRarity, (int)ItemRarity.Legendary, cap, out int rarity, out int temper);
            var wearer = bot.Body.GetComponent<IGearWearer>();
            FactionId faction = wearer != null ? wearer.Faction : bot.Side;
            BodyFit fit = wearer != null ? wearer.Body : BodyFit.None;
            string pack = wearer != null ? wearer.GearPack : null;
            ulong state = default(GearItemState).WithTemper(temper).Pack();
            var pieces = new List<ItemStack>(8);
            foreach (EquipSlot slot in EquipSlots.All)
            {
                if (slot == EquipSlot.Pet) continue;
                // the rarity it rolled, or the best below it that this tier has
                for (int r = rarity; r >= 0; r--)
                {
                    if (!inv.Gear.TryFindItem(slot, (ItemRarity)r, bot.Profile.Level, faction, fit, pack, slot == EquipSlot.Weapon ? weaponClass : WeaponClassId.None, out var found)) continue;
                    if (inv.Catalog.Get(found.itemId) == null) continue;
                    pieces.Add(new ItemStack(found.itemId, 1, state));
                    break;
                }
            }
            if (pieces.Count > 0) inv.ServerWear(pieces, out _, out _);
        }

        void Remove(Bot bot)
        {
            if (bot.Chat != null && bot.Heard != null) bot.Chat.ServerHeard -= bot.Heard;
            m_Bots.Remove(bot);
            m_ByGhost.Remove(bot.Ghost);
            if (bot.Party != null) m_ByParty.Remove(bot.Party);
            m_Names.Remove(bot.Name);
            // the game's own leaving (squad, duel and trade let go; the save does nothing: a bot has no save), then its key dies
            var leave = PlayerWorldsSetup.InstallLeave(World);
            try { leave?.OnConnectionClosed(bot.Key); }
            catch (Exception e) { Debug.LogException(e); }
            PlayerKeys.ReleaseServerBot(bot.Number);
            Removed++;
        }

        void RemoveAll()
        {
            for (int i = m_Bots.Count - 1; i >= 0; i--) Remove(m_Bots[i]);
            m_NoRealSince = double.NaN;
        }

        // ================================================================== thinking

        protected override void OnUpdate()
        {
            m_Now = SystemAPI.Time.ElapsedTime;
            m_Frame++;
            if (m_Bots.Count == 0) return;
            EntityManager em = EntityManager;
            m_Gone.Clear();
            foreach (Bot b in m_Bots)
                if (!em.Exists(b.Ghost) || b.Body == null) m_Gone.Add(b);
            foreach (Bot b in m_Gone)
            {
                Debug.LogWarning($"[WarBots] {b.Name} lost its body; taken off the war.");
                Remove(b);
            }
            WarBotSettings s = Config;
            // bots fight for real players only: none left in the war (past a short grace) and every bot goes home
            if (WarBotRules.AllGoHome(RealPlayersInWar(), ref m_NoRealSince, m_Now, s))
            {
                int was = m_Bots.Count;
                RemoveAll();
                m_NoRealSince = double.NaN;
                WentHomeAlone++;
                Debug.Log($"[WarBots] No real player left in the war for {s.noRealPlayerSeconds:0} s: all {was} bots left.");
                return;
            }
            for (int i = 0; i < m_Bots.Count; i++)
            {
                Bot b = m_Bots[i];
                if (m_Now < b.NextThink) continue;
                b.NextThink = m_Now + s.thinkSeconds;
                if (m_SeenFrame != m_Frame) { GatherSeen(); m_SeenFrame = m_Frame; }
                try { Think(b, s); }
                catch (Exception e) { Debug.LogException(e); }
            }
            LogNow(s);
        }

        /// <summary>Real players (not bots) with a body in the war area, or anywhere on a map with no areas.</summary>
        int RealPlayersInWar()
        {
            EntityManager em = EntityManager;
            bool areas = CrossfireArea.All.Count > 0;
            int n = 0;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(e.World, World) || e.Body == null || !em.Exists(e.Ghost) || m_ByGhost.ContainsKey(e.Ghost)) continue;
                if (PlayerKeys.IsServerBot(e.Key)) continue;
                if (areas && em.HasComponent<PlayerPose>(e.Ghost))
                {
                    var pose = em.GetComponentData<PlayerPose>(e.Ghost);
                    if (!CrossfireArea.AnyContains(new Vector3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f, false)) continue;
                }
                n++;
            }
            return n;
        }

        /// <summary>Every player in the war area (or everywhere, on a map with no areas): side, spot, health, target.</summary>
        void GatherSeen()
        {
            m_Seen.Clear();
            m_SeenIndex.Clear();
            EntityManager em = EntityManager;
            bool areas = CrossfireArea.All.Count > 0;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(e.World, World) || e.Body == null || !em.Exists(e.Ghost) || !em.HasComponent<PlayerPose>(e.Ghost)) continue;
                var pose = em.GetComponentData<PlayerPose>(e.Ghost);
                Vector3 at = new Vector3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
                if (areas && !CrossfireArea.AnyContains(at, false)) continue;
                FactionId side = m_ByGhost.TryGetValue(e.Ghost, out Bot bot) ? bot.Side
                    : e.Body.TryGetComponent(out PlayerIdentity identity) ? identity.Faction : FactionId.None;
                var c = new WarBotCombatant
                {
                    GhostId = em.HasComponent<GhostInstance>(e.Ghost) ? em.GetComponentData<GhostInstance>(e.Ghost).ghostId : 0,
                    Side = side,
                    Position = at,
                    Alive = IsAlive(em, e.Ghost),
                    Health = HealthOf(em, e.Ghost),
                    TargetGhostId = em.HasComponent<CombatState>(e.Ghost) ? em.GetComponentData<CombatState>(e.Ghost).TargetGhostId : 0,
                };
                m_SeenIndex[c.GhostId] = m_Seen.Count;
                m_Seen.Add(c);
            }
        }

        void Think(Bot b, WarBotSettings s)
        {
            EntityManager em = EntityManager;
            var pose = em.GetComponentData<PlayerPose>(b.Ghost);
            Vector3 me = new Vector3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
            bool alive = IsAlive(em, b.Ghost);
            float health = HealthOf(em, b.Ghost);
            var combat = em.GetComponentData<CombatState>(b.Ghost);

            if (!alive)
            {
                b.WasDead = true;
                b.Target = 0;
                b.Retreating = false;
                b.HasGoal = false;
                b.PendingAct = WarBotAct.Wait;
                return;
            }
            if (b.WasDead)
            {
                // back after a respawn: in a war it got up at its own side's Rally Point shrine and stays there; only one that got up
                // far away (outside the war) is put back on its Rally Point (the minimal unstick the brief allows)
                b.WasDead = false;
                if (WarBotRules.BackToRallyAfterRespawn(me, b.Rally, s)) { ToRally(b); return; }
            }
            if (Stuck(b, me, combat)) { ToRally(b); return; }

            NoteKill(b, combat, s);
            AnswerInvite(b, s);
            FormSquad(b, me, s);
            if (!GatesOpen())
            {
                // the Rally: the gates are shut, everyone waits in the yard (a bot puts its buffs on, as a player would)
                if (s.buffBetweenFights && TryReadyBuff(b, out SkillDefinitionSO waitBuff)) CastBuff(b, waitBuff, s);
                return;
            }

            var view = new WarBotView
            {
                GhostId = b.GhostId,
                Side = b.Side,
                Position = me,
                Alive = true,
                Health = health,
                Profile = b.Profile,
                TargetGhostId = combat.TargetGhostId,
                TargetValid = combat.TargetGhostId != 0 && m_SeenIndex.TryGetValue(combat.TargetGhostId, out int ti) && m_Seen[ti].Alive && m_Seen[ti].Side != b.Side
                              && WarBotRules.FlatDistance(me, m_Seen[ti].Position) <= s.sightMeters * s.keepTargetSightFactor,
                Walking = em.IsComponentEnabled<ServerOrderRequest>(b.Ghost) || (b.HasGoal && WarBotRules.FlatDistance(me, b.Goal) > s.goalReachedMeters),
                Retreating = b.Retreating,
                StimReady = TryReadyStim(b, s, out _),
                BuffReady = TryReadyBuff(b, out _),
                SkillReady = m_Now >= b.NextSkillAt && combat.TargetGhostId != 0 && TryReadyAttacks(b),
                Rally = b.Rally,
            };
            FillLeader(b, ref view);

            WarBotPlan plan = WarBotRules.Decide(view, m_Seen, s, b.Rng.NextDouble());
            Count(m_Plans, plan.Act == WarBotAct.Wait ? "wait" : plan.Act + (plan.Why != null ? " (" + plan.Why + ")" : ""));
            b.Target = combat.TargetGhostId;
            if (plan.Act != WarBotAct.Retreat && b.Retreating && health >= s.returnHealth) b.Retreating = false;
            if (plan.Act == WarBotAct.Wait) { b.PendingAct = WarBotAct.Wait; return; }

            // a human reaction: something new waits its reaction time first; the same plan again goes on at once
            bool isNew = plan.Act != b.PendingAct || plan.TargetGhostId != b.PendingTarget;
            if (isNew && plan.Act != WarBotAct.Skill)
            {
                b.PendingAct = plan.Act;
                b.PendingTarget = plan.TargetGhostId;
                b.ReactAt = m_Now + WarBotRules.ReactionSeconds(b.Profile, b.Rng.NextDouble());
            }
            if (plan.Act != WarBotAct.Skill && m_Now < b.ReactAt) return;
            Act(b, plan, me, s);
        }

        void Act(Bot b, in WarBotPlan plan, Vector3 me, WarBotSettings s)
        {
            EntityManager em = EntityManager;
            switch (plan.Act)
            {
                case WarBotAct.Attack:
                {
                    GetOff(b);
                    var combat = em.GetComponentData<CombatState>(b.Ghost);
                    MeleeRules.Begin(ref combat, plan.TargetGhostId);
                    em.SetComponentData(b.Ghost, combat);
                    b.Target = plan.TargetGhostId;
                    b.HasGoal = false;
                    b.PendingAct = WarBotAct.Wait;   // the next new thing reacts again
                    break;
                }
                case WarBotAct.Skill:
                    CastNextSkill(b, plan.TargetGhostId, s);
                    break;
                case WarBotAct.Buff:
                    if (TryReadyBuff(b, out SkillDefinitionSO buff)) CastBuff(b, buff, s);
                    b.PendingAct = WarBotAct.Wait;
                    break;
                case WarBotAct.Stim:
                    if (TryReadyStim(b, s, out ushort stim)) PlayerRequestServerSystem.UseOn(b.Body, stim, out _);
                    b.NextStimAt = m_Now + s.stimRetrySeconds;   // the Stim's own cooldown refuses a second one sooner anyway
                    b.PendingAct = WarBotAct.Wait;
                    break;
                case WarBotAct.Retreat:
                    if (!b.Retreating && !b.LowSaid) { Say(b, s.lowLines, s); b.LowSaid = true; }
                    b.Retreating = true;
                    Cancel(b);
                    MoveTo(b, plan.MoveTo, me, s);
                    break;
                case WarBotAct.Move:
                    b.LowSaid = false;
                    if (plan.Why == "regroup" && b.Party != null && b.Party.ServerLeader == b.Party) Say(b, s.regroupLines, s);
                    if (plan.Why == "kite") Cancel(b);
                    MoveTo(b, plan.MoveTo, me, s);
                    break;
            }
        }

        /// <summary>The next attack skill: the Cadence combo's next step when the bot tries it, pressed near the beat (as well as its skill lets it).</summary>
        void CastNextSkill(Bot b, int target, WarBotSettings s)
        {
            if (!TryReadyAttacks(b)) return;
            bool tryCombo = WarBotRules.TriesCombo(s, b.Profile.Skill, b.Rng.NextDouble());
            m_Hits.Clear();
            foreach (SkillDefinitionSO k in m_Ready) m_Hits.Add(Mathf.Max(1, k.hitCount));
            int pick = WarBotRules.NextComboSkill(m_Hits, b.LastHits, tryCombo);
            if (pick < 0) return;
            SkillDefinitionSO skill = m_Ready[pick];
            IBeatClock clock = BeatClock.Current;
            if (tryCombo && b.Cadence != null && clock != null && clock.HasBeat && clock.TryBeatAt(clock.Now, out _, out double fromBeat, out _))
                b.Cadence.ServerNotePressFromView(skill.skillId, clock.Now - fromBeat + WarBotRules.BeatError(s, b.Profile.Skill, b.Rng.NextDouble()));
            if (Server()?.ServerCast(b.Body, skill.skillId, target, out string refused) == true) b.LastHits = Mathf.Max(1, skill.hitCount);
            else Count(m_Refusals, "attack: " + (refused ?? "no server"));
            b.NextSkillAt = m_Now + Mathf.Lerp(s.skillGapMinSeconds, s.skillGapMaxSeconds, (float)b.Rng.NextDouble());
        }

        void MoveTo(Bot b, Vector3 goal, Vector3 me, WarBotSettings s)
        {
            if (b.HasGoal && WarBotRules.FlatDistance(goal, b.Goal) < s.goalReachedMeters && EntityManager.IsComponentEnabled<ServerOrderRequest>(b.Ghost)) return;
            bool far = WarBotRules.FlatDistance(me, goal) > s.rideBeyondMeters;
            if (far && b.Mounts != null && b.Mounts.SelectedId != 0 && !b.Mounts.IsMounted && !EnemyNear(b, me, s)) b.Mounts.ServerToggleRide();
            PlayerEntitiesSetup.IssueOrder(EntityManager, b.Ghost, Mm(goal), runPaid: true, chargeRun: true);
            b.HasGoal = true;
            b.Goal = goal;
        }

        void ToRally(Bot b)
        {
            Cancel(b);
            PlayerEntitiesSetup.Teleport(EntityManager, b.Ghost, Mm(b.Rally), (int)(b.RallyYaw * 100f));
            b.HasGoal = false;
            b.StillSince = m_Now;
            b.Retreating = false;
        }

        void Cancel(Bot b)
        {
            var combat = EntityManager.GetComponentData<CombatState>(b.Ghost);
            if (combat.TargetGhostId == 0) return;
            MeleeRules.Cancel(ref combat);
            EntityManager.SetComponentData(b.Ghost, combat);
            b.Target = 0;
        }

        void GetOff(Bot b)
        {
            if (b.Mounts != null && b.Mounts.IsMounted) b.Mounts.ServerToggleRide();
        }

        /// <summary>Walking somewhere for a long time without getting anywhere and not fighting: stuck (put back on the Rally Point).</summary>
        bool Stuck(Bot b, Vector3 me, in CombatState combat)
        {
            WarBotSettings s = Config;
            if (WarBotRules.FlatDistance(me, b.LastSpot) > s.stuckMeters || !b.HasGoal || combat.TargetGhostId != 0)
            {
                b.LastSpot = me;
                b.StillSince = m_Now;
                return false;
            }
            return m_Now - b.StillSince > s.stuckSeconds;
        }

        // ================================================================== kills, manners, the squad

        void NoteKill(Bot b, in CombatState combat, WarBotSettings s)
        {
            if (b.Target == 0 || combat.TargetGhostId == b.Target) return;
            int was = b.Target;
            if (!m_SeenIndex.TryGetValue(was, out int i) || m_Seen[i].Alive) return;
            if (!TryGhostOf(was, out Entity victim) || !EntityManager.HasComponent<DeathState>(victim)) return;
            if (EntityManager.GetComponentData<DeathState>(victim).KillerGhostId != b.GhostId) return;
            if (WarBotRules.MayEmote(s, b.LastEmote, m_Now, b.Rng.NextDouble())) Emote(b, s.killEmotes);
            else Say(b, s.killLines, s);
        }

        void Say(Bot b, string[] lines, WarBotSettings s)
        {
            if (b.Chat == null || b.Party == null || !b.Party.InParty) return;   // squad chat only: a bot never talks to the whole server
            if (!WarBotRules.MaySay(s, b.LastSaid, m_Now, b.Rng.NextDouble())) return;
            string line = WarBotRules.Pick(lines, b.Rng.NextDouble());
            if (string.IsNullOrEmpty(line)) return;
            b.Chat.RunSend(ChatChannel.Party, line, null, null, null);
            b.LastSaid = m_Now;
        }

        void Emote(Bot b, string[] keys)
        {
            if (b.Emotes == null) return;
            string key = WarBotRules.Pick(keys, b.Rng.NextDouble());
            if (string.IsNullOrEmpty(key)) return;
            var combat = EntityManager.GetComponentData<CombatState>(b.Ghost);
            MeleeRules.Sheathe(ref combat);   // as the emote request does: the weapon goes away
            EntityManager.SetComponentData(b.Ghost, combat);
            b.Emotes.RunEmote(false, key);
            b.LastEmote = m_Now;
        }

        /// <summary>A squad invite waits a human moment, then is taken or turned down by the rules.</summary>
        void AnswerInvite(Bot b, WarBotSettings s)
        {
            if (b.Party == null) return;
            PlayerParty from = b.Party.ServerInvitedBy;
            if (from == null) { b.Inviter = null; return; }
            if (from != b.Inviter)
            {
                b.Inviter = from;
                b.AnswerAt = m_Now + Mathf.Lerp(s.inviteAnswerMinSeconds, s.inviteAnswerMaxSeconds, (float)b.Rng.NextDouble());
                return;
            }
            if (m_Now < b.AnswerAt) return;
            bool real = !m_ByParty.ContainsKey(from);
            bool sameSide = SideOfParty(from) == b.Side;
            bool accept = WarBotRules.AcceptsInvite(s, real, sameSide, b.Party.InParty, InPartyWithRealPlayer(b));
            if (accept && b.Party.InParty) b.Party.RunPartyOp(PartyOpKind.Leave, null, false);   // a bots-only squad gives way to a player
            b.Party.RunPartyOp(PartyOpKind.Respond, null, accept);
            b.Inviter = null;
            if (!accept || !b.Party.InParty) return;
            b.Order = WarBotOrder.Follow;
            if (real)
            {
                Say(b, s.joinLines, s);
                if (WarBotRules.MayEmote(s, b.LastEmote, m_Now, b.Rng.NextDouble())) Emote(b, s.leaderEmotes);
            }
        }

        /// <summary>Bots form squads among themselves: every Nth bot of a side leads and invites free bots of its side near it.</summary>
        void FormSquad(Bot b, Vector3 me, WarBotSettings s)
        {
            if (s.botPartySize < 2 || b.Party == null || m_Now < b.NextPartyTry) return;
            b.NextPartyTry = m_Now + s.partyTryMinSeconds + b.Rng.NextDouble() * Math.Max(0f, s.partyTryMaxSeconds - s.partyTryMinSeconds);
            bool leads = b.Party.InParty ? b.Party.ServerLeader == b.Party : b.Number % (uint)s.botPartySize == 0;
            if (!leads || b.Party.ServerSize >= s.botPartySize) return;
            foreach (Bot other in m_Bots)
            {
                if (other == b || other.Side != b.Side || other.Party == null || other.Party.InParty || other.Party.ServerInvitedBy != null) continue;
                if (!m_SeenIndex.TryGetValue(other.GhostId, out int i) || WarBotRules.FlatDistance(me, m_Seen[i].Position) > s.sightMeters) continue;
                b.Party.RunPartyOp(PartyOpKind.InviteByName, other.Name, false);
                return;   // one invite at a time, as a person would
            }
        }

        /// <summary>The squad leader's spot, target and order, when the bot is in a squad another leads.</summary>
        void FillLeader(Bot b, ref WarBotView view)
        {
            PlayerParty leader = b.Party != null ? b.Party.ServerLeader : null;
            if (leader != b.Leader)
            {
                b.Leader = leader;
                b.Order = leader != null && leader != b.Party ? WarBotOrder.Follow : WarBotOrder.None;
            }
            if (leader == null || leader == b.Party || !GhostBodyRegistry.TryGetByKey(KeyOfBody(leader.gameObject), out GhostBodyEntry entry) || !EntityManager.Exists(entry.Ghost)) return;
            var pose = EntityManager.GetComponentData<PlayerPose>(entry.Ghost);
            view.HasLeader = true;
            view.LeaderPosition = new Vector3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
            view.LeaderTargetGhostId = EntityManager.HasComponent<CombatState>(entry.Ghost) ? EntityManager.GetComponentData<CombatState>(entry.Ghost).TargetGhostId : 0;
            view.Order = b.Order;
            view.HoldAt = b.HoldAt;
        }

        /// <summary>A line to the bot's player: the squad leader's follow / hold / attack in squad chat is an order.</summary>
        void OnHeard(Bot b, BodyChatLine line)
        {
            if (line.kind != BodyChatKind.Line || line.channel != ChatChannel.Party || line.outgoing || b.Party == null) return;
            PlayerParty leader = b.Party.ServerLeader;
            if (leader == null || leader == b.Party || !string.Equals(leader.MemberName, line.sender, StringComparison.OrdinalIgnoreCase)) return;
            WarBotOrder order = WarBotRules.ParseOrder(line.text, Config);
            if (order == WarBotOrder.None) return;
            b.Order = order;
            if (order == WarBotOrder.Hold && EntityManager.Exists(b.Ghost))
            {
                var pose = EntityManager.GetComponentData<PlayerPose>(b.Ghost);
                b.HoldAt = new Vector3(pose.XMm, pose.YMm, pose.ZMm) * 0.001f;
            }
        }

        // ================================================================== reading the body

        bool TryReadyAttacks(Bot b)
        {
            m_Ready.Clear();
            if (b.Skills == null || b.Skills.AvailableSkills == null) return false;
            foreach (SkillDefinitionSO k in b.Skills.AvailableSkills)
                if (k != null && k.IsAttack && b.Skills.IsLearned(k) && !b.Skills.IsLocked(k) && !b.Skills.IsOnCooldown(k)) m_Ready.Add(k);
            return m_Ready.Count > 0;
        }

        /// <summary>A buff cast; one the game refuses is left alone for <see cref="WarBotSettings.buffRetrySeconds"/>, so the bot fights instead.</summary>
        void CastBuff(Bot b, SkillDefinitionSO buff, WarBotSettings s)
        {
            if (Server()?.ServerCast(b.Body, buff.skillId, 0, out string refused) == true) return;
            b.NoBuffUntil = m_Now + s.buffRetrySeconds;
            Count(m_Refusals, "buff: " + (refused ?? "no server"));
        }

        // what the bots chose and why the game refused their casts, counted for the server log (and tests)
        readonly Dictionary<string, int> m_Plans = new Dictionary<string, int>(), m_Refusals = new Dictionary<string, int>();
        double m_NextLog;
        /// <summary>The plans the bots chose since the last log line (tests read it).</summary>
        public IReadOnlyDictionary<string, int> Plans => m_Plans;
        /// <summary>The casts the game refused since the last log line, by reason (tests read it).</summary>
        public IReadOnlyDictionary<string, int> Refusals => m_Refusals;

        static void Count(Dictionary<string, int> into, string key) { into.TryGetValue(key, out int n); into[key] = n + 1; }

        static string Line(Dictionary<string, int> d)
        {
            if (d.Count == 0) return "none";
            var sb = new System.Text.StringBuilder();
            foreach (KeyValuePair<string, int> kv in d) sb.Append(sb.Length > 0 ? ", " : "").Append(kv.Key).Append(' ').Append(kv.Value);
            return sb.ToString();
        }

        void LogNow(WarBotSettings s)
        {
            if (s.logEverySeconds <= 0f || m_Now < m_NextLog) return;
            m_NextLog = m_Now + s.logEverySeconds;
            Debug.Log($"[WarBots] {m_Bots.Count} bots, gates {(GatesOpen() ? "open" : "shut")}, {m_Seen.Count} seen; chose: {Line(m_Plans)}; refused: {Line(m_Refusals)}");
            m_Plans.Clear();
            m_Refusals.Clear();
        }

        bool TryReadyBuff(Bot b, out SkillDefinitionSO buff)
        {
            buff = null;
            if (b.Skills == null || b.Buffs == null || b.Skills.AvailableSkills == null || m_Now < b.NoBuffUntil) return false;
            foreach (SkillDefinitionSO k in b.Skills.AvailableSkills)
            {
                if (k == null || k.IsAttack || k.duration <= 0f || !b.Skills.IsLearned(k) || b.Skills.IsLocked(k) || b.Skills.IsOnCooldown(k)) continue;
                if (b.Buffs.HasBuff(k.skillId)) continue;
                buff = k;
                return true;
            }
            return false;
        }

        bool TryReadyStim(Bot b, WarBotSettings s, out ushort stim)
        {
            stim = 0;
            if (b.Inventory == null || s.stimItems == null || m_Now < b.NextStimAt) return false;
            foreach (ushort id in s.stimItems)
                if (id != 0 && b.Inventory.CountItem(id) > 0) { stim = id; return true; }
            return false;
        }

        bool EnemyNear(Bot b, Vector3 me, WarBotSettings s)
        {
            foreach (WarBotCombatant c in m_Seen)
                if (c.Alive && c.Side != b.Side && c.Side != FactionId.None && WarBotRules.FlatDistance(me, c.Position) <= s.sightMeters) return true;
            return false;
        }

        bool InPartyWithRealPlayer(Bot b)
        {
            PlayerParty leader = b.Party != null ? b.Party.ServerLeader : null;
            return leader != null && !m_ByParty.ContainsKey(leader);
        }

        FactionId SideOfParty(PlayerParty party) =>
            m_ByParty.TryGetValue(party, out Bot bot) ? bot.Side : party.TryGetComponent(out PlayerIdentity id) ? id.Faction : FactionId.None;

        static PlayerKey KeyOfBody(GameObject body) => body != null && BodyRoleTag.Of(body.transform) is { } tag ? tag.Key : PlayerKey.None;

        bool TryGhostOf(int ghostId, out Entity ghost)
        {
            ghost = Entity.Null;
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(e.World, World) || !em.Exists(e.Ghost) || !em.HasComponent<GhostInstance>(e.Ghost)) continue;
                if (em.GetComponentData<GhostInstance>(e.Ghost).ghostId != ghostId) continue;
                ghost = e.Ghost;
                return true;
            }
            return false;
        }

        PlayerRequestServerSystem Server() => World.GetExistingSystemManaged<PlayerRequestServerSystem>();

        /// <summary>The war's gates are open (Crossfire's War phase); true where no Crossfire clock runs (a test world).</summary>
        bool GatesOpen()
        {
            var clock = World.GetExistingSystemManaged<CrossfireClockServerSystem>();
            if (clock == null || !clock.Current.Known) return true;
            return clock.Current.Now.Phase == CrossfirePhase.War;
        }

        static bool IsAlive(EntityManager em, Entity ghost)
        {
            if (em.HasComponent<DeathState>(ghost) && em.GetComponentData<DeathState>(ghost).Pose != 0) return false;
            if (!em.HasComponent<PlayerVitals>(ghost)) return true;
            var v = em.GetComponentData<PlayerVitals>(ghost);
            return v.MaxHealthMilli == 0 || v.HealthMilli > 0;
        }

        static float HealthOf(EntityManager em, Entity ghost)
        {
            if (!em.HasComponent<PlayerVitals>(ghost)) return 1f;
            var v = em.GetComponentData<PlayerVitals>(ghost);
            return v.MaxHealthMilli > 0 ? Mathf.Clamp01((float)v.HealthMilli / v.MaxHealthMilli) : 1f;
        }

        /// <summary>A real player's gear score: the average rarity step and Temper of what it wears.</summary>
        static int GearScoreOf(PlayerInventory inv, WarBotSettings s)
        {
            var worn = new List<(int rarity, int temper)>(8);
            foreach (EquipSlot slot in EquipSlots.All)
            {
                if (slot == EquipSlot.Pet) continue;
                ItemStack stack = inv.GetEquipped(slot);
                if (stack.itemId == 0 || stack.count == 0 || inv.Catalog == null || !(inv.Catalog.Get(stack.itemId) is ItemDefinition def)) continue;
                worn.Add(((int)def.rarity, GearItemState.Of(stack).Temper));
            }
            return WarBotRules.GearScoreOf(worn, s.scorePerRarity);
        }

        static void BracketLevels(int bracket, out int min, out int max)
        {
            CrossfireSettings c = CrossfireSettings.Instance;
            int first = c != null ? c.minLevel : 10, size = c != null ? c.bracketSize : 10, count = c != null ? c.bracketCount : 10;
            bracket = Mathf.Clamp(bracket, 1, Mathf.Max(1, count));
            min = first + (bracket - 1) * size;
            max = bracket == count ? 112 : min + size - 1;
        }

        static WarBotRole RoleOf(WeaponClassRole role) => role switch
        {
            WeaponClassRole.Defense => WarBotRole.Tank,
            WeaponClassRole.Ranged => WarBotRole.Ranged,
            _ => WarBotRole.Damage,
        };

        string UniqueName(byte lineage, BodyType bodyType, System.Random rng, WarBotSettings s)
        {
            string name = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                name = CharacterNameGenerator.Generate(lineage, bodyType, rng);
                if (!s.uniqueNames || !NameTaken(name)) return name;
            }
            return name;
        }

        bool NameTaken(string name)
        {
            if (m_Names.Contains(name)) return true;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (e.Body != null && e.Body.TryGetComponent(out PlayerIdentity id) && string.Equals(id.CharacterName, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static int3 Mm(Vector3 m) => new int3(Mathf.RoundToInt(m.x * 1000f), Mathf.RoundToInt(m.y * 1000f), Mathf.RoundToInt(m.z * 1000f));
    }
}
