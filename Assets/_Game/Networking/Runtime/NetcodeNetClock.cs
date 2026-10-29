using KryticalNova.Core;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>Feeds <see cref="NetClock"/> from Netcode for GameObjects' <c>NetworkManager.Singleton</c>.</summary>
    public sealed class NetcodeNetClock : INetClockProvider
    {
        private static NetcodeNetClock instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (instance != null) NetClock.Uninstall(instance);
            instance = null;
        }

        // Also runs after a domain reload where SubsystemRegistration already ran; installs once, re-install is harmless.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InstallProvider()
        {
            if (instance == null) instance = new NetcodeNetClock();
            NetClock.Install(instance);
        }

        public bool IsRunning => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        public double Seconds => NetworkManager.Singleton.ServerTime.Time;
        public int Tick => NetworkManager.Singleton.ServerTime.Tick;
        public int TickRate => NetworkManager.Singleton.NetworkTickSystem != null ? (int)NetworkManager.Singleton.NetworkTickSystem.TickRate : 0;
    }
}
