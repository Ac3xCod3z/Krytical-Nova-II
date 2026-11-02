using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Player
{
    /// <summary>
    /// The route's jumps for an entity ghost (part 5b, `m3c-wallstride-design.md` section 7, rules B1-B7, design 7.1 a): NGO's
    /// <c>PointAndClickPlayer.CrossLinks</c> (a gap between roofs on the walk is jumped) and <c>TryPlanDrop</c> / <c>WatchDrop</c> (with no walk
    /// there, off the roof's edge), laid out as timed jump legs in one walk instead of a jump started at the edge. The jump's own checks
    /// and words (<see cref="PlayerJumpMotor.PlanForGhost"/>: the rank's distance and height, a clear arc), its apex as the leg's height; its
    /// Flux is the server's, as each leg starts. Off a roof's edge the legs play <see cref="WallRunPhase.LedgeJump"/> (the owner's backflip, by
    /// body type), across a gap <see cref="WallRunPhase.GapJump"/> (the normal jump; owner, 2 Nov).
    /// </summary>
    public sealed partial class PlayerWallRunMotor
    {
        // NGO PointAndClickPlayer's DropMinHeight, DropTrigger and DropReach (farthest first: a leap clear of the wall below)
        private const float LedgeDropMinHeight = 1.5f, LedgeDropTrigger = 0.6f;
        private static readonly float[] LedgeDropReach = { 4.5f, 3f, 1.8f };

        /// <summary>Why the last route's jump could not be laid out (NGO's words; the scripted run and the owner's message), or null.</summary>
        public string LastLedgeRefusal { get; private set; }

        /// <summary>
        /// The walk to <paramref name="destination"/> with its jumps: every gap between roofs on <paramref name="walk"/> (a NavMeshLink: a
        /// stretch whose middle has no NavMesh under it) jumped from its near end to its far one (NGO <c>CrossLinks</c>), or, with no walk there,
        /// the walk to the roof's edge, the drop to the farthest landing the jump takes, and the walk on (NGO <c>TryPlanDrop</c>,
        /// <c>WatchDrop</c>). A jump the rank refuses ends the walk at the edge (NGO: the move stops there, <see cref="LastLedgeRefusal"/>).
        /// Null: no jump on the way (the walk's own planner takes it).
        /// </summary>
        public WallWalkLayout PlanLedgesForGhost(Vector3 feet, Vector3 destination, NavMeshPath walk, bool walkable, in GhostWallAsk ask)
        {
            LastLedgeRefusal = null;
            if (settings == null || jumpMotor == null) return null;
            NavMeshQueryFilter filter = Filter();
            var layout = new WallWalkLayout(settings, null, feet, Vector3.up, ask.Forward, ask.GroundSpeed);
            if (walkable)
            {
                Vector3[] corners = walk.corners;
                bool jumped = false;
                for (int i = 1; i < corners.Length; i++)
                {
                    if (!IsGap(corners[i - 1], corners[i], filter)) { layout.Ground(corners[i]); continue; }
                    jumped = true;
                    string why = LedgeArc(layout, corners[i], WallRunPhase.GapJump);
                    if (why == null) continue;
                    // NGO CrossLinks: back on the link's near end and the move stops there
                    LastLedgeRefusal = string.IsNullOrEmpty(why) ? "Jump is needed to cross that gap." : why;
                    break;
                }
                return jumped && layout.FitTo(WallWalkLayout.MaxCorners) ? layout : null;
            }

            // NGO TryPlanDrop: from a roof to a place below it with no walk there
            if (feet.y - destination.y < LedgeDropMinHeight) return null;
            Vector3 flat = destination - feet;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return null;
            Vector3 way = flat.normalized;
            if (!NavMesh.Raycast(feet, feet + way * (flat.magnitude + 1f), out NavMeshHit edgeHit, filter)
                && !NavMesh.FindClosestEdge(feet, out edgeHit, filter)) return null;
            Vector3 edge = edgeHit.position;
            Vector3 outward = destination - edge;
            outward.y = 0f;
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : way;
            int ground = Physics.DefaultRaycastLayers & ~(1 << gameObject.layer);
            var landings = new System.Collections.Generic.List<Vector3>(LedgeDropReach.Length);
            foreach (float reach in LedgeDropReach)
            {
                Vector3 over = edge + outward * reach + Vector3.up * 0.5f;
                if (!Physics.Raycast(over, Vector3.down, out RaycastHit below, 500f, ground, QueryTriggerInteraction.Ignore)) continue;
                if (edge.y - below.point.y < LedgeDropMinHeight) continue;   // a ledge or the next roof, not the way down
                if (!NavMesh.SamplePosition(below.point, out NavMeshHit landing, 1.5f, filter) || Mathf.Abs(landing.position.y - below.point.y) > 0.5f) continue;
                landings.Add(landing.position);
            }
            if (landings.Count == 0) return null;
            var toEdge = new NavMeshPath();
            if (!NavMesh.CalculatePath(feet, edge, filter, toEdge) || toEdge.status != NavMeshPathStatus.PathComplete) return null;
            // NGO WatchDrop: the jump starts as the walk comes within the trigger of the edge
            WalkUpTo(layout, toEdge.corners, LedgeDropTrigger);
            string refusal = null;
            bool dropped = false;
            foreach (Vector3 landing in landings)
            {
                refusal = LedgeArc(layout, landing, WallRunPhase.LedgeJump);
                if (refusal == null) { dropped = true; break; }
            }
            if (!dropped)
            {
                LastLedgeRefusal = string.IsNullOrEmpty(refusal) ? "There is no way down from here." : refusal;
                return layout.FitTo(WallWalkLayout.MaxCorners) ? layout : null;   // NGO: the move stops at the edge
            }
            // NGO: once landed, the move walks on to the place
            var on = new NavMeshPath();
            if ((destination - layout.LastFeet).sqrMagnitude > 0.25f && NavMesh.CalculatePath(layout.LastFeet, destination, filter, on)
                && on.status == NavMeshPathStatus.PathComplete)
                for (int i = 1; i < on.corners.Length; i++) layout.Ground(on.corners[i]);
            return layout.FitTo(WallWalkLayout.MaxCorners) ? layout : null;
        }

        /// <summary>A stretch of a NavMesh path that is a NavMeshLink: no NavMesh within 0.3 m under its middle (a gap or a drop).</summary>
        private static bool IsGap(Vector3 a, Vector3 b, NavMeshQueryFilter filter)
        {
            Vector3 mid = (a + b) * 0.5f;
            return !NavMesh.SamplePosition(mid, out NavMeshHit hit, 0.3f, filter) || Mathf.Abs(hit.position.y - mid.y) > 0.3f;
        }

        /// <summary>
        /// One jump of the route from the layout's last corner to <paramref name="to"/>: NGO's jump checks (<see cref="PlayerJumpMotor.PlanForGhost"/>),
        /// then a timed arc to its landing over its own apex, played as <paramref name="phase"/> (<see cref="WallRunPhase.LedgeJump"/> off a roof's edge,
        /// <see cref="WallRunPhase.GapJump"/> across a gap: each its own clip, owner 2 Nov). Null when laid out, else the jump's words.
        /// </summary>
        private string LedgeArc(WallWalkLayout layout, Vector3 to, WallRunPhase phase)
        {
            Vector3 from = layout.LastFeet;
            string why = jumpMotor.PlanForGhost(from, to, out Vector3 landing, out float peak);
            if (why != null) return why;
            layout.Arc(phase, landing, peak - Mathf.Max(from.y, landing.y), WallWalkLayout.Facing(landing - from));
            return null;
        }
    }
}
