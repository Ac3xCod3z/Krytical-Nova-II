using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Player;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.UI
{
    /// <summary>
    /// Travel ordered from a map: starts the local player walking to a <see cref="MapTarget"/> and keeps the route
    /// both maps draw while it lasts. The route is worked out here from the NavMesh (every peer has it), so it shows
    /// for a client whose own character is moved by the server.
    /// </summary>
    public static class MapTravel
    {
        private const float RouteRefreshSeconds = 0.4f;
        private const float FeetSampleRadius = 2f;
        private const int MaxCorners = 96;

        private static readonly Vector3[] Corners = new Vector3[MaxCorners];
        private static readonly List<Vector3> RoutePoints = new List<Vector3>(MaxCorners);
        private static NavMeshPath path;
        private static float nextRefresh;
        private static Vector3 routedTo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            path = null;
            nextRefresh = 0f;
            RoutePoints.Clear();
        }

        /// <summary>The player this machine controls, or null before it exists.</summary>
        public static PointAndClickPlayer LocalPlayer()
        {
            var inv = PlayerInventory.Local;
            if (inv != null && inv.isActiveAndEnabled && inv.TryGetComponent(out PointAndClickPlayer local)) return local;
            // Before the networked player spawns (or offline), fall back to the locally controlled avatar.
            foreach (var p in PointAndClickPlayer.Active)
                if (p != null && p.isActiveAndEnabled && (!p.IsSpawned || p.IsOwner)) return p;
            return null;
        }

        /// <summary>Leads the local player to the target. False when the order was refused (no path, mid jump, ...).</summary>
        public static bool Start(MapTarget target)
        {
            PointAndClickPlayer player = LocalPlayer();
            if (player == null || !player.TravelTo(target.TravelPoint, target.Name)) return false;
            nextRefresh = 0f; // draw the new route at once
            return true;
        }

        /// <summary>
        /// The way from the player to where they are being led, as world points (player first). Empty when no travel
        /// is under way. Refreshed a few times a second, so it shortens as the player walks it.
        /// </summary>
        public static IReadOnlyList<Vector3> Route()
        {
            PointAndClickPlayer player = LocalPlayer();
            if (player == null || !player.IsTravelling)
            {
                RoutePoints.Clear();
                return RoutePoints;
            }

            Vector3 destination = player.TravelDestination;
            if (Time.unscaledTime < nextRefresh && destination == routedTo) return RoutePoints;
            nextRefresh = Time.unscaledTime + RouteRefreshSeconds;
            routedTo = destination;

            RoutePoints.Clear();
            path ??= new NavMeshPath();
            Vector3 feet = player.transform.position;
            if (player.TryGetComponent(out NavMeshAgent agent)) feet -= Vector3.up * agent.baseOffset;
            if (!NavMesh.SamplePosition(feet, out NavMeshHit from, FeetSampleRadius, NavMesh.AllAreas)
                || !NavMesh.CalculatePath(from.position, destination, NavMesh.AllAreas, path))
                return RoutePoints;

            int count = path.GetCornersNonAlloc(Corners);
            for (int i = 0; i < count; i++) RoutePoints.Add(Corners[i]);
            return RoutePoints;
        }
    }
}
