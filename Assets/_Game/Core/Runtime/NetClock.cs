using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>What a networking layer supplies to <see cref="NetClock"/>. Plain values, no netcode types.</summary>
    public interface INetClockProvider
    {
        /// <summary>True while a network session is running (listening) and the clock below is meaningful.</summary>
        bool IsRunning { get; }
        /// <summary>Server-synchronised time in seconds.</summary>
        double Seconds { get; }
        /// <summary>Current network tick.</summary>
        int Tick { get; }
        /// <summary>Network ticks per second.</summary>
        int TickRate { get; }
    }

    /// <summary>
    /// The shared, server-synchronised clock for rule code. The Networking assembly installs a provider; with none
    /// installed, or no session running, it falls back to local game time (<see cref="Time.timeAsDouble"/>), exactly
    /// what offline code read before.
    /// </summary>
    public static class NetClock
    {
        private static INetClockProvider provider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => provider = null;

        /// <summary>The installed provider, or null.</summary>
        public static INetClockProvider Provider => provider;

        /// <summary>Install (or replace) the provider. Safe to call again; null clears it.</summary>
        public static void Install(INetClockProvider newProvider) => provider = newProvider;

        /// <summary>Remove the provider only if it is the given one (so a stale owner cannot clear a newer one).</summary>
        public static void Uninstall(INetClockProvider oldProvider)
        {
            if (ReferenceEquals(provider, oldProvider)) provider = null;
        }

        /// <summary>True while a session is running and a provider is installed.</summary>
        public static bool IsRunning => provider != null && provider.IsRunning;

        /// <summary>Server time in seconds when a session runs; local game time otherwise.</summary>
        public static double Now => provider != null && provider.IsRunning ? provider.Seconds : Time.timeAsDouble;

        /// <summary>Current network tick, 0 with no session.</summary>
        public static int Tick => provider != null && provider.IsRunning ? provider.Tick : 0;

        /// <summary>Network ticks per second, 0 with no session.</summary>
        public static int TickRate => provider != null && provider.IsRunning ? provider.TickRate : 0;
    }
}
