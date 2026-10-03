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

    /// <summary>
    /// Owns the assignable skill slots (number row 1–0 plus Q/W/E/R) and routes their Input System shortcuts for
    /// movement and combat skills. Holding a key repeats skills flagged hold-to-repeat and charges charged skills.
    /// </summary>
    [DefaultExecutionOrder(InputExecutionOrder)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInput), typeof(PlayerMovementSkills))]
    public sealed class PlayerSkillHotbar : MonoBehaviour
    {
        /// <summary>Slots 0–9 are keys 1–0; the rest are Q, W, E, R.</summary>
        public const int NumberSlotCount = 10;
        public const int SlotCount = 14;
        private const int NoSlot = -1;
        private const int InputExecutionOrder = -50;
        private const string CommandMapName = "SkillCommands";
        private const string GameplayMapName = "PointAndClick";
        private static readonly string[] SlotActionNames =
            { "Slot1", "Slot2", "Slot3", "Slot4", "Slot5", "Slot6", "Slot7", "Slot8", "Slot9", "Slot10", "SlotQ", "SlotW", "SlotE", "SlotR" };
        private static readonly string[] SlotKeyLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "Q", "W", "E", "R" };
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
        /// <summary>The local player began holding a charged skill's key.</summary>
        public event Action<SkillDefinitionSO> ChargeStarted;
        /// <summary>The hold ended; true when the charge filled and the skill was cast.</summary>
        public event Action<SkillDefinitionSO, bool> ChargeEnded;
        public bool InputSuppressed { get; set; }

        private int chargingSlot = NoSlot;
        private SkillDefinitionSO chargingSkill;
        private float chargeStartTime;

        /// <summary>The slot whose charged skill is being held, or -1.</summary>
        public int ChargingSlot => chargingSlot;
        public float ChargeProgress => chargingSkill == null ? 0f
            : Mathf.Clamp01((Time.time - chargeStartTime) / Mathf.Max(0.01f, chargingSkill.chargeTime));

        /// <summary>The keyboard key shown for a zero-based slot.</summary>
        public static string GetKeyLabel(int index) => IsValidSlot(index) ? SlotKeyLabels[index] : string.Empty;

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
                slotActions[index] = commandMap.FindAction(SlotActionNames[index], true);
            characterAction = commandMap.FindAction(CharacterActionName, true);
            skillsAction = commandMap.FindAction(SkillsActionName, true);
            cancelAction = gameplayMap.FindAction(CancelActionName, true);
            commandMap.Enable();
        }

        private void OnEnable() => commandMap?.Enable();
        private void OnDisable()
        {
            EndCharge(false);
            commandMap?.Disable();
        }

        private void Update()
        {
            if (commandMap == null || !commandMap.enabled || !gameplayMap.enabled
                || !playerInput.inputIsActive || !Application.isFocused || Time.timeScale <= 0f)
            {
                EndCharge(false);
                return;
            }
            if (cancelAction.WasPressedThisFrame()) { EndCharge(false); CancelRequested?.Invoke(); return; }
            if (InputSuppressed) { EndCharge(false); return; }
            if (characterAction.WasPressedThisFrame()) CharacterWindowRequested?.Invoke();
            if (skillsAction.WasPressedThisFrame()) SkillsWindowRequested?.Invoke();

            if (chargingSlot != NoSlot) { UpdateCharge(); return; }

            for (int index = 0; index < SlotCount; index++)
            {
                if (slotActions[index].WasPressedThisFrame()) { PressSlot(index); break; }
                if (slotActions[index].IsPressed()) RepeatSlot(index);
            }
        }

        /// <summary>Key down: charged skills start filling, everything else activates at once.</summary>
        private void PressSlot(int index)
        {
            HotbarSlotEntry entry = slots[index];
            if (entry.itemType != HotbarItemType.CombatSkill || entry.combatSkill == null || !entry.combatSkill.IsCharged)
            {
                ActivateSlot(index);
                return;
            }

            if (combatSkills == null) combatSkills = GetComponent<PlayerCombatSkills>();
            if (combatSkills == null) return;
            if (!combatSkills.CanCast(entry.combatSkill, out string reason))
            {
                combatSkills.Announce(reason);
                return;
            }
            chargingSlot = index;
            chargingSkill = entry.combatSkill;
            chargeStartTime = Time.time;
            ChargeStarted?.Invoke(chargingSkill);
            Changed?.Invoke();
        }

        /// <summary>Key held: skills flagged hold-to-repeat recast as soon as they are able, without error spam.</summary>
        private void RepeatSlot(int index)
        {
            HotbarSlotEntry entry = slots[index];
            if (entry.itemType != HotbarItemType.CombatSkill || entry.combatSkill == null || !entry.combatSkill.holdToRepeat) return;
            if (combatSkills == null) combatSkills = GetComponent<PlayerCombatSkills>();
            combatSkills?.TryCastSkill(entry.combatSkill, out _, quiet: true);
        }

        private void UpdateCharge()
        {
            if (!slotActions[chargingSlot].IsPressed())
            {
                string name = chargingSkill.displayName;
                EndCharge(false);
                combatSkills.Announce($"{name} released before it was charged. No Chi spent.");
                return;
            }
            if (Time.time - chargeStartTime >= chargingSkill.chargeTime) EndCharge(true);
        }

        /// <summary>Stops the current charge; when <paramref name="cast"/> is set the filled charge is spent on the skill.</summary>
        private void EndCharge(bool cast)
        {
            if (chargingSlot == NoSlot) return;
            SkillDefinitionSO skill = chargingSkill;
            chargingSlot = NoSlot;
            chargingSkill = null;
            bool completed = cast && combatSkills != null && combatSkills.TryCastSkill(skill, out _);
            ChargeEnded?.Invoke(skill, completed);
            Changed?.Invoke();
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
