using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Relevancy: which players and monsters the server sends to a client, and who gets bandwidth first (wave 4, M9; replaces the
    /// Netcode for GameObjects <c>SpatialInterestManager</c>). Every number is here, none in code. Loaded once by the server from
    /// Resources (<see cref="ResourcePath"/>); the same asset on every map server.
    /// </summary>
    public sealed class RelevancySettings : ScriptableObject
    {
        public const string ResourcePath = "RelevancySettings";

        [Header("Who is sent to whom")]
        [Tooltip("Off: every client gets every player and monster (as before relevancy). The server's server-settings.json key \"relevancy\" (true / false) wins over this, so the live server is switched without a build.")]
        public bool enabled;
        [Tooltip("Metres. Another player is sent to a client from inside this distance. Measured flat (x and z only): a player on a roof or a cliff top is never hidden by height.")]
        [Min(1f)] public float playerRadius = 150f;
        [Tooltip("Metres. Hostiles, Strike Dummies and any later kind of object are sent from inside this distance (flat, as for players).")]
        [Min(1f)] public float objectRadius = 150f;
        [Tooltip("Metres past the radius before something already sent is taken away again, so a body on the edge does not flicker.")]
        [Min(0f)] public float leaveMargin = 16f;
        [Tooltip("Server ticks between two rebuilds of who sees whom (6 = 0.2 s at 30 ticks a second). A player or monster that appears or goes is taken up at once.")]
        [Min(1)] public int fillIntervalTicks = 6;

        [Header("Who gets bandwidth first")]
        [Tooltip("Metres. The side of the tiles that near things are sorted by: the tiles around a player are sent at full rate, farther tiles less often.")]
        [Min(1)] public int importanceTileSize = 48;
        [Tooltip("How often players are sent compared with the other kinds (written into the PlayerGhost prefab by its builder).")]
        [Min(1)] public int importancePlayer = 100;
        [Tooltip("How often Hostiles are sent (written into the HostileGhost prefab by its builder).")]
        [Min(1)] public int importanceHostile = 60;
        [Tooltip("How often Strike Dummies are sent (written into the StrikeDummyGhost prefab by its builder).")]
        [Min(1)] public int importanceDummy = 10;

        /// <summary>The asset from Resources; null when it is missing (the caller logs it and relevancy stays off).</summary>
        public static RelevancySettings Load() => Resources.Load<RelevancySettings>(ResourcePath);
    }
}
