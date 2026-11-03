using System;
using System.Collections.Generic;
using KryticalNova.Core;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities
{
    /// <summary>The zone music's state (M7.2): NGO's <c>MusicConductor.netState</c>, field for field (~14 bytes). Only a GM changes it.</summary>
    public struct MusicStateRpc : IRpcCommand
    {
        public double Epoch;
        public float Nudge;
        public short Forced;

        public static MusicStateRpc Of(in MusicSync s) => new MusicStateRpc { Epoch = s.Epoch, Forced = s.Forced, Nudge = s.Nudge };
        public MusicSync State => new MusicSync { Epoch = Epoch, Forced = Forced, Nudge = Nudge };
    }

    /// <summary>
    /// Server: sends the music state to every player in the game as they come in and whenever it changed since they last got it (NGO: the
    /// network variable's first sync and its changes), so every machine counts the same beats on the same clock. Looked at four times a second
    /// of the world's clock.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MusicStateServerSystem : SystemBase
    {
        /// <summary>Where the state is read (the scene's conductor through <see cref="MusicStateLink"/>; tests put their own).</summary>
        public static Func<(bool ok, MusicSync state)> Source;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Source = null;

        public float Interval = 0.25f;
        readonly Dictionary<Entity, MusicSync> m_Sent = new Dictionary<Entity, MusicSync>();
        readonly List<Entity> m_Gone = new List<Entity>();
        EntityQuery m_InGame;
        double m_Next;

        /// <summary>States sent (tests).</summary>
        public int Sends { get; private set; }

        protected override void OnCreate() =>
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());

        protected override void OnUpdate()
        {
            double now = SystemAPI.Time.ElapsedTime;   // the world's clock: a test world ticks faster than real time (7.1)
            if (now < m_Next) return;
            m_Next = now + Interval;
            MusicSync state = default;
            bool ok = Source != null ? Read(Source, out state) : MusicStateLink.Current != null && MusicStateLink.Current.TryRead(out state);
            if (!ok) return;
            var em = EntityManager;
            m_Gone.Clear();
            foreach (Entity c in m_Sent.Keys) if (!em.Exists(c)) m_Gone.Add(c);
            foreach (Entity c in m_Gone) m_Sent.Remove(c);
            using var connections = m_InGame.ToEntityArray(Allocator.Temp);
            foreach (Entity connection in connections)
            {
                if (m_Sent.TryGetValue(connection, out MusicSync had) && had.Same(state)) continue;
                m_Sent[connection] = state;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, MusicStateRpc.Of(state));
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                Sends++;
            }
        }

        static bool Read(Func<(bool ok, MusicSync state)> source, out MusicSync state)
        {
            (bool ok, MusicSync s) = source();
            state = s;
            return ok;
        }
    }

    /// <summary>
    /// Client: takes the server's music state into the scene's conductor (as an NGO client took the network variable) and, with no server in
    /// this process, leaves changing the music to the server's GMs.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class MusicStateClientSystem : SystemBase
    {
        /// <summary>Where a received state goes (the scene's conductor; tests put their own).</summary>
        public static Action<MusicSync> Sink;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Sink = null;

        EntityQuery m_States;

        /// <summary>States taken, and the last one (tests).</summary>
        public int Applied { get; private set; }
        public MusicSync Last { get; private set; }

        protected override void OnCreate() =>
            m_States = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<MusicStateRpc>());

        protected override void OnDestroy() => MusicStateLink.RemoteAuthority = false;

        protected override void OnUpdate()
        {
            var server = ClientServerBootstrap.ServerWorld;
            MusicStateLink.RemoteAuthority = server == null || !server.IsCreated;
            if (m_States.IsEmpty) return;
            using var states = m_States.ToComponentDataArray<MusicStateRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_States);
            foreach (MusicStateRpc rpc in states)
            {
                Last = rpc.State;
                Applied++;
                if (Sink != null) Sink(Last); else MusicStateLink.Current?.Apply(Last);
            }
        }
    }
}
