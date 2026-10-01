using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>Displays progression and handles runtime skill drag/drop without editor-only drag APIs.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ProgressionHUD : MonoBehaviour, IPointerBlockQuery
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
        [SerializeField] private CharacterCombatStats combatStats;
        [SerializeField] private PlayerCombatSkills combatSkills;
        [SerializeField] private CharacterBuffManager buffManager;
        private VisualElement root;
        private VisualElement characterWindow;
        private VisualElement skillsWindow;
        private VisualElement runTile;
        private VisualElement jumpTile;
        private VisualElement activeBuffsBar;
        private Label dragGhost;
        private readonly List<Action> removeUiCallbacks = new List<Action>();
        private readonly VisualElement[] slotElements = new VisualElement[PlayerSkillHotbar.SlotCount];
        private VisualElement dragSource;
        private MovementSkillId draggedMovementSkill;
        private SkillDefinitionSO draggedCombatSkill;
        private int draggedFromSlot = NoSlot;
        private int dragPointerId;
        private Vector2 pointerStart;
        private bool dragMoved;
        private bool initialized;
        private int suppressWorldInputFrame = -1;
        private Action activeHoldAction;
        private float nextRepeatTime;
        private const float InitialHoldDelay = 0.35f;
        private const float HoldRepeatInterval = 0.08f;
        private int statStep = 1;
        private int skillStep = 1;
        private static readonly int[] StepValues = { 1, 5, 10 };
        private readonly Button[] statStepButtons = new Button[3];
        private readonly Button[] skillStepButtons = new Button[3];

        private readonly string[] combatSkillCardIds = {
            "spiritual-rage",
            "heavenly-serenity",
            "heavenly-shield",
            "heavenly-blow",
            "garudas-prayer",
            "heavenly-repulsion",
            "heavenly-destruction",
            "reverse-flow"
        };

        private readonly string[] combatSkillAssetIds = {
            "SpiritualRage",
            "HeavenlySerenity",
            "HeavenlyShield",
            "HeavenlyBlow",
            "GarudasPrayer",
            "HeavenlyRepulsion",
            "HeavenlyDestruction",
            "ReverseFlow"
        };

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            ClientPresentationRegistry.RegisterPointerBlock(this);
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
            activeBuffsBar = root.Q("active-buffs-bar");
            dragGhost = root.Q<Label>("drag-ghost");

            if (combatSkills == null) combatSkills = skills.GetComponent<PlayerCombatSkills>();
            if (buffManager == null) buffManager = skills.GetComponent<CharacterBuffManager>();

            BindButton("open-character", ToggleCharacter);
            BindButton("open-skills", ToggleSkills);
            BindButton("close-character", HideCharacter);
            BindButton("close-skills", HideSkills);
            BindButton("grant-xp", GrantTestExperience);
            BindButton("attack-dummy", TestAttackDummy);
            BindButton("reset-dummy", TestResetDummy);
            BindButton("toggle-drop-skills", ToggleDropSkills);
            BindButton("toggle-test-gear", ToggleTestGear);
            BindButton("test-damage", () => { if (Application.isEditor || Debug.isDebugBuild) resources.ApplyDamage(TestResourceAmount); });
            BindButton("test-chi", () => { if (Application.isEditor || Debug.isDebugBuild) resources.TrySpendChi(Mathf.Min(TestResourceAmount, resources.Chi)); });
            BindButton("restore-resources", () => { if (Application.isEditor || Debug.isDebugBuild) resources.RestoreAll(); });

            for (int i = 0; i < StepValues.Length; i++)
            {
                int val = StepValues[i];
                Button statBtn = root.Q<Button>($"stat-step-{val}");
                statStepButtons[i] = statBtn;
                if (statBtn != null)
                {
                    BindButton($"stat-step-{val}", () => SetStatStep(val));
                }
                Button skillBtn = root.Q<Button>($"skill-step-{val}");
                skillStepButtons[i] = skillBtn;
                if (skillBtn != null)
                {
                    BindButton($"skill-step-{val}", () => SetSkillStep(val));
                }
            }

            for (int index = 0; index < AttributeCount; index++)
            {
                CharacterAttribute attribute = (CharacterAttribute)index;
                Button button = root.Q<Button>($"allocate-{index}");
                if (button != null)
                {
                    BindHoldable(button, () => AllocateAttribute(attribute));
                }
            }

            Button runUpgrade = root.Q<Button>("upgrade-run");
            if (runUpgrade != null)
            {
                BindHoldable(runUpgrade, () => UpgradeSkill(MovementSkillId.Run));
            }

            Button jumpUpgrade = root.Q<Button>("upgrade-jump");
            if (jumpUpgrade != null)
            {
                BindHoldable(jumpUpgrade, () => UpgradeSkill(MovementSkillId.Jump));
            }

            // Bind combat skill cards
            for (int i = 0; i < combatSkillCardIds.Length; i++)
            {
                string cardId = combatSkillCardIds[i];
                string skillId = combatSkillAssetIds[i];
                var skillDef = combatSkills != null ? combatSkills.GetSkill(skillId) : null;
                VisualElement tile = root.Q($"{cardId}-drag");
                if (tile != null && skillDef != null)
                {
                    BindPointer<PointerDownEvent>(tile, evt => OnCombatSkillPointerDown(evt, skillDef, tile));
                    BindDragSource(tile);
                }

                Button upgradeBtn = root.Q<Button>($"upgrade-{cardId}");
                if (upgradeBtn != null && skillDef != null)
                {
                    BindHoldable(upgradeBtn, () => UpgradeCombatSkill(skillDef));
                }
            }

            BindPointer<PointerUpEvent>(root, evt => { if (evt.button == LeftButton) StopHold(); });
            BindPointer<PointerCancelEvent>(root, evt => StopHold());

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
            if (combatStats == null && movement != null) combatStats = movement.GetComponent<CharacterCombatStats>();
            if (combatStats != null) combatStats.OnStatsRecalculated += Refresh;
            if (combatSkills != null)
            {
                combatSkills.Changed += Refresh;
                combatSkills.SkillMessage += Feedback;
            }
            if (buffManager != null)
            {
                buffManager.BuffsChanged += RefreshBuffs;
                buffManager.BuffMessage += Feedback;
            }

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

        private void Update()
        {
            if (activeHoldAction != null)
            {
                if (Time.unscaledTime >= nextRepeatTime)
                {
                    activeHoldAction();
                    nextRepeatTime = Time.unscaledTime + HoldRepeatInterval;
                }
            }

            if (initialized)
            {
                RefreshBuffs();
                RefreshHotbarCooldowns();
            }
        }

        private void OnDisable()
        {
            ClientPresentationRegistry.UnregisterPointerBlock(this);
            if (!initialized) return;
            StopHold();
            CancelDrag();
            resources.Changed -= RefreshResources;
            movement.CommandMessage -= Feedback;
            jumpMotor.Message -= Feedback;
            if (combatStats != null) combatStats.OnStatsRecalculated -= Refresh;
            if (combatSkills != null)
            {
                combatSkills.Changed -= Refresh;
                combatSkills.SkillMessage -= Feedback;
            }
            if (buffManager != null)
            {
                buffManager.BuffsChanged -= RefreshBuffs;
                buffManager.BuffMessage -= Feedback;
            }
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
            if (button == null) return;
            button.clicked += callback;
            removeUiCallbacks.Add(() => button.clicked -= callback);
        }

        private void BindPointer<TEvent>(VisualElement element, EventCallback<TEvent> callback)
            where TEvent : EventBase<TEvent>, new()
        {
            if (element == null) return;
            element.RegisterCallback(callback);
            removeUiCallbacks.Add(() => element.UnregisterCallback(callback));
        }

        private void BindHoldable(Button button, Action onAction)
        {
            button.clicked += () =>
            {
                onAction();
            };
            BindPointer<PointerDownEvent>(button, evt =>
            {
                if (evt.button != LeftButton) return;
                StartHold(onAction);
            });
            BindPointer<PointerUpEvent>(button, evt =>
            {
                if (evt.button == LeftButton) StopHold();
            });
            BindPointer<PointerCancelEvent>(button, evt => StopHold());
            BindPointer<PointerCaptureOutEvent>(button, evt => StopHold());
            BindPointer<PointerLeaveEvent>(button, evt => StopHold());
        }

        private void StartHold(Action action)
        {
            activeHoldAction = action;
            nextRepeatTime = Time.unscaledTime + InitialHoldDelay;
        }

        private void StopHold()
        {
            activeHoldAction = null;
        }

        private void SetStatStep(int step)
        {
            statStep = step;
            UpdateStepButtons(statStepButtons, statStep);
            Refresh();
        }

        private void SetSkillStep(int step)
        {
            skillStep = step;
            UpdateStepButtons(skillStepButtons, skillStep);
            Refresh();
        }

        private static void UpdateStepButtons(Button[] buttons, int activeStep)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null)
                    buttons[i].EnableInClassList("active-step", StepValues[i] == activeStep);
            }
        }

        private void BindDragSource(VisualElement source)
        {
            if (source == null) return;
            BindPointer<PointerMoveEvent>(source, OnPointerMove);
            BindPointer<PointerUpEvent>(source, OnPointerUp);
            BindPointer<PointerCaptureOutEvent>(source, OnCaptureLost);
            BindPointer<PointerCancelEvent>(source, OnPointerCancelled);
            source.Query<Label>().ForEach(label => label.pickingMode = PickingMode.Ignore);
        }

        public bool BlocksWorldPointer(Vector2 screenPosition)
        {
            if (!isActiveAndEnabled || !initialized || root.panel == null) return false;
            if (dragSource != null || activeHoldAction != null || suppressWorldInputFrame == Time.frameCount) return true;
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
                int baseVal = progression.GetAttribute(attribute);
                StatType statType = (StatType)index;
                float totalVal = combatStats != null ? combatStats.GetStatValue(statType) : baseVal;
                int bonusVal = Mathf.RoundToInt(totalVal - baseVal);

                Label statLabel = root.Q<Label>($"stat-{index}");
                if (statLabel != null)
                {
                    statLabel.text = bonusVal > 0 ? $"{baseVal} (+{bonusVal})" : baseVal.ToString();
                    statLabel.tooltip = bonusVal > 0
                        ? $"Base Allocated: {baseVal} + Gear/Bonus: {bonusVal} = Total: {Mathf.RoundToInt(totalVal)}"
                        : $"Allocated: {baseVal}";
                }

                Button allocBtn = root.Q<Button>($"allocate-{index}");
                allocBtn.SetEnabled(progression.CanAllocate(attribute));
                allocBtn.text = $"+{statStep}";
                allocBtn.tooltip = GetAttributeAllocationTooltip(attribute, statStep);
            }
            root.Q<Button>("grant-xp").SetEnabled(!progression.IsAtLevelCap);
            root.Q<Label>("skill-points").text = $"{progression.SkillPoints} skill point(s) available";

            RefreshSkillCard(MovementSkillId.Run, "run", runTile);
            RefreshSkillCard(MovementSkillId.Jump, "jump", jumpTile);

            for (int i = 0; i < combatSkillCardIds.Length; i++)
            {
                string cardId = combatSkillCardIds[i];
                string skillId = combatSkillAssetIds[i];
                RefreshCombatSkillCard(cardId, skillId);
            }

            for (int index = 0; index < slotElements.Length; index++)
            {
                HotbarSlotEntry entry = hotbar.GetSlotEntry(index);
                VisualElement slot = slotElements[index];

                if (entry.itemType == HotbarItemType.Movement)
                {
                    MovementSkillData def = skills.GetDefinition(entry.movementSkill);
                    slot.Q<Label>(className: "slot-name").text = def == null ? "—" : def.displayName.ToUpperInvariant();
                    slot.Q<Label>(className: "slot-rank").text = def == null ? "EMPTY" : $"RANK {skills.GetRank(entry.movementSkill)}";
                    slot.EnableInClassList("selected", entry.movementSkill != MovementSkillId.None && entry.movementSkill == skills.SelectedSkill);
                    slot.tooltip = $"{def?.displayName}: click or press {(index + 1) % PlayerSkillHotbar.SlotCount}. Right-click to clear.";
                }
                else if (entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill != null)
                {
                    SkillDefinitionSO def = entry.combatSkill;
                    slot.Q<Label>(className: "slot-name").text = def.displayName.ToUpperInvariant();
                    int rank = combatSkills != null ? combatSkills.GetRank(def) : 0;
                    slot.Q<Label>(className: "slot-rank").text = $"RANK {rank}";
                    slot.EnableInClassList("selected", false);
                    slot.tooltip = $"{def.displayName}: click or press {(index + 1) % PlayerSkillHotbar.SlotCount} to activate. Right-click to clear.";
                }
                else
                {
                    slot.Q<Label>(className: "slot-name").text = "—";
                    slot.Q<Label>(className: "slot-rank").text = "EMPTY";
                    slot.EnableInClassList("selected", false);
                    slot.tooltip = "Drop a learned skill here.";
                }
            }

            if (combatStats != null)
            {
                Label apLabel = root.Q<Label>("combat-stat-ap");
                if (apLabel != null) apLabel.text = Mathf.RoundToInt(combatStats.GetStatValue(StatType.AttackPower)).ToString();

                Label dpLabel = root.Q<Label>("combat-stat-dp");
                if (dpLabel != null) dpLabel.text = Mathf.RoundToInt(combatStats.GetStatValue(StatType.DefensePower)).ToString();

                Label hitLabel = root.Q<Label>("combat-stat-hit");
                if (hitLabel != null) hitLabel.text = Mathf.RoundToInt(combatStats.GetStatValue(StatType.HitRate)).ToString();

                Label evadeLabel = root.Q<Label>("combat-stat-evade");
                if (evadeLabel != null) evadeLabel.text = Mathf.RoundToInt(combatStats.GetStatValue(StatType.EvasionRate)).ToString();

                Label critLabel = root.Q<Label>("combat-stat-crit");
                if (critLabel != null) critLabel.text = $"{combatStats.GetStatValue(StatType.CriticalRate):0.#}%";

                Label stunLabel = root.Q<Label>("combat-stat-stun-res");
                if (stunLabel != null) stunLabel.text = $"{combatStats.GetStatValue(StatType.AcupressureStunResistance):0.#}%";

                Label speedLabel = root.Q<Label>("combat-stat-speed");
                if (speedLabel != null) speedLabel.text = $"{combatStats.GetStatValue(StatType.MoveSpeedPercent):0}%";
            }
        }

        private void RefreshCombatSkillCard(string cardPrefix, string skillAssetId)
        {
            if (combatSkills == null) return;
            var skill = combatSkills.GetSkill(skillAssetId);
            if (skill == null) return;

            bool isDropSkill = skill.requiresSkillBook;
            bool isLearned = combatSkills.IsLearned(skill);
            int rank = combatSkills.GetRank(skill);

            Label rankLabel = root.Q<Label>($"{cardPrefix}-rank");
            if (rankLabel != null)
            {
                if (isDropSkill && !isLearned)
                {
                    rankLabel.text = $"LOCKED ({skill.acquisitionNote})";
                }
                else
                {
                    rankLabel.text = rank == 0 ? $"UNLEARNED · 0 / {skill.maxRank}" : $"RANK {rank} / {skill.maxRank}";
                }
            }

            Label descLabel = root.Q<Label>($"{cardPrefix}-description");
            if (descLabel != null) descLabel.text = skill.description;

            Label valuesLabel = root.Q<Label>($"{cardPrefix}-values");
            if (valuesLabel != null)
            {
                int evalRank = rank > 0 ? rank : 1;
                string statsStr = DescribeSkillStats(skill, evalRank);
                if (rank > 0 && rank < skill.maxRank)
                {
                    statsStr += $"\nNext: {DescribeSkillStats(skill, rank + 1)}";
                }
                valuesLabel.text = statsStr;
            }

            Label reqLabel = root.Q<Label>($"{cardPrefix}-requirement");
            if (reqLabel != null)
            {
                if (isDropSkill && !isLearned)
                {
                    string reqStr = $"Requires Level {skill.requiredCharacterLevel} · {skill.acquisitionNote}";
                    if (skill.requiredCraftingArchives != null && skill.requiredCraftingArchives.Length > 0)
                    {
                        reqStr += $"\nArchives: {string.Join(", ", skill.requiredCraftingArchives)}";
                    }
                    reqLabel.text = reqStr;
                }
                else if (rank >= skill.maxRank)
                {
                    reqLabel.text = "Maximum rank reached";
                }
                else
                {
                    int nextCost = skill.GetSkillPointCostForRank(rank);
                    reqLabel.text = $"Requires Level {skill.requiredCharacterLevel} · Upgrade Cost: {nextCost} skill point(s)";
                }
            }

            Button upgrade = root.Q<Button>($"upgrade-{cardPrefix}");
            if (upgrade != null)
            {
                if (isDropSkill && !isLearned)
                {
                    upgrade.text = "REQUIRES SKILL BOOK / CRAFT";
                    upgrade.SetEnabled(false);
                    upgrade.tooltip = $"Acquire {skill.displayName} drop book or craft via required archives.";
                }
                else
                {
                    bool canUpgrade = combatSkills.CanUpgrade(skill, out string reason);
                    string action = rank == 0 ? "LEARN" : "UPGRADE";
                    string stepSuffix = skillStep > 1 ? $" (+{skillStep})" : "";
                    upgrade.text = rank >= skill.maxRank ? "MAXIMUM RANK" : $"{action} {skill.displayName.ToUpperInvariant()}{stepSuffix}";
                    upgrade.tooltip = canUpgrade ? "Spend skill points to purchase this rank. Press and hold to upgrade continuously." : reason;
                    upgrade.SetEnabled(canUpgrade);
                }
            }

            Label dragHint = root.Q<Label>($"{cardPrefix}-drag-hint");
            if (dragHint != null)
            {
                if (isDropSkill && !isLearned)
                    dragHint.text = "Available once acquired or crafted.";
                else
                    dragHint.text = rank == 0 ? $"Learn {skill.displayName}, then drag to a hotbar slot." : "Drag to a hotbar slot (1–10) to activate.";
            }

            VisualElement tile = root.Q($"{cardPrefix}-drag");
            if (tile != null)
            {
                tile.tooltip = isLearned ? $"Drag {skill.displayName} to hotbar" : isDropSkill ? skill.acquisitionNote : "Learn before assigning";
            }
        }

        private static string DescribeSkillStats(SkillDefinitionSO skill, int rank)
        {
            float chi = skill.GetChiCost(rank);
            float dur = skill.GetDuration(rank);

            if (skill.isAbsorptionShield)
            {
                float shieldPct = Mathf.Round(skill.GetShieldMultiplier(rank) * 100f);
                return $"{shieldPct}% Max HP Shield · {dur:0}s duration · {chi:0} Chi";
            }
            if (skill.hasDeadlyBlowBoost)
            {
                float deadly = skill.GetDeadlyBlowBonus(rank);
                return $"+{deadly:0.#}% Deadly Blow (mult) · {dur:0}s duration · {chi:0} Chi";
            }
            if (skill.hasLuckBoost)
            {
                float luck = skill.GetLuckBonus(rank);
                return $"+{luck:0.#}% Luck (mult) · {dur:0}s duration · {chi:0} Chi";
            }
            if (skill.hasReflection)
            {
                float refChance = skill.GetReflectionChance(rank);
                return $"{refChance:0.#}% Reflect Physical Dmg · {dur:0}s duration · {chi:0} Chi";
            }
            if (skill.hasShieldShatter)
            {
                float shatChance = skill.GetDestructionChance(rank);
                return $"{shatChance:0.#}% Shield Shatter & Dispel · {dur:0}s duration · {chi:0} Chi";
            }
            if (skill.hasAcupressureResistance)
            {
                float stunRes = skill.GetAcupressureResistance(rank);
                return $"+{stunRes:0.#}% Stun Defense · {dur:0}s duration · {chi:0} Chi";
            }
            if (skill.executionType == SkillExecutionType.Channeled)
            {
                return $"Channeled Chi Restore · {dur:0}s duration · {chi:0} Chi";
            }
            if (skill.statModifiers != null && skill.statModifiers.Length > 0)
            {
                var mod = skill.statModifiers[0];
                return $"+{mod.value:0.#}% {mod.statType} · {dur:0}s duration · {chi:0} Chi";
            }
            return $"{dur:0}s duration · {chi:0} Chi";
        }

        private void RefreshBuffs()
        {
            if (activeBuffsBar == null || buffManager == null) return;
            activeBuffsBar.Clear();

            var buffs = buffManager.ActiveBuffs;
            for (int i = 0; i < buffs.Count; i++)
            {
                var buff = buffs[i];
                var badge = new VisualElement();
                badge.AddToClassList("buff-badge");

                if (buff.Skill.isAbsorptionShield)
                    badge.AddToClassList("shield-badge");
                else if (buff.Skill.executionType == SkillExecutionType.Channeled)
                    badge.AddToClassList("channeled-badge");

                var nameLbl = new Label(buff.Skill.displayName);
                nameLbl.AddToClassList("buff-badge-name");
                badge.Add(nameLbl);

                string timerText = buff.Skill.isAbsorptionShield
                    ? $"{buff.CurrentShieldCapacity:0} HP ({buff.RemainingDuration:0}s)"
                    : $"{buff.RemainingDuration:0.0}s";
                var timerLbl = new Label(timerText);
                timerLbl.AddToClassList("buff-badge-timer");
                badge.Add(timerLbl);

                activeBuffsBar.Add(badge);
            }
        }

        private void RefreshHotbarCooldowns()
        {
            if (combatSkills == null) return;
            for (int index = 0; index < slotElements.Length; index++)
            {
                var entry = hotbar.GetSlotEntry(index);
                if (entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill != null)
                {
                    bool onCooldown = combatSkills.IsOnCooldown(entry.combatSkill);
                    slotElements[index]?.EnableInClassList("on-cooldown", onCooldown);
                    if (onCooldown)
                    {
                        float rem = combatSkills.GetRemainingCooldown(entry.combatSkill);
                        slotElements[index].Q<Label>(className: "slot-rank").text = $"{rem:0.0}s";
                    }
                    else
                    {
                        int rank = combatSkills.GetRank(entry.combatSkill);
                        slotElements[index].Q<Label>(className: "slot-rank").text = $"RANK {rank}";
                    }
                }
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
                Label formulaLabel = root.Q<Label>("resource-formula");
                if (formulaLabel != null)
                    formulaLabel.text = "STR: +1 AP, +2 Hit Rate | VIT: +20 HP, +1 Evade | AGI: +2 Evade, +1 DP | CHI: +20 Chi, +1 AP";
            }
            for (int index = 0; index < slotElements.Length; index++)
            {
                var entry = hotbar.GetSlotEntry(index);
                bool unaffordable = false;
                if (entry.itemType == HotbarItemType.Movement)
                {
                    unaffordable = skills.TryGetLearnedRank(entry.movementSkill, out var rank) && !resources.CanSpendChi(rank.chiCost);
                }
                else if (entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill != null)
                {
                    int rank = combatSkills != null ? combatSkills.GetRank(entry.combatSkill) : 1;
                    unaffordable = !resources.CanSpendChi(entry.combatSkill.GetChiCost(rank));
                }
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
            string action = rank == 0 ? "LEARN" : "UPGRADE";
            string stepSuffix = skillStep > 1 ? $" (+{skillStep})" : "";
            upgrade.text = !hasNext ? "MAXIMUM RANK" : $"{action} {definition.displayName.ToUpperInvariant()}{stepSuffix}";
            upgrade.tooltip = canUpgrade ? "Spend skill points to purchase this rank. Press and hold to upgrade continuously." : reason;
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
            var entry = hotbar.GetSlotEntry(index);
            if (entry.IsEmpty) { Feedback("Drag a learned skill from Skills [K] into this slot."); return; }

            if (entry.itemType == HotbarItemType.Movement)
                BeginDragMovement(slotElements[index], entry.movementSkill, index, evt);
            else if (entry.itemType == HotbarItemType.CombatSkill)
                BeginDragCombat(slotElements[index], entry.combatSkill, index, evt);
        }

        private void OnSkillPointerDown(PointerDownEvent evt, MovementSkillId skill, VisualElement tile)
        {
            if (evt.button != LeftButton) return;
            if (!skills.CanUse(skill)) { Feedback($"Learn {skills.GetDefinition(skill).displayName} before dragging it to a slot."); return; }
            BeginDragMovement(tile, skill, NoSlot, evt);
        }

        private void OnCombatSkillPointerDown(PointerDownEvent evt, SkillDefinitionSO skill, VisualElement tile)
        {
            if (evt.button != LeftButton) return;
            if (combatSkills == null || !combatSkills.IsLearned(skill))
            {
                Feedback(skill.requiresSkillBook
                    ? $"{skill.displayName} requires {skill.acquisitionNote}."
                    : $"Learn {skill.displayName} before dragging it to a slot.");
                return;
            }
            BeginDragCombat(tile, skill, NoSlot, evt);
        }

        private void BeginDragMovement(VisualElement source, MovementSkillId skill, int sourceSlot, PointerDownEvent evt)
        {
            if (dragSource != null) return;
            dragSource = source;
            draggedMovementSkill = skill;
            draggedCombatSkill = null;
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

        private void BeginDragCombat(VisualElement source, SkillDefinitionSO skill, int sourceSlot, PointerDownEvent evt)
        {
            if (dragSource != null) return;
            dragSource = source;
            draggedMovementSkill = MovementSkillId.None;
            draggedCombatSkill = skill;
            draggedFromSlot = sourceSlot;
            dragPointerId = evt.pointerId;
            pointerStart = evt.position;
            dragMoved = false;
            hotbar.InputSuppressed = true;
            source.CapturePointer(evt.pointerId);
            if (sourceSlot == NoSlot)
                Feedback($"Drag {skill.displayName.ToUpperInvariant()} to a hotbar slot, then release the left mouse button.");
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

            string ghostText = draggedCombatSkill != null
                ? draggedCombatSkill.displayName.ToUpperInvariant()
                : skills.GetDefinition(draggedMovementSkill).displayName.ToUpperInvariant();
            dragGhost.text = ghostText;

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
            MovementSkillId movSkill = draggedMovementSkill;
            SkillDefinitionSO comSkill = draggedCombatSkill;
            Vector2 position = evt.position;
            bool wasDrag = dragMoved || (position - pointerStart).sqrMagnitude >= DragThresholdSquared;
            int targetSlot = NoSlot;
            for (int index = 0; index < slotElements.Length; index++)
                if (slotElements[index].worldBound.Contains(position)) { targetSlot = index; break; }
            CancelDrag();

            if (wasDrag && targetSlot != NoSlot)
            {
                if (sourceSlot != NoSlot)
                {
                    hotbar.SwapSlots(sourceSlot, targetSlot);
                    Feedback($"Swapped hotbar slots {sourceSlot + 1} and {targetSlot + 1}.");
                }
                else
                {
                    bool assigned = false;
                    string name = "";
                    if (movSkill != MovementSkillId.None)
                    {
                        assigned = hotbar.AssignSkill(targetSlot, movSkill);
                        name = skills.GetDefinition(movSkill).displayName;
                    }
                    else if (comSkill != null)
                    {
                        assigned = hotbar.AssignSkill(targetSlot, comSkill);
                        name = comSkill.displayName;
                    }
                    Feedback(assigned
                        ? $"{name} assigned to key {(targetSlot + 1) % PlayerSkillHotbar.SlotCount}. Press the key or click the slot to activate."
                        : "Assignment failed.");
                }
            }
            else if (!wasDrag && sourceSlot != NoSlot)
            {
                hotbar.ActivateSlot(sourceSlot);
            }
            else if (wasDrag) Feedback("Drop cancelled. Existing assignments were kept.");
            evt.StopPropagation();
        }

        private void OnCaptureLost(PointerCaptureOutEvent evt)
        {
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

        private void AllocateAttribute(CharacterAttribute attribute)
        {
            if (!progression.CanAllocate(attribute))
            {
                StopHold();
                return;
            }
            int allocated = progression.TryAllocateMultiple(attribute, statStep);
            if (allocated > 0)
            {
                Feedback($"Allocated +{allocated} to {attribute}. ({progression.StatPoints} stat points remaining)");
                Refresh();
            }
            else
            {
                StopHold();
            }
        }

        private void UpgradeSkill(MovementSkillId skill)
        {
            if (!skills.CanUpgrade(skill, out string reason))
            {
                Feedback(reason);
                StopHold();
                return;
            }
            int upgraded = skills.TryUpgradeMultiple(skill, skillStep);
            if (upgraded > 0)
            {
                Feedback($"Purchased +{upgraded} rank(s) of {skills.GetDefinition(skill).displayName}. Current rank: {skills.GetRank(skill)}.");
                Refresh();
            }
            else
            {
                StopHold();
            }
        }

        private void UpgradeCombatSkill(SkillDefinitionSO skill)
        {
            string reason = "Combat skills unavailable";
            if (combatSkills == null || !combatSkills.CanUpgrade(skill, out reason))
            {
                Feedback(reason);
                StopHold();
                return;
            }
            int upgraded = combatSkills.TryUpgradeMultiple(skill, skillStep);
            if (upgraded > 0)
            {
                Feedback($"Purchased +{upgraded} rank(s) of {skill.displayName}. Current rank: {combatSkills.GetRank(skill)}.");
                Refresh();
            }
            else
            {
                StopHold();
            }
        }

        private void ToggleDropSkills()
        {
            if (combatSkills == null) return;
            bool anyUnlocked = false;
            foreach (var cardId in combatSkillAssetIds)
            {
                var s = combatSkills.GetSkill(cardId);
                if (s != null && s.requiresSkillBook && combatSkills.IsDropSkillUnlocked(s.skillId))
                {
                    anyUnlocked = true;
                    break;
                }
            }

            if (anyUnlocked)
            {
                combatSkills.LockAllDropSkills();
                Feedback("Locked all Rare Drop Book Skills.");
            }
            else
            {
                combatSkills.UnlockAllDropSkills();
                Feedback("Unlocked all Rare Drop Book Skills for testing!");
            }
            Refresh();
        }

        private static readonly object TestGearSource = new object();
        private bool hasTestGearEquipped = false;

        private void ToggleTestGear()
        {
            if (combatStats == null && movement != null)
                combatStats = movement.GetComponent<CharacterCombatStats>();
            if (combatStats == null) return;

            Button gearBtn = root.Q<Button>("toggle-test-gear");

            if (hasTestGearEquipped)
            {
                combatStats.RemoveModifiersFromSource(TestGearSource);
                hasTestGearEquipped = false;
                if (gearBtn != null) gearBtn.text = "Test: Equip Gear (+10 All Stats)";
                Feedback("Test Gear unequipped (-10 STR, VIT, AGI, CHI). Primary & Secondary sub-stats returned to base.");
            }
            else
            {
                // Equips gear granting +10 to all 4 primary attributes
                combatStats.AddModifier(new StatModifier(StatType.Strength, ModifierType.Flat, 10f, TestGearSource));
                combatStats.AddModifier(new StatModifier(StatType.Vitality, ModifierType.Flat, 10f, TestGearSource));
                combatStats.AddModifier(new StatModifier(StatType.Agility, ModifierType.Flat, 10f, TestGearSource));
                combatStats.AddModifier(new StatModifier(StatType.Spirit, ModifierType.Flat, 10f, TestGearSource));
                hasTestGearEquipped = true;
                if (gearBtn != null) gearBtn.text = "Test: Unequip Gear (+10 All Stats)";
                Feedback("Test Gear equipped! (+10 STR, VIT, AGI, CHI). Primary & Secondary stats scaled dynamically!");
            }
            Refresh();
        }

        private static string GetAttributeAllocationTooltip(CharacterAttribute attribute, int step)
        {
            switch (attribute)
            {
                case CharacterAttribute.Strength:
                    return $"Allocate +{step} Strength:\n• Primary: +{step * 1} Attack Power (AP)\n• Secondary: +{step * 2} Attack Rate (Hit Rate)\n(Press and hold to allocate continuously)";
                case CharacterAttribute.Vitality:
                    return $"Allocate +{step} Vitality:\n• Primary: +{step * 20} Health Points (HP)\n• Secondary: +{step * 1} Evade (Dodge)\n(Press and hold to allocate continuously)";
                case CharacterAttribute.Agility:
                    return $"Allocate +{step} Agility:\n• Primary: +{step * 2} Evade (Dodge)\n• Secondary: +{step * 1} Defense (DP)\n(Press and hold to allocate continuously)";
                case CharacterAttribute.Spirit:
                    return $"Allocate +{step} Spirit (Chi):\n• Primary: +{step * 20} Chi (Mana)\n• Secondary: +{step * 1} Attack Power (AP)\n(Press and hold to allocate continuously)";
                default:
                    return $"Allocate +{step} {attribute}.";
            }
        }

        private void ToggleCharacter() => ToggleWindow(characterWindow);
        private void HideCharacter() { SetVisible(characterWindow, false); StopHold(); CancelDrag(); }
        private void ToggleSkills() => ToggleWindow(skillsWindow);
        private void HideSkills() { SetVisible(skillsWindow, false); StopHold(); CancelDrag(); }
        private void ToggleWindow(VisualElement window)
        {
            movement.ReleaseCameraControl();
            StopHold();
            CancelDrag();
            SetVisible(window, window.resolvedStyle.display == DisplayStyle.None);
        }
        private void CloseWindows() { HideCharacter(); HideSkills(); }

        private void OnLevelGained(int level) => Feedback($"Level {level} reached. New stat and skill points awarded.");

        private void GrantTestExperience()
        {
            if (progression == null || progression.IsAtLevelCap) return;
            int needed = progression.ExperienceRequired - progression.Experience;
            progression.GrantExperience(Mathf.Max(1, needed));
        }

        private void TestAttackDummy()
        {
            CombatEntity target = null;
            var entities = FindObjectsByType<CombatEntity>(FindObjectsInactive.Exclude);
            for (int i = 0; i < entities.Length; i++)
            {
                if (movement != null && entities[i].gameObject != movement.gameObject)
                {
                    target = entities[i];
                    break;
                }
            }

            if (target == null)
            {
                Feedback("No combat dummy found in scene.");
                return;
            }

            if (combatStats == null && movement != null) combatStats = movement.GetComponent<CharacterCombatStats>();
            if (combatStats == null || target.CombatStats == null)
            {
                Feedback("Combat stats missing on player or target.");
                return;
            }

            if (buffManager == null) buffManager = GetComponent<CharacterBuffManager>();
            var defenderBuffs = target.GetComponent<CharacterBuffManager>();

            float refChance = 0f;
            float shatChance = 0f;
            float dispelChance = 0f;
            bool defenderReflect = defenderBuffs != null && defenderBuffs.HasReflection(out refChance, out _);
            bool attackerShatter = buffManager != null && buffManager.HasShieldShatter(out shatChance, out dispelChance);

            var result = CombatCalculator.CalculateAttack(
                combatStats,
                target.CombatStats,
                1.0f,
                defenderReflect,
                refChance,
                attackerShatter,
                shatChance);

            target.TakeDamage(result, combatStats);

            if (attackerShatter && dispelChance > 0f && defenderBuffs != null)
            {
                if (UnityEngine.Random.Range(0f, 100f) <= dispelChance)
                {
                    defenderBuffs.DispelStanceBuffs();
                }
            }

            ClientPresentationRegistry.Feedback?.ShowCombatFeedback(target.Transform.position, result);

            if (result.IsEvaded)
                Feedback($"Test: Dummy EVADED your attack! (HP: {target.CurrentHealth:0}/{target.MaxHealth:0})");
            else if (result.IsReflected)
                Feedback($"Test: Dummy REFLECTED attack! You took {result.ReflectedDamage:0} dmg.");
            else if (result.IsCritical)
                Feedback($"Test: CRITICAL HIT dummy for {result.DamageDealt:0} dmg! (HP: {target.CurrentHealth:0}/{target.MaxHealth:0})");
            else
                Feedback($"Test: Struck dummy for {result.DamageDealt:0} dmg! (HP: {target.CurrentHealth:0}/{target.MaxHealth:0})");
        }

        private void TestResetDummy()
        {
            var entities = FindObjectsByType<CombatEntity>(FindObjectsInactive.Exclude);
            for (int i = 0; i < entities.Length; i++)
            {
                if (movement != null && entities[i].gameObject != movement.gameObject)
                {
                    entities[i].ResetHealth();
                    Feedback($"{entities[i].EntityName} HP reset to {entities[i].CurrentHealth:0}/{entities[i].MaxHealth:0}.");
                    return;
                }
            }
            Feedback("No combat dummy found to reset.");
        }

        private void Feedback(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Label feedback = root.Q<Label>("feedback");
            if (feedback != null) feedback.text = message;
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            element.EnableInClassList("visible", visible);
        }
    }
}
