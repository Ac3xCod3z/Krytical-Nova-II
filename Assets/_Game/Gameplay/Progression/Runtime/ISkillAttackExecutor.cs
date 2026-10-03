using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// Carries out the parts of a skill cast that progression cannot: finding targets, dealing damage and playing
    /// the cast. Implemented by the combat layer, so <see cref="PlayerCombatSkills"/> never references combat code.
    /// </summary>
    public interface ISkillAttackExecutor
    {
        /// <summary>True while a previous cast's animation still locks the caster.</summary>
        bool IsCasting { get; }

        /// <summary>True when <paramref name="next"/> may start now even though <see cref="IsCasting"/>, cutting the current cast short.</summary>
        bool CanInterruptCast(SkillDefinitionSO next);

        /// <summary>The local player's current target for a single-target skill, or null.</summary>
        Transform ResolveTarget(SkillDefinitionSO skill);

        /// <summary>Checks target and range before any Chi is spent.</summary>
        bool CanExecute(SkillDefinitionSO skill, int rank, Transform target, out string reason);

        /// <summary>Applies the skill's damage. Runs only where combat is authoritative (server or offline).</summary>
        void Execute(SkillDefinitionSO skill, int rank, Transform target);

        /// <summary>Plays the cast animation and effects. Runs on every peer.</summary>
        void Present(SkillDefinitionSO skill, int rank, Transform target);
    }
}
