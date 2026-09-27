using System;
using UnityEngine;

namespace KryticalNova.Progression
{
    public enum MovementSkillId { None, Run, Jump }

    [Serializable]
    public struct MovementSkillRank
    {
        [Min(1)] public int requiredCharacterLevel;
        [Min(1)] public int skillPointCost;
        [Tooltip("Chi spent once per accepted destination command, never per second or hotbar selection.")]
        [Min(0f)] public float chiCost;
        [Tooltip("Absolute run speed in world units per second.")]
        [Min(0f)] public float runSpeed;
        [Tooltip("Maximum upward landing height difference.")]
        [Min(0f)] public float jumpHeight;
        [Tooltip("Maximum horizontal landing distance.")]
        [Min(0f)] public float jumpDistance;
    }

    /// <summary>Shared skill identity and one-based rank table. Current rank belongs to PlayerMovementSkills.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Movement Skill")]
    public sealed class MovementSkillData : ScriptableObject
    {
        public MovementSkillId skillId = MovementSkillId.Run;
        public string displayName = "Run";
        [TextArea] public string description = "Toggle running without changing your destination.";
        public MovementSkillRank[] ranks = Array.Empty<MovementSkillRank>();
        public int MaxRank => ranks?.Length ?? 0;

        /// <summary>Gets a one-based rank. Rank zero is unlearned, never a free rank-one skill.</summary>
        public bool TryGetRank(int rank, out MovementSkillRank value)
        {
            value = default;
            if (ranks == null || rank < 1 || rank > ranks.Length) return false;
            value = ranks[rank - 1];
            return true;
        }

        private void OnValidate()
        {
            if (ranks == null) return;
            for (int index = 0; index < ranks.Length; index++)
            {
                ranks[index].requiredCharacterLevel = Mathf.Max(1, ranks[index].requiredCharacterLevel);
                ranks[index].skillPointCost = Mathf.Max(1, ranks[index].skillPointCost);
                ranks[index].chiCost = Mathf.Max(0f, ranks[index].chiCost);
                ranks[index].runSpeed = Mathf.Max(0f, ranks[index].runSpeed);
                ranks[index].jumpHeight = Mathf.Max(0f, ranks[index].jumpHeight);
                ranks[index].jumpDistance = Mathf.Max(0f, ranks[index].jumpDistance);
            }
        }
    }
}
