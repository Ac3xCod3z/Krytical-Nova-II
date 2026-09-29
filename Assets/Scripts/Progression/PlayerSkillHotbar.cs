using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KryticalNova.Progression
{
    public enum HotbarItemType
    {
        None,
        Movement,
        CombatSkill
    }

    [Serializable]
    public struct HotbarSlotEntry
    {
        public HotbarItemType itemType;
        public MovementSkillId movementSkill;
        public SkillDefinitionSO combatSkill;

        public bool IsEmpty => itemType == HotbarItemType.None;
    }

    /// <summary>Owns ten assignable skill slots and routes their Input System shortcuts for movement and combat skills.</summary>
    [DefaultExecutionOrder(InputExecutionOrder)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInput), typeof(PlayerMovementSkills))]
    public sealed class PlayerSkillHotbar : MonoBehaviour
    {
        public const int SlotCount = 10;
        private const int InputExecutionOrder = -50;
        private const string CommandMapName = "SkillCommands";
        private const string GameplayMapName = "PointAndClick";
        private const string SlotActionPrefix = "Slot";
        private const string CharacterActionName = "CharacterWindow";
        private const string SkillsActionName = "SkillsWindow";
        private const string CancelActionName = "CancelOrbit";
        private readonly HotbarSlotEntry[] slots = new HotbarSlotEntry[SlotCount];
        private readonly InputAction[] slotActions = new InputAction[SlotCount];
        private PlayerMovementSkills skills;
        private PlayerCombatSkills combatSkills;
        private PlayerInput playerInput;
        private InputActionMap commandMap;
        private InputActionMap gameplayMap;
        private InputAction characterAction;
        private InputAction skillsAction;
        private InputAction cancelAction;
        public event Action Changed;
        public event Action CharacterWindowRequested;
        public event Action SkillsWindowRequested;
        public event Action CancelRequested;
        public bool InputSuppressed { get; set; }

        private void Awake()
        {
            skills = GetComponent<PlayerMovementSkills>();
            combatSkills = GetComponent<PlayerCombatSkills>();
            playerInput = GetComponent<PlayerInput>();
        }

        private void Start()
        {
            commandMap = playerInput.actions.FindActionMap(CommandMapName, true);
            gameplayMap = playerInput.actions.FindActionMap(GameplayMapName, true);
            for (int index = 0; index < SlotCount; index++)
                slotActions[index] = commandMap.FindAction(SlotActionPrefix + (index + 1), true);
            characterAction = commandMap.FindAction(CharacterActionName, true);
            skillsAction = commandMap.FindAction(SkillsActionName, true);
            cancelAction = gameplayMap.FindAction(CancelActionName, true);
            commandMap.Enable();
        }

        private void OnEnable() => commandMap?.Enable();
        private void OnDisable() => commandMap?.Disable();

        private void Update()
        {
            if (commandMap == null || !commandMap.enabled || !gameplayMap.enabled
                || !playerInput.inputIsActive || !Application.isFocused || Time.timeScale <= 0f) return;
            if (cancelAction.WasPressedThisFrame()) { CancelRequested?.Invoke(); return; }
            if (InputSuppressed) return;
            if (characterAction.WasPressedThisFrame()) CharacterWindowRequested?.Invoke();
            if (skillsAction.WasPressedThisFrame()) SkillsWindowRequested?.Invoke();
            for (int index = 0; index < SlotCount; index++)
                if (slotActions[index].WasPressedThisFrame()) { ActivateSlot(index); break; }
        }

        /// <summary>Reads the movement skill in a zero-based hotbar slot for backwards compatibility.</summary>
        public MovementSkillId GetSlot(int index) => IsValidSlot(index) && slots[index].itemType == HotbarItemType.Movement
            ? slots[index].movementSkill : MovementSkillId.None;

        /// <summary>Reads the full entry in a zero-based hotbar slot.</summary>
        public HotbarSlotEntry GetSlotEntry(int index) => IsValidSlot(index) ? slots[index] : default;

        /// <summary>Assigns a learned movement skill to a hotbar slot.</summary>
        public bool AssignSkill(int index, MovementSkillId skill)
        {
            if (!IsValidSlot(index) || !skills.CanUse(skill)) return false;
            slots[index] = new HotbarSlotEntry
            {
                itemType = HotbarItemType.Movement,
                movementSkill = skill,
                combatSkill = null
            };
            Changed?.Invoke();
            return true;
        }

        /// <summary>Assigns a combat / buff skill to a hotbar slot.</summary>
        public bool AssignSkill(int index, SkillDefinitionSO skill)
        {
            if (!IsValidSlot(index) || skill == null) return false;
            if (combatSkills == null) combatSkills = GetComponent<PlayerCombatSkills>();
            if (combatSkills != null && !combatSkills.IsLearned(skill)) return false;

            slots[index] = new HotbarSlotEntry
            {
                itemType = HotbarItemType.CombatSkill,
                movementSkill = MovementSkillId.None,
                combatSkill = skill
            };
            Changed?.Invoke();
            return true;
        }

        /// <summary>Moves a hotbar assignment to another slot, swapping occupied destinations.</summary>
        public void SwapSlots(int source, int destination)
        {
            if (!IsValidSlot(source) || !IsValidSlot(destination) || source == destination) return;
            (slots[source], slots[destination]) = (slots[destination], slots[source]);
            Changed?.Invoke();
        }

        /// <summary>Clears one assignment; removing the last slot for the selected skill returns to walking.</summary>
        public void ClearSlot(int index)
        {
            if (!IsValidSlot(index)) return;
            HotbarSlotEntry removed = slots[index];
            slots[index] = default;
            if (removed.itemType == HotbarItemType.Movement && skills.SelectedSkill == removed.movementSkill)
            {
                bool stillAssigned = false;
                for (int i = 0; i < SlotCount; i++)
                {
                    if (slots[i].itemType == HotbarItemType.Movement && slots[i].movementSkill == removed.movementSkill)
                    {
                        stillAssigned = true;
                        break;
                    }
                }
                if (!stillAssigned) skills.SelectWalk();
            }
            Changed?.Invoke();
        }

        /// <summary>Activates a slot using the same selection behavior for pointer clicks and number keys.</summary>
        public bool ActivateSlot(int index)
        {
            if (!isActiveAndEnabled || InputSuppressed || !IsValidSlot(index)) return false;

            var entry = slots[index];
            if (entry.itemType == HotbarItemType.Movement)
            {
                return skills.ToggleSelection(entry.movementSkill);
            }
            else if (entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill != null)
            {
                if (combatSkills == null) combatSkills = GetComponent<PlayerCombatSkills>();
                if (combatSkills != null)
                {
                    return combatSkills.TryCastSkill(entry.combatSkill, out _);
                }
            }
            return false;
        }

        private static bool IsValidSlot(int index) => index >= 0 && index < SlotCount;
    }
}
