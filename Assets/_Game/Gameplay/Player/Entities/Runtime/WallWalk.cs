using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    // =================================================================================================================
    // Wallstride's wall walk as legs of the walk's own path (M3c, design `m3c-wallstride-design.md` section 3). One walk's corners run on
    // the NavMesh to the wall's foot, onto the wall (the plant), along the wall's node feet and off it (top-out, step-down, a jump off);
    // a WallCorner per corner says how the segment that ends there is moved: a timed leg with NGO's eases (PlayerWallRunMotor.AddLeg), a
    // wall segment at the wall's speed, or the ground walk's own rules. The pose, the tilt and the phase are functions of the progress.
    // =================================================================================================================

    /// <summary>The phase of a segment (the numbers of NGO's <c>WallRunPhase</c>, so the presentation reads them alike).</summary>
    public static class WallPhase
    {
        public const byte None = 0;
        public const byte Plant = 1;
        public const byte Climb = 2;
        public const byte Mantle = 3;
        public const byte ToPerch = 4;
        public const byte Perched = 5;
        public const byte KickOff = 6;
        public const byte DropDown = 7;
        public const byte WallMove = 8;
        public const byte WallHold = 9;
        public const byte PushOff = 10;
        public const byte JumpOn = 11;
        /// <summary>A jump off a roof's edge with no walk down on the way (part 5b): the Jump's Flux as it starts.</summary>
        public const byte LedgeJump = 12;
        /// <summary>A jump across a gap or down a drop between roofs on the way (part 5b): the Jump's Flux as it starts.</summary>
        public const byte GapJump = 13;

        /// <summary>A route's jump (part 5b): off a roof's edge or across a gap; the Jump's Flux is paid as it starts.</summary>
        public static bool IsRouteJump(byte phase) => phase == LedgeJump || phase == GapJump;
        /// <summary>A route's jump coming down after its apex: the fall loop (owner, 2 Nov, design 9 item 7).</summary>
        public const byte RouteFall = 14;
        /// <summary>A route's jump touching down: the landing and NGO's settle before the walk goes on.</summary>
        public const byte RouteLand = 15;
    }

    /// <summary>NGO's <c>Ease</c> (how a leg covers its height or its ground part): evenly, setting off, coming to rest, both.</summary>
    public static class WallEase
    {
        public const byte Linear = 0;
        public const byte In = 1;
        public const byte Out = 2;
        public const byte InOut = 3;
    }

    /// <summary><see cref="WallPlan.Flags"/> bits.</summary>
    public static class WallFlags
    {
        /// <summary>The walk ends standing on the wall (NGO's WallHold): the player stays on it, paying Grip.</summary>
        public const byte EndsOnWall = 1;
        /// <summary>The route was cut at the corner budget; the server issues the rest from the last corner.</summary>
        public const byte Truncated = 2;
        /// <summary>A straight climb (part 2): the server takes its Flux as the plant starts (NGO <c>Begin</c>).</summary>
        public const byte PayOnPlant = 4;
        /// <summary>The server has decided the plant's Flux (paid, or not and the walk stopped at the foot); never twice.</summary>
        public const byte Paid = 8;
        /// <summary>A tree climb: the walk ends perched (NGO's Perched; <see cref="MoveState.Wall"/> = 2, upright, no Grip).</summary>
        public const byte EndsPerched = 16;
    }

    /// <summary>
    /// The wall walk of an order: the header of the <see cref="WallCorner"/> buffer, server's (owner only, like <see cref="PathPlan"/>).
    /// Segment <c>i</c> (from corner <c>i - 1</c>, or the walk's start, to corner <c>i</c>) is on the wall when
    /// <see cref="OnWallFrom"/> &lt;= i &lt; <see cref="OffWallFrom"/>.
    /// </summary>
    [GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]
    public struct WallPlan : IComponentData
    {
        [GhostField] public ushort Seq;
        /// <summary>The speed on the wall, mm/s (NGO: the ground speed when the feet met the wall x <c>wallSpeedShare</c>).</summary>
        [GhostField] public int SpeedMmps;
        [GhostField] public byte OnWallFrom, OffWallFrom;
        [GhostField] public byte Flags;
    }

    /// <summary>The owner's own wall walk for its own click (not a ghost field: a rollback leaves it alone).</summary>
    public struct ClientWallPlan : IComponentData
    {
        public WallPlan Plan;
    }

    /// <summary>
    /// How the segment ending at this corner is moved, and the body's facing at the corner. Index 0 holds the facing at the walk's start.
    /// Owner only (the observers see the pose and the tilt).
    /// </summary>
    [GhostComponent(OwnerSendType = SendToOwnerType.SendToOwner)]
    public struct WallCorner : IBufferElementData
    {
        /// <summary>0 = moved at the walk's speed (the wall's or the ground's); &gt; 0 = a timed leg of this many microseconds (NGO <c>Leg.Seconds</c>).</summary>
        [GhostField] public int Micros;
        /// <summary><see cref="WallEase"/> of the height (low nibble) and of the ground part (high nibble) of a timed leg.</summary>
        [GhostField] public byte Ease;
        [GhostField] public byte Phase;
        /// <summary>The body's up and forward at the corner, permille (zero up = upright; zero forward = the way it goes).</summary>
        [GhostField] public short UpX, UpY, UpZ, FwdX, FwdY, FwdZ;

        public byte EaseUp => (byte)(Ease & 0x0F);
        public byte EaseAlong => (byte)(Ease >> 4);
        public static byte Eases(byte up, byte along) => (byte)((up & 0x0F) | (along << 4));
    }

    /// <summary>The owner's own legs for its own click (same layout as <see cref="WallCorner"/>, not replicated).</summary>
    public struct ClientWallCorner : IBufferElementData
    {
        public WallCorner Leg;
    }

    /// <summary>
    /// The body's tilt on a wall (pick B a), to everyone: up and forward in permille and the phase for the clips. All zero = upright on
    /// the ground (the pose's yaw rules). Written by the walk on the server and the owner from the progress; a derived output, never compared.
    /// </summary>
    public struct PoseTilt : IComponentData
    {
        [GhostField] public short UpX, UpY, UpZ, FwdX, FwdY, FwdZ;
        [GhostField] public byte Phase;
        /// <summary>How long the phase's legs take together, milliseconds (the clip is fitted to it, NGO <c>WallRunPose.Duration</c>); 0 = untimed.</summary>
        [GhostField] public ushort PhaseMillis;

        public bool Tilted => UpX != 0 || UpY != 0 || UpZ != 0;
        public float3 Up => Tilted ? math.normalizesafe(new float3(UpX, UpY, UpZ), new float3(0f, 1f, 0f)) : new float3(0f, 1f, 0f);
        public float3 Forward => math.normalizesafe(new float3(FwdX, FwdY, FwdZ), new float3(0f, 0f, 1f));
    }

    /// <summary>Server only: the Grip owed for the time on a wall, in millionths of a point (paid a whole point at a time, as NGO).</summary>
    public struct WallGripOwed : IComponentData
    {
        public long Micro;
    }

    /// <summary>The wall walk's legs in integers (pure, Burst-safe; the server and the owner call it with the same plan and progress).</summary>
    public static class WallLeg
    {
        /// <summary>NGO's <c>EaseValue</c> in permille: In s^2, Out 1 - (1 - s)^2, InOut smoothstep, else even.</summary>
        public static int EaseValue(byte ease, int sPermille)
        {
            long s = math.clamp(sPermille, 0, 1000);
            switch (ease)
            {
                case WallEase.In: return (int)(s * s / 1000);
                case WallEase.Out: return (int)(1000 - (1000 - s) * (1000 - s) / 1000);
                case WallEase.InOut: return VaultLeg.Smooth((int)s);
                default: return (int)s;
            }
        }

        /// <summary>The plan belongs to the walk of <paramref name="orderSeq"/>.</summary>
        public static bool Of(in WallPlan w, ushort orderSeq) => w.Seq != 0 && w.Seq == orderSeq;

        /// <summary>Segment <paramref name="index"/> is on the wall (Grip, no Stop, wall clicks).</summary>
        public static bool OnWall(in WallPlan w, int index) => index >= w.OnWallFrom && index < w.OffWallFrom;

        /// <summary>
        /// The segment the progress lies on: its index (1..count-1), where it starts along the path, its length and its two ends. A
        /// progress at or past the end gives the last segment. False when the path has no length.
        /// </summary>
        public static bool SegmentAt(int3 start, NativeArray<int3> corners, int count, int progressMm,
            out int index, out int segStartMm, out int segLengthMm, out int3 a, out int3 b)
        {
            index = 0; segStartMm = 0; segLengthMm = 0; a = start; b = start;
            int n = math.min(count, corners.Length);
            int3 prev = start;
            int along = 0;
            bool found = false;
            for (int i = 1; i < n; i++)
            {
                int seg = PlayerPath.DistanceMm(prev, corners[i]);
                if (seg > 0)
                {
                    index = i; segStartMm = along; segLengthMm = seg; a = prev; b = corners[i];
                    found = true;
                    if (progressMm < along + seg) return true;
                }
                along += seg;
                prev = corners[i];
            }
            return found;
        }

        /// <summary>The point <paramref name="sPermille"/> of the way along a timed leg from a to b, on NGO's two eases.</summary>
        public static int3 TimedPoint(int3 a, int3 b, in WallCorner leg, int sPermille)
        {
            int along = EaseValue(leg.EaseAlong, sPermille), up = EaseValue(leg.EaseUp, sPermille);
            return new int3(
                a.x + (int)((long)(b.x - a.x) * along / 1000),
                a.y + (int)((long)(b.y - a.y) * up / 1000),
                a.z + (int)((long)(b.z - a.z) * along / 1000));
        }

        /// <summary>A timed leg's own speed: its length over its time (at least 1 mm/s).</summary>
        public static int TimedSpeedMmps(int segLengthMm, int micros) =>
            micros > 0 ? (int)math.clamp((long)segLengthMm * PlayerMovement.MicrosPerSecond / micros, 1L, int.MaxValue) : 0;

        static float3 UpOf(in WallCorner c) =>
            c.UpX != 0 || c.UpY != 0 || c.UpZ != 0 ? math.normalize(new float3(c.UpX, c.UpY, c.UpZ)) : new float3(0f, 1f, 0f);

        /// <summary>
        /// The body's tilt on a timed leg or a wall segment, <paramref name="sPermille"/> of the way from corner <paramref name="from"/> to
        /// <paramref name="to"/>: the up blends between the two; the forward too on a timed leg (NGO warps the rotation over the leg), and on
        /// the wall it is the way along projected on the face (NGO <c>WallFacing</c>). A derived output (float), never compared.
        /// </summary>
        public static PoseTilt TiltOf(in WallCorner from, in WallCorner to, int3 a, int3 b, bool wallSegment, int sPermille, byte phase, int yawCdeg)
        {
            float s = math.clamp(sPermille, 0, 1000) * 0.001f;
            float3 up = math.normalizesafe(math.lerp(UpOf(from), UpOf(to), s), new float3(0f, 1f, 0f));
            float3 travel = new float3(b - a);
            float3 heading = new float3(math.sin(math.radians(yawCdeg * 0.01f)), 0f, math.cos(math.radians(yawCdeg * 0.01f)));
            float3 fwd;
            if (wallSegment)
            {
                fwd = travel - up * math.dot(travel, up);
                if (math.lengthsq(fwd) < 1e-4f) fwd = new float3(0f, 1f, 0f) - up * up.y;   // up the wall when there is no way (NGO)
            }
            else
            {
                float3 f0 = FwdOf(in from, travel, heading), f1 = FwdOf(in to, travel, heading);
                fwd = math.lerp(f0, f1, s);
                fwd -= up * math.dot(fwd, up);
            }
            fwd = math.normalizesafe(fwd, heading);
            return new PoseTilt
            {
                UpX = (short)math.round(up.x * 1000f), UpY = (short)math.round(up.y * 1000f), UpZ = (short)math.round(up.z * 1000f),
                FwdX = (short)math.round(fwd.x * 1000f), FwdY = (short)math.round(fwd.y * 1000f), FwdZ = (short)math.round(fwd.z * 1000f),
                Phase = phase,
            };
        }

        static float3 FwdOf(in WallCorner c, float3 travel, float3 heading)
        {
            if (c.FwdX != 0 || c.FwdY != 0 || c.FwdZ != 0) return math.normalize(new float3(c.FwdX, c.FwdY, c.FwdZ));
            float3 flat = new float3(travel.x, 0f, travel.z);
            return math.lengthsq(flat) > 1e-4f ? math.normalize(flat) : heading;
        }

        /// <summary>The ground heading of a tilt's forward, hundredths of a degree (the pose's yaw), or <paramref name="fallback"/> when it points straight up or down.</summary>
        public static int YawOf(in PoseTilt t, int fallback)
        {
            if (math.abs(t.FwdX) + math.abs(t.FwdZ) < 20) return fallback;
            return PlayerPath.YawCdegOf(new int3(t.FwdX, 0, t.FwdZ), fallback);
        }

        /// <summary>The body's rotation: on a wall (or in one of its legs) the tilt's up and forward, else the pose's yaw.</summary>
        public static quaternion Rotation(in PlayerPose pose, in PoseTilt tilt) =>
            tilt.Tilted ? quaternion.LookRotationSafe(tilt.Forward, tilt.Up) : quaternion.RotateY(math.radians(pose.YawCdeg * 0.01f));

        /// <summary>The Grip owed for <paramref name="micros"/> on a wall at <paramref name="gripPerSecondMilli"/> thousandths a second, in millionths.</summary>
        public static long OwedMicro(int gripPerSecondMilli, int micros) => (long)gripPerSecondMilli * micros / 1000;
    }
}
