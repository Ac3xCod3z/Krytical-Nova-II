using System.Collections.Generic;
using KryticalNova.Networking;
using KryticalNova.World;
using KryticalNova.World.Surfaces;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Player
{
    /// <summary>
    /// Walls as a second ground, stage 2 (owner, 17-18 Oct 2026): where the zone has a baked <see cref="SurfaceGraph"/>,
    /// a click on a wall puts the player on it to stand and run like on the ground, instead of the old straight climb.
    /// The way there is walked on the NavMesh to a wall's foot, the feet meet the wall (a warped plant), and the body runs
    /// along the graph standing on the face, turned so its up is the face's normal: the game's own idle, walk and run play
    /// from its travel. From the wall a click goes on along it, over its edge onto a top, down onto the ground, or (Jump
    /// selected) jumps off toward the place clicked. There is no height limit: standing on a wall costs Grip, Flux a
    /// second, and with none left the player pushes off and flips down to the ground. Server-authoritative like the rest
    /// of the motor; NetworkTransform carries the turned body, the phase tells every machine what to play.
    /// </summary>
    public sealed partial class PlayerWallRunMotor
    {
        private const float StickProbeOut = 0.6f;
        private const float StickProbeLength = 1.2f;
        private const float WallArmTrigger = 0.6f;
        private const float NodeSnapReach = 2f;
        private const float PushOffGroundProbe = 80f;

        // The wall the body is on: the route along it and where the body is on that route.
        private SurfaceGraph wallGraph;
        private readonly List<Vector3> wallFeet = new List<Vector3>(64);
        private readonly List<Vector3> wallNormals = new List<Vector3>(64);
        private int wallIndex;
        private int wallNode = -1;
        private Vector3 wallTrack;       // where the body is along the route (the polyline of node feet)
        private Vector3 wallNormal = Vector3.up;
        private Vector3 wallTravel = Vector3.up;
        private float gripDebt;
        // How the route leaves the wall at its end.
        private bool wallExits;
        private SurfaceGraph.Join wallExit;
        private Vector3 walkOnTo;
        private bool walkOnAfterLanding;
        // On the ground, walking to a wall's foot to step onto it.
        private bool wallArmed;
        private SurfaceRoute.Route wallArmedRoute;
        private float wallArmedAt;

        /// <summary>Standing or running on a wall (walls as a second ground), on every machine.</summary>
        public bool IsOnWall => IsOnWallPhase(Phase);

        private static bool IsOnWallPhase(WallRunPhase p) => p == WallRunPhase.WallMove || p == WallRunPhase.WallHold;

        /// <summary>The zone's surface graph, when it has one.</summary>
        private static SurfaceGraph ZoneGraph
        {
            get
            {
                ZoneRules rules = ZoneRules.Current;
                return rules != null && rules.Entry != null ? rules.Entry.surfaceGraph : null;
            }
        }

        // ------------------------------------------------------------------ onto a wall from the ground

        /// <summary>A click on a wall the zone's graph covers: the new way (walls as a second ground) takes it.</summary>
        private bool WantsWallWalk(Vector3 point, Vector3 normal)
        {
            SurfaceGraph graph = ZoneGraph;
            return graph != null && settings != null && WallstrideOpen && NodeAt(graph, point, normal) >= 0;
        }

        /// <summary>A click on the ground layer steep enough to be a wall (a mountain side) that the zone's graph covers.</summary>
        public bool IsSteepWallClick(Vector3 point, Vector3 normal) =>
            settings != null && Vector3.Angle(normal, Vector3.up) >= settings.graphMinSurfaceAngle && WantsWallWalk(point, normal);

        private int NodeAt(SurfaceGraph graph, Vector3 point, Vector3 normal)
        {
            Vector3 n = normal.sqrMagnitude > 1e-4f ? normal.normalized : Vector3.zero;
            // A click on a top (a roof, the street) means that top, never the wall just under its edge (owner, 18 Oct
            // 2026: a jump at a roof snapped to the top of the wall below it and went into the building).
            if (n != Vector3.zero && Vector3.Angle(n, Vector3.up) < settings.graphMinSurfaceAngle * 0.8f) return -1;
            return graph.Nearest(point + n * settings.wallStandOff, settings.wallClickReach);
        }

        private bool TryWallWalkClicked(Vector3 point, Vector3 normal)
        {
            if (!HasAuthority)
            {
                RequestWallClickServerRpc(point, normal, movementSkills != null && movementSkills.IsJumpSelected);
                return true;
            }
            if (!ServerWallClick(point, normal, movementSkills != null && movementSkills.IsJumpSelected, out string reason)) Message?.Invoke(reason);
            return true;
        }

        /// <summary>A click while standing on a wall (PointAndClickPlayer): along it, over it, down, or a jump off.</summary>
        public bool TryWallClick(Vector3 point, Vector3 normal, bool jump)
        {
            if (!IsOnWall) return false;
            if (!HasAuthority) { RequestWallClickServerRpc(point, normal, jump); return true; }
            if (!ServerWallClick(point, normal, jump, out string reason)) Message?.Invoke(reason);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestWallClickServerRpc(Vector3 point, Vector3 normal, bool jump)
        {
            if (!RpcBudget.Allow(this, CallKind.Move, nameof(RequestWallClickServerRpc))) return;
            if (!RpcBudget.PointFits(this, nameof(RequestWallClickServerRpc), point) || !RpcBudget.PointFits(this, nameof(RequestWallClickServerRpc), normal)) return;
            if (!ServerWallClick(point, normal, jump, out string reason)) ClimbRefusedRpc(reason);
        }

        /// <summary>Every click that concerns a wall, decided here (authority).</summary>
        private bool ServerWallClick(Vector3 point, Vector3 normal, bool jump, out string reason)
        {
            reason = string.Empty;
            SurfaceGraph graph = ZoneGraph;
            if (graph == null || settings == null) { reason = "That cannot be climbed."; return false; }
            if (!WallstrideOpen) { reason = WallRunRules.Reason(ClimbVerdict.Locked, settings); return false; }
            if (resources == null || !resources.IsAlive) { reason = "Not now."; return false; }
            int target = NodeAt(graph, point, normal);

            if (!IsOnWall)
            {
                // From the ground (or a roof) onto the wall.
                if (target < 0) { reason = "That cannot be climbed."; return false; }
                if (IsBusy || (jumpMotor != null && jumpMotor.IsBusy)) { reason = "Not now."; return false; }
                if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) { reason = "Not now."; return false; }
                // Jump selected and the wall within a jump: jump at it and land on it (owner, 18 Oct 2026).
                if (jump && resources.CanSpendChi(1f) && JumpOnto(graph, point, normal, target, out reason)) return true;
                SurfaceRoute.Route route = SurfaceRoute.ToNode(graph, Feet, target, Filter());
                if (route == null || !route.HasEntry) { reason = "There is no way onto that wall from here."; return false; }
                if (!resources.CanSpendChi(1f)) { reason = WallRunRules.Reason(ClimbVerdict.NoFlux, settings); return false; }
                ArmWall(graph, route);
                return true;
            }

            int here = CurrentNode(graph);
            if (here < 0) { reason = "Not now."; return false; }
            if (jump)
            {
                // A jump to another place on a wall (this one or another), or off it toward the ground or a roof.
                if (target >= 0 && Vector3.Distance(graph.points[target], graph.points[here]) > JumpOnMinimum)
                    return JumpOnto(graph, point, normal, target, out reason);
                return JumpOffTowards(point, out reason);
            }

            if (target >= 0)
            {
                // Along the wall.
                SurfaceRoute.Route along = SurfaceRoute.Along(graph, here, target);
                if (along == null) { reason = "That part of the wall is not joined to this one."; return false; }
                FollowOnWall(graph, along);
                return true;
            }

            // Off the wall: over its edge onto a top, or down to the ground, whichever is the shorter way there.
            if (!NavMesh.SamplePosition(point, out NavMeshHit there, 1.5f, Filter())) { reason = "Choose a place to go to."; return false; }
            SurfaceRoute.Route top = SurfaceRoute.Off(graph, here, there.position, SurfaceGraph.JoinKind.Top, Filter());
            SurfaceRoute.Route down = SurfaceRoute.Off(graph, here, there.position, SurfaceGraph.JoinKind.Ground, Filter());
            SurfaceRoute.Route best = top == null ? down : down == null ? top : (top.Length <= down.Length ? top : down);
            if (best != null)
            {
                FollowOnWall(graph, best);
                return true;
            }
            // No way along the wall to it: jump for it.
            return JumpOffTowards(there.position, out reason);
        }

        private void ArmWall(SurfaceGraph graph, SurfaceRoute.Route route)
        {
            wallGraph = graph;
            wallArmedRoute = route;
            if (Flat(Feet, route.Entry.navPoint) <= WallArmTrigger + Radius)
            {
                PlantOnWall();
                return;
            }
            // Walk (or run) to the wall's foot first, then step onto it.
            var path = new NavMeshPath();
            if (!agent.CalculatePath(route.Entry.navPoint, path) || path.status != NavMeshPathStatus.PathComplete) return;
            agent.SetPath(path);
            agent.isStopped = false;
            wallArmed = true;
            wallArmedAt = Time.time;
        }

        private void WatchWallArmed()
        {
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh || wallArmedRoute == null) { wallArmed = false; return; }
            // Another order took the route over.
            if (Time.time - wallArmedAt > RearmSeconds && Flat(agent.destination, wallArmedRoute.Entry.navPoint) > 0.6f) { wallArmed = false; return; }
            if (Flat(Feet, wallArmedRoute.Entry.navPoint) > WallArmTrigger + agent.stoppingDistance + agent.velocity.magnitude * Time.deltaTime * 2f) return;
            PlantOnWall();
        }

        /// <summary>The feet meet the wall: a warped step from the ground onto the face, the body turning to stand on it.</summary>
        private void PlantOnWall()
        {
            wallArmed = false;
            SurfaceRoute.Route route = wallArmedRoute;
            wallArmedRoute = null;
            if (route == null || wallGraph == null) return;
            resumeSpeed = GroundSpeed();
            if (agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            agent.enabled = false; // the wall motor owns the body until it is off the wall
            LoadWallRoute(wallGraph, route);
            plan = new Plan { Kind = ClimbableKind.Wall, Normal = wallNormals[0], BaseFeet = Feet };

            legCount = 0;
            if (route.HasEntry && route.Entry.kind == SurfaceGraph.JoinKind.Top)
            {
                // From a roof: out over the edge, then down onto the face below, turning to stand on it head down.
                Vector3 n = wallNormals[0];
                Vector3 outward = new Vector3(n.x, 0f, n.z);
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : transform.forward;
                Vector3 node = wallFeet[0];
                Vector3 over = new Vector3(node.x, Mathf.Max(Feet.y, node.y) + settings.mantleClearance, node.z) + outward * 0.5f;
                AddLeg(WallRunPhase.Plant, over, Facing(outward), settings.wallPlantSeconds, Ease.Out, Ease.InOut);
                AddLeg(WallRunPhase.Plant, node, WallFacing(n, Vector3.down), settings.wallPlantSeconds, Ease.In, Ease.Out);
            }
            else
            {
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, wallNormals[0]);
                AddLeg(WallRunPhase.Plant, wallFeet[0], WallFacing(wallNormals[0], up), settings.wallPlantSeconds, Ease.In, Ease.Out);
            }
            afterRoute = WallRunPhase.WallMove;
            PlayRoute();
        }

        private const float JumpOnMinimum = 2f;
        private const float JumpOnRise = 3.5f;
        private const float JumpOnLift = 0.8f;

        /// <summary>
        /// A jump at a wall (owner, 18 Oct 2026): from the ground, a roof or another wall, an arc under gravity that turns the
        /// body as it comes in so the feet meet the face and it stands on it. A place higher than a jump reaches is caught
        /// low on the same face and run up to.
        /// </summary>
        private bool JumpOnto(SurfaceGraph graph, Vector3 point, Vector3 normal, int target, out string reason)
        {
            reason = string.Empty;
            Vector3 from = Feet;
            int catchNode = target;
            Vector3 targetFeet = FeetAt(graph, target);
            if (targetFeet.y - from.y > JumpOnRise)
            {
                // Too high to jump to: catch the face lower down, under the place clicked, and run up to it.
                Vector3 low = graph.points[target];
                low.y = from.y + JumpOnRise * 0.6f;
                catchNode = graph.Nearest(low, 2.5f);
                if (catchNode < 0) { reason = "Too high to jump to."; return false; }
            }
            Vector3 to = FeetAt(graph, catchNode);
            Vector3 flat = to - from;
            flat.y = 0f;
            if (flat.magnitude > settings.maxDropDistance) { reason = $"Too far: jump within {settings.maxDropDistance:0} m."; return false; }
            if (!JumpClear(from, to, IsOnWall ? transform.up : Vector3.up, graph.normals[catchNode])) { reason = "Something is in the way."; return false; }

            SurfaceRoute.Route route = catchNode == target
                ? new SurfaceRoute.Route { Nodes = new[] { target } }
                : SurfaceRoute.Along(graph, catchNode, target) ?? new SurfaceRoute.Route { Nodes = new[] { catchNode } };
            if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
                agent.velocity = Vector3.zero;
            }
            if (agent != null) agent.enabled = false;
            resumeSpeed = GroundSpeed();
            LoadWallRoute(graph, route);
            if (plan.Kind != ClimbableKind.Wall || !IsOnWall) plan = new Plan { Kind = ClimbableKind.Wall, Normal = graph.normals[catchNode], BaseFeet = from };

            float g = settings.dropGravity;
            float apexY = Mathf.Max(from.y, to.y) + JumpOnLift;
            float up = Mathf.Sqrt(2f * Mathf.Max(0.05f, apexY - from.y) / g), down = Mathf.Sqrt(2f * Mathf.Max(0.05f, apexY - to.y) / g);
            Vector3 apex = Vector3.Lerp(from, to, up / (up + down));
            Vector3 n = graph.normals[catchNode];
            if (IsOnWall && Physics.Linecast(from + transform.up * 0.6f, to + n * 0.3f, SurfaceMask(), QueryTriggerInteraction.Ignore))
            {
                // Round a corner: the arc's top swings out past it rather than through the building.
                Vector3 outward = transform.up + n;
                if (outward.sqrMagnitude > 1e-3f) apex = (from + to) * 0.5f + outward.normalized * 1.5f;
            }
            apex.y = apexY;
            legCount = 0;
            AddLeg(WallRunPhase.JumpOn, apex, Facing(flat.sqrMagnitude > 1e-4f ? flat : -n), up, Ease.Out, Ease.Linear);
            AddLeg(WallRunPhase.JumpOn, to, WallFacing(n, flat.sqrMagnitude > 1e-4f ? flat : Vector3.up), Mathf.Max(0.25f, down), Ease.In, Ease.Linear);
            afterRoute = WallRunPhase.WallMove;
            PlayRoute();
            return true;
        }

        /// <summary>
        /// A jump's way is clear: straight, or high over, or (from a wall) round the outside of a corner the way a leap
        /// goes, out from both faces.
        /// </summary>
        private bool JumpClear(Vector3 from, Vector3 to, Vector3 fromNormal, Vector3 toNormal)
        {
            int mask = SurfaceMask();
            Vector3 start = from + fromNormal * 0.6f, end = to + toNormal * 0.3f;
            if (!Physics.Linecast(start, end, mask, QueryTriggerInteraction.Ignore)) return true;
            if (!Physics.Linecast(start + Vector3.up * 1.5f, end + Vector3.up * 0.5f, mask, QueryTriggerInteraction.Ignore)) return true;
            Vector3 outward = fromNormal + toNormal;
            if (outward.sqrMagnitude < 1e-3f) return false;
            Vector3 round = (from + to) * 0.5f + outward.normalized * 1.5f;
            return !Physics.Linecast(start, round, mask, QueryTriggerInteraction.Ignore) && !Physics.Linecast(round, end, mask, QueryTriggerInteraction.Ignore);
        }

        private Vector3 FeetAt(SurfaceGraph graph, int node) =>
            graph.points[node] - graph.normals[node] * settings.wallStandOff + graph.normals[node] * settings.wallFootLift;

        /// <summary>
        /// Where movement is decided (PointAndClickPlayer): a move with no walk there at all (a roof to the street), or
        /// while running a walk much longer than the way over a wall, goes over the wall: down from a roof over its edge,
        /// or up one. The route is lined up and <paramref name="entry"/> is where the agent should walk first.
        /// </summary>
        public bool TryArmSurfaceRoute(Vector3 destination, NavMeshPath walkPath, out Vector3 entry)
        {
            entry = destination;
            wallArmed = false;
            if (!HasAuthority || settings == null || IsBusy || !WallstrideOpen) return false;
            SurfaceGraph graph = ZoneGraph;
            if (graph == null || resources == null || !resources.CanSpendChi(1f)) return false;
            bool noWalk = walkPath == null || walkPath.status != NavMeshPathStatus.PathComplete;
            if (!noWalk && (movementSkills == null || !movementSkills.IsRunning)) return false;
            SurfaceRoute.Route route = SurfaceRoute.Through(graph, Feet, destination, Filter());
            if (route == null || !route.HasEntry) return false;
            if (!noWalk && PathLength(walkPath) < route.Length * settings.autoDetourFactor) return false;
            wallGraph = graph;
            wallArmedRoute = route;
            wallArmed = true;
            wallArmedAt = Time.time;
            entry = route.Entry.navPoint;
            return true;
        }

        // ------------------------------------------------------------------ on the wall

        /// <summary>Takes a route's nodes as the way along the wall, and how it leaves the wall at its end.</summary>
        private void LoadWallRoute(SurfaceGraph graph, SurfaceRoute.Route route)
        {
            wallGraph = graph;
            wallFeet.Clear();
            wallNormals.Clear();
            foreach (int node in route.Nodes)
            {
                Vector3 n = graph.normals[node];
                wallFeet.Add(graph.points[node] - n * settings.wallStandOff + n * settings.wallFootLift);
                wallNormals.Add(n);
            }
            wallIndex = 0;
            wallNode = route.Nodes[0];
            wallExits = route.HasExit;
            wallExit = route.Exit;
            walkOnTo = route.Departure.Length > 0 ? route.Departure[route.Departure.Length - 1] : (route.HasExit ? route.Exit.navPoint : Vector3.zero);
        }

        /// <summary>The plant has put the feet on the wall: run on along the route.</summary>
        private void BeginWallMove(Leg last)
        {
            wallTrack = last.ToFeet;
            wallNormal = wallNormals.Count > 0 ? wallNormals[0] : last.Facing * Vector3.up;
            wallTravel = Vector3.ProjectOnPlane(Vector3.up, wallNormal);
            transform.SetPositionAndRotation(last.ToFeet + last.Facing * Vector3.up * Offset, last.Facing);
            wallIndex = Mathf.Min(1, wallFeet.Count);
            gripDebt = 0f;
            SetPhase(wallIndex < wallFeet.Count ? WallRunPhase.WallMove : WallRunPhase.WallHold, 0f);
            if (wallIndex >= wallFeet.Count) ArriveOnWall();
        }

        private void FollowOnWall(SurfaceGraph graph, SurfaceRoute.Route route)
        {
            LoadWallRoute(graph, route);
            wallIndex = 0;
            SetPhase(WallRunPhase.WallMove, 0f);
        }

        /// <summary>Every frame on a wall (authority): along the route, stuck to the face, turned to stand on it, paying Grip.</summary>
        private void UpdateWall()
        {
            if (resources != null && !resources.IsAlive) return; // the death motor takes the body (the de-rez)
            float dt = Time.deltaTime;

            // Grip: Flux a second, however high. None left: off the wall.
            if (settings.gripFluxPerSecond > 0f && resources != null)
            {
                gripDebt += settings.gripFluxPerSecond * dt;
                if (gripDebt >= 1f)
                {
                    float pay = Mathf.Floor(gripDebt);
                    if (!resources.TrySpendChi(pay)) { PushOff(Vector3.zero, false); return; }
                    gripDebt -= pay;
                }
            }

            if (phase == WallRunPhase.WallMove)
            {
                float remaining = GroundSpeed() * settings.wallSpeedShare * dt;
                while (remaining > 0f && wallIndex < wallFeet.Count)
                {
                    Vector3 to = wallFeet[wallIndex] - wallTrack;
                    float d = to.magnitude;
                    if (d > 1e-4f) wallTravel = to / d;
                    if (d <= remaining)
                    {
                        wallTrack = wallFeet[wallIndex];
                        remaining -= d;
                        wallIndex++;
                        continue;
                    }
                    wallTrack += to / d * remaining;
                    remaining = 0f;
                }
            }

            // Stuck to the face as it really is (sills, rock): a short probe in along the normal. The body's tilt follows
            // the face under the feet (owner, 18 Oct 2026), eased so a corner or a bulge of rock turns it smoothly.
            Vector3 nodeNormal = wallNormals.Count > 0 ? wallNormals[Mathf.Clamp(wallIndex, 0, wallNormals.Count - 1)] : wallNormal;
            Vector3 targetNormal = nodeNormal;
            Vector3 feet = wallTrack;
            Vector3 probe = wallTrack + wallNormal * StickProbeOut;
            if (Physics.Raycast(probe, -wallNormal, out RaycastHit face, StickProbeLength, SurfaceMask(), QueryTriggerInteraction.Ignore)
                && Vector3.Angle(face.normal, Vector3.up) >= settings.graphMinSurfaceAngle * 0.8f && Vector3.Angle(face.normal, nodeNormal) < 60f)
            {
                feet = face.point + face.normal * settings.wallFootLift;
                targetNormal = face.normal;
            }
            float turn = 1f - Mathf.Exp(-settings.wallTurnRate * dt);
            wallNormal = Vector3.Slerp(wallNormal, targetNormal, turn).normalized;

            Quaternion facing = WallFacing(wallNormal, wallTravel);
            Quaternion rotation = Quaternion.Slerp(transform.rotation, facing, turn);
            transform.SetPositionAndRotation(feet + rotation * Vector3.up * Offset, rotation);

            if (phase == WallRunPhase.WallMove && wallIndex >= wallFeet.Count) ArriveOnWall();
        }

        /// <summary>The end of a route on the wall: stand there, or leave the wall the way the route goes.</summary>
        private void ArriveOnWall()
        {
            if (wallGraph != null && wallFeet.Count > 0) wallNode = CurrentNode(wallGraph);
            if (!wallExits) { SetPhase(WallRunPhase.WallHold, 0f); return; }
            if (wallExit.kind == SurfaceGraph.JoinKind.Top) TopOut(wallExit.navPoint);
            else StepDown(wallExit.navPoint);
        }

        /// <summary>Over the edge onto the top: up past it, turning upright, then in over it (the mantle's two legs).</summary>
        private void TopOut(Vector3 top)
        {
            Vector3 from = Feet;
            Vector3 outward = new Vector3(wallNormal.x, 0f, wallNormal.z);
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : -transform.forward;
            Quaternion upright = Facing(-outward);
            float speed = Mathf.Max(1f, GroundSpeed() * settings.wallSpeedShare);
            Vector3 lifted = new Vector3(from.x, Mathf.Max(from.y, top.y) + settings.mantleClearance, from.z) + outward * 0.1f;
            float liftSeconds = Mathf.Max(MinimumDuration, 2f * Mathf.Max(0.2f, lifted.y - from.y) / speed);
            legCount = 0;
            AddLeg(WallRunPhase.Mantle, lifted, upright, liftSeconds, Ease.Out, Ease.In);
            AddLeg(WallRunPhase.Mantle, top, upright, settings.mantleSeconds, Ease.In, Ease.Out);
            afterRoute = WallRunPhase.None;
            walkOnAfterLanding = (walkOnTo - top).sqrMagnitude > 0.25f;
            PlayRoute();
        }

        /// <summary>Off the wall's foot onto the ground: a short hop, turning upright.</summary>
        private void StepDown(Vector3 ground)
        {
            Vector3 outward = new Vector3(wallNormal.x, 0f, wallNormal.z);
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : transform.forward;
            legCount = 0;
            AddArc(WallRunPhase.DropDown, Feet, OpenLanding(ground), 0.25f, Facing(outward));
            afterRoute = WallRunPhase.None;
            walkOnAfterLanding = (walkOnTo - ground).sqrMagnitude > 0.25f;
            PlayRoute();
        }

        /// <summary>A jump off the wall toward a clicked place, with the Jump's flip.</summary>
        private bool JumpOffTowards(Vector3 point, out string reason)
        {
            reason = string.Empty;
            if (!NavMesh.SamplePosition(point, out NavMeshHit landing, 2f, Filter()) || !OnOpenGround(landing.position))
            { reason = "Choose a place on the ground or a roof to jump to."; return false; }
            Vector3 flat = landing.position - Feet;
            flat.y = 0f;
            if (flat.magnitude > settings.maxDropDistance) { reason = $"Too far: jump off within {settings.maxDropDistance:0} m."; return false; }
            if (landing.position.y > Feet.y + 1f) { reason = "Too high to jump up to."; return false; }
            // The arc goes over whatever is between (a roof's edge, a parapet), higher when it has to, never through it.
            float lift = settings.pushOffLift;
            while (!ArcClear(Feet, landing.position, lift))
            {
                lift += ArcLiftStep;
                if (lift > settings.pushOffLift + ArcLiftStep * 3f) { reason = "Something is in the way."; return false; }
            }
            PushOff(landing.position, true, lift);
            return true;
        }

        private const float ArcLiftStep = 1.5f;
        private const int ArcSteps = 8;
        private static readonly float[] ArcHeights = { 0.35f, 1.5f };

        /// <summary>The body's way along an arc as <see cref="AddArc"/> lays it out (shins and head) meets no surface.</summary>
        private bool ArcClear(Vector3 from, Vector3 to, float lift)
        {
            float g = settings.dropGravity;
            float apexY = Mathf.Max(from.y, to.y) + Mathf.Max(0f, lift);
            float up = Mathf.Sqrt(2f * Mathf.Max(0f, apexY - from.y) / g), down = Mathf.Sqrt(2f * Mathf.Max(0.05f, apexY - to.y) / g);
            Vector3 apex = Vector3.Lerp(from, to, up / (up + down));
            apex.y = apexY;
            int mask = SurfaceMask();
            Vector3 last = from;
            for (int i = 1; i <= ArcSteps * 2; i++)
            {
                Vector3 p;
                if (i <= ArcSteps)
                {
                    float t = i / (float)ArcSteps;
                    p = Vector3.Lerp(from, apex, t);
                    p.y = from.y + (apexY - from.y) * EaseValue(Ease.Out, t);
                }
                else
                {
                    float t = (i - ArcSteps) / (float)ArcSteps;
                    p = Vector3.Lerp(apex, to, t);
                    p.y = apexY + (to.y - apexY) * EaseValue(Ease.In, t);
                }
                foreach (float h in ArcHeights)
                    if (Physics.Linecast(last + Vector3.up * h, p + Vector3.up * h, mask, QueryTriggerInteraction.Ignore)) return false;
                last = p;
            }
            return true;
        }

        /// <summary>
        /// Off the wall in a jump with a flip: to a clicked place, or (out of Grip) down to the ground out in front of the
        /// wall. A Motion Warping arc under gravity, like the kick-off and the drop.
        /// </summary>
        private void PushOff(Vector3 to, bool aimed, float lift = -1f)
        {
            Vector3 from = Feet;
            Vector3 outward = new Vector3(wallNormal.x, 0f, wallNormal.z);
            outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : -transform.forward;
            Vector3 landing = to;
            if (!aimed)
            {
                Vector3 aim = from + outward * settings.pushOffDistance;
                landing = plan.BaseFeet;
                if (Physics.Raycast(aim + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, PushOffGroundProbe, GroundMask(), QueryTriggerInteraction.Ignore)
                    && NavMesh.SamplePosition(ground.point, out NavMeshHit near, 3f, Filter()))
                    landing = near.position;
                landing = OpenLanding(landing);
            }
            Vector3 away = landing - from;
            away.y = 0f;
            legCount = 0;
            AddArc(WallRunPhase.PushOff, from, landing, lift >= 0f ? lift : settings.pushOffLift, Facing(away.sqrMagnitude > 1e-4f ? away : outward));
            afterRoute = WallRunPhase.None;
            walkOnAfterLanding = false;
            ForgetWallRoute();
            PlayRoute();
        }

        /// <summary>After landing off a wall, the walk on to the place that was clicked.</summary>
        private void WalkOnAfterLanding()
        {
            if (!walkOnAfterLanding) return;
            walkOnAfterLanding = false;
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
            var path = new NavMeshPath();
            if (agent.CalculatePath(walkOnTo, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                agent.SetPath(path);
                agent.isStopped = false;
            }
        }

        /// <summary>The graph node the body is at (nearest to its feet, out by the stand-off).</summary>
        private int CurrentNode(SurfaceGraph graph)
        {
            Vector3 n = transform.up;
            return graph.Nearest(Feet - n * settings.wallFootLift + n * settings.wallStandOff, NodeSnapReach);
        }

        /// <summary>Standing on a face: up is its normal, forward is the way along it (up the wall when there is no way).</summary>
        private Quaternion WallFacing(Vector3 normal, Vector3 travel)
        {
            Vector3 forward = Vector3.ProjectOnPlane(travel, normal);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(Vector3.up, normal);
            if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(transform.forward, normal);
            if (forward.sqrMagnitude < 1e-4f) return transform.rotation;
            return Quaternion.LookRotation(forward.normalized, normal);
        }

        private void ForgetWallRoute()
        {
            wallFeet.Clear();
            wallNormals.Clear();
            wallIndex = 0;
            wallExits = false;
        }

        /// <summary>Everything about the wall dropped (death, a cancel, a map change).</summary>
        private void ForgetWall()
        {
            wallArmed = false;
            wallArmedRoute = null;
            walkOnAfterLanding = false;
            ForgetWallRoute();
        }

        // ------------------------------------------------------------------ fights on walls (stage 4)

        /// <summary>Where the soles are, on the ground or on a wall (the body's own up, not the world's).</summary>
        public Vector3 SolePoint => Feet;

        /// <summary>On the way to a wall's foot to step onto it.</summary>
        public bool IsWallArmed => wallArmed;

        /// <summary>
        /// Server, in a fight: go after a target on a wall, or come off one after a target on the ground. The same way a
        /// click on that spot goes (along the wall, onto it from its foot, over its edge or down), or a leap when there is
        /// no way along. False when there is no way, or the body is in the middle of a hand-off.
        /// </summary>
        public bool ServerChase(Vector3 targetSoles, Vector3 targetUp)
        {
            if (!HasAuthority || (IsBusy && !IsOnWall)) return false;
            if (ServerWallClick(targetSoles, targetUp, false, out _)) return true;
            // No way along (across an alley, another building): leap after them, as a Jump click there would.
            return resources != null && resources.CanSpendChi(1f) && ServerWallClick(targetSoles, targetUp, true, out _);
        }

        /// <summary>Server, in a fight: the target is in reach; stop where the body is (on the wall, or short of it).</summary>
        public void ServerHoldForFight()
        {
            if (!HasAuthority) return;
            wallArmed = false;
            wallArmedRoute = null;
            if (phase != WallRunPhase.WallMove) return;
            wallExits = false;
            wallFeet.Clear();
            wallNormals.Clear();
            wallIndex = 0;
            if (wallGraph != null) wallNode = CurrentNode(wallGraph);
            SetPhase(WallRunPhase.WallHold, 0f);
        }

        /// <summary>On a wall: turn to face a point, round the face's normal, so a strike goes at a target on it.</summary>
        public void FaceOnWall(Vector3 point)
        {
            if (!IsOnWall) return;
            Vector3 along = Vector3.ProjectOnPlane(point - transform.position, wallNormal);
            if (along.sqrMagnitude > 1e-4f) wallTravel = along.normalized;
        }
    }
}
