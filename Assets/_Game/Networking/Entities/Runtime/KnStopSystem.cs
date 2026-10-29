using System;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Server, main thread: sees a stop request (SIGTERM, the console closing, <c>POST /stop</c>: <see cref="KnStopRequests"/>)
    /// and runs <see cref="KnStopMachine"/> on this world. Closing the door is the <see cref="KnServerStopping"/> singleton,
    /// which <see cref="KnApprovalAssembler"/> answers with a ServerStopping refusal; saving is Core's
    /// <see cref="ServerShutdown"/>; the quit is <c>Application.Quit(0)</c>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class KnStopSystem : SystemBase, IKnStopActions
    {
        /// <summary>Tests replace the quit; null = <c>Application.Quit</c>.</summary>
        public static Action<int> QuitOverride;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => QuitOverride = null;

        public KnStopMachine Machine { get; private set; }

        private EntityQuery m_Connections;
        private EntityQuery m_Stopping;

        protected override void OnCreate()
        {
            Machine = new KnStopMachine(this);
            m_Connections = GetEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>(), ComponentType.Exclude<NetworkStreamRequestDisconnect>());
            m_Stopping = GetEntityQuery(ComponentType.ReadOnly<KnServerStopping>());
        }

        protected override void OnUpdate()
        {
            if (KnStopRequests.TryTake(out KnStopSource source)) Machine.Request(Describe(source));
            Machine.Step();
        }

        private static string Describe(KnStopSource source)
        {
            switch (source)
            {
                case KnStopSource.Signal: return "The server was asked to stop (SIGTERM).";
                case KnStopSource.Console: return "The server was closed.";
                case KnStopSource.Http: return "The server was stopped by its service manager.";
                default: return "The server was asked to stop.";
            }
        }

        void IKnStopActions.CloseDoor()
        {
            if (m_Stopping.IsEmpty) EntityManager.CreateEntity(typeof(KnServerStopping));
        }

        void IKnStopActions.SaveAll()
        {
            if (!ServerShutdown.SaveAll()) Debug.LogWarning("[KnServer] Nothing is registered to save (ServerShutdown.Handler is empty): no save was written.");
        }

        void IKnStopActions.DisconnectAll()
        {
            if (m_Connections.IsEmpty) return;
            var connections = m_Connections.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
                EntityManager.AddComponentData(connections[i], new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ConnectionClose });
            connections.Dispose();
        }

        void IKnStopActions.Quit(int exitCode)
        {
            KnSignals.NotifyStopped(); // lets a closing Windows console go
            if (QuitOverride != null) QuitOverride(exitCode);
            else Application.Quit(exitCode);
        }

        void IKnStopActions.Log(string line) => Debug.Log(line);
    }
}
