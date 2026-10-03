using System;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Owns learned ranks and persistent selection. Skill selection is separate from paid destination commands.</summary>
    [DefaultExecutionOrder(InitializationOrder)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacterProgression), typeof(PlayerResources))]
    public sealed class PlayerMovementSkills : MonoBehaviour
    {
        private const int InitializationOrder = -100;
        [SerializeField] private MovementSettings movementSettings;
        [SerializeField] private MovementSkillData runSkill;
        [SerializeField] private MovementSkillData jumpSkill;
        private PlayerCharacterProgression progression;
        private PlayerResources resources;
        private int runRank;
        private int jumpRank;
        private MovementSkillId selectedSkill;
        public event Action Changed;
        public MovementSettings MovementSettings => movementSettings;
        public MovementSkillId SelectedSkill => selectedSkill;
        public bool IsRunning => selectedSkill == MovementSkillId.Run && runRank > 0;
        public bool IsJumpSelected => selectedSkill == MovementSkillId.Jump && jumpRank > 0;
        public float CurrentSpeed => BaseSpeed * GmSpeedMultiplier;
        private float BaseSpeed => movementSettings == null ? 0f : IsRunning && runSkill != null && runSkill.TryGetRank(runRank, out var rank)
            ? Mathf.Max(movementSettings.walkSpeed, rank.runSpeed) : movementSettings.walkSpeed;
        // GM speed only affects the local (owning) player, never other players simulated on a host.
        private float GmSpeedMultiplier => resources != null && (!resources.IsSpawned || resources.IsOwner)
            ? Mathf.Max(0.1f, KryticalNova.Core.GmState.SpeedMultiplier) : 1f;

        private void Awake()
        {
            progression = GetComponent<PlayerCharacterProgression>();
            resources = GetComponent<PlayerResources>();
            if (movementSettings == null || runSkill == null || jumpSkill == null
                || runSkill.skillId != MovementSkillId.Run || jumpSkill.skillId != MovementSkillId.Jump)
            {
                Debug.LogError("PlayerMovementSkills needs movement settings and valid Run/Jump skill assets.", this);
                enabled = false;
            }
        }

        /// <summary>Returns the shared definition for a known movement skill.</summary>
        public MovementSkillData GetDefinition(MovementSkillId skill)
        {
            return skill switch { MovementSkillId.Run => runSkill, MovementSkillId.Jump => jumpSkill, _ => null };
        }

        /// <summary>Returns the learned rank; zero means the skill has not been purchased.</summary>
        public int GetRank(MovementSkillId skill) => skill switch
        {
            MovementSkillId.Run => runRank, MovementSkillId.Jump => jumpRank, _ => 0
        };

        /// <summary>Reports whether a learned skill can be assigned or selected, independent of Chi affordability.</summary>
        public bool CanUse(MovementSkillId skill) => isActiveAndEnabled && GetDefinition(skill) != null && GetRank(skill) > 0;

        /// <summary>Gets the active rank's effect and resource cost.</summary>
        public bool TryGetLearnedRank(MovementSkillId skill, out MovementSkillRank rank)
        {
            rank = default;
            MovementSkillData definition = GetDefinition(skill);
            return CanUse(skill) && definition.TryGetRank(GetRank(skill), out rank);
        }

        /// <summary>Checks resource availability without charging. Invalid targets must be rejected before spending.</summary>
        public bool CanPayForCommand(MovementSkillId skill, out string reason)
        {
            reason = string.Empty;
            if (!TryGetLearnedRank(skill, out var rank)) { reason = "Learn this skill first."; return false; }
            if (resources == null || !resources.IsAlive) { reason = "You need HP to use skills."; return false; }
            if (!resources.CanSpendChi(rank.chiCost)) { reason = $"Not enough Chi. {GetDefinition(skill).displayName} needs {rank.chiCost:0.#} Chi per destination."; return false; }
            return true;
        }

        /// <summary>Charges the learned rank cost once for a validated command.</summary>
        public bool TryPayForCommand(MovementSkillId skill, out float paidCost, out string reason)
        {
            paidCost = 0f;
            if (!CanPayForCommand(skill, out reason) || !TryGetLearnedRank(skill, out var rank)) return false;
            if (!resources.TrySpendChi(rank.chiCost)) { reason = "Not enough Chi."; return false; }
            paidCost = rank.chiCost;
            return true;
        }

        /// <summary>Validates rank cap, character-level gate and skill-point cost without spending anything.</summary>
        public bool CanUpgrade(MovementSkillId skill, out string reason)
        {
            reason = string.Empty;
            if (!isActiveAndEnabled || progression == null) { reason = "Skills unavailable"; return false; }
            MovementSkillData definition = GetDefinition(skill);
            if (definition == null) { reason = "Unknown skill"; return false; }
            if (!definition.TryGetRank(GetRank(skill) + 1, out var next)) { reason = "Maximum rank reached"; return false; }
            if (progression.CurrentLevel < next.requiredCharacterLevel) { reason = $"Requires character level {next.requiredCharacterLevel}"; return false; }
            if (progression.SkillPoints < next.skillPointCost) { reason = $"Requires {next.skillPointCost} skill point(s)"; return false; }
            return true;
        }

        /// <summary>Purchases one Run or Jump rank after validating all requirements.</summary>
        public bool TryUpgrade(MovementSkillId skill)
        {
            return TryUpgradeMultiple(skill, 1) > 0;
        }

        /// <summary>Purchases up to maxCount Run or Jump ranks after validating all requirements.</summary>
        public int TryUpgradeMultiple(MovementSkillId skill, int maxCount)
        {
            if (maxCount <= 0 || !isActiveAndEnabled || progression == null) return 0;
            MovementSkillData definition = GetDefinition(skill);
            if (definition == null) return 0;

            int upgraded = 0;
            while (upgraded < maxCount)
            {
                int nextRank = GetRank(skill) + 1;
                if (!definition.TryGetRank(nextRank, out var next))
                    break;
                if (progression.CurrentLevel < next.requiredCharacterLevel)
                    break;
                if (!progression.TrySpendSkillPoints(next.skillPointCost))
                    break;

                if (skill == MovementSkillId.Run) runRank++;
                else if (skill == MovementSkillId.Jump) jumpRank++;
                upgraded++;
            }

            if (upgraded > 0)
            {
                Changed?.Invoke();
            }
            return upgraded;
        }

        /// <summary>GM: learns Run and Jump at their top ranks, ignoring level and skill points.</summary>
        public void GmSetMaxRanks()
        {
            if (!isActiveAndEnabled) return;
            runRank = runSkill.MaxRank;
            jumpRank = jumpSkill.MaxRank;
            Changed?.Invoke();
        }

        /// <summary>Selects a learned skill persistently. Selecting it again returns to walking; no Chi is charged here.</summary>
        public bool ToggleSelection(MovementSkillId skill)
        {
            if (!CanUse(skill) || resources == null || !resources.IsAlive) return false;
            selectedSkill = selectedSkill == skill ? MovementSkillId.None : skill;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Returns to ordinary walking while preserving any valid ground destination.</summary>
        public void SelectWalk()
        {
            if (selectedSkill == MovementSkillId.None) return;
            selectedSkill = MovementSkillId.None;
            Changed?.Invoke();
        }
    }
}
