using KryticalNova.Brains;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
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
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(ThinkClockSystem))]
    [BurstCompile]
    public partial struct TownLifeDecideSystem : ISystem
    {
        const int MaxBest = 8;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BrainConfig>();
            state.RequireForUpdate<TownsfolkConfig>();
            state.RequireForUpdate<TownClock>();
            state.RequireForUpdate<BrainTick>();
            state.RequireForUpdate<TownArea>();
            state.RequireForUpdate<SenseWorld>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            Entity areaEntity = SystemAPI.GetSingletonEntity<TownArea>();
            if (SystemAPI.GetComponent<TownArea>(areaEntity).Ready == 0) return;
            BrainConfig bc = SystemAPI.GetSingleton<BrainConfig>();
            TownsfolkConfig c = SystemAPI.GetSingleton<TownsfolkConfig>();
            TownClock clock = SystemAPI.GetSingleton<TownClock>();
            SenseWorld sense = SystemAPI.GetSingleton<SenseWorld>();
            uint now = SystemAPI.GetSingleton<BrainTick>().Tick;
            DynamicBuffer<TownSpot> spots = SystemAPI.GetBuffer<TownSpot>(areaEntity);
            DynamicBuffer<TownSlot> slots = SystemAPI.GetBuffer<TownSlot>(areaEntity);
            DynamicBuffer<PersonalityRow> rows = SystemAPI.GetBuffer<PersonalityRow>(SystemAPI.GetSingletonEntity<TownsfolkConfig>());
            var lifeOf = SystemAPI.GetComponentLookup<TownLife>(true);

            // claims of the gone and the stale free themselves (plan 3.5: nothing locks a bench forever)
            for (int i = 0; i < slots.Length; i++)
            {
                TownSlot s = slots[i];
                if (s.ClaimedBy == Entity.Null) continue;
                bool holderGone = !lifeOf.HasComponent(s.ClaimedBy) || lifeOf[s.ClaimedBy].Slot != i + 1;
                if (holderGone || now > s.ClaimUntilTick) { s.ClaimedBy = Entity.Null; s.ClaimerPersonality = 0; slots[i] = s; }
            }

            var best = new NativeArray<float>(MaxBest, Allocator.Temp);
            var bestSpot = new NativeArray<int>(MaxBest, Allocator.Temp);
            float arriveSq = c.ArriveRadius * c.ArriveRadius;
            // job later (perf phase): an IJobEntity; the claims would then go through a per-spot lock or a second pass
            foreach (var (life, mover, brainClock, dice, transform, entity) in SystemAPI
                         .Query<RefRW<TownLife>, RefRW<BrainMover>, RefRO<BrainClock>, RefRW<BrainDice>, RefRW<LocalTransform>>()
                         .WithAll<BrainTag, BrainActive>().WithEntityAccess())
            {
                ref TownLife L = ref life.ValueRW;
                ref BrainMover m = ref mover.ValueRW;
                bool far = brainClock.ValueRO.Tier >= 3;
                bool moving = L.State == TownLifeState.Going || L.State == TownLifeState.Strolling || L.State == TownLifeState.GoingHome;
                bool step = brainClock.ValueRO.ThinkNow != 0 || L.State == TownLifeState.Choose
                            || (far && (now + brainClock.ValueRO.StableId) % c.FarPlanStepTicks == 0);
                // far away nobody walks: it waits for its plan step, which puts it where it was going
                if (far && moving && !step) { m.Flags = BrainMoverFlags.Stop; continue; }
                if (!step || L.State == TownLifeState.Gone) continue;

                int row = RowOf(rows, L.Personality);
                if (row < 0) continue;
                PersonalityRow p = rows[row];
                float3 at = transform.ValueRO.Position;
                bool out_ = L.SentHome == 0 && TownLifeRules.StaysOut(L.ShiftStart != L.ShiftEnd, clock.Hours, L.ShiftStart, L.ShiftEnd,
                    clock.Raining != 0, p.Rain);

                if (moving)
                {
                    bool arrived = SenseQueries.FlatSq(at, m.Dest) <= arriveSq;
                    if (far && !arrived)
                    {
                        LocalTransform t = transform.ValueRO;
                        t.Position = m.Dest;
                        if (L.State == TownLifeState.Going && L.Slot > 0) t.Rotation = quaternion.RotateY(slots[L.Slot - 1].Yaw);
                        transform.ValueRW = t;
                        arrived = true;
                    }
                    if (!arrived)
                    {
                        if (now - L.StateSinceTick > c.GiveUpWalkTicks) { Release(ref L, slots); L.State = TownLifeState.Choose; }
                        else if (L.State != TownLifeState.GoingHome && !out_) { Release(ref L, slots); GoHome(ref L, ref m, spots, at, now); }
                        continue;
                    }
                    if (L.State == TownLifeState.GoingHome) { L.State = TownLifeState.Gone; m.Flags = BrainMoverFlags.Stop; continue; }
                    if (L.State == TownLifeState.Going && L.Slot > 0)
                    {
                        float stay = math.lerp(p.StaySeconds.x, p.StaySeconds.y, dice.ValueRW.Rng.NextFloat());
                        L.State = TownLifeState.AtSpot;
                        L.StateSinceTick = now;
                        L.StayUntilTick = now + (uint)math.max(1f, stay / c.TickSeconds);
                        TownSlot s = slots[L.Slot - 1];
                        s.ClaimUntilTick = L.StayUntilTick + c.StaleClaimTicks;
                        slots[L.Slot - 1] = s;
                        L.Activity = TownLifeRules.ActivityAt(spots[s.Spot].Kind);
                        m.Flags = BrainMoverFlags.Stop | BrainMoverFlags.Face;
                        m.Yaw = s.Yaw;
                        continue;
                    }
                    L.State = TownLifeState.Choose;   // a stroll ended: pick again now
                }

                if (L.State == TownLifeState.AtSpot)
                {
                    if (!out_) { Release(ref L, slots); GoHome(ref L, ref m, spots, at, now); continue; }
                    if (now < L.StayUntilTick) continue;
                    Release(ref L, slots);
                    L.State = TownLifeState.Choose;
                }

                if (L.State != TownLifeState.Choose) continue;
                if (!out_) { GoHome(ref L, ref m, spots, at, now); continue; }

                // stop at a spot, or stroll on
                bool stop = dice.ValueRW.Rng.NextFloat() < p.StopChance;
                int picked = -1;
                if (stop)
                {
                    int found = Best(spots, slots, p, L, at, clock, c, sense, bc, best, bestSpot);
                    int pick = TownLifeRules.PickWeighted(best, found, dice.ValueRW.Rng.NextFloat());
                    if (pick >= 0) picked = bestSpot[pick];
                }
                if (picked >= 0 && Claim(ref L, slots, spots[picked], (ushort)picked, entity, now, c, out float3 slotPos))
                {
                    Remember(ref L, (ushort)(picked + 1), c.RecentSpots);
                    L.State = TownLifeState.Going;
                    Walk(ref L, ref m, slotPos, L.Jogs != 0 && spots[picked].Kind == (byte)TownSpotKind.JogPoint, now);
                    continue;
                }
                int walkTo = WalkPoint(spots, at, c.PreferredDistance, ref dice.ValueRW.Rng);
                if (walkTo < 0) { L.State = TownLifeState.Choose; m.Flags = BrainMoverFlags.Stop; continue; }
                L.State = TownLifeState.Strolling;
                Walk(ref L, ref m, spots[walkTo].Pos, L.Jogs != 0, now);
            }
        }

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

        static void Release(ref TownLife L, DynamicBuffer<TownSlot> slots)
        {
            if (L.Slot > 0 && L.Slot <= slots.Length)
            {
                TownSlot s = slots[L.Slot - 1];
                s.ClaimedBy = Entity.Null;
                s.ClaimerPersonality = 0;
                slots[L.Slot - 1] = s;
            }
            L.Slot = 0;
            L.Spot = 0;
        }

        static bool Claim(ref TownLife L, DynamicBuffer<TownSlot> slots, in TownSpot spot, ushort spotIndex, Entity who, uint now,
                          in TownsfolkConfig c, out float3 pos)
        {
            pos = default;
            for (int k = 0; k < spot.SlotCount; k++)
            {
                int i = spot.FirstSlot + k;
                TownSlot s = slots[i];
                if (s.ClaimedBy != Entity.Null) continue;
                s.ClaimedBy = who;
                s.ClaimerPersonality = L.Personality;
                s.ClaimUntilTick = now + c.GiveUpWalkTicks + c.StaleClaimTicks;   // the walk there, then its stay (set on arrival)
                slots[i] = s;
                L.Slot = (ushort)(i + 1);
                L.Spot = (ushort)(spotIndex + 1);
                pos = s.Pos;
                return true;
            }
            return false;
        }

        static void Remember(ref TownLife L, ushort spot, int keep)
        {
            if (keep <= 0) return;
            if (L.Recent.Length >= keep) L.Recent.RemoveAt(0);
            L.Recent.Add(spot);
        }

        /// <summary>The best scored spots, best first, into <paramref name="best"/> / <paramref name="bestSpot"/>; how many were found.</summary>
        static int Best(DynamicBuffer<TownSpot> spots, DynamicBuffer<TownSlot> slots, in PersonalityRow p, in TownLife L, float3 at,
                        in TownClock clock, in TownsfolkConfig c, in SenseWorld sense, in BrainConfig bc, NativeArray<float> best, NativeArray<int> bestSpot)
        {
            int keep = math.min(c.PickFromBest, best.Length);
            int found = 0;
            for (int i = 0; i < spots.Length; i++)
            {
                TownSpot spot = spots[i];
                if (!TownLifeRules.Claimable(spot.Kind) || spot.Kind >= p.Likes.Length) continue;
                float like = p.Likes[spot.Kind];
                if (like <= 0f) continue;
                int free = 0, friends = 0;
                for (int k = 0; k < spot.SlotCount; k++)
                {
                    TownSlot s = slots[spot.FirstSlot + k];
                    if (s.ClaimedBy == Entity.Null) { free++; continue; }
                    if (spot.Kind == (byte)TownSpotKind.ChatCircle && s.ClaimerPersonality < 32 && (p.ChatsWith & (1u << s.ClaimerPersonality)) != 0) friends++;
                }
                bool recent = false;
                for (int r = 0; r < L.Recent.Length; r++) if (L.Recent[r] == i + 1) recent = true;
                bool playersNear = p.LikesPlayersNear != 0 && SenseQueries.NearestSq(sense, spot.Pos, 10f, bc.PlayerCell) <= 100f;
                float score = TownLifeRules.SpotScore(new TownLifeRules.SpotInputs
                {
                    Like = like,
                    Open = TownLifeRules.InHours(clock.Hours, spot.OpenFrom, spot.OpenTo),
                    Allowed = spot.OnlyFor == 0 || spot.OnlyFor == L.Personality + 1,
                    Distance = math.sqrt(SenseQueries.FlatSq(at, spot.Pos)),
                    PreferredDistance = c.PreferredDistance,
                    FreeSlots = free,
                    SlotCount = spot.SlotCount,
                    Recent = recent,
                    Raining = clock.Raining != 0,
                    Rain = p.Rain,
                    Sheltered = (spot.Flags & (byte)TownSpotFlags.Sheltered) != 0,
                    Night = clock.Period == (byte)TownPeriod.Night,
                    NeedsLight = p.NeedsLight != 0,
                    Lit = (spot.Flags & (byte)TownSpotFlags.Lit) != 0,
                    CrowdingDislike = p.Crowding,
                    Friends = p.DeclinesInvites != 0 ? 0 : friends,
                    FriendBonus = c.FriendBonus * p.ChatChance * 2f,
                    PlayersNear = playersNear,
                    LikesPlayersNear = p.LikesPlayersNear != 0,
                });
                if (score <= 0f) continue;
                // insert into the sorted best few
                int at_ = found < keep ? found : keep;
                if (at_ == keep && score <= best[keep - 1]) continue;
                if (at_ == keep) at_ = keep - 1;
                while (at_ > 0 && best[at_ - 1] < score) { best[at_] = best[at_ - 1]; bestSpot[at_] = bestSpot[at_ - 1]; at_--; }
                best[at_] = score;
                bestSpot[at_] = i;
                if (found < keep) found++;
            }
            return found;
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
    /// Writes only when a value changes.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup), OrderLast = true)]
    [UpdateAfter(typeof(BrainMoveSystem))]
    [BurstCompile]
    public partial struct TownsfolkGhostWriteSystem : ISystem
    {
        const float JumpSpeed = 8f;

        public void OnCreate(ref SystemState state) => state.RequireForUpdate<BrainConfig>();

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.GetSingleton<BrainConfig>().TickSeconds;
            DynamicBuffer<TownSpot> spots = default;
            if (SystemAPI.TryGetSingletonEntity<TownArea>(out Entity area)) spots = SystemAPI.GetBuffer<TownSpot>(area);
            foreach (var (life, transform, source, ghost) in SystemAPI
                         .Query<RefRO<TownLife>, RefRO<LocalTransform>, RefRW<TownsfolkGhostSource>, RefRW<TownsfolkGhost>>())
            {
                float3 p = transform.ValueRO.Position;
                float speed = dt > 0f ? math.distance(p.xz, source.ValueRO.LastPosition.xz) / dt : 0f;
                if (speed > JumpSpeed) speed = 0f;
                source.ValueRW.LastPosition = p;
                TownLife L = life.ValueRO;
                bool still = speed < 0.05f;
                byte activity = L.State == TownLifeState.AtSpot ? L.Activity
                    : still ? (byte)TownActivity.Stand : L.Activity;
                ushort group = 0;
                if (L.State == TownLifeState.AtSpot && L.Spot > 0 && spots.IsCreated && L.Spot <= spots.Length
                    && spots[L.Spot - 1].Kind == (byte)TownSpotKind.ChatCircle) group = L.Spot;
                TownsfolkGhost was = ghost.ValueRO;
                byte steps = TownLifeRules.SpeedSteps(speed);
                if (was.Activity == activity && was.SpeedSteps == steps && was.GroupId == group) continue;
                was.Activity = activity;
                was.SpeedSteps = steps;
                was.GroupId = group;
                ghost.ValueRW = was;
            }
        }
    }
}
