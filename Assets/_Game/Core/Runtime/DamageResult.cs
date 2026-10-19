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
        public bool IsReflected;
        public float ReflectedDamage;
        public bool ShieldShattered;
        public float RawPhysicalDamage;
        public float MitigatedPhysicalDamage;
        public float NetElementalDamage;
        public float HitChance;
        public bool BypassesShield;
    }
}
