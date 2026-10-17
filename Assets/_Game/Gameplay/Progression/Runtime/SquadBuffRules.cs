using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// When a buff a player casts is given to their Squad as well (owner, 16 Oct 2026): a Stance (offense, defense,
    /// or Evasion and Move Speed) of rank 20 or more reaches the Squad members within 20 m (both numbers in the Squad
    /// settings). The caster always gets it, at any rank. The same buff never stacks: given again, by anyone, it
    /// restarts its timer and keeps the higher of the two ranks.
    /// </summary>
    public static class SquadBuffRules
    {
        /// <summary>True when this cast of <paramref name="skill"/> at <paramref name="rank"/> reaches the Squad.</summary>
        public static bool Shares(SkillDefinitionSO skill, int rank, int fromRank) =>
            skill != null && skill.sharesWithSquad && !skill.IsAttack && rank >= fromRank;

        /// <summary>True when a Squad member at <paramref name="member"/> is within reach of the caster.</summary>
        public static bool InReach(Vector3 caster, Vector3 member, float radius) =>
            (member - caster).sqrMagnitude <= radius * radius;

        /// <summary>The rank a buff has after it is given again: never lower than the one it had.</summary>
        public static int RankAfterRecast(bool alreadyActive, int existingRank, int incomingRank) =>
            alreadyActive ? Mathf.Max(existingRank, incomingRank) : incomingRank;

        /// <summary>The line every shared buff's tooltip carries, with the Squad's numbers.</summary>
        public static string TooltipLine(SkillDefinitionSO skill, int rank, int fromRank, float radius)
        {
            if (skill == null || !skill.sharesWithSquad) return string.Empty;
            string state = rank >= fromRank ? "Now also given" : $"From rank {fromRank}, also given";
            return $"{state} to Squad members within {radius:0} m. The same Stance does not stack: cast again, by you or a Squad member, it restarts its timer.";
        }
    }

    /// <summary>
    /// The Squad around a player, as the skill system sees it (implemented by the Squad component, which the skill
    /// system does not name): who a shared buff reaches, and from what rank.
    /// </summary>
    public interface ISquadBuffTargets
    {
        /// <summary>The rank from which a shared buff reaches the Squad.</summary>
        int BuffShareRank { get; }
        /// <summary>Metres from the caster within which a Squad member is given it.</summary>
        float BuffShareRadius { get; }
        /// <summary>What the buff bar says it came from: this player's name.</summary>
        string BuffGiverName { get; }
        /// <summary>Server: the other living Squad members in this map within reach of this player. None outside a Squad.</summary>
        void ServerGetBuffTargets(System.Collections.Generic.List<PlayerCombatSkills> into);
    }
}
