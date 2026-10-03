using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// Something on the map the pointer can name and the player can be led to: an NPC, a portal, a named place or
    /// the player's own marker.
    /// </summary>
    public readonly struct MapTarget
    {
        public enum TargetKind { Npc, Portal, Place, Marker }

        public readonly string Name;
        public readonly TargetKind Kind;
        /// <summary>Where its icon sits on the map.</summary>
        public readonly Vector3 Position;
        /// <summary>Where a traveller should end up: beside an NPC, short of a portal's pad, on a place or marker.</summary>
        public readonly Vector3 TravelPoint;

        public MapTarget(string name, TargetKind kind, Vector3 position, Vector3 travelPoint)
        {
            Name = name;
            Kind = kind;
            Position = position;
            TravelPoint = travelPoint;
        }
    }
}
