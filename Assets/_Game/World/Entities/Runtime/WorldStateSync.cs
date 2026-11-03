using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.World.Entities
{
    /// <summary>
    /// The world's clock and weather (M7.1): NGO's <c>WorldEnvironmentNetwork.netState</c>, field for field (~60 bytes). It changes only when
    /// the weather turns or a GM edits the clock; every machine derives the time of day and the blend from it and <c>NetClock</c>.
    /// </summary>
    public struct WorldStateRpc : IRpcCommand
    {
        public double CycleEpoch, TransitionStart;
        public float DayLengthSeconds, TimeOffset01, TimeScale, TransitionSeconds;
        public float FromWindHeading, ToWindHeading, FromWindSpeed, ToWindSpeed;
        public int DayBase, Seed;
        public byte FromWeather, ToWeather;

        public static WorldStateRpc Of(in WorldEnvironmentState s) => new WorldStateRpc
        {
            CycleEpoch = s.CycleEpoch, TransitionStart = s.TransitionStart, DayLengthSeconds = s.DayLengthSeconds, TimeOffset01 = s.TimeOffset01,
            TimeScale = s.TimeScale, TransitionSeconds = s.TransitionSeconds, FromWindHeading = s.FromWindHeading, ToWindHeading = s.ToWindHeading,
            FromWindSpeed = s.FromWindSpeed, ToWindSpeed = s.ToWindSpeed, DayBase = s.DayBase, Seed = s.Seed, FromWeather = s.FromWeather,
            ToWeather = s.ToWeather,
        };

        public WorldEnvironmentState State => new WorldEnvironmentState
        {
            CycleEpoch = CycleEpoch, TransitionStart = TransitionStart, DayLengthSeconds = DayLengthSeconds, TimeOffset01 = TimeOffset01,
            TimeScale = TimeScale, TransitionSeconds = TransitionSeconds, FromWindHeading = FromWindHeading, ToWindHeading = ToWindHeading,
            FromWindSpeed = FromWindSpeed, ToWindSpeed = ToWindSpeed, DayBase = DayBase, Seed = Seed, FromWeather = FromWeather, ToWeather = ToWeather,
        };
    }

    /// <summary>
    /// Server: sends the world's state to every player in the game when it changed since that player last got it, and to each player as
    /// they come in (NGO: the network variable's initial sync and its changes). Looked at four times a second.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WorldStateServerSystem : SystemBase
    {
        /// <summary>Where the state is read (the scene's <c>WorldEnvironmentNetwork</c>; tests put their own).</summary>
        public static Func<(bool ok, WorldEnvironmentState state)> Source;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Source = null;

        public float Interval = 0.25f;
        readonly Dictionary<Entity, WorldEnvironmentState> m_Sent = new Dictionary<Entity, WorldEnvironmentState>();
        readonly List<Entity> m_Gone = new List<Entity>();
        EntityQuery m_InGame;
        double m_Next;

        /// <summary>States sent, updates run, updates that had a state, players in the game at the last look (tests).</summary>
        public int Sends { get; private set; }
        public int Updates { get; private set; }
        public int WithState { get; private set; }
        public int LastInGame { get; private set; }

        protected override void OnCreate() =>
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());

        protected override void OnUpdate()
        {
            Updates++;
            // the world's own clock (a test world ticks faster than real time; 2 Nov: a real-time gate let the test look only once)
            double now = SystemAPI.Time.ElapsedTime;
            if (now < m_Next) return;
            m_Next = now + Interval;
            (bool ok, WorldEnvironmentState state) = Source != null ? Source() : (WorldEnvironmentNetwork.TryGetState(out var s), s);
            if (!ok) return;
            WithState++;
            var em = EntityManager;
            m_Gone.Clear();
            foreach (Entity c in m_Sent.Keys) if (!em.Exists(c)) m_Gone.Add(c);
            foreach (Entity c in m_Gone) m_Sent.Remove(c);
            using var connections = m_InGame.ToEntityArray(Allocator.Temp);
            LastInGame = connections.Length;
            foreach (Entity connection in connections)
            {
                if (m_Sent.TryGetValue(connection, out WorldEnvironmentState had) && had.Equals(state)) continue;
                m_Sent[connection] = state;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, WorldStateRpc.Of(state));
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                Sends++;
            }
        }
    }

    /// <summary>
    /// Client: takes the server's world state into the scene's <c>WorldEnvironmentNetwork</c> (as an NGO client took the network variable)
    /// and, with no server in this process, leaves the weather and the clock's setters to the server.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class WorldStateClientSystem : SystemBase
    {
        /// <summary>Where a received state goes (the scene's environment; tests put their own).</summary>
        public static Action<WorldEnvironmentState> Sink;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Sink = null;

        EntityQuery m_States;

        /// <summary>States taken, and the last one (tests).</summary>
        public int Applied { get; private set; }
        public WorldEnvironmentState Last { get; private set; }

        protected override void OnCreate() =>
            m_States = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<WorldStateRpc>());

        protected override void OnDestroy() => WorldEnvironmentNetwork.RemoteAuthority = false;

        protected override void OnUpdate()
        {
            var server = ClientServerBootstrap.ServerWorld;
            WorldEnvironmentNetwork.RemoteAuthority = server == null || !server.IsCreated;
            if (m_States.IsEmpty) return;
            using var states = m_States.ToComponentDataArray<WorldStateRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_States);
            foreach (WorldStateRpc rpc in states)
            {
                Last = rpc.State;
                Applied++;
                if (Sink != null) Sink(Last); else WorldEnvironmentNetwork.ApplyRemoteState(Last);
            }
        }
    }
}
