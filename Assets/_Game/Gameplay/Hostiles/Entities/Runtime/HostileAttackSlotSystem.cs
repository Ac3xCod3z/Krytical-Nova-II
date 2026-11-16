using KryticalNova.Brains;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>A Hostile's place in its target's ring (M6.5, the Amalur stage manager; brain plan s3.6; sticky spots, owner pick 15 Nov 2026).</summary>
    public struct HostileSlot : IComponentData
    {
        /// <summary>
        /// 1 = it may start a swing this tick: it has the turn to strike (in the ring's fair order, longest waiting first, the attack
        /// budget left still covers a light swing; the swing itself pays its real cost) and stands on its strike spot.
        /// </summary>
        public byte Held;
        /// <summary>1 = it has a spot on its target's ring (crowding by body size, owner pick A 7 Nov 2026): it stands there, not on the wait ring.</summary>
        public byte Placed;
        /// <summary>The attack cost its swing under way holds (0 = none): given back when its recover ends.</summary>
        public byte Cost;
        /// <summary>The tick it began waiting for a swing (0 = it is swinging, or not in a fight).</summary>
        public uint WaitingSinceTick;
        /// <summary>1 = it has the turn to strike and steps in to its strike spot (the budget is kept for it until it swings or gives the turn up).</summary>
        public byte Chosen;
        /// <summary>The tick it was given the turn to strike.</summary>
        public uint ChosenTick;
        /// <summary>1 = it is walking to its spot (it walks until it is there, then holds while it stays within the dead-band).</summary>
        public byte Walking;
        /// <summary>Its spot on the ring: radians round the player (0 = +z), kept for the whole fight.</summary>
        public float Angle;
        /// <summary>The ghost id of the player whose ring the spot is on (another target: a new spot).</summary>
        public int RingGhost;
    }

    /// <summary>The attack cost held on each player this tick, by ghost id (singleton; the slot system fills it, the swing pays into it).</summary>
    public struct HostileSlotBook : IComponentData
    {
        public NativeParallelHashMap<int, int> AttackUsed;
    }

    /// <summary>
    /// The stage manager (M6.5, owner Q2 A; crowding by body size, owner pick A 7 Nov 2026; sticky spots, owner pick 15 Nov 2026): every
    /// engaged player carries a ring that Hostiles fill by their body's arc (<see cref="HostileRingRules"/>, a Bruiser's counted double,
    /// while the arcs fit in 90 % of the circle). Each keeps its spot for the whole fight: it holds it a step out, facing the player, steps
    /// in to strike from where the bodies touch when it gets the turn, and steps back after. A newcomer takes the free spot nearest where
    /// it stands; the others move only to make room for it, by the smallest push both ways, so the ring never turns. The attack capacity
    /// (10) their swings pay into (light 3, heavy 5, given back when the recover ends) lets about three swing at once; turns go longest
    /// waiting first, one that has just swung to the back. One without a spot holds the wait ring (3.5-5 m, on its own bearing) facing its
    /// target until a spot frees. It limits who swings, never who can be hit (area skills hit everyone, no cap).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(BrainSimulationGroup))]
    [UpdateAfter(typeof(HostileDecideSystem))]
    [UpdateBefore(typeof(HostileAttackSystem))]
    [BurstCompile]
    public partial struct HostileAttackSlotSystem : ISystem
    {
        struct Claim
        {
            public Entity Entity;
            public int Ghost;
            public byte Swinging, Kept, Chosen, Walking;
            public uint WaitingSince, ChosenTick;
            public float DistSq;
            public float Arc, Stand, Bearing, Angle;
            public float3 Feet, At;
        }

        struct ClaimOrder : System.Collections.Generic.IComparer<Claim>
        {
            public int Compare(Claim a, Claim b)
            {
                if (a.Ghost != b.Ghost) return a.Ghost.CompareTo(b.Ghost);
                if (a.Kept != b.Kept) return b.Kept.CompareTo(a.Kept);   // a spot once given is kept (sticky spots)
                if (a.Swinging != b.Swinging) return b.Swinging.CompareTo(a.Swinging);
                if (a.WaitingSince != b.WaitingSince) return a.WaitingSince.CompareTo(b.WaitingSince);   // longest waiting next
                if (a.DistSq != b.DistSq) return a.DistSq.CompareTo(b.DistSq);
                return a.Entity.Index.CompareTo(b.Entity.Index);
            }
        }

        struct Turn
        {
            public int Claim, Seat, Index;
            public byte Swinging, Chosen;
            public uint WaitingSince;
            public float DistSq;
        }

        struct TurnOrder : System.Collections.Generic.IComparer<Turn>
        {
            public int Compare(Turn a, Turn b)
            {
                if (a.Swinging != b.Swinging) return b.Swinging.CompareTo(a.Swinging);   // a swing under way keeps its cost
                if (a.Chosen != b.Chosen) return b.Chosen.CompareTo(a.Chosen);   // one stepping in keeps its turn
                if (a.WaitingSince != b.WaitingSince) return a.WaitingSince.CompareTo(b.WaitingSince);
                if (a.DistSq != b.DistSq) return a.DistSq.CompareTo(b.DistSq);
                return a.Index.CompareTo(b.Index);
            }
        }

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<HostileBrainConfig>();
            state.RequireForUpdate<BrainTick>();
            state.EntityManager.CreateSingleton(new HostileSlotBook { AttackUsed = new NativeParallelHashMap<int, int>(64, Allocator.Persistent) });
        }

        public void OnDestroy(ref SystemState state)
        {
            if (SystemAPI.TryGetSingleton(out HostileSlotBook book) && book.AttackUsed.IsCreated) book.AttackUsed.Dispose();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            HostileBrainConfig c = SystemAPI.GetSingleton<HostileBrainConfig>();
            uint now = SystemAPI.GetSingleton<BrainTick>().Tick;
            HostileSlotBook book = SystemAPI.GetSingleton<HostileSlotBook>();
            book.AttackUsed.Clear();

            var claims = new NativeList<Claim>(64, Allocator.Temp);
            // job later (perf phase): one job per engaged player (the plan's Burst job per tick)
            foreach (var (brain, target, slot, life, transform, entity) in SystemAPI
                         .Query<RefRO<HostileBrain>, RefRO<HostileTarget>, RefRW<HostileSlot>, RefRO<HostileLife>, RefRO<LocalTransform>>()
                         .WithAll<BrainTag, BrainActive>().WithEntityAccess())
            {
                ref HostileSlot s = ref slot.ValueRW;
                HostileBrain b = brain.ValueRO;
                int ghost = target.ValueRO.GhostId;
                if (b.Mode != HostileMode.Engage || ghost == 0)
                {
                    s = default;   // out of the fight: its spot is given up
                    continue;
                }
                bool swinging = b.Phase == HostilePhase.Tell || b.Phase == HostilePhase.Windup || b.Phase == HostilePhase.Recover;
                if (!swinging) s.Cost = 0;
                if (!swinging && s.WaitingSinceTick == 0) s.WaitingSinceTick = now;
                if (s.Cost > 0)
                {
                    book.AttackUsed.TryGetValue(ghost, out int used);
                    book.AttackUsed[ghost] = used + s.Cost;
                }
                bool kept = s.Placed != 0 && s.RingGhost == ghost;
                float3 here = transform.ValueRO.Position, feet = target.ValueRO.LastFeet;
                float bodyRadius = life.ValueRO.RadiusMm * 0.001f;
                float stand = HostileRingRules.Stand(c.PlayerRadius, bodyRadius, c.SlotStandGap);
                claims.Add(new Claim
                {
                    Entity = entity,
                    Ghost = ghost,
                    Swinging = (byte)(swinging ? 1 : 0),
                    Kept = (byte)(kept ? 1 : 0),
                    Chosen = kept ? s.Chosen : (byte)0,
                    Walking = kept ? s.Walking : (byte)0,
                    WaitingSince = swinging ? 0u : s.WaitingSinceTick,
                    ChosenTick = s.ChosenTick,
                    DistSq = SenseQueries.FlatSq(here, feet),
                    Arc = HostileRingRules.Arc(bodyRadius, stand, life.ValueRO.Tier == (byte)HostileTier.Bruiser ? c.BruiserArcFactor : 1f),
                    Stand = stand,
                    Bearing = HostileRingRules.Bearing(feet, here),
                    Angle = s.Angle,
                    Feet = feet,
                    At = here,
                });
            }
            claims.Sort(new ClaimOrder());
            int start = 0;
            for (int i = 1; i <= claims.Length; i++)
            {
                if (i < claims.Length && claims[i].Ghost == claims[start].Ghost) continue;
                SeatRing(ref state, c, claims, start, i, now, book);
                start = i;
            }
            claims.Dispose();
        }

        /// <summary>
        /// One player's ring (claims <paramref name="start"/> to <paramref name="end"/>, in <see cref="ClaimOrder"/>): spots kept, newcomers
        /// seated while the room lasts, overlaps cleared both ways, turns to strike given, and each Hostile walked to (or held on) its spot.
        /// </summary>
        void SeatRing(ref SystemState state, in HostileBrainConfig c, NativeList<Claim> claims, int start, int end, uint now, HostileSlotBook book)
        {
            int n = end - start;
            var angles = new NativeArray<float>(n, Allocator.Temp);
            var arcs = new NativeArray<float>(n, Allocator.Temp);
            var seatOf = new NativeArray<int>(n, Allocator.Temp);
            var claimOf = new NativeArray<int>(n, Allocator.Temp);
            int count = 0;
            float arcUsed = 0f, room = HostileRingRules.Room(c.SlotRingFill);
            for (int k = 0; k < n; k++) seatOf[k] = -1;

            // 1. kept spots stay (sticky); a swing under way keeps where it stands
            for (int k = 0; k < n; k++)
            {
                Claim cl = claims[start + k];
                bool left = cl.Kept != 0 && math.distancesq(HostileRingRules.Spot(cl.Feet, cl.Angle, cl.Stand + c.SlotHoldBack, cl.At.y).xz, cl.At.xz)
                    > c.SlotReseatDistance * c.SlotReseatDistance;
                if (cl.Kept != 0 ? left : cl.Swinging == 0) continue;
                Seat(k, cl.Kept != 0 ? cl.Angle : cl.Bearing, cl.Arc, ref count, ref arcUsed, angles, arcs, seatOf, claimOf);
            }
            // 2. a kept one the player ran far from takes the free spot nearest where it stands now (it never walks through the player)
            for (int k = 0; k < n; k++)
            {
                Claim cl = claims[start + k];
                if (cl.Kept == 0 || seatOf[k] >= 0) continue;
                HostileRingRules.FreeSpot(angles, arcs, count, cl.Arc, cl.Bearing, out float spot);
                Seat(k, spot, cl.Arc, ref count, ref arcUsed, angles, arcs, seatOf, claimOf);
            }
            // 3. newcomers, longest waiting first, while their arcs fit: the free spot nearest where each stands
            for (int k = 0; k < n; k++)
            {
                Claim cl = claims[start + k];
                if (seatOf[k] >= 0 || arcUsed + cl.Arc > room + 1e-4f) continue;
                HostileRingRules.FreeSpot(angles, arcs, count, cl.Arc, cl.Bearing, out float spot);
                Seat(k, spot, cl.Arc, ref count, ref arcUsed, angles, arcs, seatOf, claimOf);
            }
            // a newcomer with no gap wide enough: its neighbours make room, each moving half (nothing moves when nothing overlaps)
            HostileRingRules.Spread(angles, arcs, count);

            // the turns to strike, in the fair order, while the player's attack budget covers a light swing
            int ghost = claims[start].Ghost;
            book.AttackUsed.TryGetValue(ghost, out int spent);
            int budgetLeft = c.AttackCapacity - spent;
            var turns = new NativeList<Turn>(math.max(1, count), Allocator.Temp);
            for (int seat = 0; seat < count; seat++)
            {
                Claim cl = claims[start + claimOf[seat]];
                turns.Add(new Turn
                {
                    Claim = start + claimOf[seat], Seat = seat, Index = cl.Entity.Index, Swinging = cl.Swinging, Chosen = cl.Chosen,
                    WaitingSince = cl.WaitingSince, DistSq = cl.DistSq,
                });
            }
            turns.Sort(new TurnOrder());
            for (int t = 0; t < turns.Length; t++)
            {
                Turn turn = turns[t];
                Claim cl = claims[turn.Claim];
                HostileSlot s = SystemAPI.GetComponent<HostileSlot>(cl.Entity);
                bool swinging = cl.Swinging != 0;
                bool chosen = swinging;
                if (!swinging && cl.Chosen != 0 && now - cl.ChosenTick > c.SlotStepInTicks)
                    s.WaitingSinceTick = now;   // it could not step in in time: the turn goes on, it goes to the back
                else if (!swinging && budgetLeft >= c.AttackCostLight)
                {
                    chosen = true;
                    budgetLeft -= c.AttackCostLight;
                }
                if (chosen && !swinging && cl.Chosen == 0) s.ChosenTick = now;

                // where it stands: its strike spot (bodies a gap apart) with the turn, a step farther out without it
                float angle = angles[turn.Seat];
                float3 spot = HostileRingRules.Spot(cl.Feet, angle, chosen ? cl.Stand : cl.Stand + c.SlotHoldBack, cl.At.y);
                float off = math.distance(spot.xz, cl.At.xz);
                // a walk starts past the dead-band (the strike spot's reach with the turn) and goes on until it is settled on the spot
                bool walking = !swinging && (off > (chosen ? c.SlotStrikeArrive : c.SlotHoldDeadBand) || (cl.Walking != 0 && off > c.SlotSettle));

                s.Held = (byte)(chosen && (swinging || off <= c.SlotStrikeArrive) ? 1 : 0);
                s.Chosen = (byte)(chosen && !swinging ? 1 : 0);
                s.Walking = (byte)(walking ? 1 : 0);
                s.Placed = 1;
                s.Angle = angle;
                s.RingGhost = ghost;
                SystemAPI.SetComponent(cl.Entity, s);

                BrainMover m = SystemAPI.GetComponent<BrainMover>(cl.Entity);
                // a swing under way stands where it is (it never walks on into the player); one on its spot holds it
                if (walking) { m.Dest = spot; m.Flags = BrainMoverFlags.Run; }
                else m.Flags = (byte)(BrainMoverFlags.Stop | BrainMoverFlags.Face);
                float2 to = cl.Feet.xz - cl.At.xz;
                if (math.lengthsq(to) > 1e-6f) m.Yaw = math.atan2(to.x, to.y);
                SystemAPI.SetComponent(cl.Entity, m);
            }

            // no spot: hold the wait ring on its own bearing, facing the target, until one frees
            for (int k = 0; k < n; k++)
            {
                if (seatOf[k] >= 0) continue;
                Claim cl = claims[start + k];
                HostileSlot s = SystemAPI.GetComponent<HostileSlot>(cl.Entity);
                s.Held = 0; s.Placed = 0; s.Chosen = 0; s.Walking = 0; s.RingGhost = 0;
                SystemAPI.SetComponent(cl.Entity, s);
                BrainMover m = SystemAPI.GetComponent<BrainMover>(cl.Entity);
                float2 away = cl.At.xz - cl.Feet.xz;
                float dist = math.length(away);
                float2 dir = dist > 1e-3f ? away / dist : new float2(0f, 1f);
                float ring = math.clamp(dist, c.WaitRingMin, c.WaitRingMax);
                if (math.abs(dist - ring) < c.SlotHoldDeadBand) m.Flags = (byte)(BrainMoverFlags.Stop | BrainMoverFlags.Face);
                else { m.Dest = new float3(cl.Feet.x + dir.x * ring, cl.At.y, cl.Feet.z + dir.y * ring); m.Flags = BrainMoverFlags.Run; }
                float2 to = cl.Feet.xz - cl.At.xz;
                if (math.lengthsq(to) > 1e-6f) m.Yaw = math.atan2(to.x, to.y);
                SystemAPI.SetComponent(cl.Entity, m);
            }
            turns.Dispose();
            angles.Dispose();
            arcs.Dispose();
            seatOf.Dispose();
            claimOf.Dispose();
        }

        static void Seat(int k, float angle, float arc, ref int count, ref float arcUsed, NativeArray<float> angles, NativeArray<float> arcs,
            NativeArray<int> seatOf, NativeArray<int> claimOf)
        {
            angles[count] = HostileRingRules.Wrap(angle);
            arcs[count] = arc;
            seatOf[k] = count;
            claimOf[count] = k;
            count++;
            arcUsed += arc;
        }
    }
}
