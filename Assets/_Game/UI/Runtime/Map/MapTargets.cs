using System.Collections.Generic;
using KryticalNova.NPC;
using KryticalNova.World;
using KryticalNova.World.Portals;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// The named things currently on the map, gathered from the same live lists the map controllers draw their
    /// markers from. Used for the hover name and for double-click travel on both the world map and the minimap.
    /// </summary>
    public static class MapTargets
    {
        /// <summary>A traveller stops this far (m) from a portal's centre, outside the pad that would take them through.</summary>
        private const float PortalStandOff = 4.5f;
        private const string MarkerName = "Your marker";

        /// <summary>Fills <paramref name="into"/> (cleared first). <paramref name="places"/> = include named places, which only the world map draws.</summary>
        public static void Collect(List<MapTarget> into, Vector3 playerPosition, bool places)
        {
            into.Clear();

            foreach (var npc in NpcInteractable.Active)
            {
                if (npc == null) continue;
                into.Add(new MapTarget(npc.DisplayName, MapTarget.TargetKind.Npc, npc.Position, npc.InteractPoint));
            }

            foreach (var portal in ZonePortal.Active)
            {
                if (portal == null) continue;
                Vector3 centre = portal.transform.position;
                Vector3 toPlayer = playerPosition - centre;
                toPlayer.y = 0f;
                Vector3 side = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : -portal.transform.forward;
                into.Add(new MapTarget(portal.DisplayName, MapTarget.TargetKind.Portal, centre, centre + side * PortalStandOff));
            }

            if (places)
            {
                foreach (var poi in PointOfInterest.Active)
                    if (poi != null)
                        into.Add(new MapTarget(poi.DisplayName, MapTarget.TargetKind.Place, poi.transform.position, poi.transform.position));
            }

            if (MapWaypoint.TryGet(out Vector3 marker))
                into.Add(new MapTarget(MarkerName, MapTarget.TargetKind.Marker, marker, marker));
        }
    }
}
