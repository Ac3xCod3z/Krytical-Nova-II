using System;
using System.Collections.Generic;
using KryticalNova.Brains;
using KryticalNova.Core;
using KryticalNova.Hostiles.Entities;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities;
using KryticalNova.World;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Townsfolk
{
    using World = Unity.Entities.World;   // inside the namespace: KryticalNova.World (a namespace) would win otherwise

    /// <summary>Marks a server world that has town life (made by <see cref="TownsfolkSetup"/>).</summary>
    public struct TownsfolkOn : IComponentData { }

    /// <summary>The townsperson ghost prefab, known to the world as a singleton (baked in the player ghost subscene).</summary>
    public struct TownsfolkGhostPrefab : IComponentData
    {
        public Entity Prefab;
    }

    /// <summary>
    /// Switches town life on in a new server world (plan 3.3): ON in a built server and in the Editor's Play, OFF in EditMode tests (a test
    /// world made by another system's test never gets a crowd) and wherever <c>KN_TOWNSFOLK=0</c>; <c>1</c> forces it on.
    /// </summary>
    public static class TownsfolkSetup
    {
        public const string EnableVariable = "KN_TOWNSFOLK";
        public const string DiagVariable = "KN_TOWN_DIAG";

        /// <summary>Install on every new server world (null: from <see cref="EnableVariable"/>).</summary>
        public static bool? Enabled;

        public static bool IsOn(bool playing, string variable) => variable == "1" || (variable != "0" && playing);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Hook()
        {
            Enabled = null;
            KnWorldHooks.ServerWorldMade -= InstallIfOn;
            KnWorldHooks.ServerWorldMade += InstallIfOn;
        }

        static void InstallIfOn(World server)
        {
            if (Enabled ?? IsOn(Application.isPlaying, Environment.GetEnvironmentVariable(EnableVariable))) Install(server);
        }

        public static void Install(World server)
        {
            using var q = server.EntityManager.CreateEntityQuery(typeof(TownsfolkOn));
            if (q.IsEmpty) server.EntityManager.CreateSingleton<TownsfolkOn>();
        }
    }

    /// <summary>
    /// Feeds the players into the Brain's senses when the Hostiles' feeder does not run (no Hostiles in this world): the same rows it
    /// writes (ghost id, feet, alive, on a wall). With the Hostiles on, theirs fills the list and this does nothing, so their rows are
    /// never overwritten.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(BrainTickSystem))]
    [UpdateBefore(typeof(SenseBuildSystem))]
    [BurstCompile]
    public partial struct TownPlayerFeedSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TownsfolkConfig>();
            state.RequireForUpdate<SenseWorld>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            if (SystemAPI.HasSingleton<HostileBrainConfig>()) return;
            SenseWorld sense = SystemAPI.GetSingleton<SenseWorld>();
            sense.Players.Clear();
            foreach (var (ghost, pose, vitals, move, entity) in SystemAPI
                         .Query<RefRO<GhostInstance>, RefRO<PlayerPose>, RefRO<PlayerVitals>, RefRO<MoveState>>()
                         .WithAll<PlayerGhostTag>().WithEntityAccess())
            {
                bool alive = vitals.ValueRO.MaxHealthMilli == 0 || vitals.ValueRO.HealthMilli > 0;
                byte flags = alive ? SensedFlags.Alive : SensedFlags.Dead;
                if (move.ValueRO.Wall != 0) flags |= SensedFlags.OnWall;
                sense.Players.Add(new SensedPlayer { Ghost = entity, GhostId = ghost.ValueRO.ghostId, Feet = (float3)pose.ValueRO.Mm * 0.001f, Flags = flags });
            }
        }
    }

    /// <summary>
    /// The server's town-life bridge (plan 3.3, 3.5, 3.7), before the brain tick: installs the config, the personalities and (when the
    /// Hostiles did not) the Brain's settings; builds the area's smart spots from its asset, snapped onto the NavMesh, plus walk points
    /// sampled on it; refreshes the clock and weather once a second; and keeps the crowd at the hour's numbers: townsfolk come out of the
    /// home doors (a few a second), the ones over the numbers are sent home, the ones home are removed. GM: <see cref="Find"/>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(BrainSimulationGroup))]
    public partial class TownsfolkServerSystem : SystemBase
    {
        const int DefaultTickRate = 30;
        const uint FirstStableId = 1_000_000;   // apart from the Hostiles' own count
        const float AreaRetrySeconds = 5f, CrowdSeconds = 0.25f, ClockSeconds = 1f, DiagSeconds = 10f;

        public static TownsfolkServerSystem Find()
        {
            foreach (World w in World.All)
                if (w.IsCreated && w.IsServer() && w.GetExistingSystemManaged<TownsfolkServerSystem>() is { } system) return system;
            return null;
        }

        /// <summary>Where the clock is read (tests, or a GM time slider): hours 0-24 and rain; null = the world's clock.</summary>
        public static Func<(float hours, bool raining)> ClockOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ClockOverride = null;

        TownsfolkWorldAssets m_Assets;
        TownLifeArea m_Area;
        bool m_Installed, m_AssetsMissingLogged, m_RootMissingLogged, m_PrefabMissingLogged;
        double m_NextArea, m_NextCrowd, m_NextClock, m_NextDiag;
        uint m_NextStableId = FirstStableId, m_SpawnCount;
        // the Sky Lift arrival (owner 14 Nov 2026: no more than a share of the crowd there)
        float3 m_Arrival;
        bool m_HasArrival;
        int m_NearArrival;
        readonly List<Vector3> m_SpreadRaw = new(), m_HandCentres = new();
        readonly List<TownSpread.Point> m_Spread = new();

        /// <summary>Points spread over the district when the area was built, and townsfolk near the arrival now (gm.town, the probes).</summary>
        public static int SpreadPoints, NearArrivalNow;
        /// <summary>While true (a probe, a GM heatmap), where every townsperson out stands, refreshed with the crowd count.</summary>
        public static bool RecordPositions;
        public static readonly List<Vector3> Positions = new();
        int m_TickRate = DefaultTickRate;
        readonly Dictionary<byte, int> m_Count = new(), m_Extra = new();
        readonly Dictionary<byte, List<Entity>> m_ByPersonality = new();
        readonly List<Entity> m_Gone = new();
        EntityQuery m_Folk;
        System.Random m_Rng;

        /// <summary>GM overrides of the caps (-1 = the area's / settings').</summary>
        public int DayCapOverride = -1, NightCapOverride = -1;
        public bool Diag;

        public int Out { get; private set; }
        public int Wanted { get; private set; }
        public int SpotCount { get; private set; }
        public int SlotCount { get; private set; }
        public string AreaName => m_Area != null ? m_Area.name : "none";
        public IReadOnlyDictionary<byte, int> CountByPersonality => m_Count;
        public TownsfolkWorldAssets Assets => m_Assets;

        protected override void OnCreate()
        {
            RequireForUpdate<TownsfolkOn>();
            m_Folk = GetEntityQuery(ComponentType.ReadWrite<TownLife>());
            Diag = Environment.GetEnvironmentVariable(TownsfolkSetup.DiagVariable) == "1";
        }

        protected override void OnUpdate()
        {
            double now = SystemAPI.Time.ElapsedTime;
            if (!m_Installed && !Install()) return;
            if (!SystemAPI.TryGetSingletonEntity<TownArea>(out Entity area)) return;
            if (SystemAPI.GetComponent<TownArea>(area).Ready == 0)
            {
                if (now < m_NextArea) return;
                m_NextArea = now + AreaRetrySeconds;
                BuildArea(area);
                return;
            }
            if (now >= m_NextClock) { m_NextClock = now + ClockSeconds; RefreshClock(); }
            if (now >= m_NextCrowd) { m_NextCrowd = now + CrowdSeconds; KeepCrowd(area); }
            if (Diag && now >= m_NextDiag) { m_NextDiag = now + DiagSeconds; Debug.Log("[KnTown] " + Status()); }
        }

        bool Install()
        {
            m_Assets = Resources.Load<TownsfolkWorldAssets>(TownsfolkWorldAssets.ResourcePath);
            if (m_Assets == null || m_Assets.settings == null || m_Assets.brain == null || m_Assets.areas.Length == 0 || m_Assets.areas[0] == null)
            {
                if (!m_AssetsMissingLogged) Debug.LogWarning("[Townsfolk] Resources/" + TownsfolkWorldAssets.ResourcePath + " is missing or empty: no town life (run Krytical Nova ▸ Townsfolk ▸ Build Assets).");
                m_AssetsMissingLogged = true;
                return false;
            }
            m_Area = m_Assets.areas[0];
            m_Rng = new System.Random((int)m_Area.seed);
            EntityManager em = EntityManager;
            if (SystemAPI.TryGetSingleton(out ClientServerTickRate rate) && rate.SimulationTickRate > 0) m_TickRate = rate.SimulationTickRate;
            // the Brain is shared: the Hostiles' install made it when they are on; otherwise town life makes it
            if (!SystemAPI.HasSingleton<BrainConfig>()) em.CreateSingleton(BrainConfig.From(m_Assets.brain, m_TickRate));
            if (!SystemAPI.HasSingleton<PathBackend>())
                em.CreateSingleton(new PathBackend { Kind = PathBackendKind.NavWorld, AgentTypeId = 0, Extents = new float3(1f, 2f, 1f) });
            Entity config = em.CreateSingleton(TownsfolkConfig.From(m_Assets.settings, m_TickRate));
            DynamicBuffer<PersonalityRow> rows = em.AddBuffer<PersonalityRow>(config);
            foreach (TownsfolkPersonality p in m_Assets.personalities) if (p != null) rows.Add(RowOf(p));
            int personalities = rows.Length;   // the buffer is invalid after the structural changes below
            em.CreateSingleton(new TownClock { Hours = 12f, Period = (byte)TownPeriod.Day });
            Entity a = em.CreateSingleton(new TownArea { Radius = m_Area.radius, Cast = m_Area.cast != null ? m_Area.cast.id : (byte)0 });
            em.AddBuffer<TownSpot>(a);
            em.AddBuffer<TownSlot>(a);
            em.AddBuffer<TownScoreCache>(a);
            m_Installed = true;
            RefreshClock();
            Debug.Log($"[Townsfolk] Town life installed in {World.Name}: area {m_Area.name}, {personalities} personalities, {m_TickRate} Hz.");
            return true;
        }

        public static PersonalityRow RowOf(TownsfolkPersonality p)
        {
            var row = new PersonalityRow
            {
                Id = p.id,
                StopChance = Mathf.Clamp01(p.stopChance),
                StaySeconds = new float2(Mathf.Max(1f, p.staySeconds.x), Mathf.Max(p.staySeconds.x, p.staySeconds.y)),
                Crowding = p.crowdingDislike,
                NeedsLight = (byte)(p.needsLight ? 1 : 0),
                LikesPlayersNear = (byte)(p.likesPlayersNear ? 1 : 0),
                Rain = (byte)p.rain,
                DeclinesInvites = (byte)(p.declinesInvites ? 1 : 0),
                ChatChance = Mathf.Clamp01(p.chatChance),
            };
            foreach (TownsfolkPersonality other in p.chatsWith) if (other != null && other.id < 32) row.ChatsWith |= 1u << other.id;
            int kinds = (int)TownSpotKind.JogPoint + 1;
            for (int k = 0; k < kinds; k++) row.Likes.Add(p.Likes((TownSpotKind)k));
            return row;
        }

        /// <summary>The area's spots in the world: its root found by name, each spot and slot snapped onto the NavMesh, then walk points sampled on it.</summary>
        void BuildArea(Entity area)
        {
            GameObject rootGo = GameObject.Find(m_Area.rootName);
            if (rootGo == null)
            {
                if (!m_RootMissingLogged) Debug.Log($"[Townsfolk] No '{m_Area.rootName}' in the loaded scenes yet: town life waits for it.");
                m_RootMissingLogged = true;
                return;
            }
            Transform root = rootGo.transform;
            TownsfolkSettings s = m_Assets.settings;
            var spots = new List<TownSpot>();
            var slots = new List<TownSlot>();
            int dropped = 0;
            float rootYaw = root.eulerAngles.y;
            GameObject arrivalGo = GameObject.Find(SkyLiftLink.ArrivalMarker);
            m_HasArrival = arrivalGo != null;
            m_Arrival = m_HasArrival ? (float3)arrivalGo.transform.position : default;
            float arrivalSq = s.arrivalRadius * s.arrivalRadius;
            m_HandCentres.Clear();
            foreach (TownLifeArea.Spot spot in m_Area.spots)
            {
                Vector3 world = root.TransformPoint(new Vector3(spot.at.x, m_Area.deckHeight, spot.at.y));
                if (!Snap(world, s.snapToNavMesh, out Vector3 centre)) { dropped++; continue; }
                m_HandCentres.Add(centre);
                bool nearArrival = m_HasArrival && math.distancesq(((float3)centre).xz, m_Arrival.xz) <= arrivalSq;
                var row = new TownSpot
                {
                    Pos = centre, Kind = (byte)spot.kind, Flags = (byte)((byte)spot.flags | (nearArrival ? (byte)TownSpotFlags.NearArrival : (byte)0)), OpenFrom = (byte)Mathf.Clamp(spot.openFrom, 0, 24),
                    OpenTo = (byte)Mathf.Clamp(spot.openTo, 0, 24), OnlyFor = (byte)(spot.onlyFor != null ? spot.onlyFor.id + 1 : 0),
                    FirstSlot = (ushort)slots.Count,
                };
                int count = Mathf.Clamp(spot.slots, 1, 6);
                for (int k = 0; k < count; k++)
                {
                    TownLifeRules.SlotPlace(centre, spot.yaw + rootYaw, row.Kind, k, count, spot.spacing, out float3 p, out float yaw);
                    if (!Snap(p, 1.5f, out Vector3 snapped)) continue;
                    slots.Add(new TownSlot { Pos = snapped, Yaw = yaw, Spot = (ushort)spots.Count });
                    row.SlotCount++;
                }
                if (row.SlotCount == 0) { dropped++; continue; }
                spots.Add(row);
            }
            // walk points: strolls between spots, sampled on the area's NavMesh (deterministic from the area's seed)
            Vector3 middle = root.TransformPoint(new Vector3(0f, m_Area.deckHeight, 0f));
            int walks = AddSpread(middle, spots, slots, arrivalSq);
            for (int attempt = 0; attempt < s.walkPoints * 4 && walks < s.walkPoints; attempt++)
            {
                float a = (float)(m_Rng.NextDouble() * Math.PI * 2.0), r = m_Area.radius * Mathf.Sqrt((float)m_Rng.NextDouble());
                Vector3 probe = middle + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                if (!Snap(probe, 6f, out Vector3 p) || Mathf.Abs(p.y - middle.y) > 12f) continue;
                spots.Add(new TownSpot { Pos = p, Kind = (byte)TownSpotKind.Walk, FirstSlot = (ushort)slots.Count, SlotCount = 1 });
                slots.Add(new TownSlot { Pos = p, Spot = (ushort)(spots.Count - 1) });
                walks++;
            }
            EntityManager em = EntityManager;
            DynamicBuffer<TownSpot> spotBuffer = em.GetBuffer<TownSpot>(area);
            spotBuffer.Clear();
            foreach (TownSpot t in spots) spotBuffer.Add(t);
            DynamicBuffer<TownSlot> slotBuffer = em.GetBuffer<TownSlot>(area);
            slotBuffer.Clear();
            foreach (TownSlot t in slots) slotBuffer.Add(t);
            SpotCount = spots.Count;
            SlotCount = slots.Count;
            uint version = em.GetComponentData<TownArea>(area).Version + 1;   // the mind's score cache rebuilds with the spots
            em.SetComponentData(area, new TownArea { Centre = middle, Radius = m_Area.radius, Cast = m_Area.cast != null ? m_Area.cast.id : (byte)0, Ready = 1, Version = version,
                ArrivalFullScore = s.arrivalFullScore });
            Debug.Log($"[Townsfolk] {m_Area.name}: {spots.Count - walks} smart spots ({slots.Count - walks} slots), {walks} walk points on the NavMesh; {dropped} spots off the NavMesh dropped.");
        }

        /// <summary>
        /// Life over the whole district (owner 14 Nov 2026): points spread over every walkable floor of the area's NavMesh, sorted into
        /// viewpoints at the rim, quiet lean spots in the back streets, places to stand and walk points (<see cref="TownSpread"/>).
        /// Returns the walk points added.
        /// </summary>
        int AddSpread(Vector3 middle, List<TownSpot> spots, List<TownSlot> slots, float arrivalSq)
        {
            TownsfolkSettings s = m_Assets.settings;
            NavMeshTriangulation mesh = NavMesh.CalculateTriangulation();
            TownSpread.Sample(mesh.vertices, mesh.indices, middle, m_Area.radius, s.spreadHeight, s.spreadSpacing, s.spreadMostPoints, m_Area.seed, m_SpreadRaw);
            var edgeDistance = new float[m_SpreadRaw.Count];
            var edgeAt = new Vector3[m_SpreadRaw.Count];
            for (int i = 0; i < m_SpreadRaw.Count; i++)
            {
                if (NavMesh.FindClosestEdge(m_SpreadRaw[i], out NavMeshHit edge, NavMesh.AllAreas)) { edgeDistance[i] = edge.distance; edgeAt[i] = edge.position; }
                else { edgeDistance[i] = float.MaxValue; edgeAt[i] = m_SpreadRaw[i]; }
            }
            TownSpread.Sort(m_SpreadRaw, edgeDistance, edgeAt, middle, m_HandCentres, s, m_Area.seed, m_Spread);
            int walks = 0, places = 0;
            foreach (TownSpread.Point point in m_Spread)
            {
                if (!Snap(point.Pos, 1.5f, out Vector3 p)) continue;
                bool nearArrival = m_HasArrival && math.distancesq(((float3)p).xz, m_Arrival.xz) <= arrivalSq;
                byte flags = (byte)(nearArrival ? TownSpotFlags.NearArrival : TownSpotFlags.None);
                byte kind = (byte)TownSpotKind.Walk;
                if (point.Use == TownSpread.Use.Rim) { kind = (byte)TownSpotKind.Viewpoint; flags |= (byte)TownSpotFlags.Quiet; }
                else if (point.Use == TownSpread.Use.BackStreet) { kind = (byte)TownSpotKind.LeanWall; flags |= (byte)TownSpotFlags.Quiet; }
                else if (point.Use == TownSpread.Use.Stand) kind = (byte)TownSpotKind.Viewpoint;
                spots.Add(new TownSpot { Pos = p, Kind = kind, Flags = flags, FirstSlot = (ushort)slots.Count, SlotCount = 1 });
                slots.Add(new TownSlot { Pos = p, Yaw = math.radians(point.Yaw), Spot = (ushort)(spots.Count - 1) });
                if (kind == (byte)TownSpotKind.Walk) walks++; else places++;
            }
            SpreadPoints = walks + places;
            Debug.Log($"[Townsfolk] {m_Area.name}: {walks} walk points and {places} places to stand spread over the whole district (of {m_SpreadRaw.Count} found on the NavMesh).");
            return walks;
        }

        static bool Snap(Vector3 at, float within, out Vector3 snapped)
        {
            snapped = at;
            if (!NavMesh.SamplePosition(at, out NavMeshHit hit, Mathf.Max(0.1f, within), NavMesh.AllAreas)) return false;
            snapped = hit.position;
            return true;
        }

        void RefreshClock()
        {
            float hours = 12f;
            bool raining = false;
            if (ClockOverride != null) (hours, raining) = ClockOverride();
            else if (WorldEnvironmentNetwork.TryGetState(out WorldEnvironmentState state))
            {
                double now = NetClock.Now;
                hours = state.TimeOfDay01(now) * 24f;
                WorldEnvironmentNetwork env = WorldEnvironmentNetwork.Instance;
                bool past = state.TransitionSeconds <= 0f || now >= state.TransitionStart + state.TransitionSeconds * 0.5;
                WeatherProfile weather = env != null && env.Catalog != null ? env.Catalog.Get(past ? state.ToWeather : state.FromWeather) : null;
                raining = weather != null && weather.precipitation == PrecipitationType.Rain;
            }
            TownsfolkConfig c = SystemAPI.GetSingleton<TownsfolkConfig>();
            SystemAPI.SetSingleton(new TownClock { Hours = hours, Period = TownLifeRules.PeriodOf(hours, c.PeriodStarts), Raining = (byte)(raining ? 1 : 0) });
        }

        /// <summary>Keeps every personality at the hour's number (the area's mix within the cap): out the doors, home when over.</summary>
        void KeepCrowd(Entity area)
        {
            EntityManager em = EntityManager;
            TownClock clock = SystemAPI.GetSingleton<TownClock>();
            TownsfolkSettings s = m_Assets.settings;
            m_Count.Clear();
            foreach (List<Entity> list in m_ByPersonality.Values) list.Clear();
            m_Gone.Clear();
            using (NativeArray<Entity> folk = m_Folk.ToEntityArray(Allocator.Temp))
                foreach (Entity e in folk)
                {
                    TownLife L = em.GetComponentData<TownLife>(e);
                    if (L.State == TownLifeState.Gone) { m_Gone.Add(e); continue; }
                    if (L.SentHome != 0 || L.State == TownLifeState.GoingHome) continue;
                    m_Count[L.Personality] = (m_Count.TryGetValue(L.Personality, out int n) ? n : 0) + 1;
                    if (!m_ByPersonality.TryGetValue(L.Personality, out List<Entity> mine)) m_ByPersonality[L.Personality] = mine = new List<Entity>();
                    mine.Add(e);
                }
            foreach (Entity e in m_Gone) em.DestroyEntity(e);
            CountNearArrival(area, s);

            int dayCap = DayCapOverride >= 0 ? DayCapOverride : m_Area.dayCap > 0 ? m_Area.dayCap : s.dayCap;
            int nightCap = NightCapOverride >= 0 ? NightCapOverride : m_Area.nightCap > 0 ? m_Area.nightCap : s.nightCap;
            int cap = TownLifeRules.Cap(dayCap, nightCap, s.hardCap, clock.Period, clock.Raining != 0, s.rainCrowdShare);
            // the mix's numbers for the hour, scaled down together when they pass the cap
            int sum = 0;
            Span<int> wanted = stackalloc int[m_Area.mix.Length];
            for (int i = 0; i < m_Area.mix.Length; i++)
            {
                TownLifeArea.MixRow row = m_Area.mix[i];
                if (row.personality == null) continue;
                bool shy = clock.Raining != 0 && (row.personality.rain == RainBehaviour.GoHome || row.personality.rain == RainBehaviour.PackUp);
                int w = shy ? 0 : TownLifeRules.Wanted(row.day, row.night, row.personality.outChance, clock.Period);
                if (m_Extra.TryGetValue(row.personality.id, out int extra)) w += extra;
                wanted[i] = w;
                sum += w;
            }
            float scale = sum > cap && sum > 0 ? (float)cap / sum : 1f;
            int budget = Mathf.Max(1, Mathf.CeilToInt(s.spawnsPerSecond * CrowdSeconds));
            int total = 0;
            Out = 0;
            foreach (int n in m_Count.Values) Out += n;
            for (int i = 0; i < m_Area.mix.Length; i++)
            {
                TownsfolkPersonality p = m_Area.mix[i].personality;
                if (p == null) continue;
                int want = Mathf.FloorToInt(wanted[i] * scale + 0.5f);
                total += want;
                int have = m_Count.TryGetValue(p.id, out int h) ? h : 0;
                if (have > want && m_ByPersonality.TryGetValue(p.id, out List<Entity> list))
                    for (int k = 0; k < have - want && k < list.Count; k++)
                    {
                        TownLife L = em.GetComponentData<TownLife>(list[k]);
                        L.SentHome = 1;
                        em.SetComponentData(list[k], L);
                    }
                for (int k = have; k < want && budget > 0 && Out < s.hardCap; k++)
                {
                    if (!Spawn(area, p, clock)) break;
                    budget--;
                    Out++;
                }
            }
            Wanted = total;
        }

        /// <summary>Townsfolk near the Sky Lift arrival now; the area's spots there score less while it holds more than its share.</summary>
        void CountNearArrival(Entity area, TownsfolkSettings s)
        {
            EntityManager em = EntityManager;
            m_NearArrival = 0;
            int out_ = 0;
            if (RecordPositions) Positions.Clear();
            if (m_HasArrival || RecordPositions)
                using (NativeArray<Entity> folk = m_Folk.ToEntityArray(Allocator.Temp))
                    foreach (Entity e in folk)
                    {
                        if (!em.HasComponent<LocalTransform>(e) || em.GetComponentData<TownLife>(e).State == TownLifeState.Gone) continue;
                        out_++;
                        if (RecordPositions) Positions.Add(em.GetComponentData<LocalTransform>(e).Position);
                        if (m_HasArrival && math.distancesq(em.GetComponentData<LocalTransform>(e).Position.xz, m_Arrival.xz) <= s.arrivalRadius * s.arrivalRadius) m_NearArrival++;
                    }
            NearArrivalNow = m_NearArrival;
            TownArea a = em.GetComponentData<TownArea>(area);
            byte crowded = (byte)(TownSpread.ArrivalCrowded(m_NearArrival, out_, s.arrivalMostShare) ? 1 : 0);
            if (crowded == a.ArrivalCrowded) return;
            a.ArrivalCrowded = crowded;
            a.Version++;   // the score cache rebuilds with the arrival's spots at their new score
            em.SetComponentData(area, a);
        }

        bool Spawn(Entity area, TownsfolkPersonality p, in TownClock clock)
        {
            if (!SystemAPI.TryGetSingleton(out TownsfolkGhostPrefab prefab) || prefab.Prefab == Entity.Null)
            {
                if (!m_PrefabMissingLogged) Debug.LogWarning("[Townsfolk] No TownsfolkGhostPrefab in the ghost subscene: run Krytical Nova ▸ Townsfolk ▸ Build Assets.");
                m_PrefabMissingLogged = true;
                return false;
            }
            // a worker comes out on the shift that is running; none running: not now
            float shiftStart = 0f, shiftEnd = 0f;
            if (p.shifts.Length > 0)
            {
                bool found = false;
                foreach (Vector2Int shift in p.shifts)
                    if (TownLifeRules.InHours(clock.Hours, shift.x, shift.y)) { shiftStart = shift.x; shiftEnd = shift.y; found = true; break; }
                if (!found) return false;
            }
            EntityManager em = EntityManager;
            DynamicBuffer<TownSpot> spots = em.GetBuffer<TownSpot>(area);
            int doors = 0, walks = 0;
            for (int i = 0; i < spots.Length; i++)
            {
                if (spots[i].Kind == (byte)TownSpotKind.HomeDoor) doors++;
                else if (spots[i].Kind == (byte)TownSpotKind.Walk) walks++;
            }
            // most step out somewhere on the district (round a corner), the rest from a home door; never at the arrival while it
            // already holds its share of the crowd (owner 14 Nov 2026: life everywhere, not by the Sky Lift)
            TownsfolkSettings set = m_Assets.settings;
            byte from = doors == 0 || (walks > 0 && m_Rng.NextDouble() < set.spawnAnywhereShare) ? (byte)TownSpotKind.Walk : (byte)TownSpotKind.HomeDoor;
            int choices = from == (byte)TownSpotKind.HomeDoor ? doors : walks;
            float3 at = default;
            bool placed = false;
            for (int attempt = 0; attempt < 8 && !placed && choices > 0; attempt++)
            {
                int pick = m_Rng.Next(choices);
                for (int i = 0; i < spots.Length; i++)
                {
                    if (spots[i].Kind != from || pick-- != 0) continue;
                    if (TownSpread.MayStepOutAt(spots[i].Pos, m_HasArrival, m_Arrival, set.arrivalRadius, m_NearArrival, Out, set.arrivalMostShare))
                    {
                        at = spots[i].Pos;
                        placed = true;
                        m_NearArrival += m_HasArrival && math.distancesq(at.xz, m_Arrival.xz) <= set.arrivalRadius * set.arrivalRadius ? 1 : 0;
                    }
                    break;
                }
            }
            if (!placed) return false;

            TownArea a = em.GetComponentData<TownArea>(area);
            BrainConfig config = SystemAPI.GetSingleton<BrainConfig>();
            TownsfolkSettings s = m_Assets.settings;
            uint seed = math.hash(new uint2(m_Area.seed, ++m_SpawnCount));
            var dice = new Unity.Mathematics.Random(seed | 1u);
            Entity e = em.Instantiate(prefab.Prefab);
            em.AddComponent(e, new ComponentTypeSet(new ComponentType[]
            {
                typeof(BrainTag), typeof(BrainActive), typeof(BrainClock), typeof(BrainDice), typeof(BrainMover), typeof(BrainPath),
                typeof(TownLife), typeof(TownPick), typeof(TownsfolkGhostSource),
            }));
            em.AddBuffer<BrainEvent>(e);
            em.AddBuffer<BrainWaypoint>(e);
            float yaw = dice.NextFloat(0f, 2f * math.PI);
            em.SetComponentData(e, LocalTransform.FromPositionRotation(at, quaternion.RotateY(yaw)));
            uint id = m_NextStableId++;
            em.SetComponentData(e, new BrainClock { StableId = id, Tier = 3 });   // born far: a player near raises it at once (the Brain lowers a tier only after its hysteresis)
            em.SetComponentData(e, new BrainDice { Rng = BrainDiceRules.Create(id, config.WorldSeed ^ seed) });
            em.SetComponentData(e, new BrainMover { Dest = at, Flags = BrainMoverFlags.Stop, Yaw = yaw });
            em.SetComponentData(e, new TownsfolkGhostSource { LastPosition = at });
            em.SetComponentData(e, new TownLife
            {
                Personality = p.id,
                State = TownLifeState.Choose,
                Jogs = (byte)(p.jogs ? 1 : 0),
                WalkSpeed = dice.NextFloat(Mathf.Min(p.walkSpeed.x, p.walkSpeed.y), Mathf.Max(p.walkSpeed.x, p.walkSpeed.y) + 1e-4f),
                JogSpeed = dice.NextFloat(Mathf.Min(p.jogSpeed.x, p.jogSpeed.y), Mathf.Max(p.jogSpeed.x, p.jogSpeed.y) + 1e-4f),
                ShiftStart = shiftStart,
                ShiftEnd = shiftEnd,
                PlanJitter = dice.NextFloat(-s.planJitterHours, s.planJitterHours),
                Seed = seed,
            });
            em.SetComponentData(e, new TownsfolkGhost { Seed = seed, Cast = a.Cast, Personality = p.id, Activity = (byte)TownActivity.Stand });
            return true;
        }

        // ---- GM (gm.town.*) ----

        /// <summary>Everyone sent home and removed at once; the crowd then comes out again by the hour's numbers.</summary>
        public int Clear()
        {
            m_Extra.Clear();
            using NativeArray<Entity> folk = m_Folk.ToEntityArray(Allocator.Temp);
            EntityManager.DestroyEntity(folk);
            return folk.Length;
        }

        /// <summary>Adds <paramref name="count"/> of a personality on top of the hour's number (until <see cref="Clear"/>).</summary>
        public bool AddExtra(byte personality, int count)
        {
            if (m_Assets == null || m_Assets.PersonalityOf(personality) == null) return false;
            bool inMix = false;
            foreach (TownLifeArea.MixRow row in m_Area.mix) if (row.personality != null && row.personality.id == personality) inMix = true;
            if (!inMix) return false;
            m_Extra[personality] = (m_Extra.TryGetValue(personality, out int n) ? n : 0) + Mathf.Max(0, count);
            return true;
        }

        public string Status()
        {
            if (!m_Installed) return "Town life: not installed (no assets, or the world has it off).";
            TownClock clock = SystemAPI.GetSingleton<TownClock>();
            var sb = new System.Text.StringBuilder();
            sb.Append($"Town life on {AreaName}: {Out} out of {Wanted} wanted, {(TownPeriod)clock.Period} {clock.Hours:F1} h{(clock.Raining != 0 ? ", raining" : "")}; {SpotCount} spots.");
            foreach (KeyValuePair<byte, int> kv in m_Count)
            {
                TownsfolkPersonality p = m_Assets.PersonalityOf(kv.Key);
                sb.Append(' ').Append(p != null ? p.displayName : kv.Key.ToString()).Append(' ').Append(kv.Value).Append(';');
            }
            return sb.ToString();
        }
    }
}

namespace KryticalNova.Townsfolk
{
    /// <summary>What the GM window shows of town life (a plain facade: the window knows no Entities types, as Crossfire's view).</summary>
    public static class TownsfolkGm
    {
        public static bool HasServer => TownsfolkServerSystem.Find() != null;
        public static string Status => TownsfolkServerSystem.Find()?.Status() ?? "No town life here (no server, or Resources/TownsfolkWorldAssets not built).";
        public static bool Labels => TownsfolkViewClientSystem.DebugLabels;
    }
}
