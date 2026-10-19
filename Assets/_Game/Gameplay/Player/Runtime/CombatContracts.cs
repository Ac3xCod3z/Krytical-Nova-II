using System;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// The attack the click-to-move player starts, follows and stops, and what death cancels. <c>PlayerMeleeCombat</c>
    /// (Combat) is the one there is; the player's body names only this, so the fight sits above the body and not
    /// beside it.
    /// </summary>
    public interface IMeleeAttacker
    {
        bool IsAttacking { get; }

        /// <summary>A line for the player about the attack (out of reach, no target...).</summary>
        event Action<string> OnCombatMessage;

        void StartAttackTarget(IDamageable target);

        void CancelAttack();
    }

    /// <summary>Whether this player duels another (the duel exception of the faction rule). <c>PlayerDuel</c> (Combat) is the one there is.</summary>
    public interface IDuelState
    {
        bool IsDuellingWith(NetworkObject other);
    }

    /// <summary>
    /// Whether an attacker may strike a target (faction, duel, neutral ground). The rule is Combat's own
    /// (<c>CombatHostility.IsHostile</c>), which fills this when the game loads; empty, nothing counts as hostile.
    /// </summary>
    public static class HostilityCheck
    {
        public static Func<Transform, IDamageable, bool> Rule;

        public static bool IsHostile(Transform attacker, IDamageable target) => Rule != null && Rule(attacker, target);
    }
}
