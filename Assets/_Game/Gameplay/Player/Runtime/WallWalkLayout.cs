using System.Collections.Generic;
using KryticalNova.World;
using KryticalNova.World.Surfaces;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// The wall walk laid out as the corners of one walk (Wallstride on the entity netcode, `m3c-wallstride-design.md` section 3): the
    /// NavMesh walk to the wall's foot, the plant, the wall's node feet, the way off it and the walk on, each corner with how the segment
    /// that ends there is moved. The legs are NGO's own (<see cref="PlayerWallRunMotor"/> PlantOnWall, TopOut, StepDown, JumpOnto, PushOff,
    /// AddLeg, AddArc): the same points, times and eases. The wall's feet are snapped to the real face when it is laid out (NGO's stick
    /// probe, which NGO runs every frame) and its straight runs thinned, so the walk needs no physics. Plain Unity maths, no NavMesh: the
    /// motor finds the route, the entity adapter turns the result into millimetres.
    /// </summary>
    public sealed class WallWalkLayout
    {
        /// <summary>A leg's easing (NGO's <c>Ease</c>; the same numbers as the entity side's <c>WallEase</c>).</summary>
        public const byte Linear = 0, EaseIn = 1, EaseOut = 2, EaseInOut = 3;

        /// <summary>Most corners of one wall walk (the entity side's <c>PlayerLimits.MaxWallCorners</c>).</summary>
        public const int MaxCorners = 64;

        /// <summary>NGO's shortest leg (<c>MinimumDuration</c>).</summary>
        public const float MinimumDuration = 0.05f;

        /// <summary>How far the thinned polyline may stray from a dropped node (m), and how far the normal may turn before a node is kept (deg).</summary>
        public const float ThinTolerance = 0.02f, ThinTurnDegrees = 2f;

        /// <summary>NGO's stick probe (<c>StickProbeOut</c>, <c>StickProbeLength</c>) and its limit off the node's normal.</summary>
        public const float StickProbeOut = 0.6f, StickProbeLength = 1.2f, StickMaxOffNode = 60f;

        /// <summary>The body standing on a face, for the prop test: its radius and how far out from the face it reaches (m).</summary>
        public const float BodyRadius = 0.3f, BodyHeight = 1.7f, BodyClearance = 0.15f;

        static readonly Collider[] Overlaps = new Collider[16];

        /// <summary>
        /// A prop on the face where the body would stand (owner, 1 Nov: an air-conditioner box was walked through; no clipping): a collider
        /// in the body's capsule, standing out along the face's normal from <see cref="BodyClearance"/> above the feet. The face's own collider,
        /// triggers and <paramref name="ignoreRoot"/> (the body itself) do not count. NGO has no such check: its stick probe starts 0.6 m out and
        /// misses a box deeper than that.
        /// </summary>
        public static bool PropOnFace(Vector3 feet, Vector3 normal, Collider face, Transform ignoreRoot, int mask)
        {
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            Vector3 a = feet + n * (BodyClearance + BodyRadius), b = feet + n * (BodyHeight - BodyRadius);
            int count = Physics.OverlapCapsuleNonAlloc(a, b, BodyRadius, Overlaps, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider c = Overlaps[i];
                if (c == null || c == face || c.isTrigger) continue;
                if (ignoreRoot != null && c.transform.IsChildOf(ignoreRoot)) continue;
                return true;
            }
            return false;
        }

        /// <summary>One corner: where the feet go and how the segment that ends here is moved.</summary>
        public struct Corner
        {
            public Vector3 Feet;
            /// <summary>0 = at the walk's speed (the ground, or the wall); else a timed leg of this many seconds.</summary>
            public float Seconds;
            public byte EaseUp, EaseAlong;
            public WallRunPhase Phase;
            /// <summary>The body's up and forward at the corner (zero = upright, facing the way).</summary>
            public Vector3 Up, Forward;
        }

        /// <summary>The face under a point (NGO's stick probe): the hit point and normal, or false.</summary>
        public delegate bool FaceProbe(Vector3 origin, Vector3 direction, float length, out Vector3 point, out Vector3 normal);

        public readonly List<Corner> Corners = new List<Corner>(64);
        /// <summary>A straight climb: the server takes NGO's Flux for it as its plant starts (NGO <c>Begin</c>).</summary>
        public bool PayOnPlant;
        /// <summary>A tree climb: the walk ends perched in it (NGO's Perched: standing off the ground, crouched, no Grip).</summary>
        public bool EndsPerched;
        /// <summary>The walk ends standing on the wall (NGO WallHold).</summary>
        public bool EndsOnWall;
        /// <summary>Cut at the corner budget: the rest of the way goes on from the last corner.</summary>
        public bool Truncated;
        /// <summary>The speed on the wall (NGO: the ground speed x <c>wallSpeedShare</c>), m/s.</summary>
        public float WallSpeed;
        /// <summary>Segments <see cref="OnWallFrom"/> &lt;= i &lt; <see cref="OffWallFrom"/> are on the wall (segment i ends at corner i).</summary>
        public int OnWallFrom, OffWallFrom;

        readonly WallstrideSettings settings;
        readonly FaceProbe probe;

        public WallWalkLayout(WallstrideSettings settings, FaceProbe probe, Vector3 startFeet, Vector3 startUp, Vector3 startForward, float wallSpeed)
        {
            this.settings = settings;
            this.probe = probe;
            WallSpeed = wallSpeed;
            Corners.Add(new Corner { Feet = startFeet, Up = startUp, Forward = startForward });
            OnWallFrom = OffWallFrom = int.MaxValue;
        }

        public Vector3 LastFeet => Corners[Corners.Count - 1].Feet;

        // ------------------------------------------------------------------ the pieces (NGO's AddLeg / AddArc / the wall)

        /// <summary>A stretch of NavMesh walk to <paramref name="feet"/> (the walk's own rules).</summary>
        public void Ground(Vector3 feet)
        {
            if ((feet - LastFeet).sqrMagnitude < 1e-6f) return;
            Corners.Add(new Corner { Feet = feet });
        }

        /// <summary>NGO <c>AddLeg</c>: to <paramref name="feet"/> in <paramref name="seconds"/> (at least NGO's minimum), facing <paramref name="facing"/> on arrival.</summary>
        public void Leg(WallRunPhase phase, Vector3 feet, Quaternion facing, float seconds, byte easeUp, byte easeAlong) =>
            Corners.Add(new Corner
            {
                Feet = feet, Seconds = Mathf.Max(MinimumDuration, seconds), EaseUp = easeUp, EaseAlong = easeAlong, Phase = phase,
                Up = facing * Vector3.up, Forward = facing * Vector3.forward,
            });

        /// <summary>NGO <c>AddArc</c>: a fall under gravity from the last corner to <paramref name="to"/>, over an apex <paramref name="lift"/> above the higher.</summary>
        public void Arc(WallRunPhase phase, Vector3 to, float lift, Quaternion facing)
        {
            Vector3 from = LastFeet;
            float g = settings != null ? settings.dropGravity : 14f;
            float apexY = Mathf.Max(from.y, to.y) + Mathf.Max(0f, lift);
            float up = Mathf.Sqrt(2f * Mathf.Max(0f, apexY - from.y) / g), down = Mathf.Sqrt(2f * Mathf.Max(0.05f, apexY - to.y) / g);
            if (up >= MinimumDuration)
            {
                Vector3 apex = Vector3.Lerp(from, to, up / (up + down));
                apex.y = apexY;
                Leg(phase, apex, facing, up, EaseOut, Linear);
            }
            Leg(phase, to, facing, Mathf.Max(0.3f, down), EaseIn, Linear);
        }

        /// <summary>The body is on the wall from the next segment on (Grip, no Stop, wall clicks).</summary>
        public void WallStarts() => OnWallFrom = Corners.Count;

        /// <summary>The way off the wall starts with the next segment.</summary>
        public void WallEnds() => OffWallFrom = Corners.Count;

        /// <summary>
        /// Along the wall through <paramref name="nodes"/> from <paramref name="first"/> on, at the wall's speed: each node's feet snapped
        /// to the face (NGO's stick probe), the straight runs thinned, the body standing on the face facing the way along (NGO
        /// <c>WallFacing</c>). Returns the last normal.
        /// </summary>
        public Vector3 Along(SurfaceGraph graph, IReadOnlyList<int> nodes, int first)
        {
            var feet = new List<Vector3>(nodes.Count);
            var normals = new List<Vector3>(nodes.Count);
            for (int i = first; i < nodes.Count; i++)
            {
                FaceAt(graph, nodes[i], out Vector3 f, out Vector3 n);
                feet.Add(f);
                normals.Add(n);
            }
            if (feet.Count == 0) return Corners[Corners.Count - 1].Up;
            foreach (int k in Thin(LastFeet, feet, normals))
            {
                Vector3 travel = feet[k] - LastFeet;
                Quaternion facing = WallFacing(normals[k], travel, Corners[Corners.Count - 1].Forward);
                if ((feet[k] - LastFeet).sqrMagnitude < 1e-6f) continue;
                Corners.Add(new Corner { Feet = feet[k], Phase = WallRunPhase.WallMove, Up = normals[k], Forward = facing * Vector3.forward });
            }
            return normals[normals.Count - 1];
        }

        /// <summary>A node's feet and normal: on the real face under it when NGO's stick probe finds one, else the graph's (NGO <c>UpdateWall</c>).</summary>
        public void FaceAt(SurfaceGraph graph, int node, out Vector3 feet, out Vector3 normal)
        {
            normal = graph.normals[node];
            feet = graph.points[node] - normal * settings.wallStandOff + normal * settings.wallFootLift;
            if (probe == null) return;
            Vector3 origin = feet + normal * StickProbeOut;
            if (probe(origin, -normal, StickProbeLength, out Vector3 point, out Vector3 faceNormal)
                && Vector3.Angle(faceNormal, Vector3.up) >= settings.graphMinSurfaceAngle * 0.8f && Vector3.Angle(faceNormal, normal) < StickMaxOffNode)
            {
                feet = point + faceNormal * settings.wallFootLift;
                normal = faceNormal;
            }
        }

        /// <summary>
        /// The indices of the feet to keep: a node is dropped when the line from the last kept one to the next stays within
        /// <see cref="ThinTolerance"/> of it and the normal turns less than <see cref="ThinTurnDegrees"/>; the last is always kept.
        /// </summary>
        public static List<int> Thin(Vector3 start, List<Vector3> feet, List<Vector3> normals)
        {
            var keep = new List<int>(feet.Count);
            Vector3 anchor = start;
            Vector3 anchorNormal = normals[0];
            int i = 0;
            while (i < feet.Count)
            {
                int j = i;
                // reach as far as a straight line from the anchor still passes every node on the way, on the same face
                while (j + 1 < feet.Count && Fits(anchor, feet, normals, anchorNormal, i, j + 1)) j++;
                keep.Add(j);
                anchor = feet[j];
                anchorNormal = normals[j];
                i = j + 1;
            }
            return keep;
        }

        static bool Fits(Vector3 anchor, List<Vector3> feet, List<Vector3> normals, Vector3 anchorNormal, int from, int to)
        {
            Vector3 line = feet[to] - anchor;
            float len = line.magnitude;
            if (len < 1e-4f) return true;
            Vector3 dir = line / len;
            for (int k = from; k <= to; k++)
            {
                if (Vector3.Angle(normals[k], anchorNormal) > ThinTurnDegrees) return false;
                if (k == to) continue;
                Vector3 off = feet[k] - anchor;
                if ((off - dir * Vector3.Dot(off, dir)).magnitude > ThinTolerance) return false;
            }
            return true;
        }

        /// <summary>NGO <c>WallFacing</c>: up is the face's normal, forward the way along it (up the wall when there is no way).</summary>
        public static Quaternion WallFacing(Vector3 normal, Vector3 travel, Vector3 fallback)
        {
            Vector3 forward = Vector3.ProjectOnPlane(travel, normal);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(fallback, normal);
            if (forward.sqrMagnitude < 1e-4f) return Quaternion.FromToRotation(Vector3.up, normal);
            return Quaternion.LookRotation(forward.normalized, normal);
        }

        /// <summary>NGO <c>Facing</c>: upright, toward a direction on the ground.</summary>
        public static Quaternion Facing(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(direction.normalized, Vector3.up) : Quaternion.identity;
        }

        /// <summary>The way out from a face on the ground (its normal flattened), or <paramref name="fallback"/>.</summary>
        public static Vector3 Outward(Vector3 normal, Vector3 fallback)
        {
            Vector3 o = new Vector3(normal.x, 0f, normal.z);
            return o.sqrMagnitude > 1e-4f ? o.normalized : fallback;
        }

        // ------------------------------------------------------------------ NGO's ways onto, along and off a wall

        /// <summary>
        /// NGO <c>PlantOnWall</c>: the walk to the join's NavMesh point is already laid out; the feet meet the wall (one 0.32 s leg from the
        /// ground, or from a roof out over the edge and down onto the face head first), then along the route.
        /// </summary>
        public void PlantAndAlong(SurfaceGraph graph, SurfaceRoute.Route route, Vector3 bodyForward)
        {
            FaceAt(graph, route.Nodes[0], out Vector3 firstFeet, out Vector3 n);
            if (route.HasEntry && route.Entry.kind == SurfaceGraph.JoinKind.Top)
            {
                Vector3 outward = Outward(n, bodyForward);
                Vector3 over = new Vector3(firstFeet.x, Mathf.Max(LastFeet.y, firstFeet.y) + settings.mantleClearance, firstFeet.z) + outward * 0.5f;
                Leg(WallRunPhase.Plant, over, Facing(outward), settings.wallPlantSeconds, EaseOut, EaseInOut);
                Leg(WallRunPhase.Plant, firstFeet, WallFacing(n, Vector3.down, bodyForward), settings.wallPlantSeconds, EaseIn, EaseOut);
            }
            else
            {
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, n);
                Leg(WallRunPhase.Plant, firstFeet, WallFacing(n, up, bodyForward), settings.wallPlantSeconds, EaseIn, EaseOut);
            }
            WallStarts();
            AlongAndOff(graph, route, 1);
        }

        /// <summary>
        /// Along the route's nodes from <paramref name="first"/> (NGO <c>UpdateWall</c>), then off the way it goes (NGO <c>ArriveOnWall</c>):
        /// standing at the end, over the edge onto the top (<c>TopOut</c>), or down onto the ground (<c>StepDown</c>), and the walk on.
        /// <paramref name="landing"/> keeps a step-down off ground inside a building (NGO <c>OpenLanding</c>).
        /// </summary>
        public void AlongAndOff(SurfaceGraph graph, SurfaceRoute.Route route, int first, System.Func<Vector3, Vector3> landing = null)
        {
            if (OnWallFrom == int.MaxValue) WallStarts();
            Vector3 normal = Along(graph, route.Nodes, first);
            WallEnds();
            if (!route.HasExit) { EndsOnWall = true; return; }
            Vector3 from = LastFeet;
            Vector3 fwd = Corners[Corners.Count - 1].Forward;
            if (route.Exit.kind == SurfaceGraph.JoinKind.Top)
            {
                // NGO TopOut: up past the edge turning upright, then in over it (the mantle's two legs)
                Vector3 top = route.Exit.navPoint;
                Vector3 outward = Outward(normal, -fwd);
                Quaternion upright = Facing(-outward);
                float speed = Mathf.Max(1f, WallSpeed);
                Vector3 lifted = new Vector3(from.x, Mathf.Max(from.y, top.y) + settings.mantleClearance, from.z) + outward * 0.1f;
                float liftSeconds = Mathf.Max(MinimumDuration, 2f * Mathf.Max(0.2f, lifted.y - from.y) / speed);
                Leg(WallRunPhase.Mantle, lifted, upright, liftSeconds, EaseOut, EaseIn);
                Leg(WallRunPhase.Mantle, top, upright, settings.mantleSeconds, EaseIn, EaseOut);
            }
            else
            {
                // NGO StepDown: a short hop off the wall's foot, turning upright
                Vector3 ground = landing != null ? landing(route.Exit.navPoint) : route.Exit.navPoint;
                Arc(WallRunPhase.DropDown, ground, 0.25f, Facing(Outward(normal, fwd)));
            }
            // the walk on to the place clicked (NGO WalkOnAfterLanding, when it is more than half a metre on)
            Vector3 walkOnTo = route.Departure.Length > 0 ? route.Departure[route.Departure.Length - 1] : route.Exit.navPoint;
            if ((walkOnTo - LastFeet).sqrMagnitude > 0.25f)
                for (int i = 1; i < route.Departure.Length; i++) Ground(route.Departure[i]);
        }

        /// <summary>
        /// The corner budget: a longer layout is cut at the last wall corner that fits and ends standing there (the rest is asked again
        /// from there). False when even the way onto the wall does not fit.
        /// </summary>
        public bool FitTo(int maxCorners)
        {
            if (Corners.Count <= maxCorners) return true;
            int keep = Mathf.Min(maxCorners, OffWallFrom) - 1;   // the last corner kept is the end of a wall segment
            if (OnWallFrom == int.MaxValue || keep < OnWallFrom) return false;
            Corners.RemoveRange(keep + 1, Corners.Count - keep - 1);
            OffWallFrom = Corners.Count;
            EndsOnWall = true;
            Truncated = true;
            return true;
        }
    }
}
