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

    /// <summary>
    /// Server: how many move clicks a player may make (owner, 30 Oct 2026: 10 a second, the request limit "move" of the server's settings).
    /// <see cref="PlayerServerPlanSystem"/> asks once per click before planning; a click over the limit is refused like any other.
    /// </summary>
    public interface IClickBudget
    {
        /// <summary>True when the player of <paramref name="ghost"/> may make this click now (it is counted).</summary>
        bool Allow(Entity ghost);
    }

    /// <summary>
    /// Server: a click the server took (a walk, a vault, a run over the water, a wall walk). Moving ends a channelled buff such as
    /// Heavenly Serenity (NGO: <c>PointAndClickPlayer.ServerMoveTo</c>); filled by the game's skills (feel test item 5).
    /// </summary>
    public interface IClickTaken
    {
        void Taken(Entity ghost);
    }
}
