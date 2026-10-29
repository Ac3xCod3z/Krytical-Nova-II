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

        public static ClientServerTickRate TickRate()
        {
            var rate = new ClientServerTickRate
            {
                SimulationTickRate = SimulationTickRate,
                NetworkTickRate = NetworkTickRate,
                HandshakeApprovalTimeoutMS = HandshakeApprovalTimeoutMs,
            };
            rate.ResolveDefaults();
            return rate;
        }

        /// <summary>Creates the tick-rate singleton in a freshly created server or client world (before its first update).</summary>
        public static void Apply(World world) => world.EntityManager.CreateSingleton(TickRate());
    }
}
