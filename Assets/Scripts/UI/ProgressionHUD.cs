using System;
using System.Collections.Generic;
using KryticalNova.Player;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>Displays progression and handles runtime skill drag/drop without editor-only drag APIs.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ProgressionHUD : MonoBehaviour
    {
        private const int AttributeCount = 4;
        private const int LeftButton = 0;
        private const int RightButton = 1;
        private const int NoSlot = -1;
        private const float DragThresholdSquared = 36f;
        private const float GhostOffset = 14f;
        private const float ProgressBarScale = 100f;
        private const float TestResourceAmount = 25f;
        private const string BlockingClass = "blocks-world";
        [SerializeField] private PlayerCharacterProgression progression;
        [SerializeField] private PlayerMovementSkills skills;
        [SerializeField] private PlayerSkillHotbar hotbar;
        [SerializeField] private PointAndClickPlayer movement;
        [SerializeField] private PlayerResources resources;
        [SerializeField] private PlayerJumpMotor jumpMotor;
        private VisualElement root;
        private VisualElement characterWindow;
        private VisualElement skillsWindow;
        private VisualElement runTile;
        private VisualElement jumpTile;
        private Label dragGhost;
        private readonly List<Action> removeUiCallbacks = new List<Action>();
        private readonly VisualElement[] slotElements = new VisualElement[PlayerSkillHotbar.SlotCount];
        private VisualElement dragSource;
        private MovementSkillId draggedSkill;
        private int draggedFromSlot = NoSlot;
        private int dragPointerId;
        private Vector2 pointerStart;
        private bool dragMoved;
        private bool initialized;
        private int suppressWorldInputFrame = -1;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            if (progression == null || skills == null || hotbar == null || movement == null || resources == null || jumpMotor == null || root == null)
            {
                Debug.LogError("ProgressionHUD needs player progression, skills, hotbar, movement and a UXML document.", this);
                return;
            }
            root.pickingMode = PickingMode.Ignore;
            characterWindow = root.Q("character-window");
            skillsWindow = root.Q("skills-window");
            runTile = root.Q("run-drag");
            jumpTile = root.Q("jump-drag");
            dragGhost = root.Q<Label>("drag-ghost");
            if (characterWindow == null || skillsWindow == null || runTile == null || jumpTile == null || dragGhost == null)
            {
                Debug.LogError("ProgressionHUD is missing its expected UXML elements.", this);
                return;
            }
            BindButton("open-character", ToggleCharacter);
            BindButton("open-skills", ToggleSkills);
            BindButton("close-character", HideCharacter);
            BindButton("close-skills", HideSkills);
            BindButton("upgrade-run", () => UpgradeSkill(MovementSkillId.Run));
            BindButton("upgrade-jump", () => UpgradeSkill(MovementSkillId.Jump));
            BindButton("grant-xp", GrantTestExperience);
            BindButton("test-damage", () => { if (Application.isEditor || Debug.isDebugBuild) resources.ApplyDamage(TestResourceAmount); });
            BindButton("test-chi", () => { if (Application.isEditor || Debug.isDebugBuild) resources.TrySpendChi(Mathf.Min(TestResourceAmount, resources.Chi)); });
            BindButton("restore-resources", () => { if (Application.isEditor || Debug.isDebugBuild) resources.RestoreAll(); });
            for (int index = 0; index < AttributeCount; index++)
            {
                CharacterAttribute attribute = (CharacterAttribute)index;
                BindButton($"allocate-{index}", () => progression.TryAllocate(attribute));
            }
            for (int index = 0; index < slotElements.Length; index++)
            {
                int slotIndex = index;
                VisualElement slot = root.Q($"slot-{index}");
                slotElements[index] = slot;
                BindPointer<PointerDownEvent>(slot, evt => OnSlotPointerDown(evt, slotIndex));
                BindDragSource(slot);
            }
            BindPointer<PointerDownEvent>(runTile, evt => OnSkillPointerDown(evt, MovementSkillId.Run, runTile));
            BindDragSource(runTile);
            BindPointer<PointerDownEvent>(jumpTile, evt => OnSkillPointerDown(evt, MovementSkillId.Jump, jumpTile));
            BindDragSource(jumpTile);
            resources.Changed += RefreshResources;
            movement.CommandMessage += Feedback;
            jumpMotor.Message += Feedback;
            progression.Changed += Refresh;
            progression.LevelGained += OnLevelGained;
            skills.Changed += Refresh;
            hotbar.Changed += Refresh;
            hotbar.CharacterWindowRequested += ToggleCharacter;
            hotbar.SkillsWindowRequested += ToggleSkills;
            hotbar.CancelRequested += CloseWindows;
            SetVisible(characterWindow, false);
            SetVisible(skillsWindow, true);
            SetVisible(root.Q("development-tools"), Application.isEditor || Debug.isDebugBuild);
            initialized = true;
            Refresh();
        }

        private void Start() => Refresh();

        private void OnDisable()
        {
            if (!initialized) return;
            CancelDrag();
            resources.Changed -= RefreshResources;
            movement.CommandMessage -= Feedback;
            jumpMotor.Message -= Feedback;
            progression.Changed -= Refresh;
            progression.LevelGained -= OnLevelGained;
            skills.Changed -= Refresh;
            hotbar.Changed -= Refresh;
            hotbar.CharacterWindowRequested -= ToggleCharacter;
            hotbar.SkillsWindowRequested -= ToggleSkills;
            hotbar.CancelRequested -= CloseWindows;
            foreach (Action removeCallback in removeUiCallbacks) removeCallback();
            removeUiCallbacks.Clear();
            initialized = false;
        }

        private void BindButton(string name, Action callback)
        {
            Button button = root.Q<Button>(name);
            button.clicked += callback;
            removeUiCallbacks.Add(() => button.clicked -= callback);
        }

        private void BindPointer<TEvent>(VisualElement element, EventCallback<TEvent> callback)
            where TEvent : EventBase<TEvent>, new()
        {
            element.RegisterCallback(callback);
            removeUiCallbacks.Add(() => element.UnregisterCallback(callback));
        }

        private void BindDragSource(VisualElement source)
        {
            // Captured pointer events must be handled on the capturing element itself.
            // Do not depend on bubbling through the skill page's ScrollView to the root.
            BindPointer<PointerMoveEvent>(source, OnPointerMove);
            BindPointer<PointerUpEvent>(source, OnPointerUp);
            BindPointer<PointerCaptureOutEvent>(source, OnCaptureLost);
            BindPointer<PointerCancelEvent>(source, OnPointerCancelled);
            source.Query<Label>().ForEach(label => label.pickingMode = PickingMode.Ignore);
        }

        /// <summary>Blocks world clicks/orbit/zoom over this UI, including during a captured drag.</summary>
        public bool BlocksWorldPointer(Vector2 screenPosition)
        {
            if (!isActiveAndEnabled || !initialized || root.panel == null) return false;
            if (dragSource != null || suppressWorldInputFrame == Time.frameCount) return true;
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel,
                new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            VisualElement picked = root.panel.Pick(panelPoint);
            for (VisualElement element = picked; element != null; element = element.parent)
                if (element.ClassListContains(BlockingClass)) return true;
            return false;
        }

        private void Refresh()
        {
            if (!initialized) return;
            root.Q<Label>("level-label").text = $"LEVEL {progression.CurrentLevel}";
            root.Q<Label>("mode-label").text = skills.IsJumpSelected ? "JUMP · choose landing" : $"{(skills.IsRunning ? "RUN" : "WALK")} · {skills.CurrentSpeed:0.0} m/s";
            RefreshResources();
            root.Q<Label>("points-summary").text = $"{progression.StatPoints} stat points  |  {progression.SkillPoints} skill points";
            ProgressBar xp = root.Q<ProgressBar>("xp-bar");
            xp.value = progression.IsAtLevelCap ? ProgressBarScale : ProgressBarScale * progression.Experience / Mathf.Max(1f, progression.ExperienceRequired);
            xp.title = progression.IsAtLevelCap ? "MAXIMUM LEVEL" : $"{progression.Experience:N0} / {progression.ExperienceRequired:N0} XP";
            root.Q<Label>("stat-points").text = $"{progression.StatPoints} points available";
            root.Q<Label>("stat-note").text = $"Spend {progression.AllocationCost} point(s) per increase. Vitality raises HP; Spirit raises Chi. "
                + (resources.Data != null && resources.Data.fillGainedCapacity ? "New capacity also raises current resources. " : "Only maximum capacity increases. ")
                + "Allocations last for this session.";
            for (int index = 0; index < AttributeCount; index++)
            {
                var attribute = (CharacterAttribute)index;
                root.Q<Label>($"stat-{index}").text = progression.GetAttribute(attribute).ToString();
                root.Q<Button>($"allocate-{index}").SetEnabled(progression.CanAllocate(attribute));
            }
            root.Q<Button>("grant-xp").SetEnabled(!progression.IsAtLevelCap);
            root.Q<Label>("skill-points").text = $"{progression.SkillPoints} skill point(s) available";
            RefreshSkillCard(MovementSkillId.Run, "run", runTile);
            RefreshSkillCard(MovementSkillId.Jump, "jump", jumpTile);
            for (int index = 0; index < slotElements.Length; index++)
            {
                MovementSkillId skill = hotbar.GetSlot(index);
                MovementSkillData definition = skills.GetDefinition(skill);
                VisualElement slot = slotElements[index];
                slot.Q<Label>(className: "slot-name").text = definition == null ? "—" : definition.displayName.ToUpperInvariant();
                slot.Q<Label>(className: "slot-rank").text = definition == null ? "EMPTY" : $"RANK {skills.GetRank(skill)}";
                slot.EnableInClassList("selected", skill != MovementSkillId.None && skill == skills.SelectedSkill);
                slot.tooltip = definition == null ? "Drop a learned skill here." : $"{definition.displayName}: click or press {(index + 1) % PlayerSkillHotbar.SlotCount}. Right-click to clear.";
            }
        }

        private void RefreshResources()
        {
            if (!initialized || resources == null) return;
            ProgressBar hp = root.Q<ProgressBar>("hp-bar");
            hp.value = ProgressBarScale * resources.Health / Mathf.Max(1f, resources.MaxHealth);
            hp.title = $"HP {resources.Health:0} / {resources.MaxHealth:0}";
            ProgressBar chi = root.Q<ProgressBar>("chi-bar");
            chi.value = ProgressBarScale * resources.Chi / Mathf.Max(1f, resources.MaxChi);
            chi.title = $"CHI {resources.Chi:0} / {resources.MaxChi:0}";
            root.Q<Label>("resource-values").text = $"HP {resources.Health:0} / {resources.MaxHealth:0}    Chi {resources.Chi:0} / {resources.MaxChi:0}";
            if (resources.Data != null)
            {
                root.Q<Button>("allocate-1").tooltip = $"+{resources.Data.healthPerVitality:0.#} maximum HP per Vitality.";
                root.Q<Button>("allocate-3").tooltip = $"+{resources.Data.chiPerSpirit:0.#} maximum Chi per Spirit.";
                root.Q<Label>("resource-formula").text = $"Vitality: +{resources.Data.healthPerVitality:0.#} HP each. Spirit: +{resources.Data.chiPerSpirit:0.#} Chi each.";
            }
            for (int index = 0; index < slotElements.Length; index++)
            {
                var skill = hotbar.GetSlot(index);
                bool unaffordable = skills.TryGetLearnedRank(skill, out var rank) && !resources.CanSpendChi(rank.chiCost);
                slotElements[index]?.EnableInClassList("insufficient-chi", unaffordable);
            }
        }

        private void RefreshSkillCard(MovementSkillId skill, string prefix, VisualElement tile)
        {
            MovementSkillData definition = skills.GetDefinition(skill);
            int rank = skills.GetRank(skill);
            root.Q<Label>($"{prefix}-rank").text = rank == 0 ? $"UNLEARNED · 0 / {definition.MaxRank}" : $"RANK {rank} / {definition.MaxRank}";
            root.Q<Label>($"{prefix}-description").text = definition.description;
            string currentValue = definition.TryGetRank(rank, out var current) ? $"Current: {DescribeRank(skill, current)}" : "Not learned";
            bool hasNext = definition.TryGetRank(rank + 1, out var next);
            root.Q<Label>($"{prefix}-values").text = hasNext ? $"{currentValue}\nNext: {DescribeRank(skill, next)}" : currentValue;
            root.Q<Label>($"{prefix}-requirement").text = hasNext
                ? $"Requires level {next.requiredCharacterLevel} · Upgrade: {next.skillPointCost} skill point(s)" : "Maximum rank reached";
            Button upgrade = root.Q<Button>($"upgrade-{prefix}");
            bool canUpgrade = skills.CanUpgrade(skill, out string reason);
            upgrade.text = !hasNext ? "MAXIMUM RANK" : $"{(rank == 0 ? "LEARN" : "UPGRADE")} {definition.displayName.ToUpperInvariant()}";
            upgrade.tooltip = canUpgrade ? "Spend skill points to purchase this rank." : reason;
            upgrade.SetEnabled(canUpgrade);
            root.Q<Label>($"{prefix}-drag-hint").text = rank == 0 ? $"Learn {definition.displayName}, then drag its tile to a hotbar slot."
                : "Drag to a slot. Select with 1–0, then left-click a destination. Select again to walk.";
            tile.tooltip = rank == 0 ? "Learn this skill before assigning it." : "Drag to hotbar";
        }

        private static string DescribeRank(MovementSkillId skill, MovementSkillRank rank)
        {
            return skill == MovementSkillId.Run ? $"{rank.runSpeed:0.0} m/s · {rank.chiCost:0.#} Chi / destination"
                : $"{rank.jumpDistance:0.0} m range / +{rank.jumpHeight:0.0} m height\n{rank.chiCost:0.#} Chi / jump";
        }

        private void OnSlotPointerDown(PointerDownEvent evt, int index)
        {
            if (evt.button == RightButton)
            {
                hotbar.ClearSlot(index);
                evt.StopPropagation();
                return;
            }
            if (evt.button != LeftButton) return;
            MovementSkillId skill = hotbar.GetSlot(index);
            if (skill == MovementSkillId.None) { Feedback("Drag a learned skill from Skills [K] into this slot."); return; }
            BeginDrag(slotElements[index], skill, index, evt);
        }

        private void OnSkillPointerDown(PointerDownEvent evt, MovementSkillId skill, VisualElement tile)
        {
            if (evt.button != LeftButton) return;
            if (!skills.CanUse(skill)) { Feedback($"Learn {skills.GetDefinition(skill).displayName} before dragging it to a slot."); return; }
            BeginDrag(tile, skill, NoSlot, evt);
        }

        private void BeginDrag(VisualElement source, MovementSkillId skill, int sourceSlot, PointerDownEvent evt)
        {
            if (dragSource != null) return;
            dragSource = source;
            draggedSkill = skill;
            draggedFromSlot = sourceSlot;
            dragPointerId = evt.pointerId;
            pointerStart = evt.position;
            dragMoved = false;
            hotbar.InputSuppressed = true;
            source.CapturePointer(evt.pointerId);
            if (sourceSlot == NoSlot)
                Feedback($"Drag {skills.GetDefinition(skill).displayName.ToUpperInvariant()} to a hotbar slot, then release the left mouse button.");
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (dragSource == null || evt.pointerId != dragPointerId) return;
            evt.StopPropagation();
            Vector2 position = evt.position;
            if (!dragMoved && (position - pointerStart).sqrMagnitude < DragThresholdSquared) return;
            dragMoved = true;
            SetVisible(dragGhost, true);
            dragGhost.text = skills.GetDefinition(draggedSkill).displayName.ToUpperInvariant();
            Vector2 local = root.Q("game-ui").WorldToLocal(position);
            dragGhost.style.left = local.x + GhostOffset;
            dragGhost.style.top = local.y + GhostOffset;
            for (int index = 0; index < slotElements.Length; index++)
                slotElements[index].EnableInClassList("drop-target", slotElements[index].worldBound.Contains(position));
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (dragSource == null || evt.pointerId != dragPointerId || evt.button != LeftButton) return;
            int sourceSlot = draggedFromSlot;
            MovementSkillId skill = draggedSkill;
            Vector2 position = evt.position;
            bool wasDrag = dragMoved || (position - pointerStart).sqrMagnitude >= DragThresholdSquared;
            int targetSlot = NoSlot;
            for (int index = 0; index < slotElements.Length; index++)
                if (slotElements[index].worldBound.Contains(position)) { targetSlot = index; break; }
            CancelDrag();
            if (wasDrag && targetSlot != NoSlot)
            {
                bool assigned;
                if (sourceSlot != NoSlot)
                {
                    hotbar.SwapSlots(sourceSlot, targetSlot);
                    assigned = hotbar.GetSlot(targetSlot) == skill;
                }
                else
                {
                    assigned = hotbar.AssignSkill(targetSlot, skill);
                }
                Feedback(assigned
                    ? $"{skills.GetDefinition(skill).displayName} assigned to key {(targetSlot + 1) % PlayerSkillHotbar.SlotCount}. Press the key or click the slot to select it."
                    : "Assignment failed. Learn the skill before assigning it to a hotbar slot.");
            }
            else if (!wasDrag && sourceSlot != NoSlot && targetSlot == sourceSlot)
            {
                if (hotbar.ActivateSlot(sourceSlot))
                    Feedback(skills.IsJumpSelected ? "Jump selected. Left-click a landing point within your rank limits."
                        : skills.IsRunning ? "Run selected. Each new destination costs Chi; select Run again to walk." : "Walking selected.");
            }
            else if (wasDrag) Feedback("Drop cancelled. Existing assignments were kept.");
            evt.StopPropagation();
        }

        private void OnCaptureLost(PointerCaptureOutEvent evt)
        {
            // A child or another control losing capture is not this drag losing capture.
            if (dragSource != null && evt.pointerId == dragPointerId
                && ReferenceEquals(evt.target, dragSource))
                CancelDrag();
        }

        private void OnPointerCancelled(PointerCancelEvent evt)
        {
            if (dragSource == null || evt.pointerId != dragPointerId) return;
            CancelDrag();
            evt.StopPropagation();
        }

        private void CancelDrag()
        {
            VisualElement source = dragSource;
            dragSource = null;
            hotbar.InputSuppressed = false;
            suppressWorldInputFrame = Time.frameCount;
            if (source != null && source.HasPointerCapture(dragPointerId)) source.ReleasePointer(dragPointerId);
            SetVisible(dragGhost, false);
            foreach (VisualElement slot in slotElements) slot?.RemoveFromClassList("drop-target");
        }

        private void UpgradeSkill(MovementSkillId skill)
        {
            if (skills.TryUpgrade(skill)) Feedback($"{skills.GetDefinition(skill).displayName} upgraded. Drag its tile onto any hotbar slot.");
        }

        private void GrantTestExperience()
        {
            if (Application.isEditor || Debug.isDebugBuild)
                progression.GrantExperience(Mathf.Max(0, progression.ExperienceRequired - progression.Experience));
        }

        private void OnLevelGained(int level) => Feedback($"Level {level} reached. New stat and skill points awarded.");
        private void Feedback(string text) => root.Q<Label>("feedback").text = text;
        private void ToggleCharacter() => ToggleWindow(characterWindow);
        private void ToggleSkills() => ToggleWindow(skillsWindow);
        private void HideCharacter() { SetVisible(characterWindow, false); CancelDrag(); }
        private void HideSkills() { SetVisible(skillsWindow, false); CancelDrag(); }
        private void ToggleWindow(VisualElement window)
        {
            movement.ReleaseCameraControl();
            CancelDrag();
            SetVisible(window, window.resolvedStyle.display == DisplayStyle.None);
        }
        private void CloseWindows() { HideCharacter(); HideSkills(); }
        private void OnApplicationFocus(bool focused) { if (!focused && initialized) CancelDrag(); }
        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
