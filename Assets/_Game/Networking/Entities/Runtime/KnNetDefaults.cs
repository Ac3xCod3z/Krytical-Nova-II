using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities
{
    /// <summary>The numbers every Krytical Nova netcode world starts with (plan section 2: 30/30 ticks).</summary>
    public static class KnNetDefaults
    {
        public const int SimulationTickRate = 30;
        public const int NetworkTickRate = 30;

        /// <summary>
        /// Handshake plus approval must finish inside this (default in the package: 5 s). Raised because the Unity-token
        /// check is asynchronous; the pre-approval budget (<see cref="KnApprovalLimits.MaxSeconds"/>) stays below it.
        /// The server's value is sent to the client during the handshake.
        /// </summary>
        public const uint HandshakeApprovalTimeoutMs = 15_000;

        /// <summary>
        /// The most the stop waits for the connections to close after the disconnects were sent (seconds; the server's
        /// <c>stopGraceSeconds</c> setting overrides it). Still-open connections are force-closed after it, then the server quits.
        /// </summary>
        public const float StopGraceSeconds = 3f;

        /// <summary>
        /// The most closed connections the server handles in one tick (save, body, ghost); the rest wait in their close order for the
        /// next ticks (the server's <c>leavesPerTick</c> setting overrides it; 0 = all at once). Perf round 3, R3-7: 200 bots leaving
        /// together cost one 490-530 ms frame at about 2 ms a player. A player who joins again while queued is handled at once.
        /// </summary>
        public const int LeavesPerTick = 10;

        /// <summary>
        /// A server that falls behind runs up to this many whole ticks in one frame, each on its own, never several ticks folded into
        /// one step (the package's default batches up to 4). The player's walk and jump are integer plans the owner predicts tick by
        /// tick; a folded step moved the server's walk one tick for several and skipped a jump's landing tick (GUI run 1, 31 Oct, at
        /// 9 frames a second: every jump landed 2-25 cm short of its plan).
        /// </summary>
        public const int MaxTicksPerFrame = 4;

        /// <summary>Whole simulation ticks for a grace in seconds (at least 1).</summary>
        public static int StopGraceTicks(float seconds) => System.Math.Max(1, (int)System.Math.Ceiling(seconds * SimulationTickRate));

        public static ClientServerTickRate TickRate()
        {
            var rate = new ClientServerTickRate
            {
                SimulationTickRate = SimulationTickRate,
                NetworkTickRate = NetworkTickRate,
                HandshakeApprovalTimeoutMS = HandshakeApprovalTimeoutMs,
                MaxSimulationStepsPerFrame = MaxTicksPerFrame,
                MaxSimulationStepBatchSize = 1,
            };
            rate.ResolveDefaults();
            return rate;
        }

        /// <summary>Creates the tick-rate singleton in a freshly created server or client world (before its first update).</summary>
        public static void Apply(World world) => world.EntityManager.CreateSingleton(TickRate());
    }
}
