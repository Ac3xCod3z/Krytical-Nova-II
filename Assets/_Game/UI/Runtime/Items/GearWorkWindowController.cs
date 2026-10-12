using System;
using System.Collections.Generic;
using System.Text;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Forge;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The forge bench: Tempering, Fusion, Tuning and Reforge at an Armorer. It opens beside the Pack. The piece
    /// goes on the anvil (right-click it in the Pack, or drag it over), what the strike uses is picked under it, the
    /// ring round the anvil shows how far the piece has come and where it would get to, and one button strikes.
    /// The request goes to <see cref="PlayerGearWork"/>; the server decides. Odds and reasons come from the same
    /// rules the server uses (<see cref="GearProgressionRules"/>), so the bench never promises what the server
    /// refuses. Opened from the Armorer's dialogue and closed when the player walks away. While the layout editor
    /// has it out to be placed it shows itself with an empty anvil, wherever the player is.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class GearWorkWindowController : MonoBehaviour, IPackTarget
    {
        private enum Tab { Temper, Fusion, Tuning, Reforge }

        private const float RangeCheckSeconds = 0.25f;
        private const float StrikeSeconds = 0.45f;       // from the press to the reveal
        private const float StrikeGiveUpSeconds = 1.5f;   // no answer came (the server refused): let go of the bench
        private const float FlashSeconds = 0.6f;
        private const float ShakeSeconds = 0.32f;
        private const float GaugeSweep = 1.6f;            // of the whole gauge per second
        private const int MaterialsShown = 5;
        private const string PackWindowName = "inventory-window";
        private const string WindowName = "forge-window";
        private static readonly string[] TabNames = { "temper", "fusion", "tuning", "reforge" };
        private static readonly string[] ActLabels = { "TEMPER", "FUSE", "TUNE", "REFORGE" };
        private static readonly string[] TabTitles = { "Tempering", "Fusion", "Tuning", "Reforge" };
        private static readonly string[] FlashClasses = { "hit-success", "hit-fail", "hit-broken", "hit-held" };
        private static readonly string[] Intros =
        {
            "Tempering raises what a piece is for, up to 120%. Each Alloy adds its percent on a success. A fail takes some back, and past 60% a fail can break the piece unless a Failsafe is used.",
            "Fusion melts an untouched Rare of the same kind into a Rare or Epic piece, over twelve stages. A fail destroys the material and never harms the piece.",
            "Tuning adds a second set of stats to Rare and Epic gear, one stage at a time, for chips, Renown and Creds.",
            "Reforge turns an untouched Rare into another Rare of the same level. A fail costs only the Creds.",
        };
        private static readonly string[] Hints =
        {
            "Right-click gear in your Pack, or drag it here",
            "Right-click a Rare or Epic piece, or drag it here",
            "Right-click a Rare or Epic piece, or drag it here",
            "Right-click an untouched Rare, or drag it here",
        };

        // Gauge geometry: a dial open at the bottom, where the number sits.
        private const float GaugeFrom = 135f, GaugeSpan = 270f;
        private const float GaugeInset = 10f, GaugeWidth = 12f;
        private static readonly Color GaugeTrack = new Color(1f, 1f, 1f, 0.09f);
        private static readonly Color GaugeBreak = new Color(0.94f, 0.3f, 0.26f, 0.9f);

        private VisualElement root, window, anvil, gauge, piece, materials, chips, readout;
        private Label purse, value, hint, itemName, itemState, step, chance, gain, reason, result;
        private Button act, reset;
        private readonly Button[] tabButtons = new Button[TabNames.Length];
        private readonly ServicePanelPlacement placement = new ServicePanelPlacement();
        private Tab tab = Tab.Temper;
        private PlayerGearWork work;
        private PlayerInventory inventory;
        // The Pack, the Vault and the Deep Vault as one: materials are counted, and spent, across all three.
        private PlayerItemSources sources;
        private GearUIController gear;
        private bool initialized;
        // Open at the Armorer, as against merely shown: the layout editor shows the window to have it placed.
        private bool open, previewing;
        private bool openedPack;
        private float nextRangeCheck;

        // What is on the bench.
        private int pieceCell = -1;
        private int materialCell = -1;
        private ushort alloyItemId;
        private ushort protectionItemId;
        private readonly List<StatModifier> before = new List<StatModifier>(8), after = new List<StatModifier>(8);

        // The gauge: how full it is drawn, where it is heading, and how far a success would take it (all 0 to 1).
        private float gaugeShown, gaugeTarget, gaugePreview, gaugeBreakFrom = -1f;
        private int gaugeSegments;
        private Color gaugeColor = Color.white;

        // A strike in progress, and the flash of its result.
        private bool striking, hasPending, refreshHeld;
        private float strikeStarted;
        private GearWorkResult pending;
        private float flashEnds, shakeEnds;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q(WindowName);
            if (window == null)
            {
                Debug.LogError("[Forge] ForgeWindow.uxml is not instanced in this UI document.", this);
                return;
            }
            anvil = window.Q("forge-anvil");
            gauge = window.Q("forge-gauge");
            piece = window.Q("forge-piece");
            materials = window.Q("forge-materials");
            chips = window.Q("forge-chips");
            readout = window.Q("forge-readout");
            purse = window.Q<Label>("forge-purse");
            value = window.Q<Label>("forge-value");
            hint = window.Q<Label>("forge-hint");
            itemName = window.Q<Label>("forge-item-name");
            itemState = window.Q<Label>("forge-item-state");
            step = window.Q<Label>("forge-step");
            chance = window.Q<Label>("forge-chance");
            gain = window.Q<Label>("forge-gain");
            reason = window.Q<Label>("forge-reason");
            result = window.Q<Label>("forge-result");
            act = window.Q<Button>("forge-act");
            reset = window.Q<Button>("forge-reset");

            GearUIController.CreateItemVisual(piece, false);
            piece.RegisterCallback<ClickEvent>(_ => { if (!striking && pieceCell >= 0) SetPiece(-1); });
            piece.RegisterCallback<PointerEnterEvent>(_ => ShowTip(piece, pieceCell, "Click: take it off the forge"));
            piece.RegisterCallback<PointerLeaveEvent>(_ => HideTip());
            gauge.generateVisualContent += DrawGauge;
            act.clicked += Act;
            reset.clicked += () => { if (work != null && pieceCell >= 0 && !striking) work.RequestResetTuning(pieceCell); };
            window.Q<Button>("close-forge").clicked += Close;
            for (int i = 0; i < TabNames.Length; i++)
            {
                Tab target = (Tab)i;
                tabButtons[i] = window.Q<Button>("forge-tab-" + TabNames[i]);
                tabButtons[i].clicked += () => { if (!striking) ShowTab(target); };
            }
            SetOpen(false);

            NpcServices.Register(ServiceIds.Forge, NpcOffer.GearWork, "OPEN FORGE", Open);
            PlayerInventory.LocalChanged += Close;
            HudLayoutEditor.RegisterPreview(WindowName, Preview);
            initialized = true;
        }

        private void OnDisable()
        {
            if (!initialized) return;
            Close();
            if (previewing) Preview(false);
            HudLayoutEditor.UnregisterPreview(WindowName);
            initialized = false;
            gauge.generateVisualContent -= DrawGauge;
            PlayerInventory.LocalChanged -= Close;
            NpcServices.Unregister(ServiceIds.Forge);
            if (gear != null) gear.UnregisterPackTarget(this);
            gear = null;
            Bind(null);
        }

        private void Bind(PlayerGearWork next)
        {
            if (work != null) work.Finished -= OnFinished;
            if (sources != null) sources.Changed -= OnPackChanged;
            work = next;
            inventory = next != null ? next.Inventory : null;
            sources = next != null ? next.GetComponent<PlayerItemSources>() : null;
            if (work != null) work.Finished += OnFinished;
            if (sources != null) sources.Changed += OnPackChanged;
        }

        // ------------------------------------------------------------------ open / close

        public bool IsOpen => initialized && open;

        public void Open()
        {
            if (!initialized) return;
            previewing = false;
            PlayerGearWork next = PlayerGearWork.Local;
            if (next == null || next.Settings == null)
            {
                ClientPresentationRegistry.Messages?.ShowMessage("The forge isn't available for this character.");
                return;
            }
            Bind(next);
            pieceCell = materialCell = -1;
            striking = hasPending = refreshHeld = false;
            gaugeShown = 0f;
            SetResult(string.Empty, null);
            SetOpen(true);
            window.parent?.BringToFront();
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;

            // The Pack is where the pieces come from: it opens with the bench and the bench sits beside it.
            if (gear == null)
            {
                gear = GearUIController.Instance;
                if (gear != null) gear.RegisterPackTarget(this);
            }
            openedPack = gear != null && !gear.IsInventoryOpen;
            if (openedPack) gear.SetInventoryOpen(true);
            placement.Open(window, root.Q(PackWindowName));

            ShowTab(tab);
            gear?.SuppressWorldInput();
        }

        public void Close()
        {
            if (!initialized || !IsOpen) return;
            SetOpen(false);
            placement.Close();
            HideTip();
            pieceCell = materialCell = -1;
            striking = hasPending = refreshHeld = false;
            Bind(null);
            if (gear != null)
            {
                if (openedPack) gear.SetInventoryOpen(false);
                gear.RefreshPack(); // cells that were on the bench are free again
                gear.SuppressWorldInput();
            }
            openedPack = false;
        }

        private void SetOpen(bool shown)
        {
            open = shown;
            window.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", shown);
        }

        /// <summary>
        /// The layout editor has the bench out to be placed, away from any Armorer: it shows the tab it was last on
        /// with nothing on the anvil, and none of it can be pressed (the editor's frame lies over it).
        /// </summary>
        private void Preview(bool on)
        {
            if (!initialized || open) return; // open at the Armorer: it is its own likeness
            previewing = on;
            if (!on)
            {
                Bind(null);
                return;
            }
            PlayerGearWork local = PlayerGearWork.Local;
            if (local == null || local.Settings == null) return;
            Bind(local);
            pieceCell = materialCell = -1;
            ShowTab(tab);
        }

        private void Update()
        {
            if (!IsOpen) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !HudTyping.IsTyping(root)) { Close(); return; }
            if (work == null) { Close(); return; }
            placement.Tick();
            Animate();
            if (Time.unscaledTime < nextRangeCheck) return;
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;
            RefreshPurse(); // Luck moves with buffs, Creds with everything else
            if (work.InReach) return;
            Close();
            ClientPresentationRegistry.Messages?.ShowMessage("You walked away from the forge.");
        }

        private void ShowTab(Tab next)
        {
            tab = next;
            for (int i = 0; i < tabButtons.Length; i++) tabButtons[i].EnableInClassList("active", i == (int)tab);
            hint.text = Hints[(int)tab];
            materialCell = -1;
            SetResult(string.Empty, null);
            Refresh();
            gaugeShown = gaugeTarget; // a change of tab is not a result: no sweep
        }

        // ------------------------------------------------------------------ the Pack hands pieces over (IPackTarget)

        public string QuickHint => "place on the forge";

        public bool LocksCell(int cell) => IsOpen && cell >= 0 && (cell == pieceCell || cell == materialCell);

        public bool TryQuick(ItemLocation from) => IsOpen && from.IsBag && Place(from.index);

        public bool TryDrop(ItemLocation from, Vector2 panelPosition)
        {
            if (!IsOpen || !from.IsBag || !window.worldBound.Contains(panelPosition)) return false;
            Place(from.index);
            return true; // dropped on the bench: it never falls through to whatever is behind
        }

        /// <summary>Puts a Pack item where it belongs on the bench. False when the bench has no use for it.</summary>
        private bool Place(int cell)
        {
            if (striking || inventory == null || Settings == null) return false;
            ItemStack stack = inventory.Get(cell);
            ItemDefinition item = stack.IsEmpty ? null : inventory.GetDefinition(cell);
            if (item == null) return false;

            if (GearProgressionRules.TryFindAlloy(Settings.temper, item.itemId, out _))
            {
                alloyItemId = item.itemId;
                if (tab != Tab.Temper) ShowTab(Tab.Temper); else Refresh();
                return true;
            }
            if (GearProgressionRules.ProtectionOf(Settings.temper, item.itemId) != TemperProtection.None)
            {
                protectionItemId = item.itemId;
                if (tab != Tab.Temper) ShowTab(Tab.Temper); else Refresh();
                return true;
            }
            if (!item.IsLadderGear || item.equipSlot == EquipSlot.Pet) return false;

            GearItemState state = GearItemState.Of(stack);
            if (tab == Tab.Fusion && pieceCell >= 0 && cell != pieceCell && IsFusionMaterial(cell))
            {
                materialCell = cell;
                Refresh();
                gear?.RefreshPack();
                return true;
            }
            if (!Listed(item, state))
            {
                ClientPresentationRegistry.Messages?.ShowMessage(tab == Tab.Reforge
                    ? "Only an untouched Rare can be reforged." : $"{TabTitles[(int)tab]} needs Rare or Epic gear.");
                return true;
            }
            SetPiece(cell);
            return true;
        }

        private void SetPiece(int cell)
        {
            pieceCell = cell;
            materialCell = -1;
            SetResult(string.Empty, null);
            Refresh();
            gaugeShown = gaugeTarget;
            gear?.RefreshPack();
        }

        // ------------------------------------------------------------------ contents

        private GearProgressionSettings Settings => work != null ? work.Settings : null;
        private ItemDefinition Definition(int cell) => cell >= 0 && inventory != null ? inventory.GetDefinition(cell) : null;

        private bool Listed(ItemDefinition item, GearItemState state)
        {
            if (item == null || !item.IsLadderGear || item.equipSlot == EquipSlot.Pet) return false;
            bool rareUp = item.rarity == ItemRarity.Rare || item.rarity == ItemRarity.Epic;
            switch (tab)
            {
                case Tab.Fusion: return rareUp;
                case Tab.Tuning: return rareUp && item.equipSlot != EquipSlot.Cape;
                case Tab.Reforge: return GearProgressionRules.ReforgeBlockedReason(Settings, item, state) == null;
                default: return true;
            }
        }

        private void OnPackChanged()
        {
            // The Pack changes the moment the server answers; the bench shows it when the strike lands.
            if (striking) refreshHeld = true;
            else Refresh();
        }

        private void Refresh()
        {
            if (!(open || previewing) || work == null || inventory == null) return;
            refreshHeld = false;
            RefreshPurse();

            ItemDefinition item = Definition(pieceCell);
            ItemStack stack = pieceCell >= 0 ? inventory.Get(pieceCell) : ItemStack.Empty;
            GearItemState state = GearItemState.Of(stack);
            if (item == null || !Listed(item, state))
            {
                pieceCell = materialCell = -1;
                item = null;
                stack = ItemStack.Empty;
                state = default;
            }

            GearUIController.SetItemVisual(piece, item, 1);
            piece.EnableInClassList("empty", item == null);
            // The socket's rim and the weapon icon's own glow, in the piece's Temper colour as in the Pack, on every tab.
            GearUIController.SetRim(piece, item, state.Temper, Settings);
            GearUIController.SetTemperGlow(piece, item, state.Temper, Settings);
            hint.style.display = item == null ? DisplayStyle.Flex : DisplayStyle.None;
            materials.Clear();
            chips.Clear();
            reset.style.display = DisplayStyle.None;
            act.text = ActLabels[(int)tab];
            readout.EnableInClassList("idle", item == null);

            if (item == null)
            {
                itemName.text = string.Empty;
                itemState.text = string.Empty;
                value.text = string.Empty;
                step.text = string.Empty;
                chance.text = string.Empty;
                gain.text = Intros[(int)tab];
                reason.text = string.Empty;
                reason.style.display = DisplayStyle.None;
                act.SetEnabled(false);
                SetGauge(0f, 0f, tab == Tab.Fusion ? Settings.fusion.maxStage : tab == Tab.Tuning ? Settings.tuning.maxStage : 0,
                    tab == Tab.Temper ? BreakStart() : -1f, GaugeTrack);
                return;
            }

            itemName.text = item.displayName;
            GearUIController.ApplyRarityText(itemName, item.rarity);
            itemState.text = $"{KindOf(item)}  ·  LEVEL {item.itemLevel}" + (state.IsClean ? string.Empty : "  ·  " + state);

            string blocked;
            switch (tab)
            {
                case Tab.Fusion: blocked = FusionBench(item, stack, state); break;
                case Tab.Tuning: blocked = TuningBench(item, stack, state); break;
                case Tab.Reforge: blocked = ReforgeBench(item, state); break;
                default: blocked = TemperBench(item, stack, state); break;
            }
            reason.text = blocked ?? string.Empty;
            reason.style.display = blocked == null ? DisplayStyle.None : DisplayStyle.Flex;
            act.SetEnabled(blocked == null && !striking);
        }

        private static string KindOf(ItemDefinition item) => item.weaponClass != WeaponClassId.None
            ? WeaponClasses.DisplayName(item.weaponClass).ToUpperInvariant()
            : item.equipSlot.ToString().ToUpperInvariant();

        private Color GlowColor(ItemDefinition item, int temper) =>
            GearProgressionRules.TryGetGlow(Settings.temper, temper, out TemperGlowBand band) ? band.ColorFor(item.rarity) : gauge.resolvedStyle.color;

        private void RefreshPurse()
        {
            if (work == null) return;
            purse.text = $"{work.Creds:N0} Creds  ·  {work.Renown:N0} Renown  ·  Luck {work.Luck:0}";
        }

        /// <summary>Where on the Temper gauge a fail can first break the piece (0 to 1), or -1 when it never can.</summary>
        private float BreakStart()
        {
            TemperSettings temper = Settings.temper;
            int from = int.MaxValue;
            if (temper.breakBands != null)
                foreach (TemperBreakBand band in temper.breakBands)
                    if ((band.chanceFrom > 0f || band.chanceTo > 0f) && band.from < from) from = band.from;
            return from == int.MaxValue ? -1f : Mathf.Clamp01(from / (float)temper.hardCap);
        }

        // ------------------------------------------------------------------ the four benches

        private string TemperBench(ItemDefinition item, ItemStack stack, GearItemState state)
        {
            TemperSettings temper = Settings.temper;
            // The Alloy that fits where the piece is: Prime Alloy from the soft cap, the others below it.
            bool primeInUse = GearProgressionRules.PrimeAlloyInUse(temper);
            bool wantPrime = primeInUse && state.Temper >= temper.softCap;
            GearProgressionRules.TryFindAlloy(temper, alloyItemId, out AlloyGrade alloy);
            if (alloy.itemId == 0 || alloy.prime != wantPrime || sources.Count(alloy.itemId) == 0)
            {
                alloy = default;
                foreach (AlloyGrade candidate in temper.alloys)
                    if (candidate.prime == wantPrime && sources.Count(candidate.itemId) > 0) { alloy = candidate; break; }
                alloyItemId = alloy.itemId;
            }
            if (protectionItemId != 0 && sources.Count(protectionItemId) == 0) protectionItemId = 0;

            VisualElement alloys = Row("ALLOY");
            foreach (AlloyGrade candidate in temper.alloys)
            {
                if (candidate.prime && !primeInUse) continue; // the Alloy of a later system: not offered until it has a use
                AlloyGrade choice = candidate;
                Pick(alloys, choice.itemId, null, choice.itemId == alloyItemId, () => { alloyItemId = choice.itemId; Refresh(); });
            }
            VisualElement failsafes = Row("FAILSAFE");
            Pick(failsafes, 0, null, protectionItemId == 0, () => { protectionItemId = 0; Refresh(); });
            foreach (ushort id in new[] { temper.failsafeItemId, temper.luckyFailsafeItemId, temper.advancedFailsafeItemId })
            {
                ushort choice = id;
                Pick(failsafes, choice, null, choice == protectionItemId, () => { protectionItemId = choice; Refresh(); });
            }

            TemperProtection protection = GearProgressionRules.ProtectionOf(temper, protectionItemId);
            string blocked = GearProgressionRules.TemperBlockedReason(Settings, item, state, alloy, protection);
            long cost = GearProgressionRules.TemperCost(Settings, item, state.Temper);
            value.text = $"+{state.Temper}%";
            value.style.color = GlowColor(item, state.Temper);
            act.text = $"TEMPER   ·   {cost:N0} Creds";

            int reach = state.Temper;
            if (alloy.itemId != 0)
            {
                float odds = GearProgressionRules.TemperChance(temper, state.Temper, alloy.step, work.Luck, protection);
                float breaks = GearProgressionRules.BreakChance(temper, state.Temper);
                float luck = GearProgressionRules.LuckBonus(work.Luck, temper.luckPerPercent, temper.maxLuckBonus);
                reach = Mathf.Min(alloy.prime ? temper.hardCap : Mathf.Min(temper.hardCap, Mathf.Max(temper.softCap, state.Temper)), state.Temper + alloy.step);
                step.text = $"+{state.Temper}%  to  +{reach}%";
                SetChance(odds);
                if (luck > 0f) Chip($"Luck +{luck:0.#}%", null);
                if (protection == TemperProtection.Advanced) Chip("A fail costs nothing", "safe");
                else
                {
                    Chip($"A fail takes {temper.failLoss}% back", null);
                    if (breaks > 0f)
                    {
                        if (protection == TemperProtection.None) Chip($"{breaks:0.#}% of fails break it", "danger");
                        else Chip("The Failsafe stops a break", "safe");
                    }
                }
                string gained = Gain(item, stack, state.WithTemper(reach));
                gain.text = gained.Length > 0 ? "On a success: " + gained : string.Empty;
            }
            else
            {
                step.text = $"+{state.Temper}%";
                chance.text = string.Empty;
                gain.text = string.Empty;
            }
            SetGauge(state.Temper / (float)temper.hardCap, reach / (float)temper.hardCap, 0, BreakStart(), GlowColor(item, Mathf.Max(1, state.Temper)));
            if (blocked == null && work.Creds < cost) blocked = "You don't have enough Creds.";
            return blocked;
        }

        private bool IsFusionMaterial(int cell)
        {
            ItemDefinition target = Definition(pieceCell);
            if (target == null || cell == pieceCell) return false;
            ItemStack candidate = inventory.Get(cell);
            ItemDefinition candidateItem = candidate.IsEmpty ? null : inventory.GetDefinition(cell);
            if (candidateItem == null || !candidateItem.IsLadderGear) return false;
            GearItemState state = GearItemState.Of(inventory.Get(pieceCell));
            // Asked as if the piece were fully tempered: only the material is in question here.
            return GearProgressionRules.FusionBlockedReason(Settings, target, new GearItemState(GearItemState.MaxTemper, state.Fusion, 0),
                candidateItem, GearItemState.Of(candidate)) == null;
        }

        private string FusionBench(ItemDefinition item, ItemStack stack, GearItemState state)
        {
            FusionSettings fusion = Settings.fusion;
            if (materialCell >= 0 && !IsFusionMaterial(materialCell)) materialCell = -1;

            VisualElement picks = Row("MATERIAL");
            int found = 0;
            for (int cell = 0; cell < PlayerInventory.Capacity; cell++)
            {
                if (!IsFusionMaterial(cell)) continue;
                if (materialCell < 0) materialCell = cell;
                found++;
                if (found > MaterialsShown) continue;
                int choice = cell;
                PickCell(picks, cell, cell == materialCell, () => { materialCell = choice; Refresh(); gear?.RefreshPack(); });
            }
            if (found == 0)
            {
                int needed = GearProgressionRules.FusionMaterialLevel(Settings.statTable, item);
                Note(picks, $"An untouched Rare {KindOf(item).ToLowerInvariant()} of item level {needed}. None in your Pack.", true);
            }
            int catalysts = sources.Count(fusion.catalystItemId);
            VisualElement catalyst = Row("CATALYST");
            Pick(catalyst, fusion.catalystItemId, null, catalysts > 0, null);
            Note(catalyst, $"One {NameOf(fusion.catalystItemId)} a try. You have {catalysts}{Where(fusion.catalystItemId)}.", catalysts < 1);

            ItemStack material = materialCell >= 0 ? inventory.Get(materialCell) : ItemStack.Empty;
            ItemDefinition materialItem = materialCell >= 0 ? inventory.GetDefinition(materialCell) : null;
            string blocked = GearProgressionRules.FusionBlockedReason(Settings, item, state, materialItem, GearItemState.Of(material));
            int next = Mathf.Min(fusion.maxStage, state.Fusion + 1);
            long cost = GearProgressionRules.FusionCost(fusion, next);
            value.text = $"FS {state.Fusion}";
            value.style.color = StyleKeyword.Null;
            act.text = $"FUSE   ·   {cost:N0} Creds";
            step.text = state.Fusion >= fusion.maxStage ? $"FS {state.Fusion}" : $"FS {state.Fusion}  to  FS {next}";
            SetChance(GearProgressionRules.FusionChance(fusion, next, work.Luck));
            if (fusion.temperCostOnSuccess > 0) Chip($"A success takes {fusion.temperCostOnSuccess}% Temper", null);
            if (fusion.failConsumesMaterial) Chip("A fail destroys the material", "danger");
            Chip("The piece is never harmed", "safe");
            string gained = Gain(item, stack, GearProgressionRules.ResolveFusion(fusion, state));
            gain.text = gained.Length > 0 ? "On a success: " + gained : string.Empty;
            SetGauge(state.Fusion / (float)fusion.maxStage, next / (float)fusion.maxStage, fusion.maxStage, -1f, gauge.resolvedStyle.color);
            if (blocked == null && catalysts < 1) blocked = $"You have no {NameOf(fusion.catalystItemId)}.";
            if (blocked == null && work.Creds < cost) blocked = "You don't have enough Creds.";
            return blocked;
        }

        private string TuningBench(ItemDefinition item, ItemStack stack, GearItemState state)
        {
            TuningSettings tuning = Settings.tuning;
            string blocked = GearProgressionRules.TuneBlockedReason(Settings, item, state);
            bool canReset = state.Tune > 0 && sources.Count(tuning.resetChipItemId) > 0;
            reset.style.display = canReset ? DisplayStyle.Flex : DisplayStyle.None;
            int next = Mathf.Min(tuning.maxStage, state.Tune + 1);
            value.text = $"T {state.Tune}";
            value.style.color = StyleKeyword.Null;
            SetGauge(state.Tune / (float)tuning.maxStage, next / (float)tuning.maxStage, tuning.maxStage, -1f, gauge.resolvedStyle.color);
            if (!GearProgressionRules.TryGetBracket(tuning, next, out TuningBracket bracket))
            {
                step.text = $"Tune {state.Tune}";
                chance.text = string.Empty;
                gain.text = string.Empty;
                return blocked;
            }
            int have = sources.Count(bracket.chipItemId);
            VisualElement chipRow = Row("CHIPS");
            Pick(chipRow, bracket.chipItemId, $"x{bracket.chips}", have >= bracket.chips, null);
            Note(chipRow, $"{bracket.chips} {NameOf(bracket.chipItemId)} and {bracket.renown} Renown a try. You have {have}{Where(bracket.chipItemId)} and {work.Renown:N0}.",
                have < bracket.chips || work.Renown < bracket.renown);

            act.text = $"TUNE   ·   {bracket.creds:N0} Creds";
            step.text = $"Tune {state.Tune}  to  Tune {next}";
            SetChance(GearProgressionRules.TuneChance(tuning, next));
            if (state.Tune >= tuning.failDropsFromStage) Chip("A fail drops one stage", "danger");
            else Chip("A fail costs the materials only", "safe");
            string gained = Gain(item, stack, state.WithTune(next));
            gain.text = gained.Length > 0 ? "On a success: " + gained : string.Empty;
            if (blocked == null && have < bracket.chips) blocked = $"You need {bracket.chips} {NameOf(bracket.chipItemId)}.";
            if (blocked == null && work.Renown < bracket.renown) blocked = $"You need {bracket.renown} Renown.";
            if (blocked == null && work.Creds < bracket.creds) blocked = "You don't have enough Creds.";
            return blocked;
        }

        private string ReforgeBench(ItemDefinition item, GearItemState state)
        {
            ReforgeSettings reforge = Settings.reforge;
            string blocked = GearProgressionRules.ReforgeBlockedReason(Settings, item, state);
            long cost = GearProgressionRules.ReforgeCost(reforge, item.itemLevel);
            Note(Row("BECOMES"), $"Another Rare of item level {item.itemLevel} that you can wear, picked at random.", false);
            value.text = string.Empty;
            act.text = $"REFORGE   ·   {cost:N0} Creds";
            step.text = $"Level {item.itemLevel} Rare";
            SetChance(GearProgressionRules.ReforgeChance(reforge, work.Luck));
            Chip("A fail costs only the Creds", "safe");
            gain.text = string.Empty;
            SetGauge(0f, 0f, 0, -1f, gauge.resolvedStyle.color);
            if (blocked == null && work.Creds < cost) blocked = "You don't have enough Creds.";
            return blocked;
        }

        // ------------------------------------------------------------------ building blocks

        private VisualElement Row(string label)
        {
            var row = new VisualElement();
            row.AddToClassList("forge-material-row");
            var name = new Label(label) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("forge-material-label");
            row.Add(name);
            var picks = new VisualElement();
            picks.AddToClassList("forge-picks");
            row.Add(picks);
            materials.Add(row);
            return picks;
        }

        /// <summary>" (Pack 3 · Vault 12)" when some of a material is stored, so the count is not a surprise; empty when all of it is carried.</summary>
        private string Where(ushort itemId) =>
            sources.CountIn(ItemSourceKind.Pack, itemId) == sources.Count(itemId) ? string.Empty : $" ({sources.Describe(itemId)})";

        /// <summary>One kind of material as an icon with how many the player has, carried or stored. Item 0 is "none".</summary>
        private void Pick(VisualElement parent, ushort itemId, string tag, bool chosen, Action choose)
        {
            ItemDefinition item = itemId != 0 && inventory.Catalog != null ? inventory.Catalog.Get(itemId) : null;
            int have = itemId != 0 ? sources.Count(itemId) : 0;
            var pick = new VisualElement();
            pick.AddToClassList("item-cell");
            pick.AddToClassList("forge-pick");
            GearUIController.CreateItemVisual(pick, true);
            GearUIController.SetItemVisual(pick, item, have);
            pick.EnableInClassList("chosen", chosen && choose != null);
            pick.EnableInClassList("out", itemId != 0 && have == 0);
            if (itemId == 0)
            {
                var none = new Label("NONE") { pickingMode = PickingMode.Ignore };
                none.AddToClassList("forge-pick-none");
                pick.Add(none);
            }
            if (!string.IsNullOrEmpty(tag))
            {
                var label = new Label(tag) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("forge-pick-tag");
                pick.Add(label);
            }
            if (choose != null && (itemId == 0 || have > 0))
                pick.RegisterCallback<ClickEvent>(_ => { if (!striking) choose(); });
            if (item != null)
            {
                pick.RegisterCallback<PointerEnterEvent>(_ => { if (gear != null) gear.ShowItemTooltip(pick, item, new ItemStack(item.itemId, 1), have > 0 ? $"You have {have}{Where(item.itemId)}" : "You have none"); });
                pick.RegisterCallback<PointerLeaveEvent>(_ => HideTip());
            }
            parent.Add(pick);
        }

        /// <summary>A piece of gear in the Pack as a pick (a Fusion material).</summary>
        private void PickCell(VisualElement parent, int cell, bool chosen, Action choose)
        {
            ItemDefinition item = Definition(cell);
            var pick = new VisualElement();
            pick.AddToClassList("item-cell");
            pick.AddToClassList("forge-pick");
            GearUIController.CreateItemVisual(pick, false);
            GearUIController.SetItemVisual(pick, item, inventory.Get(cell));
            pick.EnableInClassList("chosen", chosen);
            pick.RegisterCallback<ClickEvent>(_ => { if (!striking) choose(); });
            pick.RegisterCallback<PointerEnterEvent>(_ => ShowTip(pick, cell, "Click: use as the material"));
            pick.RegisterCallback<PointerLeaveEvent>(_ => HideTip());
            parent.Add(pick);
        }

        private static void Note(VisualElement parent, string text, bool isShort)
        {
            var note = new Label(text) { pickingMode = PickingMode.Ignore };
            note.AddToClassList("forge-material-note");
            note.EnableInClassList("short", isShort);
            parent.Add(note);
        }

        private void Chip(string text, string tone)
        {
            var chip = new Label(text) { pickingMode = PickingMode.Ignore };
            chip.AddToClassList("forge-chip");
            if (tone != null) chip.AddToClassList(tone);
            chips.Add(chip);
        }

        private void SetChance(float percent)
        {
            chance.text = $"{percent:0.#}%";
            chance.EnableInClassList("risky", percent < 70f && percent >= 40f);
            chance.EnableInClassList("poor", percent < 40f);
        }

        private void ShowTip(VisualElement element, int cell, string tip)
        {
            ItemDefinition item = Definition(cell);
            if (gear != null && item != null) gear.ShowItemTooltip(element, item, inventory.Get(cell), tip);
        }

        private void HideTip()
        {
            if (gear != null) gear.HideItemTooltip();
        }

        private string NameOf(ushort itemId)
        {
            ItemDefinition item = inventory.Catalog != null ? inventory.Catalog.Get(itemId) : null;
            return item != null ? item.displayName : "material";
        }

        /// <summary>What the piece would gain by going from its state now to <paramref name="next"/>, as stat lines.</summary>
        private string Gain(ItemDefinition item, ItemStack stack, GearItemState next)
        {
            before.Clear();
            after.Clear();
            GearProgressionRules.AppendEffects(Settings, stack, item, null, before);
            GearProgressionRules.AppendEffects(Settings, new ItemStack(stack.itemId, 1, next.PackInto(stack.data)), item, null, after);
            // What is left of "after" once "before" is taken off, stat by stat.
            foreach (StatModifier old in before) after.Add(new StatModifier(old.Stat, old.ModType, -old.Value));

            var text = new StringBuilder();
            for (int i = 0; i < after.Count; i++)
            {
                bool counted = false;
                for (int j = 0; j < i && !counted; j++)
                    counted = after[j].Stat == after[i].Stat && after[j].ModType == after[i].ModType;
                if (counted) continue;
                float delta = 0f;
                for (int j = i; j < after.Count; j++)
                    if (after[j].Stat == after[i].Stat && after[j].ModType == after[i].ModType) delta += after[j].Value;
                if (Mathf.Abs(delta) < 0.005f) continue;
                if (text.Length > 0) text.Append(", ");
                text.Append(StatDisplay.FormatModifier(after[i].Stat, after[i].ModType, delta));
            }
            return text.ToString();
        }

        // ------------------------------------------------------------------ striking

        private void Act()
        {
            if (work == null || pieceCell < 0 || striking) return;
            if (tab == Tab.Fusion && materialCell < 0) return;
            SetResult(string.Empty, null);
            striking = true;
            hasPending = false;
            strikeStarted = Time.unscaledTime;
            act.SetEnabled(false);
            act.AddToClassList("striking");
            switch (tab)
            {
                case Tab.Temper: work.RequestTemper(pieceCell, alloyItemId, protectionItemId); break;
                case Tab.Fusion: work.RequestFuse(pieceCell, materialCell); break;
                case Tab.Tuning: work.RequestTune(pieceCell); break;
                case Tab.Reforge: work.RequestReforge(pieceCell); break;
            }
        }

        private void OnFinished(GearWorkResult finished)
        {
            if (!striking) { Reveal(finished); return; } // a reset, or anything not started by the button
            pending = finished;
            hasPending = true;
        }

        /// <summary>The gauge sweep, the strike's wait and the flash of its result. Runs every frame while open.</summary>
        private void Animate()
        {
            float now = Time.unscaledTime;
            if (striking)
            {
                float waited = now - strikeStarted;
                if (hasPending && waited >= StrikeSeconds) Reveal(pending);
                else if (!hasPending && waited >= StrikeGiveUpSeconds) EndStrike(); // refused: the reason was said in a message
            }
            if (!Mathf.Approximately(gaugeShown, gaugeTarget))
                gaugeShown = Mathf.MoveTowards(gaugeShown, gaugeTarget, GaugeSweep * Time.unscaledDeltaTime);

            if (flashEnds > 0f && now >= flashEnds)
            {
                flashEnds = 0f;
                // The flash was drawn over the rim, which already has the colour of the Temper the piece has now.
                foreach (string name in FlashClasses) anvil.RemoveFromClassList(name);
            }
            if (shakeEnds > 0f)
            {
                float left = shakeEnds - now;
                if (left <= 0f)
                {
                    shakeEnds = 0f;
                    piece.style.translate = StyleKeyword.Null;
                }
                else
                {
                    float strength = left / ShakeSeconds;
                    piece.style.translate = new Translate(Mathf.Sin(now * 70f) * 7f * strength, 0f);
                }
            }
            gauge.MarkDirtyRepaint(); // the preview arc breathes
        }

        private void EndStrike()
        {
            striking = false;
            hasPending = false;
            act.RemoveFromClassList("striking");
            Refresh();
        }

        private void Reveal(GearWorkResult finished)
        {
            GearItemState was = GearItemState.Of(finished.before), now = GearItemState.Of(finished.after);
            bool success = finished.outcome == GearWorkOutcome.Success, broken = finished.outcome == GearWorkOutcome.Broken;
            // An Advanced Failsafe held: nothing was lost, and the bench does not show a fail.
            bool held = finished.outcome == GearWorkOutcome.FailHeld;
            string text;
            switch (finished.kind)
            {
                case GearWorkKind.Fusion:
                    text = success ? $"Fused to FS {now.Fusion}." : "The Fusion failed. The piece is unharmed.";
                    break;
                case GearWorkKind.Tuning:
                    text = success ? $"Tuned to stage {now.Tune}."
                        : now.Tune < was.Tune ? $"Tuning failed. It fell to stage {now.Tune}." : "Tuning failed.";
                    break;
                case GearWorkKind.TuningReset:
                    text = "Tuning reset to stage 0.";
                    break;
                case GearWorkKind.Reforge:
                    text = success ? $"Reforged into {NameOf(finished.after.itemId)}." : "The Reforge failed. Only the Creds are gone.";
                    break;
                default:
                    text = success ? $"Tempered to +{now.Temper}%."
                        : broken ? "The piece broke."
                        : held ? "Held. The Advanced Failsafe took it: nothing lost."
                        : finished.outcome == GearWorkOutcome.FailProtected
                            ? (now.Temper == was.Temper ? "Failed. The Failsafe took it: nothing lost." : $"Failed. The Failsafe stopped a break: Temper +{now.Temper}%.")
                        : $"Failed. Temper fell to +{now.Temper}%.";
                    break;
            }
            SetResult(text, success ? "success" : broken ? "broken" : held ? "held" : "fail");
            if (broken) pieceCell = materialCell = -1;

            foreach (string name in FlashClasses) anvil.RemoveFromClassList(name);
            anvil.AddToClassList(success ? "hit-success" : broken ? "hit-broken" : held ? "hit-held" : "hit-fail");
            flashEnds = Time.unscaledTime + FlashSeconds;
            if (!success && !held) shakeEnds = Time.unscaledTime + ShakeSeconds;
            // The number jumps and settles: the class is put on with no transition and taken off with one.
            value.AddToClassList("pop");
            value.schedule.Execute(() => value.RemoveFromClassList("pop")).StartingIn(60);

            striking = false;
            hasPending = false;
            act.RemoveFromClassList("striking");
            Refresh();
            gear?.RefreshPack();
        }

        private void SetResult(string text, string tone)
        {
            result.text = text;
            foreach (string name in new[] { "success", "fail", "broken", "held" }) result.EnableInClassList(name, name == tone);
        }

        // ------------------------------------------------------------------ the gauge

        private void SetGauge(float now, float preview, int segments, float breakFrom, Color color)
        {
            gaugeTarget = Mathf.Clamp01(now);
            gaugePreview = Mathf.Clamp01(preview);
            gaugeSegments = segments;
            gaugeBreakFrom = breakFrom;
            gaugeColor = color;
            gauge.MarkDirtyRepaint();
        }

        private void DrawGauge(MeshGenerationContext context)
        {
            Rect area = gauge.contentRect;
            float radius = Mathf.Min(area.width, area.height) * 0.5f - GaugeInset;
            if (radius <= 4f) return;
            Vector2 centre = area.center;
            Painter2D painter = context.painter2D;
            painter.lineCap = LineCap.Butt;

            Arc(painter, centre, radius, 0f, 1f, GaugeWidth, GaugeTrack);
            // Where a fail can break the piece: a thin warning line outside the dial.
            if (gaugeBreakFrom >= 0f && gaugeBreakFrom < 1f) Arc(painter, centre, radius + GaugeWidth * 0.5f + 3f, gaugeBreakFrom, 1f, 2f, GaugeBreak);

            float breath = 0.45f + 0.35f * Mathf.Sin(Time.unscaledTime * (striking ? 22f : 4f));
            Color fill = striking ? Color.Lerp(gaugeColor, Color.white, 0.5f) : gaugeColor;
            if (gaugePreview > gaugeShown)
                Arc(painter, centre, radius, gaugeShown, gaugePreview, GaugeWidth, new Color(fill.r, fill.g, fill.b, breath));
            if (gaugeShown > 0f) Arc(painter, centre, radius, 0f, gaugeShown, GaugeWidth, new Color(fill.r, fill.g, fill.b, 1f));

            // Stages (Fusion, Tuning): the dial is cut into them.
            if (gaugeSegments > 1)
            {
                Color cut = new Color(0.03f, 0.05f, 0.08f, 1f);
                for (int i = 1; i < gaugeSegments; i++)
                {
                    float angle = (GaugeFrom + GaugeSpan * i / gaugeSegments) * Mathf.Deg2Rad;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    painter.BeginPath();
                    painter.MoveTo(centre + direction * (radius - GaugeWidth * 0.5f - 1f));
                    painter.LineTo(centre + direction * (radius + GaugeWidth * 0.5f + 1f));
                    painter.strokeColor = cut;
                    painter.lineWidth = 2f;
                    painter.Stroke();
                }
            }
        }

        private static void Arc(Painter2D painter, Vector2 centre, float radius, float from, float to, float width, Color color)
        {
            if (to <= from) return;
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(GaugeFrom + GaugeSpan * from), Angle.Degrees(GaugeFrom + GaugeSpan * to));
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.Stroke();
        }
    }
}
