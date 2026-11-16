using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Vegetation.Grass.Entities
{
    // Grass plan step 3 (owner, 15 Nov 2026: "Unity was using DOTS, ECS, Burst jobs ... we need to do that too"): the ground is
    // cut into sections, and sections into tiles, as entities in the CLIENT world only (Time Ghost style). Blades are never
    // entities: a tile will own a slice of the GPU blade pool (step 4). This assembly is left out of the server build
    // (!UNITY_SERVER) and every system in it is filtered to the client and presentation worlds.

    /// <summary>Which section a tile belongs to. Shared, so a chunk holds the tiles of one section and can be culled whole.</summary>
    public struct GrassSection : ISharedComponentData
    {
        public int2 Index;
    }

    /// <summary>Chunk component: the world XZ rectangle and height range of the tiles in this chunk (for a whole-chunk early out).</summary>
    public struct GrassChunkBounds : IComponentData
    {
        public float2 Min, Max;
        public float MinY, MaxY;
    }

    /// <summary>One tile of ground, baked from the grass mask when the tiles are made. Never changes after that.</summary>
    public struct GrassTile : IComponentData
    {
        public int2 Index;
        public float2 Min, Max;     // world XZ
        public float MinY, MaxY;    // ground height range plus the tallest grass
        public float Density;       // the highest mask density in the tile (0-1)
        public byte Biomes;         // bit per biome present
    }

    /// <summary>Tile looks, nearest first. Impostor arrives with step 6; Mid with step 5.</summary>
    public enum GrassTileLod : byte { Out = 0, Near = 1, Far = 2 }

    /// <summary>What the jobs decided for a tile this frame.</summary>
    public struct GrassTileState : IComponentData
    {
        public float Distance;      // from the camera to the nearest point of the tile, XZ
        public GrassTileLod Lod;
        public bool Visible;        // in the camera's frustum
        public bool Resident;       // its blades are built (step 4: its slice of the GPU pool is filled)
        public float DensityScale;  // the tier's density for this tile
        public float4 Tint;         // season tint (rgb, a strength); step 6 fills it, white until then
    }

    /// <summary>Singleton, written each frame on the main thread from the game camera and the rule set.</summary>
    public struct GrassView : IComponentData
    {
        public float3 Camera;
        public float4 Plane0, Plane1, Plane2, Plane3, Plane4, Plane5;
        public float NearEnd, FarEnd, Hysteresis, DensityScale;
        public int StreamBudget;
        public bool Valid;
    }

    /// <summary>Singleton: the last frame's tile counts (debug, grass.tiles).</summary>
    public struct GrassTileStats : IComponentData
    {
        public int Tiles, Near, Far, Visible, Resident, Queued, StreamedIn, StreamedOut;
    }

    /// <summary>The tile rules as pure functions (Burst and tests share them).</summary>
    public static class GrassTileRules
    {
        /// <summary>Distance from p to the nearest point of the rectangle (0 inside).</summary>
        public static float DistanceToRect(float2 p, float2 min, float2 max)
        {
            float2 d = math.max(math.max(min - p, p - max), 0f);
            return math.length(d);
        }

        /// <summary>
        /// The tile's look at this distance. A tile keeps its current look until it is <paramref name="hysteresis"/> metres past
        /// the edge, so a tile on a boundary never flips back and forth as the camera sways.
        /// </summary>
        public static GrassTileLod Choose(float distance, GrassTileLod current, float nearEnd, float farEnd, float hysteresis)
        {
            float nearLimit = nearEnd + (current == GrassTileLod.Near ? hysteresis : 0f);
            if (distance < nearLimit) return GrassTileLod.Near;
            float farLimit = farEnd + (current != GrassTileLod.Out ? hysteresis : 0f);
            return distance < farLimit ? GrassTileLod.Far : GrassTileLod.Out;
        }

        /// <summary>True when the box is at least partly inside all six planes (inward normals, dot(n, p) + w >= 0 inside).</summary>
        public static bool BoxInFrustum(float3 min, float3 max, in GrassView view)
        {
            return InFront(view.Plane0, min, max) && InFront(view.Plane1, min, max) && InFront(view.Plane2, min, max)
                && InFront(view.Plane3, min, max) && InFront(view.Plane4, min, max) && InFront(view.Plane5, min, max);
        }

        static bool InFront(float4 plane, float3 min, float3 max)
        {
            // the box corner furthest along the plane's normal
            float3 p = math.select(min, max, plane.xyz >= 0f);
            return math.dot(plane.xyz, p) + plane.w >= 0f;
        }
    }
}
