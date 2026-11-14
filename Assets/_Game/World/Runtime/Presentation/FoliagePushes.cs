using KryticalNova.Rendering;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// The swing pushes alive on this client (owner's day list 13 Nov 2026, item 20): a fixed pool of short-lived pushes that bend the grass
    /// away from a swing and let it spring back. GameFeel writes them from the swings, casts and hits every client is already shown, and a
    /// Hostile's view from its attack; <see cref="FoliageInteractorSystem"/> publishes them each frame to <c>_KN_SwingPushes</c>, and the
    /// grass's trample pass works them out once per texel. A look only: nothing is networked, a dedicated server never writes one (it has
    /// no presenter, so no <see cref="Settings"/>), and nothing allocates.
    /// </summary>
    public static class FoliagePushes
    {
        /// <summary>The most alive at once; must match KN_MAX_SWING_PUSHES in GrassTrample.compute (a test reads it).</summary>
        public const int MaxPushes = 8;
        /// <summary>float4s per push in the published array.</summary>
        public const int VectorsPerPush = 4;
        public const int PublishedVectors = MaxPushes * VectorsPerPush;

        private struct Slot
        {
            public bool Alive;
            public FoliagePushKind Kind;
            public Vector3 Position;
            public Vector2 Direction;
            public float Radius, CosHalf, Strength, Band, FrontSpeed, Spring, Sweep;
            public float Start;
            public bool Local;
        }

        private static readonly Slot[] Slots = new Slot[MaxPushes];

        /// <summary>The numbers; set by the client's feel presenter. Null (a server, a test without one) and nothing is added.</summary>
        public static FoliagePushSettings Settings;

        /// <summary>Where the camera was at the last publish; pushes far from it are not added.</summary>
        public static Vector3 Viewer;

        /// <summary>How many are alive (for tests and the GM readout).</summary>
        public static int Alive
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxPushes; i++) if (Slots[i].Alive) n++;
                return n;
            }
        }

        /// <summary>
        /// A push of <paramref name="kind"/> from <paramref name="position"/> (a swing's feet, a hit's point) toward <paramref name="forward"/>.
        /// <paramref name="scale"/> grows a Hostile's push with its body (<see cref="FoliagePushSettings.BodyScale"/>) and sets a ring's
        /// reach as a share of its shape's radius; <paramref name="sweep"/> (-1, 0, 1) leans an arc sideways with the
        /// swing; <paramref name="delaySeconds"/> holds it until the blow lands. False when it was not added (off, too far, the pool full of
        /// pushes that matter more).
        /// </summary>
        public static bool Add(FoliagePushKind kind, Vector3 position, Vector3 forward, float scale = 1f, int sweep = 0, bool local = false,
            float delaySeconds = 0f)
            => Add(kind, position, forward, scale, sweep, local, delaySeconds, Time.time, Viewer, WorldEffectsRuntime.Tier);

        /// <summary><see cref="Add(FoliagePushKind, Vector3, Vector3, float, int, bool, float)"/> with the clock, the camera and the tier given (tests).</summary>
        public static bool Add(FoliagePushKind kind, Vector3 position, Vector3 forward, float scale, int sweep, bool local, float delaySeconds,
            float now, Vector3 viewer, int tier)
        {
            FoliagePushSettings settings = Settings;
            if (settings == null || !settings.enabled) return false;
            int cap = settings.PushesAt(tier);
            if (cap <= 0) return false;
            Vector3 flat = position - viewer;
            flat.y = 0f;
            if (flat.sqrMagnitude > settings.cullDistance * settings.cullDistance) return false;

            FoliagePushSettings.Shape shape = settings.ShapeOf(kind);
            float spring = Mathf.Max(0.05f, shape.springSeconds);
            Expire(now);

            // A free slot among the first `cap`; else the one with the least left. A push of this machine's player outlives others'.
            int slot = -1, alive = 0;
            float leastLeft = float.MaxValue;
            for (int i = 0; i < MaxPushes; i++)
            {
                if (!Slots[i].Alive) continue;
                alive++;
                if (Slots[i].Local && !local) continue;
                float left = (Slots[i].Start + Slots[i].Spring - now) * Slots[i].Strength;
                if (left < leastLeft) { leastLeft = left; slot = i; }
            }
            if (alive < cap)
                for (int i = 0; i < MaxPushes; i++)
                    if (!Slots[i].Alive) { slot = i; break; }
            if (slot < 0) return false;

            Vector2 direction = new Vector2(forward.x, forward.z);
            direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector2.up;
            // A Hostile's push grows with its body; an area skill's ring reaches the skill's own range, never past its shape's radius.
            float bodyScale = kind == FoliagePushKind.Hostile ? Mathf.Clamp(scale, 0.5f, Mathf.Max(0.5f, settings.maxBodyScale))
                : kind == FoliagePushKind.Ring ? Mathf.Clamp(scale, 0.1f, 1f) : 1f;
            float radius = Mathf.Max(0.1f, shape.radius * bodyScale);
            Slots[slot] = new Slot
            {
                Alive = true,
                Kind = kind,
                Position = position,
                Direction = direction,
                Radius = radius,
                CosHalf = shape.frontSpeed > 0f ? -2f : Mathf.Cos(Mathf.Clamp(shape.arcDegrees, 1f, 360f) * 0.5f * Mathf.Deg2Rad),
                Strength = Mathf.Max(0f, shape.strength),
                Band = Mathf.Max(0.1f, shape.band * (kind == FoliagePushKind.Hostile ? bodyScale : 1f)),
                FrontSpeed = Mathf.Max(0f, shape.frontSpeed),
                Spring = spring + (shape.frontSpeed > 0f ? radius / shape.frontSpeed : 0f),   // a front's last blades start late
                Sweep = Mathf.Clamp(sweep, -1, 1) * settings.sweepShare,
                Start = now + Mathf.Max(0f, delaySeconds),
                Local = local,
            };
            return true;
        }

        /// <summary>
        /// Frees the pushes that have sprung back and writes the rest into <paramref name="into"/> (<see cref="PublishedVectors"/> long):
        /// per push (x, y, z, radius), (dirX, dirZ, cos half-arc or -2 for a front, strength), (age, front speed, band, sweep),
        /// (spring seconds, 0, 0, 0). Returns how many it wrote. No allocation.
        /// </summary>
        public static int Publish(float now, Vector4[] into)
        {
            Expire(now);
            int n = 0;
            for (int i = 0; i < MaxPushes; i++)
            {
                Slot s = Slots[i];
                if (!s.Alive || now < s.Start) continue;   // a held push waits for its blow
                int at = n * VectorsPerPush;
                into[at] = new Vector4(s.Position.x, s.Position.y, s.Position.z, s.Radius);
                into[at + 1] = new Vector4(s.Direction.x, s.Direction.y, s.CosHalf, s.Strength);
                into[at + 2] = new Vector4(now - s.Start, s.FrontSpeed, s.Band, s.Sweep);
                into[at + 3] = new Vector4(s.Spring, 0f, 0f, 0f);
                n++;
            }
            for (int i = n * VectorsPerPush; i < PublishedVectors; i++) into[i] = Vector4.zero;
            return n;
        }

        /// <summary>Forgets every push (tests, leaving Play).</summary>
        public static void Clear()
        {
            for (int i = 0; i < MaxPushes; i++) Slots[i] = default;
        }

        private static void Expire(float now)
        {
            for (int i = 0; i < MaxPushes; i++)
                if (Slots[i].Alive && now - Slots[i].Start > Slots[i].Spring) Slots[i].Alive = false;
        }

        /// <summary>
        /// The spring every blade follows after a push reaches it, <paramref name="t"/> seconds on: a quick rise, then a damped swing back
        /// that passes upright once. The C# copy of KNSwingSpring in GrassTrample.compute (a test keeps the two alike).
        /// </summary>
        public static float Spring(float t, float rate, float damping, float rise)
        {
            if (t <= 0f) return 0f;
            float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.001f, rise)));
            return up * Mathf.Exp(-damping * rate * t) * Mathf.Cos(rate * t);
        }
    }
}
