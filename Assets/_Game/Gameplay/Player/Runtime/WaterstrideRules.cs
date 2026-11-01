using System.Collections.Generic;
using KryticalNova.World;
using KryticalNova.World.Water;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Player
{
    /// <summary>
    /// Waterstride's planning rules without a body (M3c, `m3c-waterstride-design.md`; one copy of the rules): which clicks it takes, why
    /// it refuses, and the way it runs. <see cref="PlayerWaterstrideMotor"/> plans its own runs with it; the entity-netcode planners
    /// call it with the ghost's feet, because on a body of the entity netcode the motor and the agent are switched off.
    /// </summary>
    public static class WaterstrideRules
    {
        public const float StepAlong = 0.5f;      // metres between the samples of a line looking for the water's edge
        public const float LookForWater = 80f;    // farthest along a click's line the water's edge is looked for
        public const float ShoreTrigger = 0.7f;   // metres from the shore point the run onto the water starts
        public const float StrandedReach = 80f;   // farthest the nearest shore is looked for when Flux runs out
        public const float LandSampleRadius = 2f; // a land click from the water: walkable ground this near the place
        public const float WalkOnMinimum = 0.5f;  // ashore, the walk on to the place clicked is made when it is farther than this
        /// <summary>A jump click this near the water's height (or lower) over water comes down on the water.</summary>
        public const float WaterLandingTolerance = 0.3f;

        /// <summary>Where the shore is: the NavMesh, behind one seam so the rules can be tested without one.</summary>
        public interface IShore
        {
            /// <summary>The walkable place nearest <paramref name="point"/> within <paramref name="radius"/>.</summary>
            bool Sample(Vector3 point, float radius, out Vector3 found);
            /// <summary>A complete walk from <paramref name="from"/> to <paramref name="to"/> exists.</summary>
            bool PathComplete(Vector3 from, Vector3 to);
        }

        /// <summary>The shore of the NavMesh for an agent type and area mask (the game's).</summary>
        public sealed class NavMeshShore : IShore
        {
            readonly NavMeshPath m_Path = new NavMeshPath();
            public NavMeshQueryFilter Filter;

            public NavMeshShore(NavMeshQueryFilter filter) => Filter = filter;

            public bool Sample(Vector3 point, float radius, out Vector3 found)
            {
                bool ok = NavMesh.SamplePosition(point, out NavMeshHit hit, radius, Filter);
                found = ok ? hit.position : point;
                return ok;
            }

            public bool PathComplete(Vector3 from, Vector3 to)
            {
                if (NavMesh.SamplePosition(from, out NavMeshHit start, LandSampleRadius, Filter)) from = start.position;
                return NavMesh.CalculatePath(from, to, Filter, m_Path) && m_Path.status == NavMeshPathStatus.PathComplete;
            }
        }

        /// <summary>What the body is doing, for the refusals (the body's own answers; the ghost's on the entity netcode).</summary>
        public struct Ask
        {
            /// <summary>Level, Run rank and Jump rank reach the settings' numbers.</summary>
            public bool Open;
            public bool Alive;
            /// <summary>A jump, a vault or a wall move is under way.</summary>
            public bool OtherMoveBusy;
            /// <summary>Run is selected (the click runs).</summary>
            public bool Running;
            /// <summary>The Flux for one second on the water.</summary>
            public bool HasFluxForASecond;
            public bool OnWater;
            public bool Stranded;
            /// <summary>From land: the feet are on walkable ground.</summary>
            public bool OnWalkableGround;
        }

        /// <summary>The way a click runs: to the shore first (from land), over <c>route</c>, and ashore at the end (to land).</summary>
        public struct Way
        {
            /// <summary>From land: the walk goes to <see cref="ShoreFeet"/> first; the run onto the water starts <see cref="ShoreTrigger"/> before it.</summary>
            public bool FromLand;
            /// <summary>From land, with the feet already within <see cref="ShoreTrigger"/> of the shore: the run starts at once.</summary>
            public bool StartsNow;
            public Vector3 ShoreFeet;
            /// <summary>To land: ashore at <see cref="LandFeet"/>, then the walk on to <see cref="WalkOnTo"/>.</summary>
            public bool ToLand;
            public Vector3 LandFeet, WalkOnTo;
        }

        /// <summary>Waterstride is open: level, Run rank and Jump rank all reach the settings' numbers.</summary>
        public static bool IsOpen(WaterstrideSettings s, int level, int runRank, int jumpRank) =>
            s != null && level >= s.requiredLevel && runRank >= s.requiredRunRank && jumpRank >= s.requiredJumpRank;

        /// <summary>The Flux one second on the water costs (0 = free).</summary>
        public static float FluxPerSecond(WaterstrideSettings s) => s != null ? Mathf.Max(0f, s.fluxPerSecond) : 0f;

        /// <summary>
        /// The place on the water a click's camera ray meets, before its first hit (<paramref name="hitDistance"/>): the point on the
        /// surface. False when the ray meets something else first or the place is not water that can be run on.
        /// </summary>
        public static bool WaterUnderRay(WaterMap map, Ray ray, float hitDistance, out Vector3 onSurface)
        {
            onSurface = default;
            if (map == null || ray.direction.y >= -1e-4f) return false;
            float along = (map.waterLevel - ray.origin.y) / ray.direction.y;
            onSurface = ray.GetPoint(along);
            return along > 0f && along <= hitDistance + 0.05f && map.IsWater(onSurface);
        }

        /// <summary>Why a run onto (or on) the water cannot start, in NGO's words; null when it can.</summary>
        public static string CanStride(WaterstrideSettings s, in Ask a)
        {
            if (!a.Open) return $"Waterstride opens at level {s.requiredLevel} with Run and Jump at rank {s.requiredRunRank}.";
            if (!a.Alive) return "No HP remaining.";
            if (a.OtherMoveBusy) return "Finish this move first.";
            if (!a.OnWater && !a.Running) return "Select Run to run on water.";
            if (FluxPerSecond(s) > 0f && !a.HasFluxForASecond) return "Not enough Flux to run on water.";
            return null;
        }

        /// <summary>
        /// A click on the water at <paramref name="point"/> (on its surface). Fills <paramref name="route"/> with the way over the water
        /// and <paramref name="way"/> with the walk to the shore before it. Null when it runs; otherwise the reason in NGO's words.
        /// </summary>
        public static string PlanWaterClick(WaterMap map, WaterstrideSettings s, in Ask a, IShore shore, Vector3 feet, Vector3 point,
            List<Vector3> route, out Way way)
        {
            way = default;
            route.Clear();
            if (map == null || s == null) return "There is no water to run on here.";
            string why = CanStride(s, a);
            if (why != null) return why;
            if (a.Stranded) return "Out of Flux: back to the shore first.";
            point.y = map.waterLevel;
            if (!map.TryCell(point, out Vector2Int target) || !map.IsWater(target)) return "That water cannot be run on.";

            if (a.OnWater)
            {
                if (!WaterRoute.NearestWater(map, feet, s.shoreReach, out Vector2Int here)) return "No way over the water there.";
                if (!WaterRoute.Find(map, here, target, s.maxRouteCells, route)) return "No way over the water there.";
                return null;
            }

            // From land: to the shore where the line to the place first meets the water, then out over it.
            if (!a.OnWalkableGround) return "Waterstride must start on walkable ground.";
            if (!EdgeAlong(map, feet, point, out Vector3 lastLand, out Vector3 firstWater)) return "There is no shore on the way to that water.";
            if (!shore.Sample(lastLand, s.shoreReach, out Vector3 shoreFeet)) return "There is no shore to run out from there.";
            if (!WaterRoute.NearestWater(map, firstWater, s.shoreReach, out Vector2Int entry)
                || !WaterRoute.Find(map, entry, target, s.maxRouteCells, route)) { route.Clear(); return "No way over the water there."; }
            if (!shore.PathComplete(feet, shoreFeet)) { route.Clear(); return "There is no way to that shore."; }
            way.FromLand = true;
            way.ShoreFeet = shoreFeet;
            way.StartsNow = Flat(feet, shoreFeet) <= ShoreTrigger;
            return null;
        }

        /// <summary>
        /// A click on land at <paramref name="point"/> while on the water: the way over the water to the shore on the line back from
        /// there (else the shore nearest it), ashore, and on. Null when it runs; "" when it is not Waterstride's click; else the reason.
        /// </summary>
        public static string PlanLandClick(WaterMap map, WaterstrideSettings s, in Ask a, IShore shore, Vector3 feet, Vector3 point,
            List<Vector3> route, out Way way)
        {
            way = default;
            route.Clear();
            if (map == null || s == null || !a.OnWater) return string.Empty;
            if (a.Stranded) return "Out of Flux: back to the shore first.";
            if (!shore.Sample(point, LandSampleRadius, out Vector3 there)) return "Choose a walkable ground or platform surface.";
            // The water's edge on the line from that place back to the player: run to it, step ashore, walk on.
            Vector3 landFeet;
            if (!EdgeAlong(map, there, feet, out Vector3 landSide, out Vector3 waterSide) || !shore.Sample(landSide, s.shoreReach, out landFeet))
            {
                // No edge on that line: the nearest shore to the place.
                if (!shore.Sample(there, s.shoreReach, out landFeet)) return "There is no shore near there.";
                waterSide = landFeet;
            }
            if (!WaterRoute.NearestWater(map, feet, s.shoreReach, out Vector2Int here)) return "No way over the water there.";
            if (!WaterRoute.NearestWater(map, waterSide, s.shoreReach, out Vector2Int exit)
                || !WaterRoute.Find(map, here, exit, s.maxRouteCells, route)) { route.Clear(); return "No way over the water there."; }
            way.ToLand = true;
            way.LandFeet = landFeet;
            way.WalkOnTo = there;
            return null;
        }

        /// <summary>
        /// Out of Flux on the water: the way back to the walkable place nearest the feet (within <see cref="StrandedReach"/>). False when
        /// there is none (the run is called off where it is).
        /// </summary>
        public static bool PlanStranded(WaterMap map, WaterstrideSettings s, IShore shore, Vector3 feet, List<Vector3> route, out Vector3 landFeet)
        {
            route.Clear();
            landFeet = feet;
            if (map == null || s == null || !shore.Sample(feet, StrandedReach, out landFeet)) return false;
            bool found = WaterRoute.NearestWater(map, feet, s.shoreReach, out Vector2Int here)
                && WaterRoute.NearestWater(map, landFeet, s.shoreReach, out Vector2Int exit)
                && WaterRoute.Find(map, here, exit, s.maxRouteCells, route);
            if (!found) { route.Clear(); route.Add(new Vector3(landFeet.x, map.waterLevel, landFeet.z)); }
            return true;
        }

        /// <summary>Where a jump clicked at <paramref name="clicked"/> comes down on the water: the point on its surface.</summary>
        public static bool WaterLanding(WaterMap map, Vector3 clicked, out Vector3 surface)
        {
            surface = clicked;
            if (map == null || clicked.y > map.waterLevel + WaterLandingTolerance || !map.IsWater(clicked)) return false;
            surface = new Vector3(clicked.x, map.waterLevel, clicked.z);
            return true;
        }

        /// <summary>A jump came down on the water: the player stands on it (true) or falls in (false).</summary>
        public static bool StandsOnLanding(WaterstrideSettings s, bool open, bool alive, bool hasFluxForASecond) =>
            s != null && open && alive && (FluxPerSecond(s) <= 0f || hasFluxForASecond);

        /// <summary>Falling in: the HP lost, a share of Max HP and never the last point.</summary>
        public static float FallInLoss(WaterstrideSettings s, float maxHealth, float health, bool alive) =>
            alive && s != null && s.fallInHealthShare > 0f ? Mathf.Max(0f, Mathf.Min(maxHealth * s.fallInHealthShare, health - 1f)) : 0f;

        /// <summary>What a fall-in tells the player.</summary>
        public static string FallInLine(float lost) => lost >= 1f
            ? $"You fell in the water and climbed out on the shore (-{Mathf.RoundToInt(lost)} HP)."
            : "You fell in the water and climbed out on the shore.";

        /// <summary>The speed on the water: a share of the ground speed, at least 1 m/s; stranded, the settings' own.</summary>
        public static float Speed(WaterstrideSettings s, float groundSpeed, bool stranded) =>
            stranded ? s.strandedSpeed : Mathf.Max(1f, groundSpeed * s.speedShare);

        /// <summary>
        /// Walking from <paramref name="from"/> towards <paramref name="to"/>, the last place on land and the first on the water (or the
        /// other way round when <paramref name="from"/> is on land and <paramref name="to"/> on water).
        /// </summary>
        public static bool EdgeAlong(WaterMap map, Vector3 from, Vector3 to, out Vector3 lastLand, out Vector3 firstWater)
        {
            lastLand = firstWater = from;
            Vector3 flat = to - from;
            flat.y = 0f;
            float length = Mathf.Min(flat.magnitude, LookForWater);
            if (length < 0.01f) return false;
            Vector3 way = flat / flat.magnitude;
            Vector3 previous = from;
            for (float d = 0f; d <= length + 0.001f; d += StepAlong)
            {
                Vector3 p = from + way * d;
                if (map.IsWater(p))
                {
                    lastLand = previous;
                    firstWater = new Vector3(p.x, map.waterLevel, p.z);
                    return d > 0f;
                }
                previous = p;
            }
            return false;
        }

        public static float Flat(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
