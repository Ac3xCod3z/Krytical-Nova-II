using KryticalNova.World;
using KryticalNova.World.Surfaces;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Player
{
    /// <summary>
    /// Wallstride's wall walk for an entity ghost (`m3c-wallstride-design.md` section 3): NGO's <see cref="ServerWallClick"/> and
    /// <see cref="PushOff"/> worked out for a body that stands where the ghost says, laid out as one walk (<see cref="WallWalkLayout"/>)
    /// instead of driven by the motor. The same checks in the same order with the same words; the body is only read (its NavMesh agent's
    /// filter and radius, the zone's graph, the colliders), never moved.
    /// </summary>
    public sealed partial class PlayerWallRunMotor
    {
        /// <summary>What the ghost's body is doing, as NGO's checks need it (the entity side knows it, the body does not).</summary>
        public struct GhostWallAsk
        {
            public bool Open, Alive, Busy, Jump, HasFluxForOne;
            public bool OnWall;
            /// <summary>The body's up (the tilt's) and forward.</summary>
            public Vector3 Up, Forward;
            /// <summary>The ground speed the wall walk keeps (NGO: the speed when the feet meet the wall), m/s.</summary>
            public float GroundSpeed;
        }

        /// <summary>
        /// A ghost's wall walk phase on this (view) body: NGO's own <see cref="Show"/> (the clip fitted to <paramref name="seconds"/>, the
        /// feel, the feet on the wall), with <paramref name="mine"/> saying whose body it is (its sounds, its fades).
        /// </summary>
        public void ShowGhostPhase(WallRunPhase phase, float seconds, bool mine)
        {
            ghostMine = mine;
            Show(new WallRunPose { Phase = phase, Duration = seconds });
        }

        /// <summary>A wall the scripted test run uses (KnPlayHere), all its points found in the zone's own surface graph.</summary>
        public struct TestWall
        {
            /// <summary>The foot (the ground join's NavMesh point), the ground 6 m and 9 m out in front, the roof's NavMesh point over the edge.</summary>
            public Vector3 Foot, Front, FrontFar, Top;
            /// <summary>Points on the face (a click there): 4 m up, 3 m along at that height, 2.5 m up; the face's normal.</summary>
            public Vector3 Onto, Along, Lower, Normal;
        }

        /// <summary>
        /// For the scripted test run: an upright wall near <paramref name="near"/> that the zone's graph covers from its foot to a roof, with a
        /// node 4 m up, one 3 m along at that height, one 2.5 m up, and open NavMesh in front of it. Null when found, else why not.
        /// </summary>
        public string FindTestWall(Vector3 near, out TestWall wall)
        {
            wall = default;
            SurfaceGraph graph = ZoneGraph;
            if (graph == null || settings == null) return "the zone has no surface graph";
            graph.EnsureLoaded();
            var grounds = new System.Collections.Generic.List<SurfaceGraph.Join>(64);
            var tops = new System.Collections.Generic.List<SurfaceGraph.Join>(16);
            graph.JoinsNear(near, SurfaceGraph.JoinKind.Ground, 60, grounds);
            int upright = 0, tall = 0, roofed = 0;
            foreach (SurfaceGraph.Join join in grounds)
            {
                Vector3 n = graph.normals[join.node], p = graph.points[join.node];
                float angle = Vector3.Angle(n, Vector3.up);
                if (angle < 80f || angle > 100f) continue;
                upright++;
                int SameFace(Vector3 at)
                {
                    int k = graph.Nearest(at, 0.5f);
                    return k >= 0 && Vector3.Dot(graph.normals[k], n) > 0.95f ? k : -1;
                }
                Vector3 side = Vector3.Cross(Vector3.up, n).normalized;
                int high = SameFace(p + Vector3.up * 4f), low = SameFace(p + Vector3.up * 2.5f);
                int along = SameFace(p + Vector3.up * 4f + side * 3f);
                if (along < 0) along = SameFace(p + Vector3.up * 4f - side * 3f);
                if (high < 0 || low < 0 || along < 0) continue;
                tall++;
                graph.JoinsNear(p + Vector3.up * 6f, SurfaceGraph.JoinKind.Top, 10, tops);
                bool hasTop = false;
                SurfaceGraph.Join top = default;
                foreach (SurfaceGraph.Join t in tops)
                {
                    Vector3 flat = t.navPoint - p;
                    flat.y = 0f;
                    if (Vector3.Dot(graph.normals[t.node], n) > 0.9f && flat.magnitude < 4f && t.navPoint.y > p.y + 3f) { hasTop = true; top = t; break; }
                }
                if (!hasTop) continue;
                roofed++;
                Vector3 outward = WallWalkLayout.Outward(n, Vector3.forward);
                if (!NavMesh.SamplePosition(join.navPoint + outward * 6f, out NavMeshHit front, 1.5f, Filter())
                    || !NavMesh.SamplePosition(join.navPoint + outward * 9f, out NavMeshHit far, 1.5f, Filter())
                    || !OnOpenGround(front.position) || !OnOpenGround(far.position)) continue;
                Vector3 Face(int node) => graph.points[node] - graph.normals[node] * settings.wallStandOff;
                wall = new TestWall
                {
                    Foot = join.navPoint, Front = front.position, FrontFar = far.position, Top = top.navPoint,
                    Onto = Face(high), Along = Face(along), Lower = Face(low), Normal = n,
                };
                return null;
            }
            return $"no wall fits near {near.x:F0},{near.z:F0}: {grounds.Count} ground joins, {upright} upright, {tall} tall enough, {roofed} with a roof";
        }

        /// <summary>Wallstride's gate for a level and ranks (NGO <see cref="WallRunRules.IsOpen"/> with this body's settings).</summary>
        public bool WallstrideOpenFor(int level, int runRank, int jumpRank) => WallRunRules.IsOpen(level, runRank, jumpRank, settings);

        /// <summary>Grip on a wall, Flux a second (NGO <c>gripFluxPerSecond</c>).</summary>
        public float GripFluxPerSecond => settings != null ? settings.gripFluxPerSecond : 0f;

        /// <summary>
        /// The view's test of a wall click (PointAndClickPlayer in view mode): the zone's graph has a node by the point (NGO
        /// <see cref="NodeAt"/>). The gate is not asked here: a locked player's click goes to the planners, which refuse it in NGO's words.
        /// </summary>
        public bool WantsWallWalkClick(Vector3 point, Vector3 normal, bool groundLayer)
        {
            SurfaceGraph graph = ZoneGraph;
            if (graph == null || settings == null) return false;
            // the ground layer counts only where it is steep enough to be a wall (NGO IsSteepWallClick)
            if (groundLayer && Vector3.Angle(normal, Vector3.up) < settings.graphMinSurfaceAngle) return false;
            return NodeAt(graph, point, normal) >= 0;
        }

        /// <summary>
        /// A click concerning a wall, for the ghost standing at <paramref name="feet"/>: a layout of the walk there, or NGO's refusal.
        /// Empty reason and a null layout: not Wallstride's click (no graph node near it and the body is not on a wall).
        /// </summary>
        public string PlanWallForGhost(Vector3 feet, Vector3 point, Vector3 normal, in GhostWallAsk ask, Vector3 wallBase, out WallWalkLayout layout)
        {
            layout = null;
            SurfaceGraph graph = ZoneGraph;
            if (graph == null || settings == null) return "That cannot be climbed.";
            if (!ask.Open) return WallRunRules.Reason(ClimbVerdict.Locked, settings);
            if (!ask.Alive) return "Not now.";
            int target = NodeAt(graph, point, normal);
            float wallSpeed = ask.GroundSpeed * settings.wallSpeedShare;

            if (!ask.OnWall)
            {
                // From the ground (or a roof) onto the wall (NGO ServerWallClick, the same order)
                if (target < 0) return "That cannot be climbed.";
                if (ask.Busy) return "Not now.";
                if (!NavMesh.SamplePosition(feet, out NavMeshHit onMesh, 0.6f, Filter())) return "Not now.";
                feet = onMesh.position;
                if (ask.Jump && ask.HasFluxForOne)
                {
                    // NGO: a jump that does not work goes on to the way onto the wall from its foot (its reason is not told)
                    if (string.IsNullOrEmpty(JumpOntoForGhost(graph, feet, ask, target, wallSpeed, out layout)) && layout != null) return string.Empty;
                    layout = null;
                }
                SurfaceRoute.Route route = SurfaceRoute.ToNode(graph, feet, target, Filter());
                if (route == null || !route.HasEntry) return "There is no way onto that wall from here.";
                if (!ask.HasFluxForOne) return WallRunRules.Reason(ClimbVerdict.NoFlux, settings);
                layout = new WallWalkLayout(settings, StickProbe, feet, Vector3.up, ask.Forward, wallSpeed);
                // NGO ArmWall: within reach of the join's NavMesh point the feet meet the wall at once, else walk there first
                if (Flat(feet, route.Entry.navPoint) > WallArmTrigger + Radius)
                {
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(feet, route.Entry.navPoint, Filter(), path) || path.status != NavMeshPathStatus.PathComplete)
                    { layout = null; return "There is no way onto that wall from here."; }
                    // the plant starts as the agent comes within the trigger of the point (NGO WatchWallArmed)
                    WalkUpTo(layout, path.corners, WallArmTrigger + (agent != null ? agent.stoppingDistance : 0f));
                }
                layout.PlantAndAlong(graph, route, ask.Forward);
                return Fit(layout);
            }

            int here = graph.Nearest(feet - ask.Up * settings.wallFootLift + ask.Up * settings.wallStandOff, NodeSnapReach);
            if (here < 0) return "Not now.";
            if (ask.Jump)
            {
                // a jump to another place on a wall (this one or another), or off it toward the ground or a roof
                if (target >= 0 && Vector3.Distance(graph.points[target], graph.points[here]) > JumpOnMinimum)
                    return Done(JumpOntoForGhost(graph, feet, ask, target, wallSpeed, out layout), ref layout);
                return Done(JumpOffForGhost(feet, point, ask, wallBase, wallSpeed, out layout), ref layout);
            }

            if (target >= 0)
            {
                // along the wall
                SurfaceRoute.Route along = SurfaceRoute.Along(graph, here, target);
                if (along == null) return "That part of the wall is not joined to this one.";
                layout = new WallWalkLayout(settings, StickProbe, feet, ask.Up, ask.Forward, wallSpeed);
                layout.AlongAndOff(graph, along, 0, w => OpenLandingFor(w, wallBase));
                return Fit(layout);
            }

            // off the wall: over its edge onto a top, or down to the ground, whichever is the shorter way there
            if (!NavMesh.SamplePosition(point, out NavMeshHit there, 1.5f, Filter())) return "Choose a place to go to.";
            SurfaceRoute.Route top = SurfaceRoute.Off(graph, here, there.position, SurfaceGraph.JoinKind.Top, Filter());
            SurfaceRoute.Route down = SurfaceRoute.Off(graph, here, there.position, SurfaceGraph.JoinKind.Ground, Filter());
            SurfaceRoute.Route best = top == null ? down : down == null ? top : (top.Length <= down.Length ? top : down);
            if (best != null)
            {
                layout = new WallWalkLayout(settings, StickProbe, feet, ask.Up, ask.Forward, wallSpeed);
                layout.AlongAndOff(graph, best, 0, w => OpenLandingFor(w, wallBase));
                return Fit(layout);
            }
            // no way along the wall to it: jump for it
            return Done(JumpOffForGhost(feet, there.position, ask, wallBase, wallSpeed, out layout), ref layout);
        }

        /// <summary>
        /// Out of Grip (NGO <see cref="PushOff"/> not aimed): off the wall in a flip, down to the ground 3 m out in front of it, else back
        /// where the wall walk started (<paramref name="wallBase"/>), on open ground.
        /// </summary>
        public WallWalkLayout PushOffForGhost(Vector3 feet, in GhostWallAsk ask, Vector3 wallBase)
        {
            if (settings == null) return null;
            Vector3 outward = WallWalkLayout.Outward(ask.Up, -ask.Forward);
            Vector3 aim = feet + outward * settings.pushOffDistance;
            Vector3 landing = wallBase;
            if (Physics.Raycast(aim + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, PushOffGroundProbe, GroundMask(), QueryTriggerInteraction.Ignore)
                && NavMesh.SamplePosition(ground.point, out NavMeshHit near, 3f, Filter()))
                landing = near.position;
            landing = OpenLandingFor(landing, wallBase);
            var layout = new WallWalkLayout(settings, StickProbe, feet, ask.Up, ask.Forward, ask.GroundSpeed * settings.wallSpeedShare);
            Vector3 away = landing - feet;
            away.y = 0f;
            layout.WallEnds();
            layout.Arc(WallRunPhase.PushOff, landing, settings.pushOffLift, WallWalkLayout.Facing(away.sqrMagnitude > 1e-4f ? away : outward));
            return layout;
        }

        /// <summary>NGO <see cref="JumpOnto"/> for the ghost: an arc that turns the body to meet the face, then along to the place clicked.</summary>
        private string JumpOntoForGhost(SurfaceGraph graph, Vector3 from, in GhostWallAsk ask, int target, float wallSpeed, out WallWalkLayout layout)
        {
            layout = null;
            int catchNode = target;
            Vector3 targetFeet = FeetAt(graph, target);
            if (targetFeet.y - from.y > JumpOnRise)
            {
                // too high to jump to: catch the face lower down, under the place clicked, and run up to it
                Vector3 low = graph.points[target];
                low.y = from.y + JumpOnRise * 0.6f;
                catchNode = graph.Nearest(low, 2.5f);
                if (catchNode < 0) return "Too high to jump to.";
            }
            Vector3 to = FeetAt(graph, catchNode);
            Vector3 flat = to - from;
            flat.y = 0f;
            if (flat.magnitude > settings.maxDropDistance) return $"Too far: jump within {settings.maxDropDistance:0} m.";
            Vector3 fromUp = ask.OnWall ? ask.Up : Vector3.up;
            if (!JumpClear(from, to, fromUp, graph.normals[catchNode])) return "Something is in the way.";

            SurfaceRoute.Route route = catchNode == target
                ? new SurfaceRoute.Route { Nodes = new[] { target } }
                : SurfaceRoute.Along(graph, catchNode, target) ?? new SurfaceRoute.Route { Nodes = new[] { catchNode } };
            float g = settings.dropGravity;
            float apexY = Mathf.Max(from.y, to.y) + JumpOnLift;
            float up = Mathf.Sqrt(2f * Mathf.Max(0.05f, apexY - from.y) / g), down = Mathf.Sqrt(2f * Mathf.Max(0.05f, apexY - to.y) / g);
            Vector3 apex = Vector3.Lerp(from, to, up / (up + down));
            Vector3 n = graph.normals[catchNode];
            if (ask.OnWall && Physics.Linecast(from + fromUp * 0.6f, to + n * 0.3f, SurfaceMask(), QueryTriggerInteraction.Ignore))
            {
                // round a corner: the arc's top swings out past it rather than through the building
                Vector3 outward = fromUp + n;
                if (outward.sqrMagnitude > 1e-3f) apex = (from + to) * 0.5f + outward.normalized * 1.5f;
            }
            apex.y = apexY;
            layout = new WallWalkLayout(settings, StickProbe, from, ask.OnWall ? ask.Up : Vector3.up, ask.Forward, wallSpeed);
            layout.Leg(WallRunPhase.JumpOn, apex, WallWalkLayout.Facing(flat.sqrMagnitude > 1e-4f ? flat : -n), up, WallWalkLayout.EaseOut, WallWalkLayout.Linear);
            layout.Leg(WallRunPhase.JumpOn, to, WallWalkLayout.WallFacing(n, flat.sqrMagnitude > 1e-4f ? flat : Vector3.up, ask.Forward),
                Mathf.Max(0.25f, down), WallWalkLayout.EaseIn, WallWalkLayout.Linear);
            layout.WallStarts();
            layout.AlongAndOff(graph, route, 1);
            return Fit(layout);
        }

        /// <summary>NGO <see cref="JumpOffTowards"/> for the ghost: off the wall in an arc with the flip, over whatever is between.</summary>
        private string JumpOffForGhost(Vector3 feet, Vector3 point, in GhostWallAsk ask, Vector3 wallBase, float wallSpeed, out WallWalkLayout layout)
        {
            layout = null;
            if (!NavMesh.SamplePosition(point, out NavMeshHit landing, 2f, Filter()) || !OnOpenGround(landing.position))
                return "Choose a place on the ground or a roof to jump to.";
            Vector3 flat = landing.position - feet;
            flat.y = 0f;
            if (flat.magnitude > settings.maxDropDistance) return $"Too far: jump off within {settings.maxDropDistance:0} m.";
            if (landing.position.y > feet.y + 1f) return "Too high to jump up to.";
            float lift = settings.pushOffLift;
            while (!ArcClear(feet, landing.position, lift))
            {
                lift += ArcLiftStep;
                if (lift > settings.pushOffLift + ArcLiftStep * 3f) return "Something is in the way.";
            }
            layout = new WallWalkLayout(settings, StickProbe, feet, ask.Up, ask.Forward, wallSpeed);
            layout.WallEnds();
            Vector3 outward = WallWalkLayout.Outward(ask.Up, -ask.Forward);
            layout.Arc(WallRunPhase.PushOff, landing.position, lift, WallWalkLayout.Facing(flat.sqrMagnitude > 1e-4f ? flat : outward));
            return string.Empty;
        }

        /// <summary>The NavMesh walk to the wall's foot, ending as the plant would start: <paramref name="shortBy"/> before its last point.</summary>
        private static void WalkUpTo(WallWalkLayout layout, Vector3[] corners, float shortBy)
        {
            if (corners == null || corners.Length < 2) return;
            Vector3 end = corners[corners.Length - 1];
            for (int i = 1; i < corners.Length - 1; i++) layout.Ground(corners[i]);
            Vector3 prev = corners[corners.Length - 2];
            Vector3 lastLeg = end - prev;
            float len = lastLeg.magnitude;
            layout.Ground(len > shortBy ? end - lastLeg / len * shortBy : prev);
        }

        /// <summary>NGO <see cref="OpenLanding"/> with the ghost's own start of the wall walk in place of the motor's plan.</summary>
        private Vector3 OpenLandingFor(Vector3 wanted, Vector3 wallBase) => OnOpenGround(wanted) ? wanted : wallBase;

        /// <summary>NGO's stick probe against the walls (the motor's own surface mask).</summary>
        private bool StickProbe(Vector3 origin, Vector3 direction, float length, out Vector3 point, out Vector3 faceNormal)
        {
            point = default;
            faceNormal = default;
            if (!Physics.Raycast(origin, direction, out RaycastHit face, length, SurfaceMask(), QueryTriggerInteraction.Ignore)) return false;
            point = face.point;
            faceNormal = face.normal;
            return true;
        }

        private static string Fit(WallWalkLayout layout) =>
            layout.FitTo(WallWalkLayout.MaxCorners) ? string.Empty : "There is no way onto that wall from here.";

        private static string Done(string reason, ref WallWalkLayout layout)
        {
            if (!string.IsNullOrEmpty(reason)) layout = null;
            return reason ?? string.Empty;
        }
    }
}
