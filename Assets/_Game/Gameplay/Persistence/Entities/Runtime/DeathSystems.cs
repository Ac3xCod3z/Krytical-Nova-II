using KryticalNova.Player;
using KryticalNova.Player.Entities.Presentation;
using Unity.Entities;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// Server (M4 step 6a): each player's server body runs NGO's death clock, the respawn countdown and the return to a spawn point
    /// (<see cref="PlayerDeathMotor.ServerBodyTick"/>). The body switches the motor off (the ghost moves it), so its timer is driven
    /// from here; the death itself is heard by the motor from the body's CombatEntity.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerDeathServerSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out PlayerDeathMotor death))
                    death.ServerBodyTick(deltaTime);
        }
    }
}
