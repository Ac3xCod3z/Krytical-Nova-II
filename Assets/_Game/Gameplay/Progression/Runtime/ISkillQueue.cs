using System;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// Holds one attack skill the player asked for when it could not be cast yet, and casts it as soon as it can be:
    /// after walking into reach, or when the swing under way is over. Implemented by the combat layer, so
    /// <see cref="PlayerCombatSkills"/> never references combat code.
    /// </summary>
    public interface ISkillQueue
    {
        /// <summary>The skill waiting to be cast on this machine's own player, or null.</summary>
        SkillDefinitionSO Queued { get; }

        /// <summary>Raised on the owner's machine when a skill is taken into the queue or leaves it.</summary>
        event Action Changed;

        /// <summary>
        /// Owner: a press was refused because the target is out of reach (<paramref name="outOfReach"/>) or because
        /// the caster is busy. True when the queue takes it; false leaves the press refused as before.
        /// </summary>
        bool Offer(SkillDefinitionSO skill, Transform target, bool outOfReach);

        /// <summary>Owner: lets go of whatever is waiting.</summary>
        void Clear();
    }
}
