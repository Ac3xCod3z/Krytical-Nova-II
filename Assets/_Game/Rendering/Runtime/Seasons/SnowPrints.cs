using UnityEngine;

namespace KryticalNova.Rendering
{
    /// <summary>The shape a foot leaves (owner, 14 Nov 2026: players, NPCs and mounts each with their own print).</summary>
    public enum SnowPrintShape : byte { Boot = 0, Paw = 1, Path = 2 }

    /// <summary>
    /// Footprints waiting to be pressed into the snow (owner, 14 Nov 2026: a print where each foot actually plants, turned to its heading,
    /// instead of a trail). Whoever sees a foot come down adds one; <see cref="SnowTrackMap"/> stamps them all into its one map the next
    /// frame (SnowTrack.compute), so the ground reads one texel and no print is an object of its own. A fixed ring, no garbage: when more
    /// feet land in a frame than it holds, the oldest are dropped. Presentation only, per machine.
    /// </summary>
    public static class SnowPrints
    {
        /// <summary>Prints the compute takes in one frame (SnowTrack.compute KN_MAX_PRINTS).</summary>
        public const int MaxPerFrame = 64;

        static readonly Vector4[] s_Where = new Vector4[MaxPerFrame];   // x, z, sin(yaw), cos(yaw)
        static readonly Vector4[] s_Shape = new Vector4[MaxPerFrame];   // half length, half width, shape, press 0-1
        static int s_Count;

        /// <summary>True while there is snow to print into (set by the season presenter); nobody watches feet otherwise.</summary>
        public static bool Wanted { get; set; }

        /// <summary>Prints added since the game began (tests, GM status).</summary>
        public static int Added { get; private set; }

        /// <summary>Prints waiting now.</summary>
        public static int Count => s_Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_Count = 0; Wanted = false; Added = 0; }

        /// <summary>
        /// A foot came down at <paramref name="foot"/> facing <paramref name="yawDegrees"/> (world, 0 = +Z). <paramref name="size"/> is
        /// the print's length and width in metres; <paramref name="press"/> how deep it goes, 0-1.
        /// </summary>
        public static void Add(Vector3 foot, float yawDegrees, Vector2 size, SnowPrintShape shape, float press = 1f)
        {
            if (!Wanted) return;
            if (s_Count >= MaxPerFrame)
            {
                // keep the newest: shift down by one (rare: 64 feet landing in one frame)
                System.Array.Copy(s_Where, 1, s_Where, 0, MaxPerFrame - 1);
                System.Array.Copy(s_Shape, 1, s_Shape, 0, MaxPerFrame - 1);
                s_Count = MaxPerFrame - 1;
            }
            float r = yawDegrees * Mathf.Deg2Rad;
            s_Where[s_Count] = new Vector4(foot.x, foot.z, Mathf.Sin(r), Mathf.Cos(r));
            s_Shape[s_Count] = new Vector4(Mathf.Max(0.02f, size.x * 0.5f), Mathf.Max(0.02f, size.y * 0.5f), (float)shape, Mathf.Clamp01(press));
            s_Count++;
            Added++;
        }

        /// <summary>
        /// A body ploughed through deep snow from <paramref name="from"/> to <paramref name="to"/> (owner, 14 Nov 2026: deeper than a
        /// print's depth, a walker leaves a continuous path, body-wide, wider for mounts). <paramref name="halfWidth"/> in metres.
        /// </summary>
        public static void AddPath(Vector3 from, Vector3 to, float halfWidth, float press = 1f)
        {
            if (!Wanted) return;
            if (s_Count >= MaxPerFrame)
            {
                System.Array.Copy(s_Where, 1, s_Where, 0, MaxPerFrame - 1);
                System.Array.Copy(s_Shape, 1, s_Shape, 0, MaxPerFrame - 1);
                s_Count = MaxPerFrame - 1;
            }
            s_Where[s_Count] = new Vector4(from.x, from.z, to.x, to.z);
            s_Shape[s_Count] = new Vector4(Mathf.Max(0.05f, halfWidth), 0f, (float)SnowPrintShape.Path, Mathf.Clamp01(press));
            s_Count++;
            Added++;
        }

        /// <summary>Hands this frame's prints to the track map and empties the queue. Returns how many.</summary>
        internal static int Take(Vector4[] where, Vector4[] shape)
        {
            int n = s_Count;
            for (int i = 0; i < MaxPerFrame; i++)
            {
                where[i] = i < n ? s_Where[i] : Vector4.zero;
                shape[i] = i < n ? s_Shape[i] : Vector4.zero;
            }
            s_Count = 0;
            return n;
        }

        /// <summary>Drops every waiting print (no snow, no map).</summary>
        public static void Clear() => s_Count = 0;
    }
}
