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
        private const float ProgressBarScale = 100f;
        private const string BlockingClass = "blocks-world";
        [SerializeField] private PlayerCharacterProgression progression;
        [SerializeField] private PlayerMovementSkills skills;
        [SerializeField] private PlayerSkillHotbar hotbar;
        [SerializeField] private PointAndClickPlayer movement;
        [SerializeField] private PlayerResources resources;
        [SerializeField] private PlayerJumpMotor jumpMotor;
        private PlayerWallRunMotor wallRunMotor;
        private PlayerWaterstrideMotor waterMotor;
        [SerializeField] private CharacterCombatStats combatStats;
        [SerializeField] private PlayerCombatSkills combatSkills;
        [SerializeField] private CharacterBuffManager buffManager;
        private VisualElement root;
        private Hud.ComboMeter comboMeter; // the Cadence combo's COMBO word
        private VisualElement characterWindow;
        private PlayerNova boundNova;
        private PlayerSerums boundSerums;
        // The Serum lines made so far. Kept here: a skinned card has children of its own (its frame), so the list's
        // children are not just these.
        private readonly List<VisualElement> serumRows = new List<VisualElement>(SerumSettings.MaxKinds);
        private VisualElement serumRowsOwner;
        private bool renownTipAttached;

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
        private readonly List<int> shownBuffClocks = new List<int>();

        // The HUD is redrawn in pieces. Its parts are found once, here, not looked up by name on every redraw, and
        // each piece remembers what it last showed: a redraw that changes nothing writes no text and makes no string.
        private Label levelLabel, modeLabel, pointsSummary, resourceValues, resourceFormula;
        private Label vitalHp, vitalChi, vitalHpBreakdown, vitalChiBreakdown;
        private ProgressBar xpBar, hpBar, chiBar;
        private readonly Label[] slotNames = new Label[PlayerSkillHotbar.SlotCount];
        private readonly Label[] slotRanks = new Label[PlayerSkillHotbar.SlotCount];
        private readonly SlotShown[] slotShown = new SlotShown[PlayerSkillHotbar.SlotCount];
        private readonly SlotLine[] slotLines = new SlotLine[PlayerSkillHotbar.SlotCount];
        private const int Unknown = int.MinValue;
        private int shownLevel = Unknown, shownMode = Unknown, shownStatPoints = Unknown, shownSkillPoints = Unknown;
        private int shownExperience = Unknown, shownExperienceRequired = Unknown;
        private bool shownAtCap;
        private int shownHealth = Unknown, shownMaxHealth = Unknown, shownChi = Unknown, shownMaxChi = Unknown;
        private float barHealth = -1f, barMaxHealth = -1f, barChi = -1f, barMaxChi = -1f;

        /// <summary>What a Hotbar slot was last drawn from. Unchanged, its name, tooltip and icon are left alone.</summary>
        private struct SlotShown
        {
            public bool Drawn;
            public HotbarItemType Type;
            public MovementSkillId Movement;
            public UnityEngine.Object Source;
            public int Item, Number;
            public bool Selected;

            public bool Same(in SlotShown other) => Drawn && other.Drawn && Type == other.Type && Movement == other.Movement
                && ReferenceEquals(Source, other.Source) && Item == other.Item && Number == other.Number && Selected == other.Selected;
        }

        /// <summary>The small line of a slot: which kind of text it holds and the number in it.</summary>
        private struct SlotLine
        {
            public SlotLineKind Kind;
            public int Number;
        }

        private enum SlotLineKind : byte { Unset, Empty, Rank, Count, Cooldown, Charge }

        // A new player, or the HUD back on: nothing shown is taken to be still right.
        private void ForgetShown()
        {
            shownLevel = shownMode = shownStatPoints = shownSkillPoints = Unknown;
            shownExperience = shownExperienceRequired = Unknown;
            shownHealth = shownMaxHealth = shownChi = shownMaxChi = Unknown;
            barHealth = barMaxHealth = barChi = barMaxChi = -1f;
            for (int i = 0; i < slotShown.Length; i++)
            {
                slotShown[i] = default;
                slotLines[i] = default;
            }
        }

        private void SetSlotLine(int index, SlotLineKind kind, int number)
        {
            if (slotLines[index].Kind == kind && slotLines[index].Number == number) return;
            slotLines[index].Kind = kind;
            slotLines[index].Number = number;
            Label line = slotRanks[index];
            if (line == null) return;
            switch (kind)
            {
                case SlotLineKind.Rank: line.text = $"RANK {number}"; break;
                case SlotLineKind.Count: line.text = $"x{number}"; break;
                case SlotLineKind.Cooldown: line.text = $"{number / 10}.{number % 10}s"; break;
                case SlotLineKind.Charge: line.text = $"CHARGE {number}%"; break;
                default: line.text = "EMPTY"; break;
            }
        }

        // As "0" and "0.0" print them: halves go up.
        private static int Whole(float value) => Mathf.FloorToInt(value + 0.5f);
        private static int Tenths(float value) => Mathf.FloorToInt(value * 10f + 0.5f);
        private VisualElement dragSource;
        private MovementSkillId draggedMovementSkill;
        private SkillDefinitionSO draggedCombatSkill;
        private int draggedFromSlot = NoSlot;
        private bool draggedAutobuff; // the Autobuff tile is what is dragged (from its window or a slot)

        /// <summary>A place a dragged skill may be dropped besides the Hotbar (the Autobuff's cells in the Autopilot window).</summary>
        private struct DropTarget
        {
            public VisualElement Element;
            public Func<SkillDefinitionSO, bool> Accepts;
            public Action<SkillDefinitionSO> Dropped;
        }
        private readonly List<DropTarget> dropTargets = new List<DropTarget>(8);

        /// <summary>Lets an element outside the HUD's own windows take a dragged combat skill (the Autobuff's cells).</summary>
        public void RegisterDropTarget(VisualElement element, Func<SkillDefinitionSO, bool> accepts, Action<SkillDefinitionSO> dropped)
        {
            if (element == null) return;
            UnregisterDropTarget(element);
            dropTargets.Add(new DropTarget { Element = element, Accepts = accepts, Dropped = dropped });
        }

        public void UnregisterDropTarget(VisualElement element) => dropTargets.RemoveAll(t => t.Element == element);

        /// <summary>Makes an element outside the HUD's own windows a source of drags (it must call Begin... on a press).</summary>
        public void BindExternalDragSource(VisualElement source) => BindDragSource(source);

        /// <summary>The Autobuff tile in the Autopilot window was pressed: it may be dragged to a Hotbar slot.</summary>
        public void BeginExternalAutobuffDrag(VisualElement source, PointerDownEvent evt)
        {
            if (evt.button == LeftButton) BeginDragAutobuff(source, NoSlot, evt);
        }

        /// <summary>A buff in an Autobuff cell was pressed: it may be dragged to a Hotbar slot or another cell.</summary>
        public void BeginExternalSkillDrag(VisualElement source, SkillDefinitionSO skill, PointerDownEvent evt)
        {
            if (skill != null) OnCombatSkillPointerDown(evt, skill, source);
        }

        private bool HotbarHasAutobuff => hotbar != null && hotbar.Autobuff != null;
        private int dragPointerId;
        private Vector2 pointerStart;
        private bool dragMoved;
        private bool initialized; // a player is bound
        private bool uiReady;
        private GameObject boundPlayer;
        private PlayerInventory boundInventory;
        private int suppressWorldInputFrame = -1;
        // A held "+" repeats after this long, this often (milliseconds); a refused spend stops it until the next press.
        private const long HoldDelayMs = 350, HoldRepeatMs = 80;
        private bool holdStopped;
        // How many points one press of a "+" spends: one choice for the Character window, one for the Arsenal.
        private readonly StepDropdown statSteps = new StepDropdown("kn.statStep");
        private readonly StepDropdown skillSteps = new StepDropdown("kn.skillStep");
        private int statStep => statSteps.Step;
        private int skillStep => skillSteps.Step;

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
        private Label classSkillsHeader; // names the weapon class whose skills are shown

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
            comboMeter?.Dispose();
            comboMeter = new Hud.ComboMeter(root);
            characterWindow = root.Q("character-window");
            skillsWindow = root.Q("skills-window");
            levelLabel = root.Q<Label>("level-label");
            modeLabel = root.Q<Label>("mode-label");
            pointsSummary = root.Q<Label>("points-summary");
            resourceValues = root.Q<Label>("resource-values");
            resourceFormula = root.Q<Label>("resource-formula");
            vitalHp = root.Q<Label>("vital-hp");
            vitalChi = root.Q<Label>("vital-chi");
            vitalHpBreakdown = root.Q<Label>("vital-hp-breakdown");
            vitalChiBreakdown = root.Q<Label>("vital-chi-breakdown");
            xpBar = root.Q<ProgressBar>("xp-bar");
            hpBar = root.Q<ProgressBar>("hp-bar");
            chiBar = root.Q<ProgressBar>("chi-bar");
            ForgetShown();
            // Shown by the layout editor to be placed, they are brought up to date as when they open.
            HudLayoutEditor.RegisterPreview("character-window", on => { if (on) RefreshShown(characterWindow); });
            HudLayoutEditor.RegisterPreview("skills-window", on => { if (on) RefreshShown(skillsWindow); });
            runTile = root.Q("run-drag");
            jumpTile = root.Q("jump-drag");
            activeBuffsBar = root.Q("active-buffs-bar");
            boostList = new BoostListView(root.Q("boost-list"));
            boostList.AddSource(AddBoosterLines);
            boostList.AddSource(AddPassLines);
            // The Skills window is a tree: tiles on tiers, the chosen skill's card beside them.
            VisualElement skillCards = root.Q("skills-body");
            skillTree = null;
            if (skillsWindow != null && skillCards != null)
            {
                skillTree = new SkillTreeView(BindDragSource);
                skillTree.Build(skillsWindow, skillCards);
                skillTree.Pressed += OnSkillTreePressed;
                skillTree.TabChanged += _ => RefreshSkillTree();
                // The list's own lines between its sections (headings, a note on locked skills) have no place beside
                // a tree: what a locked skill wants is on its card.
                foreach (VisualElement loose in skillCards.contentContainer.Children())
                    if (loose is Label && (loose.ClassListContains("drop-book-note") || loose.ClassListContains("category-header")))
                        loose.style.display = DisplayStyle.None;
            }
            dragGhost = root.Q<Label>("drag-ghost");
            // What is dragged sits on the cursor, by its middle, whatever its size (the icon or the text chip).
            if (dragGhost != null) dragGhost.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));

            BindButton("open-character", ToggleCharacter);
            BindButton("open-skills", ToggleSkills);
            BindButton("close-character", HideCharacter);
            BindButton("close-skills", HideSkills);

            statSteps.Picked += OnStepPicked;
            skillSteps.Picked += OnStepPicked;
            removeUiCallbacks.Add(() => { statSteps.Picked -= OnStepPicked; skillSteps.Picked -= OnStepPicked; });

            for (int index = 0; index < AttributeCount; index++)
            {
                CharacterAttribute attribute = (CharacterAttribute)index;
                Button button = root.Q<Button>($"allocate-{index}");
                if (button != null)
                {
                    BindHoldable(button, () => AllocateAttribute(attribute), statSteps);
                }
            }

            Button runUpgrade = root.Q<Button>("upgrade-run");
            if (runUpgrade != null)
            {
                BindHoldable(runUpgrade, () => UpgradeSkill(MovementSkillId.Run), skillSteps);
            }

            Button jumpUpgrade = root.Q<Button>("upgrade-jump");
            if (jumpUpgrade != null)
            {
                BindHoldable(jumpUpgrade, () => UpgradeSkill(MovementSkillId.Jump), skillSteps);
            }

            // Bind combat skill cards
            for (int i = 0; i < combatSkillCardIds.Length; i++)
                BindCombatSkillCard(combatSkillCardIds[i], combatSkillAssetIds[i]);
            generatedCardIds.Clear();
            generatedSkillIds.Clear();
            classSkillsHeader = null;

            // Every press starts a hold afresh (the root hears it first, on the way down to the button).
            EventCallback<PointerDownEvent> pressStarts = _ => holdStopped = false;
            root.RegisterCallback(pressStarts, TrickleDown.TrickleDown);
            removeUiCallbacks.Add(() => root.UnregisterCallback(pressStarts, TrickleDown.TrickleDown));

            for (int index = 0; index < slotElements.Length; index++)
            {
                int slotIndex = index;
                VisualElement slot = root.Q($"slot-{index}");
                slotElements[index] = slot;
                // Icon under the labels, cooldown sweep over it. Built here so the UXML keeps one line per slot.
                slotIcons[index] = SlotLayer(slot, "slot-icon", 0);
                slotCooldowns[index] = SlotLayer(slot, "slot-cooldown", 1);
                slotNames[index] = slot.Q<Label>(className: "slot-name");
                slotRanks[index] = slot.Q<Label>(className: "slot-rank");
                BindPointer<PointerDownEvent>(slot, evt => OnSlotPointerDown(evt, slotIndex));
                BindDragSource(slot);
                // What the slot holds, read over it while the pointer rests there (owner, 15 Oct 2026).
                slot.RegisterCallback<PointerEnterEvent>(_ => ShowSlotTooltip(slotIndex));
                slot.RegisterCallback<PointerLeaveEvent>(_ => HideSlotTooltip(slotIndex));
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
                }, skillSteps);
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
            // Class by class, each in slot order (S1..S8, Stance, Guard); shared skills by level.
            pending.Sort((a, b) =>
            {
                int c = a.weaponClass.CompareTo(b.weaponClass);
                if (c != 0) return c;
                c = a.classSlot.CompareTo(b.classSlot);
                return c != 0 ? c : a.requiredCharacterLevel.CompareTo(b.requiredCharacterLevel);
            });

            if (generatedSkillIds.Count == 0)
            {
                classSkillsHeader = new Label("CLASS SKILLS");
                classSkillsHeader.AddToClassList("category-header");
                classSkillsHeader.AddToClassList("eyebrow");
                classSkillsHeader.AddToClassList("section");
                body.Add(classSkillsHeader);
            }

            foreach (SkillDefinitionSO skill in pending)
            {
                string cardId = "skill-" + skill.skillId.ToLowerInvariant();
                var card = new VisualElement { name = $"{cardId}-card" };
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

        /// <summary>
        /// Only the skills of the weapon class in hand are listed (shared skills always are). The other classes'
        /// cards stay built and hidden, so swapping weapons swaps the list at once.
        /// </summary>
        private void RefreshClassCards()
        {
            if (combatSkills == null) return;
            for (int i = 0; i < generatedCardIds.Count; i++)
            {
                SkillCardParts parts = PartsOfCard(generatedCardIds[i]);
                if (parts.Card == null) parts.Card = root.Q($"{generatedCardIds[i]}-card");
                VisualElement card = parts.Card;
                if (card == null) continue;
                var skill = combatSkills.GetSkill(generatedSkillIds[i]);
                card.style.display = skill != null && combatSkills.IsInActiveKit(skill) ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (classSkillsHeader == null) return;
            var active = combatSkills.ActiveClass;
            classSkillsHeader.text = active != WeaponClassId.None
                ? $"{WeaponClasses.DisplayName(active).ToUpperInvariant()} SKILLS" : "CLASS SKILLS";
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
            wallRunMotor = player.GetComponent<PlayerWallRunMotor>();
            waterMotor = player.GetComponent<PlayerWaterstrideMotor>();
            combatStats = player.GetComponent<CharacterCombatStats>();
            combatSkills = player.GetComponent<PlayerCombatSkills>();
            buffManager = player.GetComponent<CharacterBuffManager>();
            boosts = player.GetComponent<PlayerExperienceBoosts>();
            boundNova = player.GetComponent<PlayerNova>();
            boundSerums = player.GetComponent<PlayerSerums>();
            if (progression == null || skills == null || hotbar == null || movement == null || resources == null || jumpMotor == null)
            {
                Debug.LogError($"ProgressionHUD: '{player.name}' is missing progression, skills, hotbar, movement, resources or jump components.", this);
                return;
            }

            resources.Changed += RefreshResources;
            boundInventory = player.GetComponent<PlayerInventory>();
            if (boundInventory != null) boundInventory.Changed += RequestRefresh; // hotbar item counts
            movement.CommandMessage += Feedback;
            jumpMotor.Message += Feedback;
            if (combatStats != null) combatStats.OnStatsRecalculated += RequestRefresh;
            if (combatSkills != null)
            {
                combatSkills.Changed += RequestRefresh;
                combatSkills.SkillMessage += Feedback;
            }
            if (buffManager != null)
            {
                buffManager.BuffsChanged += RefreshBuffs;
                buffManager.BuffMessage += Feedback;
            }
            if (boosts != null) boosts.Changed += RefreshBoostList;
            progression.Changed += RequestRefresh;
            progression.LevelGained += OnLevelGained;
            if (boundNova != null) boundNova.Changed += RequestRefresh; // the Standing worn changes no stat
            skills.Changed += RequestRefresh;
            hotbar.Changed += RequestRefresh;
            hotbar.CharacterWindowRequested += ToggleCharacter;
            hotbar.SkillsWindowRequested += ToggleSkills;
            hotbar.CancelRequested += CloseWindows;

            boundPlayer = player;
            initialized = true;
            ForgetShown();
            EnsureGeneratedSkillCards();
            Refresh();
            // The two windows are drawn once while the player is arriving, so the first time each is opened costs no
            // more than any other (finding a hundred skill cards' parts takes a tenth of a second). Not on this
            // frame, which is the one the player is being made on and long enough: one window on each of the next two.
            windowsToDraw = 2;
            drawWindowsFrom = Time.frameCount + 2;
        }

        // The Character Sheet and the Skills window still owed their first drawing after a player arrived, and the
        // frame from which they are drawn.
        private int windowsToDraw, drawWindowsFrom;

        private void UnbindPlayer()
        {
            if (!initialized) return;
            StopHold();
            CancelDrag();
            resources.Changed -= RefreshResources;
            if (boundInventory != null) boundInventory.Changed -= RequestRefresh;
            boundInventory = null;
            movement.CommandMessage -= Feedback;
            jumpMotor.Message -= Feedback;
            if (combatStats != null) combatStats.OnStatsRecalculated -= RequestRefresh;
            if (combatSkills != null)
            {
                combatSkills.Changed -= RequestRefresh;
                combatSkills.SkillMessage -= Feedback;
            }
            if (buffManager != null)
            {
                buffManager.BuffsChanged -= RefreshBuffs;
                buffManager.BuffMessage -= Feedback;
            }
            if (boosts != null) boosts.Changed -= RefreshBoostList;
            progression.Changed -= RequestRefresh;
            progression.LevelGained -= OnLevelGained;
            if (boundNova != null) boundNova.Changed -= RequestRefresh;
            boundNova = null;
            boundSerums = null;
            skills.Changed -= RequestRefresh;
            hotbar.Changed -= RequestRefresh;
            hotbar.CharacterWindowRequested -= ToggleCharacter;
            hotbar.SkillsWindowRequested -= ToggleSkills;
            hotbar.CancelRequested -= CloseWindows;
            initialized = false;
            boundPlayer = null;
        }

        private void Start() => Refresh();

        // Something the HUD shows has changed. It is redrawn once, at the end of the frame, however many things
        // said so in it: a single buff cast raises three or four of these, and each used to redraw everything.
        private bool refreshWanted;
        // A window that is shut is not redrawn while it is shut: it is brought up to date when it opens.
        private bool characterStale = true, skillsStale = true;

        private void RequestRefresh() => refreshWanted = true;

        private void LateUpdate()
        {
            if (windowsToDraw > 0 && initialized && Time.frameCount >= drawWindowsFrom)
            {
                // A window opened meanwhile has been drawn already: it is not drawn twice.
                if (windowsToDraw == 2) { if (characterStale) RefreshCharacterSheet(); }
                else if (skillsStale) RefreshSkillList();
                windowsToDraw--;
                return;
            }
            if (!refreshWanted) return;
            refreshWanted = false;
            using var timed = KryticalNova.Diagnostics.PerformanceLog.Time("hud.redraw");
            Refresh();
        }

        private void Update()
        {
            comboMeter?.Tick();
            if ((statSteps.IsOpen || skillSteps.IsOpen) && UnityEngine.InputSystem.Keyboard.current != null
                && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                statSteps.Close();
                skillSteps.Close();
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
            comboMeter?.Dispose();
            comboMeter = null;
            if (!uiReady) return;
            HudLayoutEditor.UnregisterPreview("character-window");
            HudLayoutEditor.UnregisterPreview("skills-window");
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

        /// <summary>
        /// A "+" button: Unity's own repeating click spends once on the press, again every 80 ms once it has been held
        /// for 350 ms, and never on the release (the old hold added one more point when let go). The ▾ beside it
        /// picks how much one press spends. A spend that cannot happen (no points left) stops the hold until the next
        /// press, so a held button does not repeat the refusal.
        /// </summary>
        private void BindHoldable(Button button, Action onAction, StepDropdown steps)
        {
            steps?.Attach(button);
            button.clickable = new Clickable(() => { if (!holdStopped) onAction(); }, HoldDelayMs, HoldRepeatMs);
        }

        private void StopHold()
        {
            holdStopped = true;
        }

        // Every "+" of the window shows the new step.
        private void OnStepPicked(int step) => Refresh();

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
            if (dragSource != null || suppressWorldInputFrame == Time.frameCount) return true;
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel,
                new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            VisualElement picked = root.panel.Pick(panelPoint);
            for (VisualElement element = picked; element != null; element = element.parent)
                if (element.ClassListContains(BlockingClass)) return true;
            return false;
        }

        /// <summary>The Renown block at the top of the Character Sheet: what is held, the Ascension and the Standing worn.</summary>
        private void RefreshRenown()
        {
            VisualElement block = root.Q("renown-block");
            if (block == null) return;
            if (!renownTipAttached)
            {
                renownTipAttached = true;
                HudTextTooltip.Attach(block, () => "RENOWN", RenownBreakdown);
            }
            NovaSettings rules = boundNova != null ? boundNova.Settings : null;
            root.Q<Label>("renown-value").text = progression.Renown.ToString("N0");
            root.Q<Label>("renown-ascension").text = rules != null
                ? NovaRules.AscensionLine(rules.ascension, boundNova.Ascension).Replace("Ascension ", string.Empty) : "+0";
            root.Q<Label>("renown-standing").text = (boundNova != null ? boundNova.WornStanding : null) ?? "None";
        }

        /// <summary>
        /// The Serums section, at the end of the sheet under the stats: one entry for every kind, how many the character has taken of
        /// how many and, on a line under it, what they add in all ("Strength Serum   37 / 100" over "+111 Attack
        /// Power"; "0 / 100" and "+0 ..." for a kind not taken yet, so the section is always there to be read). It is
        /// the last section of the sheet. The stats below already include it: it
        /// reaches them as modifiers, like gear. Redrawn with the stats.
        /// </summary>
        private void RefreshSerums()
        {
            VisualElement block = root.Q("serum-block");
            VisualElement list = root.Q("serum-list");
            if (block == null || list == null) return;
            SerumSettings rules = boundSerums != null ? boundSerums.Settings : null;
            SerumState taken = boundSerums != null ? boundSerums.State : default;
            block.style.display = rules != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (rules == null) return;
            if (serumRowsOwner != list)
            {
                serumRowsOwner = list; // the HUD was rebuilt: the old lines went with it
                serumRows.Clear();
            }

            int row = 0, kinds = SerumRules.KindCount(rules);
            for (int kind = 0; kind < kinds; kind++)
            {
                int count = taken.Taken(kind);
                if (row == serumRows.Count) serumRows.Add(AddSerumRow(list));
                VisualElement line = serumRows[row];
                row++;
                line.style.display = DisplayStyle.Flex;
                ((Label)line[0]).text = rules.serums[kind].displayName;
                ((Label)line[1]).text = $"{count} / {rules.Cap}";
                ((Label)line[2]).text = SerumRules.Gains(rules.serums[kind], count);
            }
            for (int i = row; i < serumRows.Count; i++) serumRows[i].style.display = DisplayStyle.None;
        }

        private static VisualElement AddSerumRow(VisualElement list)
        {
            // Two lines a kind: its name with how many were taken of how many, and under it what they add in all
            // (a total such as "+2 Hit Rate, +2 Evasion" does not fit beside the name).
            var line = new VisualElement();
            line.AddToClassList("combat-stat-row");
            line.AddToClassList("serum-row");
            var name = new Label();
            name.AddToClassList("combat-stat-label");
            var taken = new Label();
            taken.AddToClassList("combat-stat-label");
            var total = new Label();
            total.AddToClassList("combat-stat-value");
            total.AddToClassList("serum-gain");
            line.Add(name);
            line.Add(taken);
            line.Add(total);
            list.Add(line);
            return line;
        }

        /// <summary>What the hover on the Renown block says: where Renown goes, and what both ladders give right now.</summary>
        private string RenownBreakdown()
        {
            var text = new System.Text.StringBuilder(320);
            text.Append("Spent at a Herald on Ascension and Nova Standings, and at the forge on Tuning.");
            NovaSettings rules = boundNova != null ? boundNova.Settings : null;
            if (rules == null) return text.ToString();

            int level = boundNova.Ascension;
            text.Append("\n\n").Append(NovaRules.AscensionLine(rules.ascension, level));
            text.Append("\n+").Append(level * rules.ascension.attributesPerLevel).Append(" Strength, Agility, Vitality and Spirit");
            int toNext = NovaRules.LevelsToNextTier(rules.ascension, level);
            if (toNext > 0)
                text.Append("\n").Append(toNext).Append(toNext == 1 ? " more level" : " more levels").Append(" to Nova ")
                    .Append(NovaRules.Numeral(NovaRules.Tier(rules.ascension, level) + 1));

            text.Append("\n\nNova Standings (every one you hold counts)");
            NovaState state = boundNova.State;
            foreach (StandingTrackInfo info in rules.standings.tracks)
            {
                int held = state.Standing(info.track);
                text.Append("\n");
                if (held <= 0) { text.Append(info.name).Append(": not held"); continue; }
                text.Append(NovaRules.StandingName(rules.standings, boundNova.Faction, info.track, held))
                    .Append(" (").Append(held).Append(" / ").Append(rules.standings.MaxLevel).Append("): ")
                    .Append(StatDisplay.Name(info.stat)).Append(" +")
                    .Append(NovaRules.StandingPercent(rules.standings, held).ToString("0.#")).Append('%');
            }
            return text.ToString();
        }

        /// <summary>
        /// Redraws what is on screen: the HUD itself always, the Character Sheet and the Skills window only while
        /// they are open. Those two hold a hundred skill cards and every stat, and redrawing them took a tenth of a
        /// second: paid on every cast of a buff, with both windows shut, it was the worst hitch in a fight.
        /// </summary>
        private void Refresh()
        {
            if (!initialized) return;
            refreshWanted = false;
            RefreshHud();
            if (IsShown(characterWindow)) RefreshCharacterSheet(); else characterStale = true;
            if (IsShown(skillsWindow)) RefreshSkillList(); else skillsStale = true;
        }

        private static bool IsShown(VisualElement window) => window != null && window.resolvedStyle.display != DisplayStyle.None;

        /// <summary>A window was just shown: it is brought up to date if anything changed while it was shut.</summary>
        private void RefreshShown(VisualElement window)
        {
            if (!initialized) return;
            if (window == characterWindow && characterStale) RefreshCharacterSheet();
            else if (window == skillsWindow && skillsStale) RefreshSkillList();
        }

        private void RefreshHud()
        {
            int level = progression.CurrentLevel;
            if (level != shownLevel)
            {
                shownLevel = level;
                levelLabel.text = $"LEVEL {level}";
            }

            // Jump chosen, or walking or running at a speed (told apart to a tenth, as it is printed).
            int mode = skills.IsJumpSelected ? -1 : Tenths(skills.CurrentSpeed) * 2 + (skills.IsRunning ? 1 : 0);
            if (mode != shownMode)
            {
                shownMode = mode;
                modeLabel.text = skills.IsJumpSelected ? "JUMP · choose landing" : $"{(skills.IsRunning ? "RUN" : "WALK")} · {skills.CurrentSpeed:0.0} m/s";
            }

            RefreshResources();

            int statPoints = progression.StatPoints, skillPoints = progression.SkillPoints;
            if (statPoints != shownStatPoints || skillPoints != shownSkillPoints)
            {
                shownStatPoints = statPoints;
                shownSkillPoints = skillPoints;
                pointsSummary.text = $"{statPoints} stat points  |  {skillPoints} skill points";
            }

            bool atCap = progression.IsAtLevelCap;
            int experience = progression.Experience, required = progression.ExperienceRequired;
            if (experience != shownExperience || required != shownExperienceRequired || atCap != shownAtCap)
            {
                shownExperience = experience;
                shownExperienceRequired = required;
                shownAtCap = atCap;
                xpBar.value = atCap ? ProgressBarScale : ProgressBarScale * experience / Mathf.Max(1f, required);
                xpBar.title = atCap ? "MAXIMUM LEVEL" : $"{experience:N0} / {required:N0} XP";
            }
            RefreshHotbarSlots();
        }

        private void RefreshCharacterSheet()
        {
            characterStale = false;
            RefreshRenown();
            RefreshSerums();
            RefreshVitals(); // left alone while the window was shut
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
            RefreshCombatStatLabels();
        }

        private void RefreshSkillList()
        {
            skillsStale = false;
            root.Q<Label>("skill-points").text = $"{progression.SkillPoints} skill point(s) available";

            RefreshSkillCard(MovementSkillId.Run, "run", runTile);
            RefreshSkillCard(MovementSkillId.Jump, "jump", jumpTile);

            for (int i = 0; i < combatSkillCardIds.Length; i++)
            {
                string cardId = combatSkillCardIds[i];
                string skillId = combatSkillAssetIds[i];
                RefreshCombatSkillCard(cardId, skillId);
            }
            EnsureGeneratedSkillCards(); // the skill list grows once the character's faction is known
            for (int i = 0; i < generatedCardIds.Count; i++)
                RefreshCombatSkillCard(generatedCardIds[i], generatedSkillIds[i]);
            RefreshClassCards();
            RefreshSkillTree();
        }

        // ------------------------------------------------------------------ the skill tree (the Arsenal)

        private SkillTreeView skillTree;
        private readonly List<SkillTreeView.Node> treeNodes = new List<SkillTreeView.Node>(24);
        private readonly List<(string, string)> treeLinks = new List<(string, string)>(16);
        private readonly List<SkillTreeView.Group> treeGroups = new List<SkillTreeView.Group>(4);
        private readonly List<string> treeTiers = new List<string>(6);
        private readonly Dictionary<(int, int), int> treeLanes = new Dictionary<(int, int), int>(24);
        private const string RunKey = "run", JumpKey = "jump", WallstrideKey = "wallstride", WaterstrideKey = "waterstride";

        // The tiers by the level that opens a skill: 1, 30, 60, 70, 80.
        private static readonly string[] LevelTiers = { "LV 1", "LV 30", "LV 60", "LV 70", "LV 80" };
        private static readonly string[] MoveTiers = { "MOVE" };
        // The lines along a class's skills: the area line (1 and its Zenith technique, 8), the single line (2, 4, the
        // level 60 hit, 6), the two-hit line (levels 1, 30, 60, 80), the triple line (3, 5, the level 60 flurry, 7).
        // Drawn where both ends are on the tab.
        private static readonly (ClassSkillSlot from, ClassSkillSlot to)[] ClassLinks =
        {
            (ClassSkillSlot.S1, ClassSkillSlot.S8), (ClassSkillSlot.S2, ClassSkillSlot.S4), (ClassSkillSlot.S4, ClassSkillSlot.Lv60Single),
            (ClassSkillSlot.Lv60Single, ClassSkillSlot.S6), (ClassSkillSlot.S3, ClassSkillSlot.S5), (ClassSkillSlot.S5, ClassSkillSlot.Lv60Multi),
            (ClassSkillSlot.Lv60Multi, ClassSkillSlot.S7),
            (ClassSkillSlot.Lv1TwoHit, ClassSkillSlot.Lv30TwoHit), (ClassSkillSlot.Lv30TwoHit, ClassSkillSlot.Lv60TwoHit),
            (ClassSkillSlot.Lv60TwoHit, ClassSkillSlot.Lv80TwoHit),
        };
        // The lines among the shared wards: the two Instructor skills lead to the Skill Chip and Dossier ones.
        private static readonly (string from, string to)[] SharedLinks =
        {
            ("SpiritualRage", "HeavenlyShield"), ("HeavenlySerenity", "HeavenlyBlow"), ("HeavenlyShield", "GarudasPrayer"),
            ("HeavenlyBlow", "GarudasPrayer"), ("GarudasPrayer", "HeavenlyRepulsion"), ("GarudasPrayer", "ReverseFlow"),
            ("GarudasPrayer", "HeavenlyDestruction"),
        };

        // The tier a level opens on: 1, 30, 60, 70, 80.
        private static int TierOf(int level) => level >= 80 ? 4 : level >= 70 ? 3 : level >= 60 ? 2 : level >= 30 ? 1 : 0;

        // The group a ward stands in: its class's Stance, its class's Guard, or the shared column.
        private static int WardGroup(SkillDefinitionSO skill) =>
            skill.IsClassSkill && skill.classSlot == ClassSkillSlot.Stance ? 0 : skill.IsClassSkill && skill.classSlot == ClassSkillSlot.Guard ? 1 : 2;

        private void RefreshSkillTree()
        {
            if (skillTree == null || skills == null) return;
            treeNodes.Clear();
            treeLinks.Clear();
            treeGroups.Clear();
            treeTiers.Clear();
            treeLanes.Clear();

            WeaponClassId active = combatSkills != null ? combatSkills.ActiveClass : WeaponClassId.None;
            string className = active != WeaponClassId.None ? WeaponClasses.DisplayName(active).ToUpperInvariant() : "SKILLS";
            string heading;
            switch (skillTree.Current)
            {
                case SkillTreeView.Tab.Strides:
                    heading = "STRIDES   ·   HOW YOU GET ABOUT";
                    treeTiers.AddRange(MoveTiers);
                    treeGroups.Add(new SkillTreeView.Group { Label = "RUN", Column = 0f, Width = 1f });
                    treeGroups.Add(new SkillTreeView.Group { Label = "JUMP", Column = 1f, Width = 1f });
                    AddMovementNode(MovementSkillId.Run, RunKey, runTile, 0f);
                    AddMovementNode(MovementSkillId.Jump, JumpKey, jumpTile, 1f);
                    AddWallstrideNode(2f);
                    AddWaterstrideNode(3f);
                    break;
                case SkillTreeView.Tab.Wards:
                    heading = $"WARDS   ·   {className} AND SHARED";
                    treeTiers.AddRange(LevelTiers);
                    LayOutSkills(new[] { "STANCE", "GUARD", "SHARED" }, skill => !skill.IsAttack, WardGroup);
                    foreach ((string from, string to) in SharedLinks) treeLinks.Add((from, to));
                    break;
                default:
                    heading = $"STRIKES   ·   {className} AND SHARED";
                    treeTiers.AddRange(LevelTiers);
                    LayOutSkills(SkillKinds.AttackLabels, skill => skill.IsAttack, skill => (int)SkillKinds.Of(skill));
                    break;
            }
            skillTree.Show(heading, treeTiers, treeGroups, treeNodes, treeLinks);
        }

        /// <summary>
        /// The combat skills the tab shows, each in its group's columns on its level's tier. A group is as wide as the
        /// most skills it has on one tier, so two skills of a kind that open at the same level stand side by side.
        /// </summary>
        private void LayOutSkills(string[] groupLabels, Func<SkillDefinitionSO, bool> onTab, Func<SkillDefinitionSO, int> groupOf)
        {
            if (combatSkills == null) return;
            var placed = new List<(SkillDefinitionSO skill, int group, int tier, VisualElement card)>();
            for (int i = 0; i < combatSkillCardIds.Length; i++)
            {
                SkillDefinitionSO skill = combatSkills.GetSkill(combatSkillAssetIds[i]);
                if (skill == null || !onTab(skill)) continue;
                VisualElement tile = root.Q($"{combatSkillCardIds[i]}-drag");
                placed.Add((skill, groupOf(skill), TierOf(skill.requiredCharacterLevel), tile != null ? tile.parent : null));
            }
            var bySlot = new Dictionary<ClassSkillSlot, string>();
            for (int i = 0; i < generatedCardIds.Count; i++)
            {
                SkillDefinitionSO skill = combatSkills.GetSkill(generatedSkillIds[i]);
                if (skill == null || !combatSkills.IsInActiveKit(skill) || !onTab(skill)) continue;
                placed.Add((skill, groupOf(skill), TierOf(skill.requiredCharacterLevel), root.Q($"{generatedCardIds[i]}-card")));
                if (skill.IsClassSkill) bySlot[skill.classSlot] = skill.skillId;
            }
            // Class skills first within a group, then by level, so the lines of a class read left to right.
            placed.Sort((a, b) =>
            {
                int c = a.group.CompareTo(b.group);
                if (c != 0) return c;
                c = b.skill.IsClassSkill.CompareTo(a.skill.IsClassSkill);
                if (c != 0) return c;
                c = a.skill.classSlot.CompareTo(b.skill.classSlot);
                return c != 0 ? c : a.skill.requiredCharacterLevel.CompareTo(b.skill.requiredCharacterLevel);
            });

            // How many lanes each group needs: the most skills it has on any one tier (at least one).
            var lanesOf = new int[groupLabels.Length];
            for (int g = 0; g < lanesOf.Length; g++) lanesOf[g] = 1;
            foreach (var entry in placed)
            {
                int count = treeLanes.TryGetValue((entry.group, entry.tier), out int so_far) ? so_far + 1 : 1;
                treeLanes[(entry.group, entry.tier)] = count;
                if (count > lanesOf[entry.group]) lanesOf[entry.group] = count;
            }
            var startOf = new float[groupLabels.Length];
            float column = 0f;
            for (int g = 0; g < groupLabels.Length; g++)
            {
                startOf[g] = column;
                treeGroups.Add(new SkillTreeView.Group { Label = groupLabels[g], Column = column, Width = lanesOf[g] });
                column += lanesOf[g];
            }
            treeLanes.Clear();
            foreach (var entry in placed)
            {
                int lane = treeLanes.TryGetValue((entry.group, entry.tier), out int used) ? used : 0;
                treeLanes[(entry.group, entry.tier)] = lane + 1;
                AddCombatNode(entry.skill, startOf[entry.group] + lane, entry.tier, entry.card);
            }
            foreach ((ClassSkillSlot from, ClassSkillSlot to) in ClassLinks)
                if (bySlot.TryGetValue(from, out string a) && bySlot.TryGetValue(to, out string b)) treeLinks.Add((a, b));
        }

        private void AddMovementNode(MovementSkillId skill, string key, VisualElement tile, float column)
        {
            MovementSkillData definition = skills.GetDefinition(skill);
            if (definition == null) return;
            int rank = skills.GetRank(skill);
            bool hasNext = definition.TryGetRank(rank + 1, out MovementSkillRank next);
            treeNodes.Add(new SkillTreeView.Node
            {
                Key = key, Name = definition.displayName, Icon = definition.icon, Column = column, Row = 0,
                Rank = rank > 0 ? $"{rank}/{definition.MaxRank}" : null, Learned = rank > 0,
                TooLow = hasNext && progression != null && progression.CurrentLevel < next.requiredCharacterLevel,
                CanUpgrade = skills.CanUpgrade(skill, out _), Card = tile != null ? tile.parent : null,
            });
        }

        /// <summary>
        /// Wallstride, the passive that opens by itself (running up walls and into trees): locked until level 60 with
        /// Run and Jump at rank 20, and its tile says which of the three are still to come.
        /// </summary>
        private void AddWallstrideNode(float column)
        {
            if (wallRunMotor == null || wallRunMotor.Settings == null || skills == null) return;
            WallstrideSettings s = wallRunMotor.Settings;
            bool open = wallRunMotor.WallstrideOpen;
            int level = progression != null ? progression.CurrentLevel : 0;
            string needs = open ? "Open: click a wall or a tree with Run or Jump selected, or run or jump into one."
                : $"Opens at level {s.requiredLevel} ({level}), Run rank {s.requiredRunRank} ({skills.RunRank}) and Jump rank {s.requiredJumpRank} ({skills.JumpRank}).";
            treeGroups.Add(new SkillTreeView.Group { Label = "WALLSTRIDE", Column = column, Width = 1f });
            VisualElement card = PassiveCard(WallstrideKey, "Wallstride");
            FillPassiveCard(card, WallstrideKey, s.icon, open,
                "Run up walls, buildings, cliffs and into trees, to take the high ground or get away. Click a wall or a tree with Run or Jump selected, or run or jump into one. Leave by choice: run down, over the top, or leap off to any point you click.",
                $"Grip: {s.gripFluxPerSecond:0.#} Flux a second on a wall · walls {s.minWallHeight:0.#} m and higher · always on once open",
                open ? "Open." : PassiveNeeds(s.requiredLevel, s.requiredRunRank, s.requiredJumpRank, level));
            treeNodes.Add(new SkillTreeView.Node
            {
                Key = WallstrideKey, Name = "Wallstride", Icon = s.icon, Column = column, Row = 0,
                Rank = open ? "PASSIVE" : "LOCKED", Learned = open, Locked = !open, TooLow = level < s.requiredLevel,
                Tooltip = $"Wallstride  ·  Run up walls and into trees, to get the high ground or get away. {needs}", Card = card,
            });
        }

        /// <summary>
        /// Waterstride, the passive beside Wallstride (running on water): locked until level 60 with Run and Jump at rank
        /// 20, like Wallstride; its tile and its card in the details box (owner, 22 Oct 2026: "its own icon and tooltip
        /// like Wallstride").
        /// </summary>
        private void AddWaterstrideNode(float column)
        {
            if (waterMotor == null || waterMotor.Settings == null || skills == null) return;
            WaterstrideSettings s = waterMotor.Settings;
            bool open = waterMotor.WaterstrideOpen;
            int level = progression != null ? progression.CurrentLevel : 0;
            string needs = open ? "Open: click water with Run selected."
                : $"Opens at level {s.requiredLevel} ({level}), Run rank {s.requiredRunRank} ({skills.RunRank}) and Jump rank {s.requiredJumpRank} ({skills.JumpRank}).";
            treeGroups.Add(new SkillTreeView.Group { Label = "WATERSTRIDE", Column = column, Width = 1f });
            VisualElement card = PassiveCard(WaterstrideKey, "Waterstride");
            FillPassiveCard(card, WaterstrideKey, s.icon, open,
                "Run on water: across rivers and lakes, the short way over or away from a fight. Click water with Run selected and you run to the shore and out across it, and stand on the water where you stop. Click land to run ashore and on. Out of Flux, you are carried back to the nearest shore.",
                $"{s.fluxPerSecond:0.#} Flux a second on the water, running or standing · at run speed · always on once open",
                open ? "Open." : PassiveNeeds(s.requiredLevel, s.requiredRunRank, s.requiredJumpRank, level));
            treeNodes.Add(new SkillTreeView.Node
            {
                Key = WaterstrideKey, Name = "Waterstride", Icon = s.icon, Column = column, Row = 0,
                Rank = open ? "PASSIVE" : "LOCKED", Learned = open, Locked = !open, TooLow = level < s.requiredLevel,
                Tooltip = $"Waterstride  ·  Run on water, across rivers and lakes. {needs}", Card = card,
            });
        }

        /// <summary>"Needs level 60 (52 now) · Run rank 20 (20, met) · Jump rank 20 (14 now)": what a locked passive still waits for.</summary>
        private string PassiveNeeds(int requiredLevel, int requiredRun, int requiredJump, int level)
        {
            string Part(string what, int needed, int now) => $"{what} {needed} ({now}{(now >= needed ? ", met" : " now")})";
            return $"Needs {Part("level", requiredLevel, level)} · {Part("Run rank", requiredRun, skills.RunRank)} · {Part("Jump rank", requiredJump, skills.JumpRank)}";
        }

        /// <summary>
        /// The card of a passive of the Strides tab (Wallstride, Waterstride), built once into the details box beside
        /// the tree like the generated skill cards: its tile with the icon, what it does, its numbers and what it
        /// still needs. A passive is never learned or dragged, so it has no button.
        /// </summary>
        private VisualElement PassiveCard(string id, string title)
        {
            VisualElement body = root.Q("skills-body");
            if (body == null) return null;
            VisualElement card = body.Q($"{id}-card");
            if (card != null) return card;
            card = new VisualElement { name = $"{id}-card" };
            card.AddToClassList("skill-card");
            card.AddToClassList("kn-card");
            card.AddToClassList("passive-skill-card");
            var token = new VisualElement { name = $"{id}-drag" };
            token.AddToClassList("skill-token");
            var name = new Label(title.ToUpperInvariant());
            name.AddToClassList("skill-icon");
            token.Add(name);
            var rank = new Label { name = $"{id}-rank" };
            rank.AddToClassList("skill-rank");
            token.Add(rank);
            card.Add(token);
            card.Add(CardLabel($"{id}-description", "help"));
            card.Add(CardLabel($"{id}-values", "section"));
            card.Add(CardLabel($"{id}-requirement", "muted"));
            card.style.display = DisplayStyle.None;
            body.Add(card);
            return card;
        }

        private static void FillPassiveCard(VisualElement card, string id, Sprite icon, bool open, string description, string values, string requirement)
        {
            if (card == null) return;
            SetTokenArt(card.Q($"{id}-drag"), icon);
            card.Q<Label>($"{id}-rank").text = open ? "PASSIVE · ALWAYS ON" : "LOCKED";
            card.Q<Label>($"{id}-description").text = description;
            card.Q<Label>($"{id}-values").text = values;
            card.Q<Label>($"{id}-requirement").text = requirement;
        }

        private void AddCombatNode(SkillDefinitionSO skill, float column, int row, VisualElement card)
        {
            int rank = combatSkills.GetRank(skill);
            bool learned = rank > 0, locked = combatSkills.IsLocked(skill) && !learned;
            treeNodes.Add(new SkillTreeView.Node
            {
                Key = skill.skillId, Name = skill.displayName, Icon = skill.icon, Column = column, Row = row,
                Rank = learned ? $"{rank}/{skill.maxRank}" : locked ? "LOCKED" : null, Learned = learned, Locked = locked,
                TooLow = progression != null && progression.CurrentLevel < skill.requiredCharacterLevel,
                CanUpgrade = !locked && combatSkills.CanUpgrade(skill, out _), Card = card,
            });
        }

        // A press on a tile has chosen it; a skill that is learned may be dragged to the Hotbar from there as from its card.
        private void OnSkillTreePressed(string key, PointerDownEvent evt, VisualElement tile)
        {
            if (key == RunKey || key == JumpKey)
            {
                MovementSkillId movement = key == RunKey ? MovementSkillId.Run : MovementSkillId.Jump;
                if (skills != null && skills.CanUse(movement)) OnSkillPointerDown(evt, movement, tile);
                return;
            }
            SkillDefinitionSO skill = combatSkills != null ? combatSkills.GetSkill(key) : null;
            if (skill != null && combatSkills.IsLearned(skill)) OnCombatSkillPointerDown(evt, skill, tile);
        }

        private void RefreshHotbarSlots()
        {
            for (int index = 0; index < slotElements.Length; index++)
            {
                HotbarSlotEntry entry = hotbar.GetSlotEntry(index);
                VisualElement slot = slotElements[index];
                if (slot == null) continue;

                // What the slot is to show, as a few values: the same as last time, and it is left as it stands.
                var now = new SlotShown { Drawn = true };
                MovementSkillData movementDef = null;
                bool item = false;
                if (entry.itemType == HotbarItemType.Movement)
                {
                    movementDef = skills.GetDefinition(entry.movementSkill);
                    now.Type = HotbarItemType.Movement;
                    now.Movement = entry.movementSkill;
                    now.Source = movementDef;
                    now.Number = movementDef != null ? skills.GetRank(entry.movementSkill) : 0;
                    now.Selected = entry.movementSkill != MovementSkillId.None && entry.movementSkill == skills.SelectedSkill;
                }
                else if (entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill != null)
                {
                    now.Type = HotbarItemType.CombatSkill;
                    now.Source = entry.combatSkill;
                    now.Number = combatSkills != null ? combatSkills.GetRank(entry.combatSkill) : 0;
                }
                else if (entry.itemType == HotbarItemType.Item && hotbar.ItemSource != null)
                {
                    item = true;
                    now.Type = HotbarItemType.Item;
                    now.Item = entry.itemId;
                    now.Number = hotbar.ItemSource.CountItem(entry.itemId);
                }
                else if (entry.itemType == HotbarItemType.Autobuff && hotbar.Autobuff != null)
                {
                    now.Type = HotbarItemType.Autobuff;
                    now.Number = hotbar.Autobuff.SlottedCount;
                    now.Item = hotbar.Autobuff.IsAvailable ? 1 : 0; // the licence running out redraws the slot
                    now.Selected = hotbar.Autobuff.IsRunning;
                }
                if (now.Same(slotShown[index])) continue;
                slotShown[index] = now;

                string key = PlayerSkillHotbar.GetKeyLabel(index);
                slot.EnableInClassList("on-cooldown", false); // skills re-apply theirs every frame
                Sprite icon = null;
                Label slotName = slotNames[index];

                if (now.Type == HotbarItemType.Movement)
                {
                    MovementSkillData def = movementDef;
                    icon = def != null ? def.icon : null;
                    slotName.text = def == null ? "—" : def.displayName.ToUpperInvariant();
                    SetSlotLine(index, def == null ? SlotLineKind.Empty : SlotLineKind.Rank, now.Number);
                    slot.EnableInClassList("selected", now.Selected);
                    slot.tooltip = $"{def?.displayName}: click or press {key}. Right-click to clear.";
                }
                else if (now.Type == HotbarItemType.CombatSkill)
                {
                    SkillDefinitionSO def = entry.combatSkill;
                    icon = def.icon;
                    slotName.text = def.displayName.ToUpperInvariant();
                    SetSlotLine(index, SlotLineKind.Rank, now.Number);
                    slot.EnableInClassList("selected", false);
                    string hold = def.IsCharged ? $"hold {key} to charge" : def.holdToRepeat ? $"press or hold {key}" : $"click or press {key}";
                    slot.tooltip = $"{def.displayName}: {hold} to activate. Right-click to clear.";
                }
                else if (item)
                {
                    icon = hotbar.ItemSource.GetItemIcon(entry.itemId);
                    slotName.text = hotbar.ItemSource.GetItemLabel(entry.itemId).ToUpperInvariant();
                    SetSlotLine(index, SlotLineKind.Count, now.Number);
                    slot.EnableInClassList("selected", false);
                    slot.EnableInClassList("on-cooldown", now.Number == 0);
                    slot.tooltip = $"Click or press {key} to use. Right-click to clear.";
                }
                else if (now.Type == HotbarItemType.Autobuff)
                {
                    // The Autobuff tile: its Pass's picture, the count of buffs slotted, greyed while the licence is out.
                    icon = hotbar.Autobuff.Icon;
                    slotName.text = hotbar.Autobuff.Label;
                    SetSlotLine(index, SlotLineKind.Count, now.Number);
                    slot.EnableInClassList("selected", now.Selected);
                    slot.EnableInClassList("on-cooldown", now.Item == 0);
                    slot.tooltip = now.Item == 0 ? "Autoward has expired." : $"Autoward: casts every ward you slotted. Click or press {key}. Right-click to clear.";
                }
                else
                {
                    slotName.text = "—";
                    SetSlotLine(index, SlotLineKind.Empty, 0);
                    slot.EnableInClassList("selected", false);
                    slot.tooltip = "Drop a learned skill here.";
                }

                // With an icon the name is left to the tooltip; without one the slot keeps its text label.
                SetIcon(slotIcons[index], icon);
                slot.EnableInClassList("has-icon", icon != null);
            }
        }

        private void RefreshCombatStatLabels()
        {
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

                // Gear stats: nothing but gear grants them, so they read 0 on a bare character.
                Label critDefLabel = root.Q<Label>("combat-stat-crit-def");
                if (critDefLabel != null) critDefLabel.text = $"{combatStats.GetStatValue(StatType.CriticalDefensePercent):0.#}%";
                Label xpRateLabel = root.Q<Label>("combat-stat-xp-rate");
                if (xpRateLabel != null) xpRateLabel.text = $"+{combatStats.GetStatValue(StatType.KillExpRatePercent):0.#}%";
                Label xpAmountLabel = root.Q<Label>("combat-stat-xp-amount");
                if (xpAmountLabel != null) xpAmountLabel.text = $"+{combatStats.GetStatValue(StatType.KillExpAmountPercent):0.#}%";
            }
        }

        /// <summary>
        /// The parts of one skill card, found once. Looking each up by name on every redraw meant seven searches of the
        /// whole HUD for each of a hundred cards: a tenth of a second for the Skills window.
        /// </summary>
        private sealed class SkillCardParts
        {
            public Label Rank, Description, Values, Requirement, DragHint;
            public Button Upgrade;
            public VisualElement Tile;
            // What the card was last drawn from: a card whose skill has not changed is left as it is.
            public int DrawnRank = -1, DrawnStep = -1;
            public bool DrawnLearned, DrawnLocked, DrawnCanUpgrade;
            public string DrawnWhyNot;
            public VisualElement DrawnUnder;
            public VisualElement Card;
        }

        private readonly Dictionary<string, SkillCardParts> skillCardParts = new Dictionary<string, SkillCardParts>();

        private SkillCardParts PartsOfCard(string cardPrefix)
        {
            // Found under another root (the HUD was built again): looked up afresh.
            if (skillCardParts.TryGetValue(cardPrefix, out SkillCardParts parts) && parts.DrawnUnder == root) return parts;
            parts = new SkillCardParts
            {
                Rank = root.Q<Label>($"{cardPrefix}-rank"),
                Description = root.Q<Label>($"{cardPrefix}-description"),
                Values = root.Q<Label>($"{cardPrefix}-values"),
                Requirement = root.Q<Label>($"{cardPrefix}-requirement"),
                Upgrade = root.Q<Button>($"upgrade-{cardPrefix}"),
                DragHint = root.Q<Label>($"{cardPrefix}-drag-hint"),
                Tile = root.Q($"{cardPrefix}-drag"),
                DrawnUnder = root,
            };
            // A card that is not built yet is looked for again next time.
            if (parts.Rank != null || parts.Upgrade != null || parts.Tile != null) skillCardParts[cardPrefix] = parts;
            return parts;
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

            SkillCardParts parts = PartsOfCard(cardPrefix);
            string whyNot = null;
            bool upgradable = !(isDropSkill && !isLearned) && combatSkills.CanUpgrade(skill, out whyNot);
            if (parts.DrawnRank == rank && parts.DrawnStep == skillStep && parts.DrawnLearned == isLearned && parts.DrawnLocked == isDropSkill
                && parts.DrawnCanUpgrade == upgradable && parts.DrawnWhyNot == whyNot) return; // nothing it shows has changed
            parts.DrawnWhyNot = whyNot;
            parts.DrawnRank = rank;
            parts.DrawnStep = skillStep;
            parts.DrawnLearned = isLearned;
            parts.DrawnLocked = isDropSkill;
            parts.DrawnCanUpgrade = upgradable;

            Label rankLabel = parts.Rank;
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

            Label descLabel = parts.Description;
            if (descLabel != null)
            {
                // A Stance says how it reaches the Squad, with the Squad's own numbers (owner, 16 Oct 2026).
                string squad = SquadLine(skill, rank);
                descLabel.text = string.IsNullOrEmpty(squad) ? skill.description : $"{skill.description}\n{squad}";
            }

            Label valuesLabel = parts.Values;
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

            Label reqLabel = parts.Requirement;
            if (reqLabel != null)
            {
                if (isDropSkill && !isLearned)
                {
                    string reqStr = $"Requires Level {skill.requiredCharacterLevel} · {skill.acquisitionNote}";
                    if (skill.requiredCraftingArchives != null && skill.requiredCraftingArchives.Length > 0)
                    {
                        reqStr += $"\nPages: {string.Join(", ", skill.requiredCraftingArchives)}";
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
                    reqLabel.text = $"Requires Level {skill.requiredCharacterLevel} · Enhance: {nextCost} skill point(s)";
                }
            }

            Button upgrade = parts.Upgrade;
            if (upgrade != null)
            {
                if (isDropSkill && !isLearned)
                {
                    upgrade.text = "REQUIRES SKILL CHIP / DOSSIER";
                    upgrade.SetEnabled(false);
                    upgrade.tooltip = $"Find the {skill.displayName} Skill Chip, or craft it from its Dossiers.";
                }
                else
                {
                    bool canUpgrade = combatSkills.CanUpgrade(skill, out string reason);
                    string action = rank == 0 ? "LEARN" : "ENHANCE";
                    string stepSuffix = skillStep > 1 ? $" (+{skillStep})" : "";
                    upgrade.text = rank >= skill.maxRank ? "MAXIMUM RANK" : $"{action} {skill.displayName.ToUpperInvariant()}{stepSuffix}";
                    upgrade.tooltip = canUpgrade ? "Spend skill points on the next rank. Press and hold to keep enhancing." : reason;
                    upgrade.SetEnabled(canUpgrade);
                }
            }

            Label dragHint = parts.DragHint;
            if (dragHint != null)
            {
                if (isDropSkill && !isLearned)
                    dragHint.text = "Available once acquired or crafted.";
                else
                    dragHint.text = rank == 0 ? $"Learn {skill.displayName}, then drag to a hotbar slot."
                        : skill.IsCharged ? "Drag to a hotbar slot (1–0, Q W E R T), then hold the key to charge."
                        : skill.holdToRepeat ? "Drag to a hotbar slot (1–0, Q W E R T). Hold the key to keep attacking."
                        : "Drag to a hotbar slot (1–0, Q W E R T) to activate.";
            }

            VisualElement tile = parts.Tile;
            if (tile != null)
            {
                tile.tooltip = isLearned ? $"Drag {skill.displayName} to hotbar" : isDropSkill ? skill.acquisitionNote : "Learn before assigning";
                SetTokenArt(tile, skill.icon);
            }
        }

        // What a skill does at a rank, in one line: shared with the Dossier card and the Hotbar hover (SkillKinds).
        private static string DescribeSkillStats(SkillDefinitionSO skill, int rank) => SkillKinds.Describe(skill, rank);

        private string SquadLine(SkillDefinitionSO skill, int rank) => combatSkills == null ? string.Empty
            : SquadBuffRules.TooltipLine(skill, rank, combatSkills.SquadShareRank, combatSkills.SquadShareRadius);

        /// <summary>Rebuilds the buff badges when the set of buffs changes; timers tick in <see cref="RefreshBuffTimers"/>.</summary>
        private void RefreshBuffs()
        {
            if (activeBuffsBar == null || buffManager == null) return;
            activeBuffsBar.Clear();
            shownBuffs.Clear();
            shownBuffTimers.Clear();
            shownBuffClocks.Clear();

            var buffs = buffManager.ActiveBuffs;
            for (int i = 0; i < buffs.Count; i++)
            {
                var buff = buffs[i];
                var badge = new VisualElement();
                badge.AddToClassList("buff-badge");
                badge.tooltip = buff.FromSquadmate && !string.IsNullOrEmpty(buff.Giver) ? $"{buff.Skill.displayName} (from {buff.Giver})" : buff.Skill.displayName;

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
                shownBuffClocks.Add(Unknown);
            }
            RefreshBuffTimers();
        }

        // What the player switched on with an item (the XP Boosters) is not on the buff bar: it is the running list
        // at the side of the screen (BoostListView), text with a clock, since it lasts for hours.
        private PlayerExperienceBoosts boosts;
        private BoostListView boostList;

        private void AddBoosterLines(List<BoostListView.Line> lines)
        {
            if (boosts == null) return;
            ItemCatalog catalog = boosts.TryGetComponent(out PlayerInventory pack) ? pack.Catalog : null;
            for (int i = 0; i < boosts.Count; i++)
            {
                float left = boosts.SecondsLeft(i);
                if (left <= 0f) continue;
                var booster = boosts.BoosterAt(i);
                ItemDefinition item = catalog != null ? catalog.Get(booster.itemId) : null;
                lines.Add(new BoostListView.Line
                {
                    Name = item != null ? item.displayName : "XP Booster", SecondsLeft = left, Detail = PlayerExperienceBoosts.Describe(booster)
                });
            }
        }

        // What a Pass or a Permit keeps running for days goes in the same list (owner, 23 Oct 2026: "so that someone can see
        // how much time they have left at a glance"): Autopilot, Autoward and the player's own Kiosk.
        private static void AddPassLines(List<BoostListView.Line> lines)
        {
            long now = DateTime.UtcNow.Ticks;
            var autopilot = KryticalNova.Autopilot.PlayerAutopilot.Local;
            if (autopilot != null && !autopilot.Free && autopilot.HasTime)
                lines.Add(new BoostListView.Line { Name = "Autopilot", SecondsLeft = autopilot.SecondsLeft, Detail = autopilot.TimeLeftLine });
            var autoward = KryticalNova.Autopilot.PlayerAutobuff.Local;
            if (autoward != null && autoward.IsActive)
                lines.Add(new BoostListView.Line
                {
                    Name = "Autoward", SecondsLeft = (float)TimeSpan.FromTicks(autoward.ExpiresUtc - now).TotalSeconds, Detail = autoward.TimeLeftLine
                });
            var kiosk = KryticalNova.Economy.PlayerKiosk.Local;
            if (kiosk != null && kiosk.View.exists && kiosk.View.expiresUtc > now)
                lines.Add(new BoostListView.Line
                {
                    Name = "Kiosk", SecondsLeft = (float)TimeSpan.FromTicks(kiosk.View.expiresUtc - now).TotalSeconds,
                    Detail = kiosk.View.open ? $"Your Kiosk \"{kiosk.ShopName}\" is open." : "Your Kiosk's Permit time.",
                });
        }

        private void RefreshBoostList() => boostList?.Tick(true);

        private void RefreshBuffTimers()
        {
            boostList?.Tick();
            if (activeBuffsBar == null || buffManager == null) return;
            if (buffManager.ActiveBuffs.Count != shownBuffs.Count) { RefreshBuffs(); return; }
            for (int i = 0; i < shownBuffs.Count; i++)
            {
                var buff = shownBuffs[i];
                float left = buff.RemainingDuration;
                // A clock is written when the number on it changes, not every frame: whole seconds while there are
                // more than ten left, tenths for the last ten.
                bool shield = buff.Skill.isAbsorptionShield, tenths = !shield && left < BuffTenthsBelow;
                int clock = shield ? Whole(buff.CurrentShieldCapacity) * 4096 + Mathf.Min(4095, Whole(left))
                    : tenths ? Tenths(left) : -1 - Mathf.CeilToInt(left);
                if (clock == shownBuffClocks[i]) continue;
                shownBuffClocks[i] = clock;
                shownBuffTimers[i].text = shield ? $"{buff.CurrentShieldCapacity:0} HP ({left:0}s)"
                    : tenths ? $"{left:0.0}s" : $"{Mathf.CeilToInt(left)}s";
            }
        }

        private const float BuffTenthsBelow = 10f;

        private void RefreshHotbarCooldowns()
        {
            if (combatSkills == null) return;
            for (int index = 0; index < slotElements.Length; index++)
            {
                if (slotElements[index] == null) continue;
                var entry = hotbar.GetSlotEntry(index);
                bool charging = hotbar.ChargingSlot == index;
                slotElements[index].EnableInClassList("charging", charging);
                // The skill the character is walking in to cast, or will cast when its swing is over.
                SkillDefinitionSO waiting = combatSkills.QueuedSkill;
                slotElements[index].EnableInClassList("queued", waiting != null && entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill == waiting);
                float cooldownLeft01 = 0f;
                if (entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill != null)
                {
                    bool onCooldown = combatSkills.IsOnCooldown(entry.combatSkill);
                    slotElements[index]?.EnableInClassList("on-cooldown", onCooldown);
                    if (onCooldown && entry.combatSkill.cooldown > 0f)
                        cooldownLeft01 = Mathf.Clamp01(combatSkills.GetRemainingCooldown(entry.combatSkill) / entry.combatSkill.cooldown);
                    // The line under the slot is written only when the number on it changes.
                    if (charging) SetSlotLine(index, SlotLineKind.Charge, Whole(hotbar.ChargeProgress * 100f));
                    else if (onCooldown) SetSlotLine(index, SlotLineKind.Cooldown, Tenths(combatSkills.GetRemainingCooldown(entry.combatSkill)));
                    else SetSlotLine(index, SlotLineKind.Rank, combatSkills.GetRank(entry.combatSkill));
                }
                // The sweep drains downward as the cooldown runs out.
                slotCooldowns[index].style.height = Length.Percent(cooldownLeft01 * 100f);
            }
        }

        private void RefreshResources()
        {
            if (!initialized || resources == null) return;
            // Called on every change of HP or Flux, which while either is filling up is many times a second: the
            // bars move with the true values, the numbers are written only when a whole number on them changes.
            float healthNow = resources.Health, maxHealthNow = resources.MaxHealth, chiNow = resources.Chi, maxChiNow = resources.MaxChi;
            if (healthNow != barHealth || maxHealthNow != barMaxHealth)
            {
                barHealth = healthNow;
                barMaxHealth = maxHealthNow;
                hpBar.value = ProgressBarScale * healthNow / Mathf.Max(1f, maxHealthNow);
            }
            if (chiNow != barChi || maxChiNow != barMaxChi)
            {
                barChi = chiNow;
                barMaxChi = maxChiNow;
                chiBar.value = ProgressBarScale * chiNow / Mathf.Max(1f, maxChiNow);
            }
            int health = Whole(healthNow), maxHealth = Whole(maxHealthNow), chi = Whole(chiNow), maxChi = Whole(maxChiNow);
            bool healthChanged = health != shownHealth || maxHealth != shownMaxHealth, chiChanged = chi != shownChi || maxChi != shownMaxChi;
            if (healthChanged)
            {
                shownHealth = health;
                shownMaxHealth = maxHealth;
                hpBar.title = $"HP {healthNow:0} / {maxHealthNow:0}";
            }
            if (chiChanged)
            {
                shownChi = chi;
                shownMaxChi = maxChi;
                chiBar.title = $"FLUX {chiNow:0} / {maxChiNow:0}";
            }
            if (healthChanged || chiChanged)
            {
                if (resourceValues != null) resourceValues.text = $"HP {healthNow:0} / {maxHealthNow:0}    Flux {chiNow:0} / {maxChiNow:0}";
                if (resources.Data != null && resourceFormula != null)
                    resourceFormula.text = "STR: +1 AP, +2 Hit Rate | VIT: +20 HP, +1 Evade | AGI: +2 Evade, +1 DP | SPI: +20 Flux, +1 AP";
                // The Character Sheet's own numbers, while it is open; shut, it is brought up to date when it opens.
                if (IsShown(characterWindow)) RefreshVitals(); else characterStale = true;
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
            Label hpLabel = vitalHp;
            if (hpLabel != null) hpLabel.text = $"{resources.Health:0} / {resources.MaxHealth:0}";
            Label chiLabel = vitalChi;
            if (chiLabel != null) chiLabel.text = $"{resources.Chi:0} / {resources.MaxChi:0}";
            if (combatStats == null) return;

            Label hpBreakdown = vitalHpBreakdown;
            if (hpBreakdown != null)
            {
                combatStats.GetPoolBreakdown(StatType.MaxHP, out float hpBase, out float fromVit, out float hpBonus);
                hpBreakdown.text = $"Base {hpBase:0} · Vitality {FormatSigned(fromVit)} · Gear/Pets/Mounts {FormatSigned(hpBonus)}";
            }
            Label chiBreakdown = vitalChiBreakdown;
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
                ? $"Requires level {next.requiredCharacterLevel} · Enhance: {next.skillPointCost} skill point(s)" : "Maximum rank reached";
            Button upgrade = root.Q<Button>($"upgrade-{prefix}");
            bool canUpgrade = skills.CanUpgrade(skill, out string reason);
            string action = rank == 0 ? "LEARN" : "ENHANCE";
            string stepSuffix = skillStep > 1 ? $" (+{skillStep})" : "";
            upgrade.text = !hasNext ? "MAXIMUM RANK" : $"{action} {definition.displayName.ToUpperInvariant()}{stepSuffix}";
            upgrade.tooltip = canUpgrade ? "Spend skill points on the next rank. Press and hold to keep enhancing." : reason;
            upgrade.SetEnabled(canUpgrade);
            root.Q<Label>($"{prefix}-drag-hint").text = rank == 0 ? $"Learn {definition.displayName}, then drag its tile to a hotbar slot."
                : "Drag to a slot. Select with 1–0, then left-click a destination. Select again to walk.";
            tile.tooltip = rank == 0 ? "Learn this skill before assigning it." : "Drag to hotbar";
            SetTokenArt(tile, definition.icon);
        }

        private static string DescribeRank(MovementSkillId skill, MovementSkillRank rank)
        {
            return skill == MovementSkillId.Run ? $"{rank.runSpeed:0.0} m/s · {rank.chiCost:0.#} Flux / destination"
                : $"{rank.jumpDistance:0.0} m range / +{rank.jumpHeight:0.0} m height\n{rank.chiCost:0.#} Flux / jump";
        }

        /// <summary>
        /// The card of what a Hotbar slot holds, over the slot: an item's own card (the one the Pack shows), or a
        /// skill's name, rank, kind and numbers with the key that fires it. Nothing while a drag is on, and nothing
        /// for an empty slot.
        /// </summary>
        private void ShowSlotTooltip(int index)
        {
            if (hotbar == null || dragSource != null || index < 0 || index >= slotElements.Length) return;
            VisualElement slot = slotElements[index];
            HotbarSlotEntry entry = hotbar.GetSlotEntry(index);
            string key = PlayerSkillHotbar.GetKeyLabel(index);
            if (entry.itemType == HotbarItemType.Item)
            {
                GearUIController cards = GearUIController.Instance;
                ItemDefinition def = PlayerInventory.Local != null && PlayerInventory.Local.Catalog != null ? PlayerInventory.Local.Catalog.Get(entry.itemId) : null;
                if (cards == null || def == null) return;
                int count = hotbar.ItemSource != null ? hotbar.ItemSource.CountItem(entry.itemId) : 0;
                cards.ShowItemTooltip(slot, def, new ItemStack(entry.itemId, (ushort)Mathf.Max(1, count)), $"Left-click: use ({key})", false, null, above: true);
                return;
            }
            if (entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill != null)
            {
                SkillDefinitionSO def = entry.combatSkill;
                int rank = combatSkills != null ? Mathf.Max(1, combatSkills.GetRank(def)) : 1;
                string hold = def.IsCharged ? $"Hold {key} to charge" : def.holdToRepeat ? $"Press or hold {key}" : $"Click or press {key}";
                string squad = SquadLine(def, rank);
                HudTextTooltip.ShowFor(slot, $"{def.displayName}  ·  Rank {rank}",
                    $"<b>{SkillKinds.Label(def)}</b>  ·  {SkillKinds.Describe(def, rank)}{(string.IsNullOrEmpty(squad) ? string.Empty : "\n" + squad)}\n{hold}  ·  Right-click to clear", above: true);
                return;
            }
            if (entry.itemType == HotbarItemType.Movement)
            {
                MovementSkillData def = skills != null ? skills.GetDefinition(entry.movementSkill) : null;
                if (def == null) return;
                int rank = skills.GetRank(entry.movementSkill);
                string values = skills.TryGetLearnedRank(entry.movementSkill, out MovementSkillRank learned) ? "\n" + DescribeRank(entry.movementSkill, learned) : string.Empty;
                HudTextTooltip.ShowFor(slot, $"{def.displayName}  ·  Rank {rank}", $"<b>STRIDE</b>{values}\nClick or press {key} to select  ·  Right-click to clear", above: true);
                return;
            }
            if (entry.itemType == HotbarItemType.Autobuff && hotbar.Autobuff != null)
            {
                HudTextTooltip.ShowFor(slot, "Autoward", hotbar.Autobuff.Describe(key), above: true);
            }
        }

        private void HideSlotTooltip(int index)
        {
            if (index < 0 || index >= slotElements.Length) return;
            HudTextTooltip.HideFor(slotElements[index]);
            GearUIController.Instance?.HideItemTooltip();
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
            else if (entry.itemType == HotbarItemType.Autobuff)
                BeginDragAutobuff(slotElements[index], index, evt);
        }

        private void BeginDragAutobuff(VisualElement source, int sourceSlot, PointerDownEvent evt)
        {
            if (dragSource != null || !HotbarHasAutobuff) return;
            dragSource = source;
            draggedMovementSkill = MovementSkillId.None;
            draggedCombatSkill = null;
            draggedAutobuff = true;
            draggedFromSlot = sourceSlot;
            dragPointerId = evt.pointerId;
            pointerStart = evt.position;
            dragMoved = false;
            hotbar.InputSuppressed = true;
            source.CapturePointer(evt.pointerId);
            if (sourceSlot == NoSlot) Feedback("Drag AUTOWARD to a hotbar slot, then release the left mouse button.");
            evt.StopPropagation();
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
            draggedAutobuff = false;
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
            draggedAutobuff = false;
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

            MovementSkillData draggedMovement = draggedCombatSkill == null && !draggedAutobuff ? skills.GetDefinition(draggedMovementSkill) : null;
            Sprite ghostIcon = draggedAutobuff ? hotbar.Autobuff?.Icon : draggedCombatSkill != null ? draggedCombatSkill.icon : draggedMovement?.icon;
            string ghostText = draggedAutobuff ? "AUTOWARD" : draggedCombatSkill != null
                ? draggedCombatSkill.displayName.ToUpperInvariant()
                : draggedMovement != null ? draggedMovement.displayName.ToUpperInvariant() : string.Empty;
            // The icon itself follows the cursor; skills without one keep the text chip.
            dragGhost.text = ghostIcon != null ? string.Empty : ghostText;
            dragGhost.EnableInClassList("icon-ghost", ghostIcon != null);
            dragGhost.style.backgroundImage = ghostIcon != null ? new StyleBackground(ghostIcon) : StyleKeyword.Null;

            Vector2 local = root.Q("game-ui").WorldToLocal(position);
            dragGhost.style.left = local.x;
            dragGhost.style.top = local.y;
            for (int index = 0; index < slotElements.Length; index++)
                slotElements[index].EnableInClassList("drop-target", slotElements[index].worldBound.Contains(position));
            // The other places a skill may land (the Autobuff's cells) light up the same way when they would take it.
            foreach (DropTarget target in dropTargets)
            {
                bool over = draggedCombatSkill != null && target.Element.panel != null && target.Element.resolvedStyle.display != DisplayStyle.None
                    && target.Element.worldBound.Contains(position) && (target.Accepts == null || target.Accepts(draggedCombatSkill));
                target.Element.EnableInClassList("drop-target", over);
            }
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (dragSource == null || evt.pointerId != dragPointerId || evt.button != LeftButton) return;
            int sourceSlot = draggedFromSlot;
            MovementSkillId movSkill = draggedMovementSkill;
            SkillDefinitionSO comSkill = draggedCombatSkill;
            bool autobuffDrag = draggedAutobuff;
            Vector2 position = evt.position;
            bool wasDrag = dragMoved || (position - pointerStart).sqrMagnitude >= DragThresholdSquared;
            int targetSlot = NoSlot;
            for (int index = 0; index < slotElements.Length; index++)
                if (slotElements[index].worldBound.Contains(position)) { targetSlot = index; break; }
            // A skill let go over one of the other places it may land (an Autobuff cell) goes there.
            DropTarget landed = default;
            if (wasDrag && targetSlot == NoSlot && comSkill != null)
                foreach (DropTarget target in dropTargets)
                    if (target.Element.panel != null && target.Element.resolvedStyle.display != DisplayStyle.None
                        && target.Element.worldBound.Contains(position) && (target.Accepts == null || target.Accepts(comSkill))) { landed = target; break; }
            CancelDrag();

            if (landed.Element != null)
            {
                landed.Dropped?.Invoke(comSkill);
                Feedback($"{comSkill.displayName} slotted into Autoward.");
            }
            else if (wasDrag && targetSlot != NoSlot)
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
                    if (autobuffDrag)
                    {
                        assigned = hotbar.AssignAutobuff(targetSlot);
                        name = "Autobuff";
                    }
                    else if (movSkill != MovementSkillId.None)
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
            draggedAutobuff = false;
            // The Hotbar listens again, unless the cursor is in a text field (chat): typing must not cast.
            if (hotbar != null) hotbar.InputSuppressed = HudTyping.IsTyping(root);
            suppressWorldInputFrame = Time.frameCount;
            if (source != null && source.HasPointerCapture(dragPointerId)) source.ReleasePointer(dragPointerId);
            SetVisible(dragGhost, false);
            foreach (VisualElement slot in slotElements) slot?.RemoveFromClassList("drop-target");
            foreach (DropTarget target in dropTargets) target.Element?.RemoveFromClassList("drop-target");
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
                    return $"Allocate +{step} Spirit:\n• Primary: +{step * 20} Max Flux\n• Secondary: +{step * 1} Attack Power (AP)\n(Press and hold to allocate continuously)";
                default:
                    return $"Allocate +{step} {attribute}.";
            }
        }

        private void ToggleCharacter() => ToggleWindow(characterWindow);
        private void HideCharacter() { SetVisible(characterWindow, false); StopHold(); CancelDrag(); statSteps.Close(); }
        private void ToggleSkills() => ToggleWindow(skillsWindow);
        private void HideSkills() { SetVisible(skillsWindow, false); StopHold(); CancelDrag(); skillSteps.Close(); }
        private void ToggleWindow(VisualElement window)
        {
            if (movement != null) movement.ReleaseCameraControl();
            StopHold();
            CancelDrag();
            bool show = window.resolvedStyle.display == DisplayStyle.None;
            SetVisible(window, show);
            if (show)
            {
                RefreshShown(window);
                window.BringToFront();
            }
        }
        private void CloseWindows() { HideCharacter(); HideSkills(); }

        private void OnLevelGained(int level) => Feedback($"Level {level} reached. New stat and skill points awarded.");

        // A message stays this long. Left up for good, an old refusal ("Choose a walkable ground...") reads as the
        // state of things now.
        private const long MessageMilliseconds = 6000;
        private IVisualElementScheduledItem messageFade;

        private void Feedback(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            Label feedback = root.Q<Label>("feedback");
            if (feedback == null) return;
            feedback.text = message;
            feedback.style.visibility = Visibility.Visible;
            messageFade?.Pause();
            messageFade = feedback.schedule.Execute(() => feedback.style.visibility = Visibility.Hidden).StartingIn(MessageMilliseconds);
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            element.EnableInClassList("visible", visible);
        }
    }
}
