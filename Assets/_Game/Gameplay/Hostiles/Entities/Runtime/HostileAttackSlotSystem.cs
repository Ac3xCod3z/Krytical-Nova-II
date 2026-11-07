using KryticalNova.Brains;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>A Hostile's place in its target's ring (M6.5, the Amalur stage manager; brain plan s3.6).</summary>
    public struct HostileSlot : IComponentData
    {
        /// <summary>
        /// 1 = it may start a swing this tick: it has a place on its target's ring and, in the ring's fair order (longest waiting first),
        /// the attack budget left still covers a light swing (the swing itself pays its real cost).
        /// </summary>
        public byte Held;
        /// <summary>1 = it has a place on its target's ring (crowding by body size, owner pick A 7 Nov 2026): it stands there, not on the wait ring.</summary>
        public byte Placed;
        /// <summary>The attack cost its swing under way holds (0 = none): given back when its recover ends.</summary>
        public byte Cost;
        /// <summary>The tick it began waiting for a swing (0 = it is swinging, or not in a fight).</summary>
        public uint WaitingSinceTick;
    }

    /// <summary>The attack cost held on each player this tick, by ghost id (singleton; the slot system fills it, the swing pays into it).</summary>
    public struct HostileSlotBook : IComponentData
    {
        public NativeParallelHashMap<int, int> AttackUsed;
    }

    /// <summary>
    /// The stage manager (M6.5, owner Q2 A; crowding by body size, owner pick A 7 Nov 2026): every engaged player carries a ring that
    /// Hostiles fill by their body's arc (<see cref="HostileRingRules"/>: each stands at the player's radius + its radius + a gap, its arc as
    /// wide as its body, a Bruiser's counted double, while the arcs fit in 90 % of the circle), laid round the ring so no two overlap, and
    /// an attack capacity (10) their swings pay into (light 3, heavy 5, given back when the recover ends), so about three swing at once
    /// and the rest rotate in. A Hostile already swinging keeps its slot; the others are served longest
    /// waiting first among those in reach, then nearest; one that has just swung goes to the back. One without a slot holds the wait ring (3.5-5 m, on its own
    /// bearing) facing its target. It limits who swings, never who can be hit (area skills hit everyone, no cap).
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
            public byte Swinging;
            public byte InReach;
            public uint WaitingSince;
            public float DistSq;
            public float Arc, Stand, Bearing;
            public float3 Feet;
        }

        struct ByBearing : System.Collections.Generic.IComparer<float2>
        {
            public int Compare(float2 a, float2 b) => a.x.CompareTo(b.x);   // x = bearing, y = the claim's index
        }

        struct ClaimOrder : System.Collections.Generic.IComparer<Claim>
        {
            public int Compare(Claim a, Claim b)
            {
                if (a.Ghost != b.Ghost) return a.Ghost.CompareTo(b.Ghost);
                if (a.Swinging != b.Swinging) return b.Swinging.CompareTo(a.Swinging);   // swinging first: they keep their slots
                if (a.InReach != b.InReach) return b.InReach.CompareTo(a.InReach);   // then those that can swing now (one out of reach never hogs a slot)
                if (a.WaitingSince != b.WaitingSince) return a.WaitingSince.CompareTo(b.WaitingSince);   // longest waiting next
                if (a.DistSq != b.DistSq) return a.DistSq.CompareTo(b.DistSq);
                return a.Entity.Index.CompareTo(b.Entity.Index);
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
                if (b.Mode != HostileMode.Engage || target.ValueRO.GhostId == 0)
                {
                    s = default;
                    continue;
                }
                bool swinging = b.Phase == HostilePhase.Tell || b.Phase == HostilePhase.Windup || b.Phase == HostilePhase.Recover;
                if (!swinging) s.Cost = 0;
                if (!swinging && s.WaitingSinceTick == 0) s.WaitingSinceTick = now;
                if (s.Cost > 0)
                {
                    book.AttackUsed.TryGetValue(target.ValueRO.GhostId, out int used);
                    book.AttackUsed[target.ValueRO.GhostId] = used + s.Cost;
                }
                float3 here = transform.ValueRO.Position, feet = target.ValueRO.LastFeet;
                float bodyRadius = life.ValueRO.RadiusMm * 0.001f;
                float stand = HostileRingRules.Stand(c.PlayerRadius, bodyRadius, c.SlotStandGap, life.ValueRO.AttackRange + c.ReachSlack);
                claims.Add(new Claim
                {
                    Entity = entity,
                    Ghost = target.ValueRO.GhostId,
                    Swinging = (byte)(swinging ? 1 : 0),
                    InReach = (byte)(HostileRules.InReach(transform.ValueRO.Position, target.ValueRO.LastFeet, life.ValueRO.AttackRange, c.ReachSlack, life.ValueRO.ReachAbove) ? 1 : 0),
                    WaitingSince = swinging ? 0u : s.WaitingSinceTick,
                    DistSq = SenseQueries.FlatSq(transform.ValueRO.Position, target.ValueRO.LastFeet),
                    Arc = HostileRingRules.Arc(bodyRadius, stand, life.ValueRO.Tier == (byte)HostileTier.Bruiser ? c.BruiserArcFactor : 1f),
                    Stand = stand,
                    Bearing = HostileRingRules.Bearing(feet, here),
                    Feet = feet,
                });
            }
            claims.Sort(new ClaimOrder());

            float room = HostileRingRules.Room(c.SlotRingFill);
            var holders = new NativeList<float2>(16, Allocator.Temp);
            int ghost = 0;
            float arcUsed = 0f;
            int budgetLeft = 0;
            for (int i = 0; i < claims.Length; i++)
            {
                Claim claim = claims[i];
                if (i == 0 || claim.Ghost != ghost)
                {
                    PlaceHolders(ref state, claims, holders);
                    ghost = claim.Ghost;
                    arcUsed = 0f;
                    book.AttackUsed.TryGetValue(ghost, out int spent);
                    budgetLeft = c.AttackCapacity - spent;
                }
                // one swinging keeps its place even past the room (it was given it); the others while their arcs still fit
                bool placed = claim.Swinging != 0 || arcUsed + claim.Arc <= room + 1e-4f;
                if (placed) { arcUsed += claim.Arc; holders.Add(new float2(claim.Bearing, i)); }
                // the turn to swing goes in the same fair order: the attack system meets them in entity order, so it is decided here
                bool held = claim.Swinging != 0;
                if (!held && placed && budgetLeft >= c.AttackCostLight) { held = true; budgetLeft -= c.AttackCostLight; }
                HostileSlot s = SystemAPI.GetComponent<HostileSlot>(claim.Entity);
                s.Held = (byte)(held ? 1 : 0);
                s.Placed = (byte)(placed ? 1 : 0);
                SystemAPI.SetComponent(claim.Entity, s);
                if (placed) continue;

                // no place: hold the wait ring on its own bearing, facing the target
                BrainMover m = SystemAPI.GetComponent<BrainMover>(claim.Entity);
                float3 at = SystemAPI.GetComponent<LocalTransform>(claim.Entity).Position;
                float3 feet = SystemAPI.GetComponent<HostileTarget>(claim.Entity).LastFeet;
                float2 away = at.xz - feet.xz;
                float dist = math.length(away);
                float2 dir = dist > 1e-3f ? away / dist : new float2(0f, 1f);
                float ring = math.clamp(dist, c.WaitRingMin, c.WaitRingMax);
                if (math.abs(dist - ring) < 0.25f) m.Flags = (byte)(BrainMoverFlags.Stop | BrainMoverFlags.Face);
                else { m.Dest = new float3(feet.x + dir.x * ring, at.y, feet.z + dir.y * ring); m.Flags = BrainMoverFlags.Run; }
                float2 to = feet.xz - at.xz;
                if (math.lengthsq(to) > 1e-6f) m.Yaw = math.atan2(to.x, to.y);
                SystemAPI.SetComponent(claim.Entity, m);
            }
            PlaceHolders(ref state, claims, holders);
            holders.Dispose();
            claims.Dispose();
        }

        /// <summary>
        /// One player's ring: its holders (bearing, claim index) laid round it so no two arcs overlap (<see cref="HostileRingRules.Layout"/>);
        /// one not swinging walks to its place at its own stand distance and faces the player. Clears <paramref name="holders"/>.
        /// </summary>
        void PlaceHolders(ref SystemState state, NativeList<Claim> claims, NativeList<float2> holders)
        {
            int n = holders.Length;
            if (n == 0) return;
            holders.Sort(new ByBearing());
            var bearings = new NativeArray<float>(n, Allocator.Temp);
            var arcs = new NativeArray<float>(n, Allocator.Temp);
            var places = new NativeArray<float>(n, Allocator.Temp);
            for (int k = 0; k < n; k++)
            {
                bearings[k] = holders[k].x;
                arcs[k] = claims[(int)holders[k].y].Arc;
            }
            HostileRingRules.Layout(bearings, arcs, places);
            for (int k = 0; k < n; k++)
            {
                Claim claim = claims[(int)holders[k].y];
                if (claim.Swinging != 0) continue;   // a swing under way stays where it is
                float3 at = SystemAPI.GetComponent<LocalTransform>(claim.Entity).Position;
                float3 spot = new float3(claim.Feet.x + math.sin(places[k]) * claim.Stand, at.y, claim.Feet.z + math.cos(places[k]) * claim.Stand);
                BrainMover m = SystemAPI.GetComponent<BrainMover>(claim.Entity);
                if (math.lengthsq(spot.xz - at.xz) < 0.25f * 0.25f) m.Flags = (byte)(BrainMoverFlags.Stop | BrainMoverFlags.Face);
                else { m.Dest = spot; m.Flags = BrainMoverFlags.Run; }
                float2 to = claim.Feet.xz - at.xz;
                if (math.lengthsq(to) > 1e-6f) m.Yaw = math.atan2(to.x, to.y);
                SystemAPI.SetComponent(claim.Entity, m);
            }
            bearings.Dispose();
            arcs.Dispose();
            places.Dispose();
            holders.Clear();
        }
    }
}
