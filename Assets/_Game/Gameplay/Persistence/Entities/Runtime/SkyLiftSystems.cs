using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>TEMPORARY (owner, 9 Nov 2026): a player asks the Marshal's sky lift; <see cref="Down"/> 1 = back to town, 0 = up to Concord 040.</summary>
    public struct SkyLiftRpc : IRpcCommand
    {
        public byte Down;
    }

    /// <summary>
    /// Server: moves the asking player to the sky lift marker the way a GM teleport does (<c>gm.tp</c> on the player's own body, so the
    /// server decides: NavMesh snap, wall and water let go, one placement every client snaps to). Limited as an account call. Any
    /// player may ask while this is a preview; it is removed with the real Marshal service.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SkyLiftServerSystem : SystemBase
    {
        EntityQuery m_Requests;

        public int Rides { get; private set; }

        protected override void OnCreate() =>
            m_Requests = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<SkyLiftRpc>());

        protected override void OnUpdate()
        {
            if (m_Requests.IsEmpty) return;
            var em = EntityManager;
            using var received = m_Requests.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
            using var asks = m_Requests.ToComponentDataArray<SkyLiftRpc>(Allocator.Temp);
            em.DestroyEntity(m_Requests);
            for (int i = 0; i < asks.Length; i++)
            {
                Entity connection = received[i].SourceConnection;
                if (connection == Entity.Null || !em.Exists(connection) || !em.HasComponent<ConnectionPlayerKey>(connection)) continue;
                if (!em.HasComponent<NetworkStreamInGame>(connection)) continue;
                ConnectionPlayerKey key = em.GetComponentData<ConnectionPlayerKey>(connection);
                if (RpcBudget.Spend(key.PlayerKey, CallKind.Account, UnityEngine.Time.realtimeSinceStartup) != RpcBudget.SpendResult.Allowed) continue;
                if (PlayerGm.CommandRunner == null) continue;
                string marker = asks[i].Down != 0 ? SkyLiftLink.ReturnMarker : SkyLiftLink.ArrivalMarker;
                string answer = PlayerGm.CommandRunner(key.Serial, "gm.tp " + marker);
                Rides++;
                UnityEngine.Debug.Log($"[SkyLift] player {key.Serial}: {answer}");
            }
        }
    }

    /// <summary>Client: gives the Marshal's button its way to the server (<see cref="SkyLiftLink.Requester"/>).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SkyLiftClientSystem : SystemBase
    {
        System.Action<bool> m_Requester;

        protected override void OnCreate()
        {
            m_Requester = down =>
            {
                if (!World.IsCreated) return;
                var em = EntityManager;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new SkyLiftRpc { Down = (byte)(down ? 1 : 0) });
                em.AddComponent<SendRpcCommandRequest>(rpc);
            };
            SkyLiftLink.Requester = m_Requester;
        }

        protected override void OnDestroy()
        {
            if (SkyLiftLink.Requester == m_Requester) SkyLiftLink.Requester = null;
        }

        protected override void OnUpdate() { }
    }
}
