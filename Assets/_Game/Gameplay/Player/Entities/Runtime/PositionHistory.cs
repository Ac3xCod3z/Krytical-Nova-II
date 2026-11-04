using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    /// <summary>One tick of a hittable ghost's place: its feet in millimetres on that server tick.</summary>
    public struct PositionSample
    {
        public uint Tick;
        public int3 FeetMm;
    }

    /// <summary>
    /// Server only, never sent (M4.3, plan decision: our own ring, no Unity Physics): where a hittable ghost stood on each of the last
    /// <see cref="PositionHistoryRules.Ticks"/> server ticks, so a hit is tested where the attacker saw the target (its interpolation tick).
    /// </summary>
    public struct PositionHistory : IComponentData
    {
        public FixedList512Bytes<PositionSample> Samples;
    }

    /// <summary>The hit body of a ghost: an upright capsule standing on its feet (the player prefab's collider: r 0.5 m, h 2 m).</summary>
    public struct HitProxy
    {
        public int RadiusMm, HeightMm;

        public static HitProxy Player => new HitProxy { RadiusMm = 500, HeightMm = 2000 };
    }

    /// <summary>The ring's rules, pure (EditMode-tested): recording, the claim clamp, the hit test at a tick.</summary>
    public static class PositionHistoryRules
    {
        /// <summary>Ticks kept: 16 (533 ms at 30 Hz), the oldest view time a hit may claim.</summary>
        public const int Ticks = 16;

        /// <summary>The place on <paramref name="tick"/>; the same tick again replaces it, the oldest beyond <see cref="Ticks"/> goes.</summary>
        public static void Record(ref PositionHistory ring, NetworkTick tick, int3 feetMm)
        {
            if (!tick.IsValid) return;
            uint t = tick.TickIndexForValidTick;
            ref FixedList512Bytes<PositionSample> s = ref ring.Samples;
            if (s.Length > 0 && s[s.Length - 1].Tick == t) { s[s.Length - 1] = new PositionSample { Tick = t, FeetMm = feetMm }; return; }
            if (s.Length >= Ticks) s.RemoveAt(0);
            s.Add(new PositionSample { Tick = t, FeetMm = feetMm });
        }

        /// <summary>
        /// The tick a hit is tested on: the attacker's view tick (<paramref name="seen"/>, a claim), never in the future (now), and refused
        /// when it is older than the ring (the section 53 clamp pattern: an older claim is not trusted; the hit fails, it is not moved up).
        /// </summary>
        public static NetworkTick TestTick(NetworkTick now, NetworkTick seen, out bool refused)
        {
            refused = false;
            if (!now.IsValid) { refused = true; return now; }
            if (!seen.IsValid || seen.IsNewerThan(now)) return now;
            if (now.TicksSince(seen) >= Ticks) { refused = true; return seen; }
            return seen;
        }

        /// <summary>Where the ghost stood on <paramref name="tick"/>; false when the ring has no sample of it (it was not there yet).</summary>
        public static bool TryFeetAt(in PositionHistory ring, NetworkTick tick, out int3 feetMm)
        {
            feetMm = default;
            if (!tick.IsValid) return false;
            uint t = tick.TickIndexForValidTick;
            for (int i = ring.Samples.Length - 1; i >= 0; i--)
                if (ring.Samples[i].Tick == t) { feetMm = ring.Samples[i].FeetMm; return true; }
            return false;
        }

        /// <summary>
        /// The capsule test: the attacker's feet within <paramref name="reachMm"/> of the target's upright capsule (its axis less its radius,
        /// on the ground plane) and level with it (within the capsule's height plus the reach).
        /// </summary>
        public static bool WithinReach(int3 attackerFeetMm, int3 targetFeetMm, HitProxy proxy, int reachMm)
        {
            long dx = attackerFeetMm.x - targetFeetMm.x, dz = attackerFeetMm.z - targetFeetMm.z;
            long reach = (long)reachMm + proxy.RadiusMm;
            if (dx * dx + dz * dz > reach * reach) return false;
            int dy = attackerFeetMm.y - targetFeetMm.y;
            return dy >= -proxy.HeightMm - reachMm && dy <= proxy.HeightMm + reachMm;
        }

        /// <summary>
        /// A hit the attacker saw on <paramref name="seen"/>: tested where the target stood then. False when the claim is older than the ring
        /// (<paramref name="refused"/>), when the target was not there on that tick, or when it was out of reach.
        /// </summary>
        public static bool HitTestAtTick(in PositionHistory target, HitProxy proxy, NetworkTick now, NetworkTick seen, int3 attackerFeetMm, int reachMm,
            out bool refused)
        {
            NetworkTick at = TestTick(now, seen, out refused);
            if (refused || !TryFeetAt(in target, at, out int3 feet)) return false;
            return WithinReach(attackerFeetMm, feet, proxy, reachMm);
        }
    }

    /// <summary>Server: at the end of every server tick, each hittable ghost's place goes into its ring.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct PositionHistoryWriteSystem : ISystem
    {
        public void OnCreate(ref SystemState state) => state.RequireForUpdate<NetworkTime>();

        public void OnUpdate(ref SystemState state)
        {
            NetworkTick tick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            if (!tick.IsValid) return;
            foreach (var (ring, pose) in SystemAPI.Query<RefRW<PositionHistory>, RefRO<PlayerPose>>())
                PositionHistoryRules.Record(ref ring.ValueRW, tick, pose.ValueRO.Mm);
        }
    }
}
