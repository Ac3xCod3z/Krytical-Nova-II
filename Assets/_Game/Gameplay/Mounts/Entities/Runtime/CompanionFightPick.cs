using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace KryticalNova.Mounts.Entities
{
    /// <summary>One companion in a combat stance looking for its fight (phase 3): its owner, the owner's own target, and what it fights now.</summary>
    public struct CompanionSeek
    {
        public int OwnerGhostId;
        public float3 Owner;
        /// <summary>The owner's basic-attack target (<c>CombatState.TargetGhostId</c>), 0 none.</summary>
        public int OwnerTarget;
        /// <summary>The Hostile it fought at the last look, 0 none.</summary>
        public int Current;
        public float Range;
    }

    /// <summary>One Hostile as the pick sees it: its ghost id, feet, size, who it is after, and whether it is alive.</summary>
    public struct HostileSeen
    {
        public int GhostId;
        public float3 Feet;
        public float Radius;
        /// <summary>The ghost id of the player it is after (<c>HostileTarget.GhostId</c>), 0 none.</summary>
        public int TargetGhostId;
        public bool Alive;
    }

    /// <summary>
    /// Which Hostile a Mount Companion in a combat stance fights (day list item 19, phase 3): Autopilot's target logic
    /// (<c>AutopilotController.Fight / Pick / Allowed</c>) cut to the companion's short range round its owner. It never starts a fight alone:
    /// the owner's own target first, else the Hostile it already fights while that one is still after the owner, else the nearest Hostile
    /// after the owner, else none. Only living Hostiles within <see cref="CompanionSeek.Range"/> of the owner count. Pure and Burst-safe:
    /// <see cref="CompanionPickJob"/> runs it for every companion on the server.
    /// </summary>
    public static class CompanionFightPick
    {
        /// <summary>The index in <paramref name="hostiles"/> it should fight, or -1.</summary>
        public static int Pick(in CompanionSeek seek, in NativeArray<HostileSeen> hostiles)
        {
            float rangeSq = seek.Range * seek.Range;
            int current = -1, nearest = -1;
            float nearestSq = float.MaxValue;
            for (int i = 0; i < hostiles.Length; i++)
            {
                HostileSeen h = hostiles[i];
                if (!h.Alive || h.GhostId == 0) continue;
                float sq = FlatSq(h.Feet, seek.Owner);
                if (sq > rangeSq) continue;
                if (seek.OwnerTarget != 0 && h.GhostId == seek.OwnerTarget) return i;   // the owner's own target first
                if (h.TargetGhostId != seek.OwnerGhostId || seek.OwnerGhostId == 0) continue;
                if (h.GhostId == seek.Current) current = i;
                if (sq < nearestSq) { nearestSq = sq; nearest = i; }
            }
            return current >= 0 ? current : nearest;
        }

        static float FlatSq(float3 a, float3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }
    }

    /// <summary>The server's pick for every companion in a combat stance at one look, in parallel (Burst).</summary>
    [BurstCompile]
    public struct CompanionPickJob : IJobFor
    {
        [ReadOnly] public NativeArray<CompanionSeek> Seeks;
        [ReadOnly] public NativeArray<HostileSeen> Hostiles;
        [WriteOnly] public NativeArray<int> Picked;

        public void Execute(int index) => Picked[index] = CompanionFightPick.Pick(Seeks[index], Hostiles);
    }
}
