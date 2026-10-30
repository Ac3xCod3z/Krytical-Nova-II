using Unity.Entities;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// Server: the price of a running click (the Run rank's Flux per destination). Filled by the game's skills, which this assembly
    /// cannot see. <see cref="PlayerServerPlanSystem"/> asks <see cref="CanPay"/> before it plans a click that asks to run and calls
    /// <see cref="Pay"/> once the plan stands; a click it cannot pay for is refused, as on the Netcode for GameObjects server.
    /// </summary>
    public interface IRunCost
    {
        /// <summary>True when the player of <paramref name="ghost"/> may run to one more destination now; else why not.</summary>
        bool CanPay(Entity ghost, out string reason);

        /// <summary>Takes the price of one running destination. Called only after <see cref="CanPay"/> said yes on the same tick.</summary>
        void Pay(Entity ghost);
    }
}
