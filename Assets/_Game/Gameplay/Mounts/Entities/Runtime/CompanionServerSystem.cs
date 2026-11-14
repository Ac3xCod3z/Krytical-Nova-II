using System.Collections.Generic;
using KryticalNova.Brains;
using KryticalNova.Combat;
using KryticalNova.Combat.Entities;
using KryticalNova.Core;
using KryticalNova.Crossfire;
using KryticalNova.Hostiles.Entities;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using KryticalNova.World;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Mounts.Entities
{
    /// <summary>
    /// Server: <see cref="IMountCompanionLink"/> for the bodies of the entity netcode: the four boxes' companion HP and rest go to the
    /// body's own connection (as <c>EntityMountOwnerLink</c> sends the boxes' EXP and hunger).
    /// </summary>
    public sealed class EntityMountCompanionLink : IMountCompanionLink
    {
        /// <summary>Messages sent (tests).</summary>
        public static int Sent { get; private set; }

        public bool Companion(Component part, byte isOut, byte stance, ushort hp1, ushort hp2, ushort hp3, ushort hp4, uint rest1, uint rest2, uint rest3, uint rest4)
        {
            BodyRoleTag tag = BodyRoleTag.Of(part);
            if (tag == null || tag.Kind != NetRoleKind.ServerBody || tag.Key == PlayerKey.None) return false;
            if (!GhostBodyRegistry.TryGetByKey(tag.Key, out GhostBodyEntry entry) || entry.World == null || !entry.World.IsCreated) return false;
            EntityManager em = entry.World.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            using var connections = query.ToEntityArray(Allocator.Temp);
            using var ids = query.ToComponentDataArray<NetworkId>(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
            {
                if (ids[i].Value != entry.NetworkId) continue;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new MountCompanionBookRpc
                {
                    Out = isOut, Stance = stance, Hp1 = hp1, Hp2 = hp2, Hp3 = hp3, Hp4 = hp4, Rest1 = rest1, Rest2 = rest2, Rest3 = rest3, Rest4 = rest4,
                });
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connections[i] });
                Sent++;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Server (day list item 19, phases 1-4): one Mount Companion ghost for every server body whose <see cref="PlayerMounts"/> has it out.
    /// It follows the owner's player ghost by <see cref="CompanionFollower"/> (every <see cref="MountSettings.companionFollowSeconds"/>),
    /// carries the HP, stance and warps in its <see cref="CompanionView"/>, and keeps a hidden <see cref="CompanionBody"/> where it stands so
    /// enemy players' area skills reach it. Sent home or knocked out, it shows the going for a moment (so every view plays the vanish) and
    /// is then destroyed; a body that leaves the world (logout, map change) takes its companion at once.
    /// Phase 3: in the DPS or HEALER stance it looks for its fight every <see cref="MountSettings.companionFightSeconds"/> (all companions at
    /// once in <see cref="CompanionPickJob"/>, Burst; nothing is queried while none is in a combat stance). DPS runs to the Hostile and strikes
    /// in its owner's name with no threat; HEALER heals its owner over time while they fight. Both act as FREE while the owner is in PvP.
    /// Phase 4: TANK runs to the Hostile too, taunts it (and every Hostile after its owner near it) every
    /// <see cref="MountSettings.companionTankTauntSeconds"/> with a <see cref="BrainEventKind.Taunt"/> for its own ghost id, takes less damage
    /// (<see cref="CompanionBody.TakenScale"/>) and deals none. While it acts as TANK it is in the Hostiles' sense world
    /// (<see cref="CompanionSenseFeedSystem"/>) and its body takes their blows (<see cref="StandInTargets"/>); PvE only, as the other two.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CompanionServerSystem : SystemBase
    {
        sealed class Live
        {
            public Entity Ghost;
            public readonly CompanionFollower Follow = new CompanionFollower();
            public CompanionBody Body;
            public Vector3 LastOwner;
            public bool HasLast, Leaving;
            public float Wait, LeaveAt;
            public int KnockOutsSeen;
            public byte Warps;
            // phase 3: the fight (DPS, HEALER)
            public PlayerMounts Mounts;
            public Entity OwnerGhost;
            public int OwnerGhostId, NetworkId;
            public Vector3 OwnerAt;
            public CompanionStance Acting;
            public bool InPvp, HasGoal;
            public Vector3 Goal, FaceAt;
            public int Fight;
            public float NextStrikeAt, NextPulseAt;
            public byte Strikes, Pulses;
            // phase 4: the taunt (TANK)
            public int SelfGhostId;
            public float NextTauntAt;
            public byte Taunts;
        }

        readonly Dictionary<GameObject, Live> m_Live = new Dictionary<GameObject, Live>();
        readonly HashSet<GameObject> m_Seen = new HashSet<GameObject>();
        readonly List<GameObject> m_Drop = new List<GameObject>();
        readonly List<Live> m_Seeking = new List<Live>();
        EntityQuery m_Hostiles, m_BrainTick;
        HitRecordServerSystem m_Hits;
        float m_LookWait;

        /// <summary>Companions spawned and destroyed (tests).</summary>
        public int Spawned { get; private set; }
        public int Destroyed { get; private set; }
        public int LiveCount => m_Live.Count;
        /// <summary>Phase 3 (tests and the Play proof): looks that ran a pick, strikes dealt, heal pulses given.</summary>
        public int Looks { get; private set; }
        public int StrikesDealt { get; private set; }
        public int PulsesGiven { get; private set; }
        /// <summary>Phase 4 (tests and the Play proof): taunt events written on Hostiles.</summary>
        public int TauntsWritten { get; private set; }

        protected override void OnCreate()
        {
            RequireForUpdate<MountCompanionGhostPrefab>();
            MountCompanionLink.Current ??= new EntityMountCompanionLink();
            m_BrainTick = GetEntityQuery(ComponentType.ReadOnly<BrainTick>());
            m_Hostiles = GetEntityQuery(ComponentType.ReadOnly<HostileTarget>(), ComponentType.ReadOnly<HostileBrain>(), ComponentType.ReadOnly<HostileLife>(),
                ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<GhostInstance>(), ComponentType.ReadOnly<BrainEvent>());
        }

        protected override void OnDestroy()
        {
            foreach (Live live in m_Live.Values) if (live.Body != null) Object.Destroy(live.Body.gameObject);
            m_Live.Clear();
        }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            float now = (float)SystemAPI.Time.ElapsedTime;
            Entity prefab = SystemAPI.GetSingleton<MountCompanionGhostPrefab>().Prefab;
            m_Seen.Clear();
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<LocalTransform>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out PlayerMounts mounts)) continue;
                m_Seen.Add(entry.Body);
                m_Live.TryGetValue(entry.Body, out Live live);
                IBodyMountCompanion c = mounts;
                MountSettings settings = mounts.Settings;
                LocalTransform owner = em.GetComponentData<LocalTransform>(entry.Ghost);
                Vector3 ownerAt = owner.Position;
                float ownerYaw = YawOf(owner.Rotation);
                if (c.CompanionOut)
                {
                    if (live != null && live.Leaving) { DestroyGhost(em, live); live = null; }
                    live ??= Spawn(em, prefab, entry.Body, mounts, ownerAt, ownerYaw, now);
                    Step(em, entry, live, mounts, settings, ownerAt, ownerYaw, dt, now);
                }
                else if (live != null && !live.Leaving) BeginLeave(em, live, c, settings, now);
            }

            m_LookWait += dt;
            if (m_LookWait >= FightTuningOfAny().LookSeconds)
            {
                m_LookWait = 0f;
                Look(em, now);
            }

            m_Drop.Clear();
            foreach (KeyValuePair<GameObject, Live> kv in m_Live)
            {
                bool bodyGone = kv.Key == null || !m_Seen.Contains(kv.Key);
                if (bodyGone || (kv.Value.Leaving && now >= kv.Value.LeaveAt)) m_Drop.Add(kv.Key);
            }
            foreach (GameObject body in m_Drop)
            {
                DestroyGhost(em, m_Live[body]);
                m_Live.Remove(body);
            }
        }

        Live Spawn(EntityManager em, Entity prefab, GameObject body, PlayerMounts mounts, Vector3 ownerAt, float ownerYaw, float now)
        {
            MountSettings settings = mounts.Settings;
            CompanionTuning t = settings != null ? settings.Companion : CompanionTuning.Default;
            var live = new Live { Ghost = em.Instantiate(prefab), KnockOutsSeen = ((IBodyMountCompanion)mounts).CompanionKnockOuts, Mounts = mounts };
            Vector3 spot = CompanionRules.RestSpot(ownerAt, ownerYaw, t);
            live.Follow.Place(spot, ownerYaw);
            live.Body = CompanionBody.Make(mounts, settings != null ? settings.companionBodyRadius : 0.9f, settings != null ? settings.companionBodyHeight : 2.2f);
            live.Body.transform.position = spot;
            StandInTargets.Register(World, live.Ghost, live.Body);   // phase 4: a TANK is struck through this body
            em.SetComponentData(live.Ghost, LocalTransform.FromPositionRotation(spot, quaternion.RotateY(math.radians(ownerYaw))));
            m_Live[body] = live;
            Spawned++;
            Debug.Log($"[KnCompanion] server: spawned for {body.name} at {spot}");
            return live;
        }

        void Step(EntityManager em, GhostBodyEntry entry, Live live, PlayerMounts mounts, MountSettings settings, Vector3 ownerAt, float ownerYaw, float dt, float now)
        {
            IBodyMountCompanion c = mounts;
            live.Mounts = mounts;
            live.OwnerGhost = entry.Ghost;
            live.NetworkId = entry.NetworkId;
            live.OwnerAt = ownerAt;
            live.Wait += dt;
            float every = settings != null ? settings.companionFollowSeconds : 0.1f;
            if (live.Wait >= every)
            {
                float step = live.Wait;
                live.Wait = 0f;
                float ownerSpeed = live.HasLast ? CompanionFlat(ownerAt, live.LastOwner) / step : 0f;
                live.LastOwner = ownerAt;
                live.HasLast = true;
                CompanionTuning t = settings != null ? settings.Companion : CompanionTuning.Default;
                // phase 3 (DPS): to its strike spot while it fights, unless it fell so far behind that the follow warps it back
                if (live.HasGoal && CompanionFlat(live.Follow.Position, ownerAt) < t.WarpDistance)
                    live.Follow.StepToward(live.Goal, live.FaceAt, c.CompanionRunSpeed, step);
                else if (live.Follow.Step(ownerAt, ownerYaw, ownerSpeed, c.CompanionRunSpeed, step, now, t) == CompanionFollower.Result.Warped) live.Warps++;
                var place = LocalTransform.FromPositionRotation(live.Follow.Position, quaternion.RotateY(math.radians(live.Follow.Yaw)));
                LocalTransform had = em.GetComponentData<LocalTransform>(live.Ghost);
                if (!had.Position.Equals(place.Position) || !had.Rotation.Equals(place.Rotation)) em.SetComponentData(live.Ghost, place);
                if (live.Body != null) live.Body.transform.position = live.Follow.Position;
            }
            if (live.Body != null) live.Body.TakenScale = CompanionFightRules.TakenScale(live.Acting, FightOf(mounts));
            live.SelfGhostId = em.HasComponent<GhostInstance>(live.Ghost) ? em.GetComponentData<GhostInstance>(live.Ghost).ghostId : 0;
            int ownerGhostId = em.HasComponent<GhostInstance>(entry.Ghost) ? em.GetComponentData<GhostInstance>(entry.Ghost).ghostId : 0;
            live.OwnerGhostId = ownerGhostId;
            var view = new CompanionView
            {
                OwnerGhostId = ownerGhostId, Box = (byte)c.CompanionBox, Stage = (byte)c.CompanionStage,
                SpeedStep = (byte)math.clamp((int)math.round(live.Follow.Speed / CompanionView.SpeedStepMetres), 0, 255),
                State = (byte)CompanionState.Out, Stance = c.CompanionStance, HpPermille = (ushort)math.clamp(c.CompanionHpPermille, 0, CompanionRules.Permille),
                Warps = live.Warps,
                Strikes = live.Strikes, Pulses = live.Pulses, FightGhostId = live.Fight, ActingStance = (byte)live.Acting, Taunts = live.Taunts,
            };
            WriteView(em, live.Ghost, view);
        }

        void BeginLeave(EntityManager em, Live live, IBodyMountCompanion c, MountSettings settings, float now)
        {
            live.Leaving = true;
            live.HasGoal = false;
            live.Fight = 0;
            live.LeaveAt = now + (settings != null ? settings.companionVanishSeconds : 0.6f);
            bool knockedOut = c.CompanionKnockOuts != live.KnockOutsSeen;
            CompanionView view = em.GetComponentData<CompanionView>(live.Ghost);
            view.State = (byte)(knockedOut ? CompanionState.KnockedOut : CompanionState.Leaving);
            view.SpeedStep = 0;
            view.FightGhostId = 0;
            if (knockedOut) view.HpPermille = 0;
            view.ActingStance = (byte)CompanionStance.Free;   // out of the Hostiles' sense world at once
            live.Acting = CompanionStance.Free;
            WriteView(em, live.Ghost, view);
            StandInTargets.Unregister(World, live.Ghost);
            if (live.Body != null) { Object.Destroy(live.Body.gameObject); live.Body = null; }
            Debug.Log($"[KnCompanion] server: {(knockedOut ? "knocked out" : "going home")}");
        }

        void DestroyGhost(EntityManager em, Live live)
        {
            StandInTargets.Unregister(World, live.Ghost);
            if (live.Body != null) { Object.Destroy(live.Body.gameObject); live.Body = null; }
            if (em.Exists(live.Ghost)) em.DestroyEntity(live.Ghost);
            Destroyed++;
        }

        static void WriteView(EntityManager em, Entity ghost, CompanionView view)
        {
            CompanionView had = em.GetComponentData<CompanionView>(ghost);
            if (had.OwnerGhostId == view.OwnerGhostId && had.Box == view.Box && had.Stage == view.Stage && had.SpeedStep == view.SpeedStep
                && had.State == view.State && had.Stance == view.Stance && had.HpPermille == view.HpPermille && had.Warps == view.Warps
                && had.Strikes == view.Strikes && had.Pulses == view.Pulses && had.FightGhostId == view.FightGhostId
                && had.ActingStance == view.ActingStance && had.Taunts == view.Taunts) return;
            em.SetComponentData(ghost, view);
        }

        // ------------------------------------------------------------------ phase 3: the DPS and HEALER stances

        static CompanionFightTuning FightOf(PlayerMounts mounts) =>
            mounts != null && mounts.Settings != null ? mounts.Settings.CompanionFight : CompanionFightTuning.Default;

        static string StanceName(CompanionStance s) =>
            s == CompanionStance.Dps ? "DPS" : s == CompanionStance.Healer ? "HEALER" : s == CompanionStance.Tank ? "TANK" : "FREE";

        CompanionFightTuning FightTuningOfAny()
        {
            foreach (Live live in m_Live.Values)
                if (live.Mounts != null && live.Mounts.Settings != null) return live.Mounts.Settings.CompanionFight;
            return CompanionFightTuning.Default;
        }

        /// <summary>A combat stance acts as FREE while its owner is in PvP: a PvP zone, a duel or a Crossfire battlefield (FREE is the only PvP stance).</summary>
        static bool OwnerInPvp(PlayerMounts mounts, Vector3 ownerAt) =>
            ZoneRules.AlwaysOnPvp || CrossfireArea.AnyContains(ownerAt, false) || (mounts.TryGetComponent(out PlayerDuel duel) && duel.IsDuelling);

        void Look(EntityManager em, float now)
        {
            m_Seeking.Clear();
            foreach (Live live in m_Live.Values)
            {
                if (live.Leaving || live.Mounts == null) continue;
                var chosen = (CompanionStance)((IBodyMountCompanion)live.Mounts).CompanionStance;
                bool combat = CompanionFightRules.IsCombat(chosen);
                bool inPvp = combat && OwnerInPvp(live.Mounts, live.OwnerAt);
                if (combat && inPvp != live.InPvp)
                {
                    string mount = live.Mounts.Selected != null ? live.Mounts.Selected.displayName : "mount";
                    live.Mounts.TellOwner(inPvp ? $"PvP here: your {mount} stays out of the fight (FREE) until you leave."
                        : $"Your {mount} is back in its {StanceName(chosen)} stance.");
                }
                live.InPvp = inPvp;
                live.Acting = CompanionFightRules.Effective(chosen, inPvp);
                if (live.Acting == CompanionStance.Dps || live.Acting == CompanionStance.Healer || live.Acting == CompanionStance.Tank) m_Seeking.Add(live);
                else { live.Fight = 0; live.HasGoal = false; }
            }
            if (m_Seeking.Count == 0) return;   // nothing in a combat stance: no query, no job
            Looks++;

            using var ghosts = m_Hostiles.ToComponentDataArray<GhostInstance>(Allocator.TempJob);
            using var targets = m_Hostiles.ToComponentDataArray<HostileTarget>(Allocator.TempJob);
            using var brains = m_Hostiles.ToComponentDataArray<HostileBrain>(Allocator.TempJob);
            using var lives = m_Hostiles.ToComponentDataArray<HostileLife>(Allocator.TempJob);
            using var places = m_Hostiles.ToComponentDataArray<LocalTransform>(Allocator.TempJob);
            using var hostileEntities = m_Hostiles.ToEntityArray(Allocator.TempJob);
            var seen = new NativeArray<HostileSeen>(ghosts.Length, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < ghosts.Length; i++)
                seen[i] = new HostileSeen
                {
                    GhostId = ghosts[i].ghostId, Feet = places[i].Position, Radius = lives[i].RadiusMm / 1000f,
                    TargetGhostId = targets[i].GhostId, Alive = brains[i].Mode != HostileMode.Dead,
                };
            var seeks = new NativeArray<CompanionSeek>(m_Seeking.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var picked = new NativeArray<int>(m_Seeking.Count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < m_Seeking.Count; i++)
            {
                Live live = m_Seeking[i];
                int ownerTarget = em.Exists(live.OwnerGhost) && em.HasComponent<CombatState>(live.OwnerGhost)
                    ? em.GetComponentData<CombatState>(live.OwnerGhost).TargetGhostId : 0;
                seeks[i] = new CompanionSeek
                {
                    OwnerGhostId = live.OwnerGhostId, Owner = live.OwnerAt, OwnerTarget = ownerTarget, Current = live.Fight,
                    Range = live.Mounts.Settings != null ? live.Mounts.Settings.companionFightRange : CompanionFightTuning.Default.Range,
                    SelfGhostId = live.SelfGhostId,
                };
            }
            new CompanionPickJob { Seeks = seeks, Hostiles = seen, Picked = picked }.ScheduleParallel(seeks.Length, 8, default).Complete();

            try
            {
                for (int i = 0; i < m_Seeking.Count; i++)
                {
                    Live live = m_Seeking[i];
                    CompanionFightTuning t = live.Mounts.Settings != null ? live.Mounts.Settings.CompanionFight : CompanionFightTuning.Default;
                    int at = picked[i];
                    if (live.Acting == CompanionStance.Dps) Dps(live, at >= 0 ? seen[at] : default, at >= 0, t, now);
                    else if (live.Acting == CompanionStance.Tank) Tank(em, live, at, seen, hostileEntities, t, now);
                    else Heal(live, at >= 0 ? seen[at].GhostId : 0, t, now);
                }
            }
            finally
            {
                seen.Dispose();
                seeks.Dispose();
                picked.Dispose();
            }
        }

        void Dps(Live live, in HostileSeen hostile, bool has, in CompanionFightTuning t, float now)
        {
            live.Fight = has ? hostile.GhostId : 0;
            if (!has) { live.HasGoal = false; return; }
            Vector3 feet = hostile.Feet;
            Vector3 goal = CompanionFightRules.StrikeSpot(live.Follow.Position, feet, hostile.Radius, t);
            goal.y = live.OwnerAt.y;
            live.Goal = goal;
            live.FaceAt = feet;
            live.HasGoal = true;
            if (now < live.NextStrikeAt || !CompanionFightRules.InReach(live.Follow.Position, feet, hostile.Radius, t)) return;

            Transform body = CombatTargetRegistry.BodyOf(World, hostile.GhostId);
            if (body == null || !body.TryGetComponent(out IDamageable struck) || !struck.IsAlive) return;
            if (!live.Mounts.TryGetComponent(out CharacterCombatStats ownerStats)) return;
            live.NextStrikeAt = now + Mathf.Max(0.2f, t.StrikeSeconds);
            DamageResult result = CompanionFightRules.Strike(CombatCalculator.CalculateAttack(ownerStats, struck.CombatStats), t);
            struck.TakeDamage(result, ownerStats);   // in the owner's name: the kill and the tag are theirs; NoThreat keeps it out of aggro
            string label = live.Mounts.Selected != null ? live.Mounts.Selected.displayName : "Mount";
            m_Hits ??= World.GetExistingSystemManaged<HitRecordServerSystem>();
            m_Hits?.RecordFor(live.OwnerGhost, ownerStats, struck, result, 3, true, label);
            HitNumbers.Send(World, live.NetworkId, body.position + Vector3.up * 1.5f, result, true);
            live.Strikes++;
            StrikesDealt++;
            live.Mounts.CountCompanionFight(true);
        }

        /// <summary>
        /// TANK (phase 4): to its spot in front of the Hostile it fights (as DPS, but it never strikes), and every TauntSeconds a taunt for its own
        /// ghost id on that Hostile and on every Hostile after its owner within TauntRadius: the threat system lifts it above the top, so they turn
        /// on it. It deals no damage and does not heal; its body takes less (<see cref="CompanionBody.TakenScale"/>, set in the step).
        /// </summary>
        void Tank(EntityManager em, Live live, int at, NativeArray<HostileSeen> seen, NativeArray<Entity> hostiles, in CompanionFightTuning t, float now)
        {
            bool has = at >= 0;
            live.Fight = has ? seen[at].GhostId : 0;
            if (!has) { live.HasGoal = false; return; }
            HostileSeen fight = seen[at];
            Vector3 feet = fight.Feet;
            Vector3 goal = CompanionFightRules.StrikeSpot(live.Follow.Position, feet, fight.Radius, t);
            goal.y = live.OwnerAt.y;
            live.Goal = goal;
            live.FaceAt = feet;
            live.HasGoal = true;
            if (!CompanionFightRules.TauntDue(true, now, live.NextTauntAt) || live.SelfGhostId == 0) return;
            if (!m_BrainTick.TryGetSingleton(out BrainTick tick)) return;
            live.NextTauntAt = now + Mathf.Max(0.5f, t.TauntSeconds);
            var taunt = new BrainEvent { Kind = BrainEventKind.Taunt, GhostId = live.SelfGhostId, Tick = tick.Tick + 1 };
            float3 self = live.Follow.Position;
            int written = 0;
            for (int i = 0; i < seen.Length; i++)
            {
                if (i != at && !CompanionFightPick.Tauntable(seen[i], live.OwnerGhostId, self, t.TauntRadius)) continue;
                if (!em.Exists(hostiles[i]) || !em.HasBuffer<BrainEvent>(hostiles[i])) continue;
                em.GetBuffer<BrainEvent>(hostiles[i]).Add(taunt);
                written++;
            }
            if (written == 0) return;
            TauntsWritten += written;
            live.Taunts++;
            live.Mounts.CountCompanionTaunt();
        }

        void Heal(Live live, int fight, in CompanionFightTuning t, float now)
        {
            live.Fight = fight;
            live.HasGoal = false;
            if (now < live.NextPulseAt || !live.Mounts.TryGetComponent(out PlayerResources owner)) return;
            if (!CompanionFightRules.ShouldHeal(fight != 0, owner.IsAlive, owner.Health, owner.MaxHealth, t)) return;
            live.NextPulseAt = now + Mathf.Max(0.2f, t.PulseSeconds);
            owner.Heal(CompanionFightRules.PulseHeal(owner.MaxHealth, t));   // a heal puts no threat on a Hostile here (none is sent)
            live.Pulses++;
            PulsesGiven++;
            live.Mounts.CountCompanionFight(false);
        }

        static float CompanionFlat(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        /// <summary>Degrees about Y of a rotation's forward.</summary>
        public static float YawOf(quaternion rotation)
        {
            float3 f = math.mul(rotation, new float3(0f, 0f, 1f));
            return math.degrees(math.atan2(f.x, f.z));
        }
    }
}
