namespace KryticalNova.Progression
{
    /// <summary>
    /// On a target that is harder or easier to hit depending on who attacks it (a Hostile far above a player's
    /// level). Read by whatever rolls a hit and handed to <see cref="CombatCalculator"/>.
    /// </summary>
    public interface IHitChanceModifier
    {
        /// <summary>Percent points added to the attacker's chance to hit. Negative = harder to hit.</summary>
        float HitChanceModifierFor(CharacterCombatStats attacker);
    }
}
