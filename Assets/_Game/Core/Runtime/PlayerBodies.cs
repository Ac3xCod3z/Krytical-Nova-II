using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// A player's body as the world sees it (a portal walked into, travel between maps, the rain): where it is, who owns
    /// it, whether this machine controls it, whether it is mid-jump, and settling it into a new map. In Core so that
    /// World, which sits below the player, need not know the player (owner, 19 Oct 2026: travel stays in World).
    /// <c>PointAndClickPlayer</c> is the one there is.
    /// </summary>
    public interface IPlayerBody
    {
        Transform Transform { get; }
        ulong OwnerClientId { get; }

        /// <summary>Spawned and owned by this machine.</summary>
        bool IsLocal { get; }

        bool IsJumping { get; }

        /// <summary>After a map change: the camera, occluder fade and NavMesh of the new map.</summary>
        void RebindToScene();
    }

    /// <summary>Every enabled player body (local, remote and the offline avatar). The player keeps the list; this reads it.</summary>
    public static class PlayerBodies
    {
        public static IReadOnlyList<IPlayerBody> All { get; private set; } = Array.Empty<IPlayerBody>();

        /// <summary>Called by the player with the list it keeps of itself.</summary>
        public static void Use(IReadOnlyList<IPlayerBody> list) => All = list ?? Array.Empty<IPlayerBody>();
    }
}
