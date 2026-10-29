using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities
{
    /// <summary>Server: counts down <see cref="KnPendingDisconnect"/> and then closes the connection (the refusal RPC has left by then).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class KnRefuseDisconnectSystem : SystemBase
    {
        private EntityQuery m_Pending;

        protected override void OnCreate()
        {
            m_Pending = GetEntityQuery(ComponentType.ReadWrite<KnPendingDisconnect>());
        }

        protected override void OnUpdate()
        {
            if (m_Pending.IsEmpty) return;
            var entities = m_Pending.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var pending = EntityManager.GetComponentData<KnPendingDisconnect>(entities[i]);
                if (--pending.TicksLeft > 0)
                {
                    EntityManager.SetComponentData(entities[i], pending);
                    continue;
                }
                EntityManager.RemoveComponent<KnPendingDisconnect>(entities[i]);
                EntityManager.AddComponentData(entities[i], new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ApprovalFailure });
            }
            entities.Dispose();
        }
    }

    /// <summary>Client: keeps the server's <see cref="JoinRefusedRpc"/> as the <see cref="KnJoinRefusal"/> singleton for the join UI.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class KnJoinRefusedClientSystem : SystemBase
    {
        private EntityQuery m_Refused;
        private EntityQuery m_Stored;

        protected override void OnCreate()
        {
            m_Refused = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<JoinRefusedRpc>());
            m_Stored = GetEntityQuery(ComponentType.ReadWrite<KnJoinRefusal>());
        }

        protected override void OnUpdate()
        {
            if (m_Refused.IsEmpty) return;
            var refusals = m_Refused.ToComponentDataArray<JoinRefusedRpc>(Allocator.Temp);
            var last = refusals[refusals.Length - 1];
            refusals.Dispose();
            EntityManager.DestroyEntity(m_Refused);
            var value = new KnJoinRefusal { Reason = last.Reason, Detail = last.Detail };
            if (m_Stored.IsEmpty) EntityManager.CreateSingleton(value);
            else m_Stored.SetSingleton(value);
        }
    }
}
