namespace KryticalNova.GameFeel
{
    /// <summary>
    /// When a run "takes off" (<see cref="GameFeelPresenter.RunStarted"/>). Its dust and smear play for every run
    /// that starts and for every new destination chosen while running (owner, 11 Oct 2026), a moment apart at
    /// least. The kick of the view on the player's own screen is rarer: once for a run from a standstill, and again
    /// only after a quiet spell, so that clicking about quickly does not pump the view.
    /// </summary>
    public static class RunTakeOffRules
    {
        /// <summary>The dust and the smear: a run's start or a new order, unless one played less than <paramref name="repeatSeconds"/> ago.</summary>
        public static bool TakesOff(float now, float lastTakeOff, float repeatSeconds) => now - lastTakeOff >= repeatSeconds;

        /// <summary>The kick of the view, for a take-off that plays.</summary>
        /// <param name="now">The time of the run's start or of the order.</param>
        /// <param name="lastStill">When the character last stood still.</param>
        /// <param name="lastOrder">When it last set off or was ordered on.</param>
        /// <param name="lastKick">When the view was last kicked.</param>
        /// <param name="restSeconds">A run that starts within this long of standing still starts from a standstill.</param>
        /// <param name="quietSeconds">With no standstill, this long must pass with no order at all.</param>
        public static bool KicksView(float now, float lastStill, float lastOrder, float lastKick, float restSeconds, float quietSeconds)
        {
            // From a standstill: once for each time the character stood still, however many orders follow.
            bool fromRest = lastKick < lastStill && now - lastStill <= restSeconds;
            return fromRest || now - lastOrder >= quietSeconds;
        }
    }
}
