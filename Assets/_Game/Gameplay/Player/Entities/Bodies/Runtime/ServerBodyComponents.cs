using Unity.Entities;
using KryticalNova.Core;

namespace KryticalNova.Player.Entities.Bodies
{
    /// <summary>
    /// Server only, on a player ghost: the server's key of the player it belongs to (never the NetworkId, which netcode hands out
    /// again). Added by <see cref="ServerBodyAdmission"/> when the ghost is spawned; not a ghost field, so it never travels.
    /// </summary>
    public struct PlayerGhostKey : IComponentData
    {
        public ulong Key;

        public PlayerKey PlayerKey => new PlayerKey(Key);
    }

    /// <summary>
    /// Server only, on a player ghost: its player has left. The ghost already has no owner (NetworkId 0) and is about to be
    /// destroyed; nothing may move it or sync a body from it in between (<see cref="PlayerLeaveHandler"/>, design 3.1 step 1).
    /// </summary>
    public struct PlayerLeaving : IComponentData
    {
    }
}
