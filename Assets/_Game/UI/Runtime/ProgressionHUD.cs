using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
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

        /// <summary>The local hotbar, shared with the settings/network panels so they can suppress its keys while typing.</summary>
        public PlayerSkillHotbar Hotbar => hotbar;
        private VisualElement skillsWindow;
        private VisualElement runTile;
        private VisualElement jumpTile;
        private VisualElement activeBuffsBar;
        private Label dragGhost;
        private readonly List<Action> removeUiCallbacks = new List<Action>();
        private readonly VisualElement[] slotElements = new VisualElement[PlayerSkillHotbar.SlotCount];
        private readonly VisualElement[] slotIcons = new VisualElement[PlayerSkillHotbar.SlotCount];
        private readonly VisualElement[] slotCooldowns = new VisualElement[PlayerSkillHotbar.SlotCount];
        private readonly List<ActiveBuff> shownBuffs = new List<ActiveBuff>();
        private readonly List<Label> shownBuffTimers = new List<Label>();
        private VisualElement dragSource;
        private MovementSkillId draggedMovementSkill;
        private SkillDefinitionSO draggedCombatSkill;
        private int draggedFromSlot = NoSlot;
        private int dragPointerId;
        private Vector2 pointerStart;
        private bool dragMoved;
        private bool initialized; // a player is bound
        private bool uiReady;
        private GameObject boundPlayer;
        private PlayerInventory boundInventory;
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

        // Cards built at runtime for skills that have no authored card (parallel lists, like the arrays above).
        private readonly List<string> generatedCardIds = new List<string>();
        private readonly List<string> generatedSkillIds = new List<string>();

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            ClientPresentationRegistry.RegisterPointerBlock(this);
            root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null)
            {
                Debug.LogError("ProgressionHUD needs a UXML document.", this);
                return;
            }
            root.pickingMode = PickingMode.Ignore;
            SyntySkin.Decorate(root);
            characterWindow = root.Q("character-window");
            skillsWindow = root.Q("skills-window");
            runTile = root.Q("run-drag");
            jumpTile = root.Q("jump-drag");
            activeBuffsBar = root.Q("active-buffs-bar");
            dragGhost = root.Q<Label>("drag-ghost");

            BindButton("open-character", ToggleCharacter);
            BindButton("open-skills", ToggleSkills);
            BindButton("close-character", HideCharacter);
            BindButton("close-skills", HideSkills);

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
                BindCombatSkillCard(combatSkillCardIds[i], combatSkillAssetIds[i]);
            generatedCardIds.Clear();
            generatedSkillIds.Clear();

            BindPointer<PointerUpEvent>(root, evt => { if (evt.button == LeftButton) StopHold(); });
            BindPointer<PointerCancelEvent>(root, evt => StopHold());

            for (int index = 0; index < slotElements.Length; index++)
            {
                int slotIndex = index;
                VisualElement slot = root.Q($"slot-{index}");
                slotElements[index] = slot;
                // Icon under the labels, cooldown sweep over it. Built here so the UXML keeps one line per slot.
                slotIcons[index] = SlotLayer(slot, "slot-icon", 0);
                slotCooldowns[index] = SlotLayer(slot, "slot-cooldown", 1);
                BindPointer<PointerDownEvent>(slot, evt => OnSlotPointerDown(evt, slotIndex));
                BindDragSource(slot);
            }
            BindPointer<PointerDownEvent>(runTile, evt => OnSkillPointerDown(evt, MovementSkillId.Run, runTile));
            BindDragSource(runTile);
            BindPointer<PointerDownEvent>(jumpTile, evt => OnSkillPointerDown(evt, MovementSkillId.Jump, jumpTile));
            BindDragSource(jumpTile);

            SetVisible(characterWindow, false);
            SetVisible(skillsWindow, false);
            uiReady = true;

            // Follow the local player: the scene's offline avatar, or this peer's networked player when hosting/joining.
            PlayerInventory.LocalChanged += OnLocalPlayerChanged;
            BindPlayer(PlayerInventory.Local != null ? PlayerInventory.Local.gameObject
                : progression != null && progression.gameObject.activeInHierarchy ? progression.gameObject : null);
        }

        private static VisualElement SlotLayer(VisualElement slot, string className, int index)
        {
            VisualElement layer = slot.Q(className: className);
            if (layer != null) return layer; // the HUD re-enabled: reuse what the last pass added
            layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList(className);
            slot.Insert(index, layer);
            return layer;
        }

        /// <summary>Shows <paramref name="sprite"/> on an icon element, or hides the element when there is none.</summary>
        private static void SetIcon(VisualElement element, Sprite sprite)
        {
            if (element == null) return;
            element.style.backgroundImage = sprite != null ? new StyleBackground(sprite) : StyleKeyword.None;
            element.style.display = sprite != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The icon on a skill card's drag tile, created on first use (authored and generated cards alike).</summary>
        private static void SetTokenArt(VisualElement token, Sprite sprite)
        {
            if (token == null) return;
            VisualElement art = token.Q(className: "skill-art");
            if (art == null)
            {
                if (sprite == null) return;
                art = new VisualElement { pickingMode = PickingMode.Ignore };
                art.AddToClassList("skill-art");
                token.Add(art);
            }
            SetIcon(art, sprite);
            token.EnableInClassList("has-art", sprite != null);
        }

        private void BindCombatSkillCard(string cardId, string skillId)
        {
            // Resolved per use: the HUD rebinds to whichever player is local (offline avatar or networked player).
            VisualElement tile = root.Q($"{cardId}-drag");
            if (tile != null)
            {
                BindPointer<PointerDownEvent>(tile, evt =>
                {
                    var def = combatSkills != null ? combatSkills.GetSkill(skillId) : null;
                    if (def != null) OnCombatSkillPointerDown(evt, def, tile);
                });
                BindDragSource(tile);
            }

            Button upgradeBtn = root.Q<Button>($"upgrade-{cardId}");
            if (upgradeBtn != null)
            {
                BindHoldable(upgradeBtn, () =>
                {
                    var def = combatSkills != null ? combatSkills.GetSkill(skillId) : null;
                    if (def != null) UpgradeCombatSkill(def);
                });
            }
        }

        /// <summary>
        /// Skills without a hand-written card in the UXML (the blade skills) get one built here, using the same
        /// element names as the authored cards so refresh, drag and upgrade work identically.
        /// </summary>
        private void EnsureGeneratedSkillCards()
        {
            if (combatSkills == null) return;
            VisualElement body = root.Q("skills-body");
            if (body == null) return;

            var pending = new List<SkillDefinitionSO>();
            foreach (SkillDefinitionSO skill in combatSkills.AvailableSkills)
            {
                if (skill == null || Array.IndexOf(combatSkillAssetIds, skill.skillId) >= 0
                    || generatedSkillIds.Contains(skill.skillId)) continue;
                pending.Add(skill);
            }
            if (pending.Count == 0) return;
            pending.Sort((a, b) => a.requiredCharacterLevel.CompareTo(b.requiredCharacterLevel));

            if (generatedSkillIds.Count == 0)
            {
                var header = new Label("BLADE SKILLS");
                header.AddToClassList("category-header");
                header.AddToClassList("eyebrow");
                header.AddToClassList("section");
                body.Add(header);
            }

            foreach (SkillDefinitionSO skill in pending)
            {
                string cardId = "skill-" + skill.skillId.ToLowerInvariant();
                var card = new VisualElement();
                card.AddToClassList("skill-card");
                card.AddToClassList("kn-card");
                if (skill.requiresSkillBook) card.AddToClassList("rare-skill-card");

                var token = new VisualElement { name = $"{cardId}-drag" };
                token.AddToClassList("skill-token");
                token.AddToClassList(skill.requiresSkillBook ? "rare-token" : "blade-token");
                var icon = new Label(skill.displayName.ToUpperInvariant());
                icon.AddToClassList("skill-icon");
                token.Add(icon);
                var rank = new Label { name = $"{cardId}-rank" };
                rank.AddToClassList("skill-rank");
                token.Add(rank);
                card.Add(token);

                card.Add(CardLabel($"{cardId}-description", "help"));
                card.Add(CardLabel($"{cardId}-values", "section"));
                card.Add(CardLabel($"{cardId}-requirement", skill.requiresSkillBook ? "drop-book-note" : "muted"));
                card.Add(new Button { name = $"upgrade-{cardId}" });
                card.Add(CardLabel($"{cardId}-drag-hint", "help"));
                body.Add(card);

                generatedCardIds.Add(cardId);
                generatedSkillIds.Add(skill.skillId);
                BindCombatSkillCard(cardId, skill.skillId);
            }
        }

        private static Label CardLabel(string name, string className)
        {
            var label = new Label { name = name };
            label.AddToClassList(className);
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private void OnLocalPlayerChanged()
        {
            // Local is briefly null during the offline-to-networked handoff; keep the last player until the next one arrives.
            if (PlayerInventory.Local != null) BindPlayer(PlayerInventory.Local.gameObject);
        }

        /// <summary>Points every player-facing reference at <paramref name="player"/> and moves the event subscriptions.</summary>
        private void BindPlayer(GameObject player)
        {
            if (player != null && boundPlayer == player) return;
            UnbindPlayer();
            if (player == null) return;

            progression = player.GetComponent<PlayerCharacterProgression>();
            skills = player.GetComponent<PlayerMovementSkills>();
            hotbar = player.GetComponent<PlayerSkillHotbar>();
            movement = player.GetComponent<PointAndClickPlayer>();
            resources = player.GetComponent<PlayerResources>();
            jumpMotor = player.GetComponent<PlayerJumpMotor>();
            combatStats = player.GetComponent<CharacterCombatStats>();
            combatSkills = player.GetComponent<PlayerCombatSkills>();
            buffManager = player.GetComponent<CharacterBuffManager>();
            if (progression == null || skills == null || hotbar == null || movement == null || resources == null || jumpMotor == null)
            {
                Debug.LogError($"ProgressionHUD: '{player.name}' is missing progression, skills, hotbar, movement, resources or jump components.", this);
                return;
            }

            resources.Changed += RefreshResources;
            boundInventory = player.GetComponent<PlayerInventory>();
            if (boundInventory != null) boundInventory.Changed += Refresh; // hotbar item counts
            movement.CommandMessage += Feedback;
            jumpMotor.Message += Feedback;
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

            boundPlayer = player;
            initialized = true;
            EnsureGeneratedSkillCards();
            Refresh();
        }

        private void UnbindPlayer()
        {
            if (!initialized) return;
            StopHold();
            CancelDrag();
            resources.Changed -= RefreshResources;
            if (boundInventory != null) boundInventory.Changed -= Refresh;
            boundInventory = null;
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
            initialized = false;
            boundPlayer = null;
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
                RefreshBuffTimers();
                RefreshHotbarCooldowns();
            }
        }

        private void OnDisable()
        {
            ClientPresentationRegistry.UnregisterPointerBlock(this);
            if (!uiReady) return;
            PlayerInventory.LocalChanged -= OnLocalPlayerChanged;
            UnbindPlayer();
            foreach (Action removeCallback in removeUiCallbacks) removeCallback();
            removeUiCallbacks.Clear();
            uiReady = false;
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
            root.Q<Label>("skill-points").text = $"{progression.SkillPoints} skill point(s) available";

            RefreshSkillCard(MovementSkillId.Run, "run", runTile);
            RefreshSkillCard(MovementSkillId.Jump, "jump", jumpTile);

            for (int i = 0; i < combatSkillCardIds.Length; i++)
            {
                string cardId = combatSkillCardIds[i];
                string skillId = combatSkillAssetIds[i];
                RefreshCombatSkillCard(cardId, skillId);
            }
            for (int i = 0; i < generatedCardIds.Count; i++)
                RefreshCombatSkillCard(generatedCardIds[i], generatedSkillIds[i]);

            for (int index = 0; index < slotElements.Length; index++)
            {
                HotbarSlotEntry entry = hotbar.GetSlotEntry(index);
                VisualElement slot = slotElements[index];
                if (slot == null) continue;
                string key = PlayerSkillHotbar.GetKeyLabel(index);
                slot.EnableInClassList("on-cooldown", false); // skills re-apply theirs every frame
                Sprite icon = null;

                if (entry.itemType == HotbarItemType.Movement)
                {
                    MovementSkillData def = skills.GetDefinition(entry.movementSkill);
                    icon = def != null ? def.icon : null;
                    slot.Q<Label>(className: "slot-name").text = def == null ? "—" : def.displayName.ToUpperInvariant();
                    slot.Q<Label>(className: "slot-rank").text = def == null ? "EMPTY" : $"RANK {skills.GetRank(entry.movementSkill)}";
                    slot.EnableInClassList("selected", entry.movementSkill != MovementSkillId.None && entry.movementSkill == skills.SelectedSkill);
                    slot.tooltip = $"{def?.displayName}: click or press {key}. Right-click to clear.";
                }
                else if (entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill != null)
                {
                    SkillDefinitionSO def = entry.combatSkill;
                    icon = def.icon;
                    slot.Q<Label>(className: "slot-name").text = def.displayName.ToUpperInvariant();
                    int rank = combatSkills != null ? combatSkills.GetRank(def) : 0;
                    slot.Q<Label>(className: "slot-rank").text = $"RANK {rank}";
                    slot.EnableInClassList("selected", false);
                    string hold = def.IsCharged ? $"hold {key} to charge" : def.holdToRepeat ? $"press or hold {key}" : $"click or press {key}";
                    slot.tooltip = $"{def.displayName}: {hold} to activate. Right-click to clear.";
                }
                else if (entry.itemType == HotbarItemType.Item && hotbar.ItemSource != null)
                {
                    int count = hotbar.ItemSource.CountItem(entry.itemId);
                    icon = hotbar.ItemSource.GetItemIcon(entry.itemId);
                    slot.Q<Label>(className: "slot-name").text = hotbar.ItemSource.GetItemLabel(entry.itemId).ToUpperInvariant();
                    slot.Q<Label>(className: "slot-rank").text = $"x{count}";
                    slot.EnableInClassList("selected", false);
                    slot.EnableInClassList("on-cooldown", count == 0);
                    slot.tooltip = $"Click or press {key} to use. Right-click to clear.";
                }
                else
                {
                    slot.Q<Label>(className: "slot-name").text = "—";
                    slot.Q<Label>(className: "slot-rank").text = "EMPTY";
                    slot.EnableInClassList("selected", false);
                    slot.tooltip = "Drop a learned skill here.";
                }

                // With an icon the name is left to the tooltip; without one the slot keeps its text label.
                SetIcon(slotIcons[index], icon);
                slot.EnableInClassList("has-icon", icon != null);
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

                Label critDmgLabel = root.Q<Label>("combat-stat-crit-dmg");
                if (critDmgLabel != null)
                {
                    float baseCrit = combatStats.TuningData != null ? combatStats.TuningData.baseCriticalMultiplier * 100f : 150f;
                    critDmgLabel.text = $"{baseCrit + combatStats.GetStatValue(StatType.CriticalDamagePercent):0.#}%";
                }

                Label eleAtkLabel = root.Q<Label>("combat-stat-ele-atk");
                if (eleAtkLabel != null) eleAtkLabel.text = Mathf.RoundToInt(combatStats.GetStatValue(StatType.ElementalAttack)).ToString();

                Label eleDefLabel = root.Q<Label>("combat-stat-ele-def");
                if (eleDefLabel != null) eleDefLabel.text = Mathf.RoundToInt(combatStats.GetStatValue(StatType.ElementalDefense)).ToString();

                Label luckLabel = root.Q<Label>("vital-luck");
                if (luckLabel != null) luckLabel.text = Mathf.RoundToInt(combatStats.GetStatValue(StatType.Luck)).ToString();

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

            // "Drop skill" below means a book skill whose book has not been acquired yet (still locked).
            bool isDropSkill = combatSkills.IsLocked(skill);
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
                    dragHint.text = rank == 0 ? $"Learn {skill.displayName}, then drag to a hotbar slot."
                        : skill.IsCharged ? "Drag to a hotbar slot (1–0, Q W E R), then hold the key to charge."
                        : skill.holdToRepeat ? "Drag to a hotbar slot (1–0, Q W E R). Hold the key to keep attacking."
                        : "Drag to a hotbar slot (1–0, Q W E R) to activate.";
            }

            VisualElement tile = root.Q($"{cardPrefix}-drag");
            if (tile != null)
            {
                tile.tooltip = isLearned ? $"Drag {skill.displayName} to hotbar" : isDropSkill ? skill.acquisitionNote : "Learn before assigning";
                SetTokenArt(tile, skill.icon);
            }
        }

        private static string DescribeSkillStats(SkillDefinitionSO skill, int rank)
        {
            float chi = skill.GetChiCost(rank);
            float dur = skill.GetDuration(rank);
            float progress = skill.GetRankProgress(rank);

            if (skill.IsAttack)
            {
                float damage = Mathf.Lerp(skill.minDamagePercent, skill.maxDamagePercent, progress);
                float bonus = Mathf.Lerp(skill.minBonusDamagePercent, skill.maxBonusDamagePercent, progress);
                string hits = skill.hitCount > 1 ? $" over {skill.hitCount} hits" : string.Empty;
                string yang = bonus > 100f ? $" · Yang {bonus:0.#}%" : string.Empty;
                string reach = skill.IsAreaAttack ? $"{skill.GetRange(rank):0.#} m radius (all enemies)" : $"{skill.GetRange(rank):0.#} m reach";
                return $"{damage:0}% damage{hits}{yang} · {reach} · {chi:0} Chi";
            }
            if (skill.rankScaledModifiers != null && skill.rankScaledModifiers.Length > 0)
            {
                var parts = new List<string>();
                foreach (var mod in skill.rankScaledModifiers)
                    parts.Add($"+{Mathf.Lerp(mod.minValue, mod.maxValue, progress):0.#}% {DescribeStat(mod.statType)}");
                string scope = skill.consumedOnNextHit ? $"next hit, within {dur:0}s" : $"{dur:0}s duration";
                return $"{string.Join(" · ", parts)} · {scope} · {chi:0} Chi";
            }

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

        private static string DescribeStat(StatType stat)
        {
            switch (stat)
            {
                case StatType.AttackPower: return "Damage";
                case StatType.CriticalRate: return "Critical Rate";
                case StatType.ElementalDefense: return "Elemental Defense";
                case StatType.ElementalAttack: return "Elemental Attack";
                case StatType.DefensePower: return "Defense";
                default: return stat.ToString();
            }
        }

        /// <summary>Rebuilds the buff badges when the set of buffs changes; timers tick in <see cref="RefreshBuffTimers"/>.</summary>
        private void RefreshBuffs()
        {
            if (activeBuffsBar == null || buffManager == null) return;
            activeBuffsBar.Clear();
            shownBuffs.Clear();
            shownBuffTimers.Clear();

            var buffs = buffManager.ActiveBuffs;
            for (int i = 0; i < buffs.Count; i++)
            {
                var buff = buffs[i];
                var badge = new VisualElement();
                badge.AddToClassList("buff-badge");
                badge.tooltip = buff.Skill.displayName;

                if (buff.Skill.isAbsorptionShield)
                    badge.AddToClassList("shield-badge");
                else if (buff.Skill.executionType == SkillExecutionType.Channeled)
                    badge.AddToClassList("channeled-badge");

                if (buff.Skill.icon != null)
                {
                    var icon = new VisualElement();
                    icon.AddToClassList("buff-badge-icon");
                    SetIcon(icon, buff.Skill.icon);
                    badge.Add(icon);
                }
                else
                {
                    var nameLbl = new Label(buff.Skill.displayName);
                    nameLbl.AddToClassList("buff-badge-name");
                    badge.Add(nameLbl);
                }

                var timerLbl = new Label();
                timerLbl.AddToClassList("buff-badge-timer");
                badge.Add(timerLbl);

                activeBuffsBar.Add(badge);
                shownBuffs.Add(buff);
                shownBuffTimers.Add(timerLbl);
            }
            RefreshBuffTimers();
        }

        private void RefreshBuffTimers()
        {
            if (activeBuffsBar == null || buffManager == null) return;
            if (buffManager.ActiveBuffs.Count != shownBuffs.Count) { RefreshBuffs(); return; }
            for (int i = 0; i < shownBuffs.Count; i++)
            {
                var buff = shownBuffs[i];
                shownBuffTimers[i].text = buff.Skill.isAbsorptionShield
                    ? $"{buff.CurrentShieldCapacity:0} HP ({buff.RemainingDuration:0}s)"
                    : $"{buff.RemainingDuration:0.0}s";
            }
        }

        private void RefreshHotbarCooldowns()
        {
            if (combatSkills == null) return;
            for (int index = 0; index < slotElements.Length; index++)
            {
                if (slotElements[index] == null) continue;
                var entry = hotbar.GetSlotEntry(index);
                bool charging = hotbar.ChargingSlot == index;
                slotElements[index].EnableInClassList("charging", charging);
                float cooldownLeft01 = 0f;
                if (entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill != null)
                {
                    bool onCooldown = combatSkills.IsOnCooldown(entry.combatSkill);
                    slotElements[index]?.EnableInClassList("on-cooldown", onCooldown);
                    if (onCooldown && entry.combatSkill.cooldown > 0f)
                        cooldownLeft01 = Mathf.Clamp01(combatSkills.GetRemainingCooldown(entry.combatSkill) / entry.combatSkill.cooldown);
                    if (charging)
                    {
                        slotElements[index].Q<Label>(className: "slot-rank").text = $"CHARGE {hotbar.ChargeProgress * 100f:0}%";
                    }
                    else if (onCooldown)
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
                // The sweep drains downward as the cooldown runs out.
                slotCooldowns[index].style.height = Length.Percent(cooldownLeft01 * 100f);
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
            RefreshVitals();
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

        /// <summary>
        /// Character-page vitals. Totals are the same server-synced maximums the HUD bars use; the breakdown splits them
        /// into class/level base, the Vitality/Spirit contribution, and flat/percent pool bonuses (gear, pets, mounts, buffs).
        /// </summary>
        private void RefreshVitals()
        {
            Label hpLabel = root.Q<Label>("vital-hp");
            if (hpLabel != null) hpLabel.text = $"{resources.Health:0} / {resources.MaxHealth:0}";
            Label chiLabel = root.Q<Label>("vital-chi");
            if (chiLabel != null) chiLabel.text = $"{resources.Chi:0} / {resources.MaxChi:0}";
            if (combatStats == null) return;

            Label hpBreakdown = root.Q<Label>("vital-hp-breakdown");
            if (hpBreakdown != null)
            {
                combatStats.GetPoolBreakdown(StatType.MaxHP, out float hpBase, out float fromVit, out float hpBonus);
                hpBreakdown.text = $"Base {hpBase:0} · Vitality {FormatSigned(fromVit)} · Gear/Pets/Mounts {FormatSigned(hpBonus)}";
            }
            Label chiBreakdown = root.Q<Label>("vital-chi-breakdown");
            if (chiBreakdown != null)
            {
                combatStats.GetPoolBreakdown(StatType.MaxChi, out float chiBase, out float fromSpirit, out float chiBonus);
                chiBreakdown.text = $"Base {chiBase:0} · Spirit {FormatSigned(fromSpirit)} · Gear/Pets/Mounts {FormatSigned(chiBonus)}";
            }
        }

        private static string FormatSigned(float value) => value >= 0f ? $"+{value:0}" : $"{value:0}";

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
            SetTokenArt(tile, definition.icon);
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
            else if (entry.itemType == HotbarItemType.Item)
            {
                // Items are placed from the inventory; on the bar a click simply uses one.
                hotbar.ActivateSlot(index);
                suppressWorldInputFrame = Time.frameCount;
                evt.StopPropagation();
            }
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

            MovementSkillData draggedMovement = draggedCombatSkill == null ? skills.GetDefinition(draggedMovementSkill) : null;
            Sprite ghostIcon = draggedCombatSkill != null ? draggedCombatSkill.icon : draggedMovement.icon;
            string ghostText = draggedCombatSkill != null
                ? draggedCombatSkill.displayName.ToUpperInvariant()
                : draggedMovement.displayName.ToUpperInvariant();
            // The icon itself follows the cursor; skills without one keep the text chip.
            dragGhost.text = ghostIcon != null ? string.Empty : ghostText;
            dragGhost.EnableInClassList("icon-ghost", ghostIcon != null);
            dragGhost.style.backgroundImage = ghostIcon != null ? new StyleBackground(ghostIcon) : StyleKeyword.Null;

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
                    Feedback($"Swapped hotbar slots {PlayerSkillHotbar.GetKeyLabel(sourceSlot)} and {PlayerSkillHotbar.GetKeyLabel(targetSlot)}.");
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
                        ? $"{name} assigned to key {PlayerSkillHotbar.GetKeyLabel(targetSlot)}. Press the key or click the slot to activate."
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
            if (hotbar != null) hotbar.InputSuppressed = false;
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
            if (movement != null) movement.ReleaseCameraControl();
            StopHold();
            CancelDrag();
            bool show = window.resolvedStyle.display == DisplayStyle.None;
            SetVisible(window, show);
            if (show) window.BringToFront();
        }
        private void CloseWindows() { HideCharacter(); HideSkills(); }

        private void OnLevelGained(int level) => Feedback($"Level {level} reached. New stat and skill points awarded.");

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
