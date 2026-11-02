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
            public bool Open, Alive, Busy, Jump, Run, HasFluxForOne;
            /// <summary>The ghost's Flux now (the straight climb's price is checked against it, NGO <c>CanSpendChi(fluxCost)</c>).</summary>
            public float Flux;
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

        // A ghost's checks for NGO's Decide (null = the body's own, as NGO)
        private GhostWallAsk? ghostAsk;

        /// <summary>The straight climb's price, Flux (NGO <c>fluxCost</c> 12, paid when the plant starts).</summary>
        public float ClimbFlux => settings != null ? settings.fluxCost : 0f;

        /// <summary>
        /// NGO's straight climb for a ghost (part 2, `m3c-wallstride-design.md` section 4): a click with Run on a climbable surface the
        /// zone's graph does not cover. The server finds the surface again from the point (NGO <c>FindSurface</c>), NGO's
        /// <see cref="Decide"/> judges it in NGO's words, and the climb is laid out as one walk: the run to its foot (cut where NGO's
        /// arming starts the plant) and NGO's legs (<see cref="StartRoute"/>): the plant, up the face, over the top (or the kick-off).
        /// Null layout and empty reason: not a climb (not a climbable surface: the ordinary click takes it).
        /// </summary>
        public string PlanClimbForGhost(Vector3 feet, Vector3 point, Vector3 normal, in GhostWallAsk ask, out WallWalkLayout layout)
        {
            layout = null;
            if (settings == null) return string.Empty;
            Collider surface = FindSurface(point, normal);
            if (surface != null && VendorStall.Is(surface)) return "Vendor stalls can't be climbed.";
            if (surface == null || !IsClimbableObject(surface, out _)) return string.Empty;
            if (!NavMesh.SamplePosition(feet, out NavMeshHit onMesh, 0.6f, Filter())) return "Not now.";
            feet = onMesh.position;
            ghostAsk = ask;
            bool ok;
            Plan climb;
            string reason;
            try { ok = Decide(point, normal, surface, feet, false, out climb, out reason); }
            finally { ghostAsk = null; }
            if (!ok) return reason;
            layout = ClimbLayout(feet, climb, in ask);
            return layout != null ? string.Empty : "There is no way to the foot of that wall.";
        }

        /// <summary>
        /// NGO <c>ArmOrBegin</c> + <see cref="StartRoute"/> as one layout: within reach of the foot the climb begins at once, else the run to
        /// the foot, the plant starting as it arrives. Null when there is no walk to the foot.
        /// </summary>
        private WallWalkLayout ClimbLayout(Vector3 feet, Plan climb, in GhostWallAsk ask)
        {
            var layout = new WallWalkLayout(settings, null, feet, Vector3.up, ask.Forward, ask.GroundSpeed * settings.wallSpeedShare);
            if (Flat(feet, climb.BaseFeet) > ArmTrigger + Radius)
            {
                Vector3 foot = NavMesh.SamplePosition(climb.BaseFeet, out NavMeshHit near, FootReach, Filter()) ? near.position : climb.BaseFeet;
                var path = new NavMeshPath();
                if (!NavMesh.CalculatePath(feet, foot, Filter(), path) || path.status != NavMeshPathStatus.PathComplete) return null;
                WalkUpTo(layout, path.corners, ArmTrigger + (agent != null ? agent.stoppingDistance : 0f));
            }
            ClimbLegs(layout, climb, ask.GroundSpeed, ask.Forward, false);
            return layout;
        }

        /// <summary>Which of NGO's self-starting ways a plain click took (part 5): the scripted run and the tests log it.</summary>
        public enum AutoWay : byte { None, OverWall, UpClimb, Ledge }

        // NGO PointAndClickPlayer's destination sample radius (DefaultSampleRadius), as the ghost's walk planner samples the click
        private const float AutoSampleRadius = 2f;

        /// <summary>
        /// NGO's self-starting run for a ghost (part 5, `m3c-wallstride-design.md` section 6, rules A1-A4): a plain ground click asks,
        /// in NGO's order (<c>TryPlanRoute</c>), the way over a wall by the zone's graph (<see cref="TryArmSurfaceRoute"/>: no walk there, or
        /// Running and the walk at least <c>autoDetourFactor</c> times the route) and then the straight climb up a face the place stands on
        /// (<see cref="TryArmTowards"/>). Laid out exactly as the wall click (part 1) and the climb (part 2) are. Then, Wallstride or not, the
        /// route's jumps (part 5b, section 7): a gap between roofs on the walk, or, with no walk there, the drop off the roof's edge
        /// (<see cref="PlanLedgesForGhost"/>). Null: none of them (the vault or the plain walk takes the click); never a refusal, as NGO falls through.
        /// </summary>
        public WallWalkLayout PlanAutoForGhost(Vector3 feet, Vector3 destination, in GhostWallAsk ask, out AutoWay way)
        {
            way = AutoWay.None;
            if (settings == null || !ask.Alive || ask.Busy || ask.OnWall) return null;
            NavMeshQueryFilter filter = Filter();
            if (!NavMesh.SamplePosition(feet, out NavMeshHit onMesh, 0.6f, filter)
                || !NavMesh.SamplePosition(destination, out NavMeshHit there, AutoSampleRadius, filter)) return null;
            feet = onMesh.position;
            destination = there.position;
            var walk = new NavMeshPath();
            bool walkable = NavMesh.CalculatePath(feet, destination, filter, walk) && walk.status == NavMeshPathStatus.PathComplete;

            // A1: over a wall by the graph (NGO TryArmSurfaceRoute; the prop blockers of the wall walk, owner 1 Nov)
            SurfaceGraph graph = ZoneGraph;
            if (ask.Open && graph != null && ask.HasFluxForOne && (!walkable || ask.Run))
            {
                SurfaceRoute.Route route = SurfaceRoute.Through(graph, feet, destination, filter, PropBlocker(graph));
                if (route != null && route.HasEntry && (!walkable || PathLength(walk) >= route.Length * settings.autoDetourFactor))
                {
                    var layout = new WallWalkLayout(settings, StickProbe, feet, Vector3.up, ask.Forward, ask.GroundSpeed * settings.wallSpeedShare);
                    bool reached = true;
                    if (Flat(feet, route.Entry.navPoint) > WallArmTrigger + Radius)
                    {
                        var path = new NavMeshPath();
                        reached = NavMesh.CalculatePath(feet, route.Entry.navPoint, filter, path) && path.status == NavMeshPathStatus.PathComplete;
                        if (reached) WalkUpTo(layout, path.corners, WallArmTrigger + (agent != null ? agent.stoppingDistance : 0f));
                    }
                    if (reached)
                    {
                        layout.PlantAndAlong(graph, route, ask.Forward);
                        if (layout.FitTo(WallWalkLayout.MaxCorners)) { way = AutoWay.OverWall; return layout; }
                    }
                }
            }

            // A3: up a face the place stands on, Running (NGO TryArmTowards)
            if (ask.Open && ask.Run)
            {
                WallWalkLayout up = UpClimbForGhost(feet, destination, walk, walkable, in ask);
                if (up != null) { way = AutoWay.UpClimb; return up; }
            }

            // B1-B6: the route's jumps (part 5b), last before the vault (NGO TryPlanRoute: the drop after the wall's ways)
            WallWalkLayout ledges = PlanLedgesForGhost(feet, destination, walk, walkable, in ask);
            if (ledges != null) way = AutoWay.Ledge;
            return ledges;
        }

        /// <summary>NGO <see cref="TryArmTowards"/> for a ghost (A3), laid out as part 2's climb, then on to the place (owner's change).</summary>
        private WallWalkLayout UpClimbForGhost(Vector3 feet, Vector3 destination, NavMeshPath walk, bool walkable, in GhostWallAsk ask)
        {
            NavMeshQueryFilter filter = Filter();
            Vector3 flat = destination - feet;
            flat.y = 0f;
            float straight = flat.magnitude;
            if (straight < 0.5f || destination.y - feet.y < settings.minWallHeight * 0.5f) return null;
            if (walkable && PathLength(walk) < straight * settings.autoDetourFactor) return null;
            int count = Physics.RaycastNonAlloc(feet + Vector3.up * 1f, flat / straight, Hits, Mathf.Min(straight, settings.autoLookAhead),
                SurfaceMask(), QueryTriggerInteraction.Ignore);
            if (!Nearest(count, out RaycastHit hit)) return null;
            ghostAsk = ask;
            bool ok;
            Plan climb;
            try { ok = Decide(hit.point, hit.normal, hit.collider, feet, false, out climb, out _); }
            finally { ghostAsk = null; }
            if (!ok || Mathf.Abs(climb.TopFeet.y - destination.y) > 2f) return null;
            WallWalkLayout up = ClimbLayout(feet, climb, in ask);
            if (up == null) return null;
            // owner's change from NGO (1 Nov, design 6.1): on from the top to the place (NGO stands on the top), as the way over a wall walks on
            var on = new NavMeshPath();
            if ((destination - up.LastFeet).sqrMagnitude > 0.25f && NavMesh.CalculatePath(up.LastFeet, destination, filter, on)
                && on.status == NavMeshPathStatus.PathComplete)
                for (int i = 1; i < on.corners.Length; i++) up.Ground(on.corners[i]);
            return up;
        }

        /// <summary>A jump at a wall, judged (part 4): where it aims, the climb it catches, for the entity side to find where its arc meets the wall.</summary>
        public sealed class GhostCatch
        {
            /// <summary>Where the jump takes off and where it aims (the feet), the arc's peak (feet height).</summary>
            public Vector3 StartFeet, AimFeet;
            public float Peak;
            /// <summary>The climb's base (the stand-off plane's point at its foot), the highest a catch may be, the face's outward normal.</summary>
            public Vector3 BaseFeet, ClimbEndFeet, Normal;
            // the motor's own climb (its type is the motor's), boxed: read back by CatchLayoutForGhost
            internal object Climb;
        }

        /// <summary>
        /// NGO's jump at a wall for a ghost (part 4, `m3c-wallstride-design.md` section 5, rules C1-C4, and the owner's change 5.3): Jump selected
        /// and a click on a climbable surface the zone's graph does not cover. NGO's <see cref="Decide"/> from a jump (no Run needed, a slant up to
        /// 65 degrees), then the jump motor's own checks and flight (<see cref="PlayerJumpMotor.PlanCatchForGhost"/>), all in NGO's words and
        /// order, aimed at the place clicked (the body's middle there; owner, 1 Nov) within the face's range: not below the climb's base, not
        /// above its end. Where the arc meets the wall is the entity side's (it flies the arc), then <see cref="CatchLayoutForGhost"/>. Null
        /// <paramref name="shape"/> and empty reason: not a climbable surface (the ordinary jump takes the click).
        /// </summary>
        public string PlanCatchForGhost(Vector3 feet, Vector3 point, Vector3 normal, in GhostWallAsk ask, out GhostCatch shape)
        {
            shape = null;
            if (settings == null) return string.Empty;
            Collider surface = FindSurface(point, normal);
            if (surface != null && VendorStall.Is(surface)) return "Vendor stalls can't be climbed.";
            if (surface == null || !IsClimbableObject(surface, out _)) return string.Empty;
            bool onMesh = NavMesh.SamplePosition(feet, out NavMeshHit near, 0.6f, Filter());
            if (onMesh) feet = near.position;
            ghostAsk = ask;
            bool ok;
            Plan climb;
            string reason;
            try { ok = Decide(point, normal, surface, feet, true, out climb, out reason); }
            finally { ghostAsk = null; }
            if (!ok) return reason;
            // the owner's catch (5.3): aimed at the place clicked, the body's middle there, within the face's range
            float half = (agent != null ? agent.height : 2f) * 0.5f;
            Vector3 aim = new Vector3(climb.BaseFeet.x, Mathf.Clamp(point.y - half, climb.BaseFeet.y, Mathf.Max(climb.BaseFeet.y, climb.ClimbEndFeet.y)),
                climb.BaseFeet.z);
            // NGO TryJumpToCatch, in its order (busy and cooldown are the ghost's, refused before a click gets here)
            if (jumpMotor == null) return "Jump animation is not configured.";
            if (!onMesh) return "Jump must start on walkable ground.";
            string why = jumpMotor.PlanCatchForGhost(feet, aim, out float peak);
            if (why != null) return why;
            Vector3 n = climb.Normal;
            n.y = 0f;
            shape = new GhostCatch
            {
                StartFeet = feet, AimFeet = aim, Peak = peak, BaseFeet = climb.BaseFeet,
                ClimbEndFeet = new Vector3(climb.BaseFeet.x, Mathf.Max(climb.BaseFeet.y, climb.ClimbEndFeet.y), climb.BaseFeet.z),
                Normal = n.sqrMagnitude > 1e-4f ? n.normalized : -ask.Forward, Climb = climb,
            };
            return string.Empty;
        }

        /// <summary>
        /// The climb that runs on where a jump at a wall met it (<paramref name="catchFeet"/>, found by the entity side on the arc it flies):
        /// NGO's <c>StartRoute(fromJump: true)</c>, the plant quick when the jump met the face head-on.
        /// </summary>
        public WallWalkLayout CatchLayoutForGhost(GhostCatch shape, Vector3 catchFeet, in GhostWallAsk ask)
        {
            Vector3 flight = catchFeet - shape.StartFeet;
            flight.y = 0f;
            Vector3 facing = flight.sqrMagnitude > 1e-4f ? flight.normalized : ask.Forward;
            var layout = new WallWalkLayout(settings, null, catchFeet, Vector3.up, facing, ask.GroundSpeed * settings.wallSpeedShare);
            ClimbLegs(layout, (Plan)shape.Climb, ask.GroundSpeed, facing, true);
            return layout;
        }

        /// <summary>
        /// Perched in a tree (part 3): a click on the ground below drops down to it (NGO <see cref="ServerDropTo"/>, the same checks and
        /// words): a walkable place within 1.5 m on open ground, within 25 m of the tree, an arc under gravity over a 0.4 m lift.
        /// </summary>
        public string PlanDropForGhost(Vector3 feet, Vector3 point, Vector3 forward, out WallWalkLayout layout)
        {
            layout = null;
            if (settings == null) return "Not now.";
            if (!NavMesh.SamplePosition(point, out NavMeshHit landing, 1.5f, Filter()) || !OnOpenGround(landing.position))
                return "Choose a walkable place to drop down to.";
            Vector3 flat = landing.position - feet;
            flat.y = 0f;
            if (flat.magnitude > settings.maxDropDistance) return $"Too far: drop within {settings.maxDropDistance:0} m of the tree.";
            layout = new WallWalkLayout(settings, null, feet, Vector3.up, forward, 0f);
            layout.WallEnds();
            layout.Arc(WallRunPhase.DropDown, landing.position, 0.4f, Facing(landing.position - feet));
            return string.Empty;
        }

        /// <summary>The kind of a climbable surface at a point (the scripted run logs it): Tree, Wall, or none.</summary>
        public string ClimbKindAt(Vector3 point, Vector3 normal)
        {
            Collider surface = FindSurface(point, normal);
            if (surface == null || !IsClimbableObject(surface, out ClimbableSurface marker)) return "none";
            return marker != null ? marker.kind.ToString() : "Wall";
        }

        /// <summary>
        /// NGO <see cref="StartRoute"/> as layout legs, from where the run to the foot ended, or (<paramref name="fromJump"/>) from the catch point:
        /// then the plant is quick when the jump met the surface head-on and longer the wider it came at it (NGO 680-687).
        /// </summary>
        private void ClimbLegs(WallWalkLayout layout, Plan climb, float groundSpeed, Vector3 forward, bool fromJump)
        {
            Vector3 start = layout.LastFeet;
            float climbSpeed = WallRunRules.ClimbSpeed(groundSpeed, settings);
            Quaternion upright = Facing(-climb.Normal);
            Quaternion leaned = upright * Quaternion.Euler(-settings.climbLean, 0f, 0f);
            float plantSeconds = settings.plantSeconds;
            if (fromJump)
            {
                float slant = WallRunRules.ApproachAngle(forward, climb.Normal);
                plantSeconds = Mathf.Lerp(Mathf.Min(settings.plantSeconds, 0.12f), settings.plantSeconds + 0.12f,
                    Mathf.Clamp01(slant / Mathf.Max(1f, settings.maxJumpApproachAngle)));
            }
            float plantRise = climbSpeed * plantSeconds * 0.5f;
            Vector3 plantTo = new Vector3(climb.BaseFeet.x, Mathf.Max(start.y, climb.BaseFeet.y) + plantRise, climb.BaseFeet.z);
            plantTo.y = Mathf.Min(plantTo.y, Mathf.Max(climb.ClimbEndFeet.y, climb.BaseFeet.y));
            layout.Leg(WallRunPhase.Plant, plantTo, leaned, plantSeconds, WallWalkLayout.EaseIn, WallWalkLayout.EaseOut);
            Vector3 from = plantTo;
            float rise = climb.ClimbEndFeet.y - plantTo.y;
            if (rise > 0.05f)
            {
                layout.Leg(WallRunPhase.Climb, climb.ClimbEndFeet, leaned, rise / climbSpeed, WallWalkLayout.Linear, WallWalkLayout.Linear);
                from = climb.ClimbEndFeet;
            }
            if (climb.HasTop)
            {
                // a tree: into it to the place to stand, and perched there (NGO 704, 720); else over the edge onto the top
                WallRunPhase over = climb.Kind == ClimbableKind.Tree ? WallRunPhase.ToPerch : WallRunPhase.Mantle;
                layout.EndsPerched = climb.Kind == ClimbableKind.Tree;
                Vector3 inward = climb.TopFeet - from;
                inward.y = 0f;
                float inDistance = inward.magnitude;
                Vector3 inDirection = inDistance > 1e-3f ? inward / inDistance : Vector3.zero;
                float liftIn = Mathf.Min(0.35f, inDistance * 0.3f);
                Vector3 lifted = new Vector3(from.x, Mathf.Max(from.y, climb.TopFeet.y) + settings.mantleClearance, from.z) + inDirection * liftIn;
                float liftSeconds = Mathf.Max(MinimumDuration, 2f * (lifted.y - from.y) / climbSpeed);
                layout.Leg(over, lifted, upright, liftSeconds, WallWalkLayout.EaseOut, WallWalkLayout.EaseIn);
                float alongSpeed = 2f * liftIn / liftSeconds;
                float rest = Mathf.Max(0f, inDistance - liftIn);
                float overSeconds = alongSpeed > 0.1f ? 2f * rest / alongSpeed : settings.mantleSeconds;
                overSeconds = Mathf.Clamp(overSeconds, settings.mantleSeconds * 0.5f, settings.mantleSeconds * 1.5f);
                layout.Leg(over, climb.TopFeet, upright, overSeconds, WallWalkLayout.EaseIn, WallWalkLayout.EaseOut);
            }
            else
            {
                // no top in reach: kick off it, down to the ground in front of it (NGO)
                Vector3 outward = climb.Normal;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 1e-4f ? outward.normalized : -forward;
                Vector3 aim = from + outward * settings.kickOffDistance;
                Vector3 landing = NavMesh.SamplePosition(new Vector3(aim.x, climb.BaseFeet.y, aim.z), out NavMeshHit hit, 2f, Filter()) ? hit.position : climb.BaseFeet;
                if (!OnOpenGround(landing)) landing = climb.BaseFeet;
                layout.Arc(WallRunPhase.KickOff, landing, settings.kickOffLift, Facing(outward));
            }
            layout.PayOnPlant = true;
        }

        /// <summary>
        /// For the scripted test run (part 2): a face near <paramref name="near"/> that NGO's straight climb takes with Run at level 60 and
        /// the zone's graph does not cover: horizontal rays at chest height from NavMesh points around, the face judged by NGO's own Decide.
        /// <paramref name="stand"/> is where the ray started (on the NavMesh, 6-8 m out). Null when found, else why not.
        /// </summary>
        public string FindTestClimb(Vector3 near, GhostWallAsk ask, out Vector3 stand, out Vector3 point, out Vector3 normal, bool tree = false)
        {
            stand = point = normal = default;
            if (settings == null) return "no Wallstride settings";
            int rays = 0, climbable = 0, covered = 0;
            var why = new System.Collections.Generic.Dictionary<string, int>();
            for (float r = 6f; r <= 40f; r += 4f)
            for (int deg = 0; deg < 360; deg += 20)
            {
                Vector3 way = Quaternion.Euler(0f, deg, 0f) * Vector3.forward;
                if (!NavMesh.SamplePosition(near + way * r, out NavMeshHit from, 2f, Filter()) || !OnOpenGround(from.position)) continue;
                for (int turn = 0; turn < 360; turn += 45)
                {
                    Vector3 dir = Quaternion.Euler(0f, turn, 0f) * Vector3.forward;
                    rays++;
                    if (!Physics.Raycast(from.position + Vector3.up * FaceProbeHeight, dir, out RaycastHit hit, 8f, SurfaceMask(), QueryTriggerInteraction.Ignore)) continue;
                    if (hit.distance < 6f || !IsClimbableObject(hit.collider, out ClimbableSurface marker) || VendorStall.Is(hit.collider)) continue;
                    if (tree != (marker != null && marker.kind == ClimbableKind.Tree)) continue;   // a tree, or not a tree, as asked
                    climbable++;
                    if (WantsWallWalkClick(hit.point, hit.normal, false)) { covered++; continue; }
                    ghostAsk = ask;
                    bool ok;
                    string reason;
                    try { ok = Decide(hit.point, hit.normal, hit.collider, from.position, false, out Plan climb, out reason) && climb.HasTop; }
                    finally { ghostAsk = null; }
                    if (!ok) { string k = string.IsNullOrEmpty(reason) ? "no top" : reason; why[k] = why.TryGetValue(k, out int c) ? c + 1 : 1; continue; }
                    stand = from.position;
                    point = hit.point;
                    normal = hit.normal;
                    return null;
                }
            }
            var parts = new System.Collections.Generic.List<string>();
            foreach (var kv in why) parts.Add($"{kv.Value}x \"{kv.Key}\"");
            return $"no face fits near {near.x:F0},{near.z:F0}: {rays} rays, {climbable} climbable, {covered} covered by the graph (the wall walk's); " +
                string.Join(", ", parts);
        }

        /// <summary>
        /// For the scripted test run: the face of the tree whose trunk stands at <paramref name="trunk"/>, seen from open NavMesh 6-8 m out with a
        /// chest-height ray aimed straight at the trunk (a trunk is too thin for <see cref="FindTestClimb"/>'s 45-degree rays: gui-tree-4, 1 Nov,
        /// 1216 rays, 0 met it), judged by NGO's Decide. Null when found, else why not.
        /// </summary>
        public string FindTestTreeClimb(Vector3 trunk, GhostWallAsk ask, out Vector3 stand, out Vector3 point, out Vector3 normal)
        {
            stand = point = normal = default;
            if (settings == null) return "no Wallstride settings";
            int stands = 0, hits = 0;
            var why = new System.Collections.Generic.Dictionary<string, int>();
            for (float r = 6f; r <= 8f; r += 1f)
            for (int deg = 0; deg < 360; deg += 15)
            {
                Vector3 way = Quaternion.Euler(0f, deg, 0f) * Vector3.forward;
                if (!NavMesh.SamplePosition(trunk + way * r, out NavMeshHit from, 1f, Filter()) || !OnOpenGround(from.position)) continue;
                stands++;
                Vector3 origin = from.position + Vector3.up * FaceProbeHeight;
                Vector3 aim = new Vector3(trunk.x, origin.y, trunk.z) - origin;
                if (!Physics.Raycast(origin, aim.normalized, out RaycastHit hit, aim.magnitude + 1f, SurfaceMask(), QueryTriggerInteraction.Ignore)) continue;
                if (!IsClimbableObject(hit.collider, out ClimbableSurface marker) || marker == null || marker.kind != ClimbableKind.Tree) continue;
                hits++;
                ghostAsk = ask;
                bool ok;
                string reason;
                try { ok = Decide(hit.point, hit.normal, hit.collider, from.position, false, out Plan climb, out reason) && climb.HasTop; }
                finally { ghostAsk = null; }
                if (!ok) { string k = string.IsNullOrEmpty(reason) ? "no top" : reason; why[k] = why.TryGetValue(k, out int c) ? c + 1 : 1; continue; }
                stand = from.position;
                point = hit.point;
                normal = hit.normal;
                return null;
            }
            var parts = new System.Collections.Generic.List<string>();
            foreach (var kv in why) parts.Add($"{kv.Value}x \"{kv.Key}\"");
            return $"no face of the tree at {trunk.x:F0},{trunk.z:F0}: {stands} places to stand 6-8 m out, {hits} rays met the trunk; " + string.Join(", ", parts);
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
        /// <summary>The view's test of a straight climb's click: a vendor's stall (refused in NGO's words) or a climbable surface (NGO).</summary>
        public bool IsClimbableClick(Collider surface) => surface != null && (VendorStall.Is(surface) || IsClimbableObject(surface, out _));

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
            // a prop on the face (owner, 1 Nov: no clipping): the way goes round it, and a click on it goes to the nearest free place
            System.Func<int, bool> blocked = PropBlocker(graph);
            if (target >= 0 && blocked(target))
            {
                target = NearestFree(graph, target, blocked);
                if (target < 0) return "Something is in the way.";
            }

            if (!ask.OnWall)
            {
                // From the ground (or a roof) onto the wall (NGO ServerWallClick, the same order)
                if (target < 0) return "That cannot be climbed.";
                if (ask.Busy) return "Not now.";
                if (!NavMesh.SamplePosition(feet, out NavMeshHit onMesh, 0.6f, Filter())) return "Not now.";
                feet = onMesh.position;
                // a jump onto the wall is the real jump now (owner, 1 Nov; PlanJumpOntoCatchForGhost, the catch's path): one that does not work
                // goes on to the way onto the wall from its foot, as NGO
                SurfaceRoute.Route route = SurfaceRoute.ToNode(graph, feet, target, Filter(), blocked);
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
                SurfaceRoute.Route along = SurfaceRoute.Along(graph, here, target, blocked);
                if (along == null) return "That part of the wall is not joined to this one.";
                layout = new WallWalkLayout(settings, StickProbe, feet, ask.Up, ask.Forward, wallSpeed);
                layout.AlongAndOff(graph, along, 0, w => OpenLandingFor(w, wallBase));
                return Fit(layout);
            }

            // off the wall: over its edge onto a top, or down to the ground, whichever is the shorter way there
            if (!NavMesh.SamplePosition(point, out NavMeshHit there, 1.5f, Filter())) return "Choose a place to go to.";
            SurfaceRoute.Route top = SurfaceRoute.Off(graph, here, there.position, SurfaceGraph.JoinKind.Top, Filter(), blocked);
            SurfaceRoute.Route down = SurfaceRoute.Off(graph, here, there.position, SurfaceGraph.JoinKind.Ground, Filter(), blocked);
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

        /// <summary>
        /// The nodes a body cannot stand on for a prop on the face (<see cref="WallWalkLayout.PropOnFace"/>), asked once per node per plan.
        /// The face's own collider is the one NGO's probe meets in along the node's normal.
        /// </summary>
        private System.Func<int, bool> PropBlocker(SurfaceGraph graph)
        {
            var known = new System.Collections.Generic.Dictionary<int, bool>();
            int mask = GroundMask();
            return node =>
            {
                if (known.TryGetValue(node, out bool b)) return b;
                Vector3 n = graph.normals[node], p = graph.points[node];
                Collider face = Physics.Raycast(p, -n, out RaycastHit hit, settings.wallStandOff + 0.5f, SurfaceMask(), QueryTriggerInteraction.Ignore) ? hit.collider : null;
                b = WallWalkLayout.PropOnFace(FeetAt(graph, node), n, face, transform.root, mask);
                known[node] = b;
                return b;
            };
        }

        /// <summary>The free node nearest a blocked one, along the graph's edges within 1.5 m; -1 when there is none.</summary>
        private static int NearestFree(SurfaceGraph graph, int start, System.Func<int, bool> blocked)
        {
            var seen = new System.Collections.Generic.HashSet<int> { start };
            var queue = new System.Collections.Generic.Queue<int>();
            queue.Enqueue(start);
            int best = -1;
            float bestD = 1.5f;
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                for (int e = graph.edgeStart[at]; e < graph.edgeStart[at + 1]; e++)
                {
                    int next = graph.edgeTo[e];
                    if (!seen.Add(next)) continue;
                    float d = Vector3.Distance(graph.points[next], graph.points[start]);
                    if (d > 1.5f) continue;
                    if (!blocked(next) && d < bestD) { bestD = d; best = next; }
                    queue.Enqueue(next);
                }
            }
            return best;
        }

        /// <summary>
        /// A jump from the ground onto a wall of the zone's graph, as the real jump (owner, 1 Nov, `m3c-wallstride-design.md` 5.4; NGO's
        /// <see cref="JumpOnto"/> flew a gravity arc): the grab is the place clicked while it is within the Jump rank's reach, else the graph's
        /// point nearest the highest the jump reaches under it, then the wall walk runs up to the click. NGO's checks and words (the gate, life,
        /// Flux for a second, "Too high to jump to.", "Too far: jump within 25 m.", "Something is in the way."), then the jump's own. The layout
        /// starts at the grab (where the jump ends): a plant onto the face, eased by the slant as a catch's, then along. Empty reason and a null
        /// layout: not this (no graph node by the click, or the body is on a wall: NGO's leap).
        /// </summary>
        public string PlanJumpOntoCatchForGhost(Vector3 feet, Vector3 point, Vector3 normal, in GhostWallAsk ask, out Vector3 grabFeet, out float peak,
            out WallWalkLayout layout)
        {
            layout = null;
            grabFeet = feet;
            peak = feet.y;
            SurfaceGraph graph = ZoneGraph;
            if (graph == null || settings == null || ask.OnWall) return string.Empty;
            int target = NodeAt(graph, point, normal);
            if (target < 0) return string.Empty;
            System.Func<int, bool> blocked = PropBlocker(graph);
            if (blocked(target)) target = NearestFree(graph, target, blocked);
            if (target < 0) return "Something is in the way.";
            if (!ask.Open) return WallRunRules.Reason(ClimbVerdict.Locked, settings);
            if (!ask.Alive || ask.Busy) return "Not now.";
            if (!ask.HasFluxForOne) return WallRunRules.Reason(ClimbVerdict.NoFlux, settings);
            if (!NavMesh.SamplePosition(feet, out NavMeshHit onMesh, 0.6f, Filter())) return "Jump must start on walkable ground.";
            feet = onMesh.position;
            if (jumpMotor == null) return "Jump animation is not configured.";
            int grab = target;
            float reach = jumpMotor.GhostJumpReach;
            if (FeetAt(graph, target).y - feet.y > reach)
            {
                // above the jump's reach: grab the face as high as the jump gets under the place clicked, and run up to it
                Vector3 low = graph.points[target];
                low.y = feet.y + reach;
                grab = graph.Nearest(low, 2.5f);
                if (grab < 0) return "Too high to jump to.";
                if (blocked(grab)) grab = NearestFree(graph, grab, blocked);
                if (grab < 0) return "Something is in the way.";
            }
            Vector3 to = FeetAt(graph, grab);
            Vector3 flat = to - feet;
            flat.y = 0f;
            if (flat.magnitude > settings.maxDropDistance) return $"Too far: jump within {settings.maxDropDistance:0} m.";
            Vector3 n = graph.normals[grab];
            if (!JumpClear(feet, to, Vector3.up, n)) return "Something is in the way.";
            string why = jumpMotor.PlanJumpOntoForGhost(feet, to, out peak);
            if (why != null) return why;

            SurfaceRoute.Route route = grab == target
                ? new SurfaceRoute.Route { Nodes = new[] { target } }
                : SurfaceRoute.Along(graph, grab, target, blocked) ?? new SurfaceRoute.Route { Nodes = new[] { grab } };
            Vector3 travel = flat.sqrMagnitude > 1e-4f ? flat.normalized : -n;
            layout = new WallWalkLayout(settings, StickProbe, to, Vector3.up, travel, ask.GroundSpeed * settings.wallSpeedShare);
            // the plant onto the face: quick head-on, longer the wider it came at it (the catch's, NGO 680-687), the turn eased in
            float slant = WallRunRules.ApproachAngle(travel, n);
            float plant = Mathf.Lerp(Mathf.Min(settings.plantSeconds, 0.12f), settings.plantSeconds + 0.12f,
                Mathf.Clamp01(slant / Mathf.Max(1f, settings.maxJumpApproachAngle)));
            layout.Leg(WallRunPhase.Plant, to + Vector3.up * 0.15f, WallWalkLayout.WallFacing(n, Vector3.up, travel), plant, WallWalkLayout.EaseIn, WallWalkLayout.EaseOut);
            layout.WallStarts();
            layout.AlongAndOff(graph, route, 1);
            grabFeet = to;
            string fit = Fit(layout);
            if (!string.IsNullOrEmpty(fit)) layout = null;
            return fit;
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
                if (PropBlocker(graph)(catchNode)) catchNode = NearestFree(graph, catchNode, PropBlocker(graph));
                if (catchNode < 0) return "Something is in the way.";
            }
            Vector3 to = FeetAt(graph, catchNode);
            Vector3 flat = to - from;
            flat.y = 0f;
            if (flat.magnitude > settings.maxDropDistance) return $"Too far: jump within {settings.maxDropDistance:0} m.";
            Vector3 fromUp = ask.OnWall ? ask.Up : Vector3.up;
            if (!JumpClear(from, to, fromUp, graph.normals[catchNode])) return "Something is in the way.";

            SurfaceRoute.Route route = catchNode == target
                ? new SurfaceRoute.Route { Nodes = new[] { target } }
                : SurfaceRoute.Along(graph, catchNode, target, PropBlocker(graph)) ?? new SurfaceRoute.Route { Nodes = new[] { catchNode } };
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
