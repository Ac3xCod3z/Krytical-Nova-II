using KryticalNova.Brains;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// The town-life mind (plan 3.1, 3.5, 3.7, 3.8): townsfolk are brain entities of the shared Brain (its clock, tiers, dice, mover and
    /// paths); this is their own mind, in place of the Hostiles' idle wander. On each think of a townsperson: at a spot until its stay is
    /// over, then the next pick by its personality, the hour and the weather (the best few smart spots, one at random by score), a stroll
    /// to a walk point when it does not stop, home when its plan or the spawner says so. Far from every player (Brain tier 3, which never
    /// thinks) it takes a plan step every few seconds instead: no walking, it is put at its next place (Kingdom Come). Hostiles never see
    /// a townsperson: it carries none of their components.
    /// <para>
    /// Jobs from the start (owner, 13 Nov 2026), all Burst, nothing managed: (1) the score cache job, only when the hour, period, rain or
    /// spots change; (2) the stale-claim sweep; (3) players-near per spot (parallel, beside the sweep); (4) the think job, parallel per
    /// townsperson, which reads the slots and only writes its own components plus its wishes (a claim, a release, a stay) into lists;
    /// (5) the claim job, single, which applies releases and stays and then hands out slots in StableId order, so two townsfolk never
    /// get one slot and every run of the same seed is the same. Main-thread cost is the scheduling only.
    /// </para>
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(ThinkClockSystem))]
    [BurstCompile]
    public partial struct TownLifeDecideSystem : ISystem
    {
        internal const int MaxBest = 8;

        struct CacheKey : System.IEquatable<CacheKey>
        {
            public int Hour, Rows, Spots;
            public byte Period, Raining;
            public uint Version;

            public bool Equals(CacheKey o) => Hour == o.Hour && Rows == o.Rows && Spots == o.Spots && Period == o.Period && Raining == o.Raining && Version == o.Version;
        }

        CacheKey m_Key;
        bool m_KeyValid;
        EntityQuery m_Folk;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BrainConfig>();
            state.RequireForUpdate<TownsfolkConfig>();
            state.RequireForUpdate<TownClock>();
            state.RequireForUpdate<BrainTick>();
            state.RequireForUpdate<TownArea>();
            state.RequireForUpdate<SenseWorld>();
            m_Folk = SystemAPI.QueryBuilder().WithAll<TownLife, TownPick>().Build();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            Entity areaEntity = SystemAPI.GetSingletonEntity<TownArea>();
            TownArea area = SystemAPI.GetComponent<TownArea>(areaEntity);
            if (area.Ready == 0) return;
            if (!SystemAPI.HasBuffer<TownScoreCache>(areaEntity))
            {
                state.EntityManager.AddBuffer<TownScoreCache>(areaEntity);   // once, before any job of the area runs
                m_KeyValid = false;
            }
            Entity configEntity = SystemAPI.GetSingletonEntity<TownsfolkConfig>();
            BrainConfig bc = SystemAPI.GetSingleton<BrainConfig>();
            TownsfolkConfig c = SystemAPI.GetSingleton<TownsfolkConfig>();
            TownClock clock = SystemAPI.GetSingleton<TownClock>();
            SenseWorld sense = SystemAPI.GetSingleton<SenseWorld>();
            uint now = SystemAPI.GetSingleton<BrainTick>().Tick;
            int spotCount = state.EntityManager.GetBuffer<TownSpot>(areaEntity, true).Length;
            int rowCount = state.EntityManager.GetBuffer<PersonalityRow>(configEntity, true).Length;

            var spotLookup = SystemAPI.GetBufferLookup<TownSpot>(true);
            var rowLookup = SystemAPI.GetBufferLookup<PersonalityRow>(true);
            var slotLookupRw = SystemAPI.GetBufferLookup<TownSlot>();
            var cacheLookupRw = SystemAPI.GetBufferLookup<TownScoreCache>();
            JobHandle deps = state.Dependency;

            // 1. the fixed score part, only when the hour, the period, the rain or the spots changed
            var key = new CacheKey
            {
                Hour = (int)math.floor(TownLifeRules.Wrap(clock.Hours)), Rows = rowCount, Spots = spotCount,
                Period = clock.Period, Raining = clock.Raining, Version = area.Version,
            };
            if (!m_KeyValid || !key.Equals(m_Key))
            {
                m_Key = key;
                m_KeyValid = true;
                deps = new ScoreCacheJob
                {
                    Area = areaEntity, Config = configEntity, Clock = clock,
                    Spots = spotLookup, Rows = rowLookup, Cache = cacheLookupRw,
                }.Schedule(deps);
            }

            // 2. claims of the gone and the stale free themselves (plan 3.5: nothing locks a bench forever)
            JobHandle swept = new StaleClaimSweepJob
            {
                Area = areaEntity, Now = now, Slots = slotLookupRw, Life = SystemAPI.GetComponentLookup<TownLife>(true),
            }.Schedule(deps);

            // 3. which spots have a player near (for those who like an audience), beside the sweep
            var playersNear = CollectionHelper.CreateNativeArray<byte>(math.max(1, spotCount), state.WorldUpdateAllocator);
            JobHandle near = new PlayersNearJob
            {
                Area = areaEntity, Spots = spotLookup, Sense = sense, Cell = bc.PlayerCell, Near = playersNear,
            }.ScheduleParallel(spotCount, 16, state.Dependency);

            // 4. every townsperson thinks in parallel; its wishes go into the lists
            int folk = math.max(1, m_Folk.CalculateEntityCountWithoutFiltering());
            var claims = new NativeList<ClaimWish>(folk, state.WorldUpdateAllocator);
            var slotWrites = new NativeList<SlotWish>(folk, state.WorldUpdateAllocator);
            JobHandle thought = new ThinkJob
            {
                Area = areaEntity, Config = configEntity, C = c, Clock = clock, Now = now,
                Spots = spotLookup, Slots = SystemAPI.GetBufferLookup<TownSlot>(true), Rows = rowLookup,
                Cache = SystemAPI.GetBufferLookup<TownScoreCache>(true), PlayersNear = playersNear,
                Claims = claims.AsParallelWriter(), SlotWrites = slotWrites.AsParallelWriter(),
            }.ScheduleParallel(JobHandle.CombineDependencies(swept, near));

            // 5. one job hands out the slots, in StableId order
            state.Dependency = new ClaimResolveJob
            {
                Area = areaEntity, C = c, Now = now, Claims = claims, SlotWrites = slotWrites,
                Spots = spotLookup, Slots = slotLookupRw,
                Life = SystemAPI.GetComponentLookup<TownLife>(), Mover = SystemAPI.GetComponentLookup<BrainMover>(),
                Pick = SystemAPI.GetComponentLookup<TownPick>(true),
            }.Schedule(thought);
        }

        // ---- the wishes the think job leaves for the claim job ----

        internal struct ClaimWish : System.IComparable<ClaimWish>
        {
            public uint StableId;
            public Entity Who;
            /// <summary>Far from every player (Brain tier 3): its walk is set but it stands, its plan step puts it there.</summary>
            public byte Far;
            public int CompareTo(ClaimWish other) => StableId.CompareTo(other.StableId);
        }

        internal struct SlotWish
        {
            /// <summary>Slot index (not + 1).</summary>
            public int Slot;
            public Entity Who;
            /// <summary>1 = it stays there until <see cref="Until"/>; 0 = it lets go.</summary>
            public byte Stay;
            public uint Until;
        }

        [BurstCompile]
        struct ScoreCacheJob : IJob
        {
            public Entity Area, Config;
            public TownClock Clock;
            [ReadOnly] public BufferLookup<TownSpot> Spots;
            [ReadOnly] public BufferLookup<PersonalityRow> Rows;
            public BufferLookup<TownScoreCache> Cache;

            public void Execute()
            {
                DynamicBuffer<TownSpot> spots = Spots[Area];
                DynamicBuffer<PersonalityRow> rows = Rows[Config];
                DynamicBuffer<TownScoreCache> cache = Cache[Area];
                cache.ResizeUninitialized(rows.Length * spots.Length);
                bool raining = Clock.Raining != 0, night = Clock.Period == (byte)TownPeriod.Night;
                for (int r = 0; r < rows.Length; r++)
                {
                    PersonalityRow p = rows[r];
                    for (int i = 0; i < spots.Length; i++)
                    {
                        TownSpot spot = spots[i];
                        float like = TownLifeRules.Claimable(spot.Kind) && spot.Kind < p.Likes.Length ? p.Likes[spot.Kind] : 0f;
                        float v = TownLifeRules.StaticPart(like,
                            TownLifeRules.InHours(Clock.Hours, spot.OpenFrom, spot.OpenTo),
                            spot.OnlyFor == 0 || spot.OnlyFor == p.Id + 1,
                            raining, p.Rain, (spot.Flags & (byte)TownSpotFlags.Sheltered) != 0,
                            night, p.NeedsLight != 0, (spot.Flags & (byte)TownSpotFlags.Lit) != 0);
                        cache[r * spots.Length + i] = new TownScoreCache { Value = v };
                    }
                }
            }
        }

        [BurstCompile]
        struct StaleClaimSweepJob : IJob
        {
            public Entity Area;
            public uint Now;
            public BufferLookup<TownSlot> Slots;
            [ReadOnly] public ComponentLookup<TownLife> Life;

            public void Execute()
            {
                DynamicBuffer<TownSlot> slots = Slots[Area];
                for (int i = 0; i < slots.Length; i++)
                {
                    TownSlot s = slots[i];
                    if (s.ClaimedBy == Entity.Null) continue;
                    bool holderGone = !Life.HasComponent(s.ClaimedBy) || Life[s.ClaimedBy].Slot != i + 1;
                    if (holderGone || Now > s.ClaimUntilTick) { s.ClaimedBy = Entity.Null; s.ClaimerPersonality = 0; slots[i] = s; }
                }
            }
        }

        [BurstCompile]
        struct PlayersNearJob : IJobFor
        {
            const float Radius = 10f;
            public Entity Area;
            [ReadOnly] public BufferLookup<TownSpot> Spots;
            [ReadOnly] public SenseWorld Sense;
            public float Cell;
            public NativeArray<byte> Near;

            public void Execute(int i)
            {
                float3 at = Spots[Area][i].Pos;
                Near[i] = (byte)(SenseQueries.NearestSq(Sense, at, Radius, Cell) <= Radius * Radius ? 1 : 0);
            }
        }

        [BurstCompile]
        [WithAll(typeof(BrainTag), typeof(BrainActive))]
        partial struct ThinkJob : IJobEntity
        {
            public Entity Area, Config;
            public TownsfolkConfig C;
            public TownClock Clock;
            public uint Now;
            [ReadOnly] public BufferLookup<TownSpot> Spots;
            [ReadOnly] public BufferLookup<TownSlot> Slots;
            [ReadOnly] public BufferLookup<PersonalityRow> Rows;
            [ReadOnly] public BufferLookup<TownScoreCache> Cache;
            [ReadOnly] public NativeArray<byte> PlayersNear;
            public NativeList<ClaimWish>.ParallelWriter Claims;
            public NativeList<SlotWish>.ParallelWriter SlotWrites;

            void Execute(Entity entity, ref TownLife L, ref BrainMover m, in BrainClock brainClock, ref BrainDice dice,
                         ref LocalTransform transform, ref TownPick pick)
            {
                pick = default;
                bool far = brainClock.Tier >= 3;
                bool moving = L.State == TownLifeState.Going || L.State == TownLifeState.Strolling || L.State == TownLifeState.GoingHome;
                bool step = brainClock.ThinkNow != 0 || L.State == TownLifeState.Choose
                            || (far && (Now + brainClock.StableId) % C.FarPlanStepTicks == 0);
                // far away nobody walks: it waits for its plan step, which puts it where it was going
                if (far && moving && !step) { m.Flags = BrainMoverFlags.Stop; return; }
                if (!step || L.State == TownLifeState.Gone) return;

                DynamicBuffer<PersonalityRow> rows = Rows[Config];
                int row = RowOf(rows, L.Personality);
                if (row < 0) return;
                PersonalityRow p = rows[row];
                DynamicBuffer<TownSpot> spots = Spots[Area];
                DynamicBuffer<TownSlot> slots = Slots[Area];
                float3 at = transform.Position;
                bool out_ = L.SentHome == 0 && TownLifeRules.StaysOut(L.ShiftStart != L.ShiftEnd, Clock.Hours, L.ShiftStart, L.ShiftEnd,
                    Clock.Raining != 0, p.Rain);

                if (moving)
                {
                    float radius = L.State == TownLifeState.GoingHome ? C.HomeDoorRadius : C.ArriveRadius;
                    bool arrived = SenseQueries.FlatSq(at, m.Dest) <= radius * radius;
                    if (far && !arrived)
                    {
                        transform.Position = m.Dest;
                        if (L.State == TownLifeState.Going && L.Slot > 0) transform.Rotation = quaternion.RotateY(slots[L.Slot - 1].Yaw);
                        arrived = true;
                    }
                    if (!arrived)
                    {
                        if (Now - L.StateSinceTick > C.GiveUpWalkTicks) { Release(ref L, entity); L.State = TownLifeState.Choose; }
                        else if (L.State != TownLifeState.GoingHome && !out_) { Release(ref L, entity); GoHome(ref L, ref m, spots, at, Now); }
                        return;
                    }
                    if (L.State == TownLifeState.GoingHome) { L.State = TownLifeState.Gone; m.Flags = BrainMoverFlags.Stop; return; }
                    if (L.State == TownLifeState.Going && L.Slot > 0)
                    {
                        float stay = math.lerp(p.StaySeconds.x, p.StaySeconds.y, dice.Rng.NextFloat());
                        L.State = TownLifeState.AtSpot;
                        L.StateSinceTick = Now;
                        L.StayUntilTick = Now + (uint)math.max(1f, stay / C.TickSeconds);
                        TownSlot s = slots[L.Slot - 1];
                        SlotWrites.AddNoResize(new SlotWish { Slot = L.Slot - 1, Who = entity, Stay = 1, Until = L.StayUntilTick + C.StaleClaimTicks });
                        L.Activity = TownLifeRules.ActivityAt(spots[s.Spot].Kind);
                        m.Flags = BrainMoverFlags.Stop | BrainMoverFlags.Face;
                        m.Yaw = s.Yaw;
                        return;
                    }
                    L.State = TownLifeState.Choose;   // a stroll ended: pick again now
                }

                if (L.State == TownLifeState.AtSpot)
                {
                    if (!out_) { Release(ref L, entity); GoHome(ref L, ref m, spots, at, Now); return; }
                    if (Now < L.StayUntilTick) return;
                    Release(ref L, entity);
                    L.State = TownLifeState.Choose;
                }

                if (L.State != TownLifeState.Choose) return;
                if (!out_) { GoHome(ref L, ref m, spots, at, Now); return; }

                // stop at a spot (the claim job hands out the slot), or stroll on
                int walkTo = -1;
                if (dice.Rng.NextFloat() < p.StopChance)
                {
                    var best = new FixedList64Bytes<float>();
                    var bestSpot = new FixedList64Bytes<int>();
                    int found = Best(spots, slots, Cache[Area], row, p, L, at, ref best, ref bestSpot);
                    int chosen = TownLifeRules.PickWeighted(best, found, dice.Rng.NextFloat());
                    if (chosen >= 0)
                    {
                        walkTo = WalkPoint(spots, at, C.PreferredDistance, ref dice.Rng);   // where it strolls if every slot is gone
                        pick = new TownPick { Spot = (ushort)(bestSpot[chosen] + 1), WalkTo = (ushort)(walkTo + 1), Score = best[chosen] };
                        L.State = TownLifeState.Choose;
                        m.Flags = BrainMoverFlags.Stop;
                        Claims.AddNoResize(new ClaimWish { StableId = brainClock.StableId, Who = entity, Far = (byte)(far ? 1 : 0) });
                        return;
                    }
                }
                walkTo = WalkPoint(spots, at, C.PreferredDistance, ref dice.Rng);
                if (walkTo < 0) { L.State = TownLifeState.Choose; m.Flags = BrainMoverFlags.Stop; return; }
                L.State = TownLifeState.Strolling;
                Walk(ref L, ref m, spots[walkTo].Pos, L.Jogs != 0, Now);
            }

            void Release(ref TownLife L, Entity who)
            {
                if (L.Slot > 0) SlotWrites.AddNoResize(new SlotWish { Slot = L.Slot - 1, Who = who });
                L.Slot = 0;
                L.Spot = 0;
            }

            /// <summary>The best scored spots, best first, into <paramref name="best"/> / <paramref name="bestSpot"/>; how many were found.</summary>
            int Best(DynamicBuffer<TownSpot> spots, DynamicBuffer<TownSlot> slots, DynamicBuffer<TownScoreCache> cache, int row, in PersonalityRow p,
                     in TownLife L, float3 at, ref FixedList64Bytes<float> best, ref FixedList64Bytes<int> bestSpot)
            {
                int keep = math.min(C.PickFromBest, MaxBest);
                int baseIndex = row * spots.Length;
                if (baseIndex + spots.Length > cache.Length) return 0;   // the cache is rebuilt this tick's next run
                float friendBonus = C.FriendBonus * p.ChatChance * 2f;
                for (int i = 0; i < spots.Length; i++)
                {
                    float fixedPart = cache[baseIndex + i].Value;
                    if (fixedPart <= 0f) continue;
                    TownSpot spot = spots[i];
                    int free = 0, friends = 0;
                    for (int k = 0; k < spot.SlotCount; k++)
                    {
                        TownSlot s = slots[spot.FirstSlot + k];
                        if (s.ClaimedBy == Entity.Null) { free++; continue; }
                        if (spot.Kind == (byte)TownSpotKind.ChatCircle && s.ClaimerPersonality < 32 && (p.ChatsWith & (1u << s.ClaimerPersonality)) != 0) friends++;
                    }
                    if (free == 0) continue;
                    bool recent = false;
                    for (int r = 0; r < L.Recent.Length; r++) if (L.Recent[r] == i + 1) recent = true;
                    float score = TownLifeRules.Finish(fixedPart, math.sqrt(SenseQueries.FlatSq(at, spot.Pos)), C.PreferredDistance, free,
                        spot.SlotCount, recent, p.Crowding, p.DeclinesInvites != 0 ? 0 : friends, friendBonus,
                        p.LikesPlayersNear != 0 && i < PlayersNear.Length && PlayersNear[i] != 0);
                    if (score <= 0f) continue;
                    // insert into the sorted best few
                    if (best.Length == keep && score <= best[keep - 1]) continue;
                    if (best.Length < keep) { best.Add(0f); bestSpot.Add(0); }
                    int slot = best.Length - 1;
                    while (slot > 0 && best[slot - 1] < score) { best[slot] = best[slot - 1]; bestSpot[slot] = bestSpot[slot - 1]; slot--; }
                    best[slot] = score;
                    bestSpot[slot] = i;
                }
                return best.Length;
            }
        }

        [BurstCompile]
        struct ClaimResolveJob : IJob
        {
            public Entity Area;
            public TownsfolkConfig C;
            public uint Now;
            public NativeList<ClaimWish> Claims;
            [ReadOnly] public NativeList<SlotWish> SlotWrites;
            [ReadOnly] public BufferLookup<TownSpot> Spots;
            public BufferLookup<TownSlot> Slots;
            public ComponentLookup<TownLife> Life;
            public ComponentLookup<BrainMover> Mover;
            [ReadOnly] public ComponentLookup<TownPick> Pick;

            public void Execute()
            {
                DynamicBuffer<TownSlot> slots = Slots[Area];
                DynamicBuffer<TownSpot> spots = Spots[Area];
                // lets-go and stays first (each touches only its own holder's slot), so a slot freed this tick can be taken this tick
                for (int w = 0; w < SlotWrites.Length; w++)
                {
                    SlotWish wish = SlotWrites[w];
                    if (wish.Slot < 0 || wish.Slot >= slots.Length) continue;
                    TownSlot s = slots[wish.Slot];
                    if (s.ClaimedBy != wish.Who) continue;
                    if (wish.Stay != 0) s.ClaimUntilTick = wish.Until;
                    else { s.ClaimedBy = Entity.Null; s.ClaimerPersonality = 0; }
                    slots[wish.Slot] = s;
                }
                Claims.AsArray().Sort();
                for (int n = 0; n < Claims.Length; n++)
                {
                    Entity who = Claims[n].Who;
                    TownPick want = Pick[who];
                    TownLife L = Life[who];
                    BrainMover m = Mover[who];
                    int spotIndex = want.Spot - 1;
                    if (spotIndex >= 0 && spotIndex < spots.Length && Claim(ref L, slots, spots[spotIndex], (ushort)spotIndex, who, out float3 slotPos))
                    {
                        Remember(ref L, (ushort)want.Spot, C.RecentSpots);
                        L.State = TownLifeState.Going;
                        Walk(ref L, ref m, slotPos, L.Jogs != 0 && spots[spotIndex].Kind == (byte)TownSpotKind.JogPoint, Now);
                    }
                    else if (want.WalkTo > 0 && want.WalkTo <= spots.Length)
                    {
                        L.State = TownLifeState.Strolling;
                        Walk(ref L, ref m, spots[want.WalkTo - 1].Pos, L.Jogs != 0, Now);
                    }
                    else { L.State = TownLifeState.Choose; m.Flags = BrainMoverFlags.Stop; }
                    if (Claims[n].Far != 0) m.Flags = BrainMoverFlags.Stop;   // far away nobody walks (as the think job's own rule)
                    Life[who] = L;
                    Mover[who] = m;
                }
            }

            bool Claim(ref TownLife L, DynamicBuffer<TownSlot> slots, in TownSpot spot, ushort spotIndex, Entity who, out float3 pos)
            {
                pos = default;
                for (int k = 0; k < spot.SlotCount; k++)
                {
                    int i = spot.FirstSlot + k;
                    TownSlot s = slots[i];
                    if (s.ClaimedBy != Entity.Null) continue;
                    s.ClaimedBy = who;
                    s.ClaimerPersonality = L.Personality;
                    s.ClaimUntilTick = Now + C.GiveUpWalkTicks + C.StaleClaimTicks;   // the walk there, then its stay (set on arrival)
                    slots[i] = s;
                    L.Slot = (ushort)(i + 1);
                    L.Spot = (ushort)(spotIndex + 1);
                    pos = s.Pos;
                    return true;
                }
                return false;
            }
        }

        // ---- shared steps (pure) ----

        static int RowOf(DynamicBuffer<PersonalityRow> rows, byte id)
        {
            for (int i = 0; i < rows.Length; i++) if (rows[i].Id == id) return i;
            return -1;
        }

        static void Walk(ref TownLife L, ref BrainMover m, float3 to, bool jog, uint now)
        {
            m.Dest = to;
            m.Speed = jog ? L.JogSpeed : L.WalkSpeed;
            m.Flags = 0;
            L.Activity = (byte)(jog ? TownActivity.Jog : TownActivity.Walk);
            L.StateSinceTick = now;
        }

        static void GoHome(ref TownLife L, ref BrainMover m, DynamicBuffer<TownSpot> spots, float3 at, uint now)
        {
            int door = -1;
            float bestSq = float.MaxValue;
            for (int i = 0; i < spots.Length; i++)
            {
                if (spots[i].Kind != (byte)TownSpotKind.HomeDoor) continue;
                float d = SenseQueries.FlatSq(at, spots[i].Pos);
                if (d < bestSq) { bestSq = d; door = i; }
            }
            if (door < 0) { L.State = TownLifeState.Gone; m.Flags = BrainMoverFlags.Stop; return; }
            L.State = TownLifeState.GoingHome;
            Walk(ref L, ref m, spots[door].Pos, false, now);
        }

        static void Remember(ref TownLife L, ushort spot, int keep)
        {
            if (keep <= 0) return;
            if (L.Recent.Length >= keep) L.Recent.RemoveAt(0);
            L.Recent.Add(spot);
        }

        /// <summary>A walk point to stroll to: of four rolled, the first within the preferred distance, else the nearest of them; -1 when the area has none.</summary>
        static int WalkPoint(DynamicBuffer<TownSpot> spots, float3 at, float preferred, ref Random rng)
        {
            int count = 0;
            for (int i = 0; i < spots.Length; i++) if (spots[i].Kind == (byte)TownSpotKind.Walk) count++;
            if (count == 0) return -1;
            int bestIndex = -1;
            float bestSq = float.MaxValue;
            for (int roll = 0; roll < 4; roll++)
            {
                int n = rng.NextInt(count);
                for (int i = 0; i < spots.Length; i++)
                {
                    if (spots[i].Kind != (byte)TownSpotKind.Walk) continue;
                    if (n-- != 0) continue;
                    float d = SenseQueries.FlatSq(at, spots[i].Pos);
                    if (d < 1f) break;   // where it stands already
                    if (d <= preferred * preferred) return i;
                    if (d < bestSq) { bestSq = d; bestIndex = i; }
                    break;
                }
            }
            return bestIndex;
        }
    }

    /// <summary>
    /// Fills <see cref="TownsfolkGhost"/> on the server, last in the brain tick: the activity (walking or jogging while it moves, its spot's
    /// while it stays), the speed from how far it went this tick (a far plan step's jump counts as standing) and the chat circle it shares.
    /// Writes only when a value changes. A parallel Burst job (owner, 13 Nov 2026).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup), OrderLast = true)]
    [UpdateAfter(typeof(BrainMoveSystem))]
    [BurstCompile]
    public partial struct TownsfolkGhostWriteSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<BrainConfig>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            SystemAPI.TryGetSingletonEntity<TownArea>(out Entity area);
            state.Dependency = new GhostWriteJob
            {
                Dt = SystemAPI.GetSingleton<BrainConfig>().TickSeconds,
                Area = area,
                Spots = SystemAPI.GetBufferLookup<TownSpot>(true),
            }.ScheduleParallel(state.Dependency);
        }

        [BurstCompile]
        partial struct GhostWriteJob : IJobEntity
        {
            const float JumpSpeed = 8f;
            public float Dt;
            public Entity Area;
            [ReadOnly] public BufferLookup<TownSpot> Spots;

            void Execute(in TownLife L, in LocalTransform transform, ref TownsfolkGhostSource source, ref TownsfolkGhost ghost)
            {
                float3 p = transform.Position;
                float speed = Dt > 0f ? math.distance(p.xz, source.LastPosition.xz) / Dt : 0f;
                if (speed > JumpSpeed) speed = 0f;
                source.LastPosition = p;
                bool still = speed < 0.05f;
                byte activity = L.State == TownLifeState.AtSpot ? L.Activity
                    : still ? (byte)TownActivity.Stand : L.Activity;
                ushort group = 0;
                if (L.State == TownLifeState.AtSpot && L.Spot > 0 && Area != Entity.Null && Spots.HasBuffer(Area))
                {
                    DynamicBuffer<TownSpot> spots = Spots[Area];
                    if (L.Spot <= spots.Length && spots[L.Spot - 1].Kind == (byte)TownSpotKind.ChatCircle) group = L.Spot;
                }
                byte steps = TownLifeRules.SpeedSteps(speed);
                if (ghost.Activity == activity && ghost.SpeedSteps == steps && ghost.GroupId == group) return;
                ghost.Activity = activity;
                ghost.SpeedSteps = steps;
                ghost.GroupId = group;
            }
        }
    }
}
