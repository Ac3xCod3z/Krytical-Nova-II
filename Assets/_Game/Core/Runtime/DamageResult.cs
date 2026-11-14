namespace KryticalNova.Core
{
    /// <summary>
    /// What one strike did: the damage dealt, whether it was critical, evaded or reflected, and the parts it was made of.
    /// It lives in Core because the combat feedback sink (<see cref="ICombatFeedbackSink"/>) carries it to the screen.
    /// </summary>
    public struct DamageResult
    {
        public float DamageDealt;
        public bool IsCritical;
        public bool IsEvaded;
        /// <summary>Build 17, item 8: the defender blocked (the hit landed for less).</summary>
        public bool IsBlocked;
        public bool IsReflected;
        public float ReflectedDamage;
        public bool ShieldShattered;
        public float RawPhysicalDamage;
        public float MitigatedPhysicalDamage;
        public float NetElementalDamage;
        public float HitChance;
        public bool BypassesShield;
        /// <summary>
        /// Day list item 19, phase 3: a Mount Companion's strike (DPS stance) is dealt in its owner's name (kill credit and the tag stay
        /// the owner's) but adds no threat to the Hostile it hits: a companion never draws aggro.
        /// </summary>
        public bool NoThreat;
    }
}
