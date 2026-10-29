using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Client: as soon as a connection exists that has no NetworkId yet (Handshake or Approval), sends the
    /// <see cref="KnJoinPayload"/> as <see cref="KnApprovalRequest"/> chunks, once. With no payload it sends nothing and the
    /// server's timeout ends the attempt.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class KnClientApprovalSendSystem : SystemBase
    {
        private EntityQuery m_Waiting;
        private EntityQuery m_Payload;
        private readonly List<string> m_Chunks = new List<string>();

        protected override void OnCreate()
        {
            m_Waiting = GetEntityQuery(ComponentType.ReadOnly<NetworkStreamConnection>(),
                ComponentType.Exclude<NetworkId>(), ComponentType.Exclude<KnApprovalSent>());
            m_Payload = GetEntityQuery(ComponentType.ReadOnly<KnJoinPayload>());
        }

        protected override void OnUpdate()
        {
            if (m_Waiting.IsEmpty || !m_Payload.TryGetSingleton(out KnJoinPayload payload)) return;
            var connections = m_Waiting.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
            {
                Entity connection = connections[i];
                var state = EntityManager.GetComponentData<NetworkStreamConnection>(connection).CurrentState;
                if (state != ConnectionState.State.Handshake && state != ConnectionState.State.Approval) continue;
                EntityManager.AddComponent<KnApprovalSent>(connection);

                if (!KnApprovalChunks.TrySplit(payload.Text.ToString(), KnApprovalLimits.Default.MaxChunks, m_Chunks))
                {
                    Debug.LogError("[KnClient] the join payload is empty, not ASCII or too long; giving up on this connection.");
                    EntityManager.AddComponentData(connection, new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ApprovalFailure });
                    continue;
                }
                for (int k = 0; k < m_Chunks.Count; k++)
                {
                    Entity rpc = EntityManager.CreateEntity(typeof(KnApprovalRequest), typeof(SendRpcCommandRequest));
                    EntityManager.SetComponentData(rpc, new KnApprovalRequest { Index = k, Count = m_Chunks.Count, Data = new FixedString512Bytes(m_Chunks[k]) });
                    EntityManager.SetComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                }
            }
            connections.Dispose();
        }
    }
}
