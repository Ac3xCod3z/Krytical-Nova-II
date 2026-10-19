using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.Progression
{
    public interface IDamageable
    {
        string EntityName { get; }
        bool IsAlive { get; }
        float CurrentHealth { get; }
        float MaxHealth { get; }
        Transform Transform { get; }
        CharacterCombatStats CombatStats { get; }
        DamageResult TakeDamage(DamageResult damage, CharacterCombatStats attacker);
    }
}
