using KryticalNova.Core;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// <see cref="NetClock"/> for the entity netcode (M7.1, 2 Nov 2026): server time in seconds from the network tick, the same on the
    /// server and every client (a client's estimate of the server's tick), so the sky, the weather and every rule that reads
    /// <see cref="NetClock.Now"/> agree between machines. Running while a world of the entity netcode has a valid tick.
    /// </summary>
    public sealed class EntitiesNetClock : INetClockProvider
    {
        public static readonly EntitiesNetClock Instance = new EntitiesNetClock();

        internal bool Running;
        internal double SecondsNow;
        internal int TickNow, Rate;

        public bool IsRunning => Running;
        public double Seconds => SecondsNow;
        public int Tick => TickNow;
        public int TickRate => Rate;

        /// <summary>Seconds of a tick and its fraction at <paramref name="rate"/> ticks a second.</summary>
        public static double SecondsOf(uint tickIndex, float fraction, int rate) =>
            rate > 0 ? (tickIndex + (double)UnityEngine.Mathf.Clamp01(fraction) - 1.0) / rate : 0.0;
    }

    /// <summary>
    /// Keeps <see cref="EntitiesNetClock"/> current and installed: the server world writes it; a client world writes it only when no
    /// server world runs in this process (a built client), so one process never mixes the two.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class EntitiesNetClockSystem : SystemBase
    {
        protected override void OnDestroy()
        {
            if (World.IsServer() || ClientServerBootstrap.ServerWorld == null) EntitiesNetClock.Instance.Running = false;
        }

        protected override void OnUpdate()
        {
            if (!World.IsServer() && ClientServerBootstrap.ServerWorld != null && ClientServerBootstrap.ServerWorld.IsCreated) return;
            if (!SystemAPI.TryGetSingleton(out NetworkTime time) || !time.ServerTick.IsValid) return;
            int rate = SystemAPI.TryGetSingleton(out ClientServerTickRate tickRate) && tickRate.SimulationTickRate > 0 ? tickRate.SimulationTickRate : 30;
            var clock = EntitiesNetClock.Instance;
            clock.Rate = rate;
            clock.TickNow = (int)time.ServerTick.TickIndexForValidTick;
            clock.SecondsNow = EntitiesNetClock.SecondsOf(time.ServerTick.TickIndexForValidTick, time.ServerTickFraction, rate);
            clock.Running = true;
            if (!ReferenceEquals(NetClock.Provider, clock)) NetClock.Install(clock);
        }
    }
}
