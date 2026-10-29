using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Switches connection approval on in every world that has this assembly (server and client both need it), once, before the
    /// driver listens or connects. After this every join goes through <see cref="KnApprovalRequest"/> chunks.
    /// </summary>
#if KN_DEV_BOTS || UNITY_EDITOR
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
#else
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation | WorldSystemFilterFlags.ClientSimulation)]
#endif
    [UpdateInGroup(typeof(NetworkReceiveSystemGroup))]
    [UpdateBefore(typeof(NetworkStreamConnectSystem))]
    [UpdateBefore(typeof(NetworkStreamListenSystem))]
    public partial struct KnApprovalSetupSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NetworkStreamDriver>();
        }

        public void OnUpdate(ref SystemState state)
        {
            SystemAPI.GetSingletonRW<NetworkStreamDriver>().ValueRW.RequireConnectionApproval = true;
            state.Enabled = false;
        }
    }
}
