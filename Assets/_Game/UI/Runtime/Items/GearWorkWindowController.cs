using System;
using System.Collections.Generic;
using System.Text;
using KryticalNova.Core;
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
    /// The forge window: Tempering, Fusion, Tuning and Reforge at an Armorer. It lists the gear in the Pack, shows
    /// what the chosen tab would do to the chosen piece (odds, risk, cost, what it gains) and sends the request to
    /// <see cref="PlayerGearWork"/>; the server decides. Odds and reasons come from the same rules the server uses
    /// (<see cref="GearProgressionRules"/>), so the window never promises what the server refuses. Opened from the
    /// Armorer's dialogue and closed when the player walks away.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class GearWorkWindowController : MonoBehaviour
    {
        private enum Tab { Temper, Fusion, Tuning, Reforge }

        private const float RangeCheckSeconds = 0.25f;
        private static readonly string[] TabNames = { "temper", "fusion", "tuning", "reforge" };
        private static readonly string[] ActLabels = { "TEMPER", "FUSE", "TUNE", "REFORGE" };
        private static readonly string[] Intros =
        {
            "Tempering raises what a piece is for, up to 120%. Each Alloy adds its percent on a success; a fail takes some back, and past 60% a fail can break the piece unless a Failsafe is used.",
            "Fusion melts an untouched Rare of the same kind into a Rare or Epic piece. Twelve stages. A fail destroys the material and never harms the piece.",
            "Tuning adds a second set of stats to Rare and Epic gear, one stage at a time, for chips, Renown and Creds.",
            "Reforge turns an untouched Rare into another Rare of the same level. A fail costs only the Creds.",
        };

        private VisualElement root, window, options;
        private ScrollView items;
        private Label purse, intro, itemName, itemState, optionsTitle, odds, reason, result;
        private Button act, reset;
        private readonly Button[] tabButtons = new Button[TabNames.Length];
        private Tab tab = Tab.Temper;
        private PlayerGearWork work;
        private PlayerInventory inventory;
        private bool initialized;
        private float nextRangeCheck;

        private int selectedCell = -1;
        private int materialCell = -1;
        private ushort alloyItemId;
        private ushort protectionItemId;
        private readonly List<StatModifier> before = new List<StatModifier>(8), after = new List<StatModifier>(8);

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q("forge-window");
            if (window == null)
            {
                Debug.LogError("[Forge] ForgeWindow.uxml is not instanced in this UI document.", this);
                return;
            }
            items = window.Q<ScrollView>("forge-items");
            options = window.Q("forge-options");
            purse = window.Q<Label>("forge-purse");
            intro = window.Q<Label>("forge-intro");
            itemName = window.Q<Label>("forge-item-name");
            itemState = window.Q<Label>("forge-item-state");
            optionsTitle = window.Q<Label>("forge-options-title");
            odds = window.Q<Label>("forge-odds");
            reason = window.Q<Label>("forge-reason");
            result = window.Q<Label>("forge-result");
            act = window.Q<Button>("forge-act");
            reset = window.Q<Button>("forge-reset");
            act.clicked += Act;
            reset.clicked += () => { if (work != null && selectedCell >= 0) work.RequestResetTuning(selectedCell); };
            window.Q<Button>("close-forge").clicked += Close;
            for (int i = 0; i < TabNames.Length; i++)
            {
                Tab target = (Tab)i;
                tabButtons[i] = window.Q<Button>("forge-tab-" + TabNames[i]);
                tabButtons[i].clicked += () => ShowTab(target);
            }
            SetOpen(false);

            NpcServices.Register(ServiceIds.Forge, NpcOffer.GearWork, "OPEN FORGE", Open);
            PlayerInventory.LocalChanged += Close;
            initialized = true;
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            PlayerInventory.LocalChanged -= Close;
            NpcServices.Unregister(ServiceIds.Forge);
            Bind(null);
        }

        private void Bind(PlayerGearWork next)
        {
            if (work != null) work.Finished -= OnFinished;
            if (inventory != null) inventory.Changed -= Refresh;
            work = next;
            inventory = next != null ? next.Inventory : null;
            if (work != null) work.Finished += OnFinished;
            if (inventory != null) inventory.Changed += Refresh;
        }

        // ------------------------------------------------------------------ open / close

        public bool IsOpen => initialized && window.style.display == DisplayStyle.Flex;

        public void Open()
        {
            if (!initialized) return;
            PlayerGearWork next = PlayerGearWork.Local;
            if (next == null || next.Settings == null)
            {
                ClientPresentationRegistry.Messages?.ShowMessage("The forge isn't available for this character.");
                return;
            }
            Bind(next);
            selectedCell = materialCell = -1;
            SetResult(string.Empty, null);
            SetOpen(true);
            window.parent?.BringToFront();
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;
            ShowTab(tab);
            GearUIController.Instance?.SuppressWorldInput();
        }

        public void Close()
        {
            if (!initialized || !IsOpen) return;
            SetOpen(false);
            Bind(null);
            GearUIController.Instance?.SuppressWorldInput();
        }

        private void SetOpen(bool open)
        {
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", open);
        }

        private void Update()
        {
            if (!IsOpen) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !HudTyping.IsTyping(root)) { Close(); return; }
            if (work == null) { Close(); return; }
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
            intro.text = Intros[(int)tab];
            act.text = ActLabels[(int)tab];
            materialCell = -1;
            Refresh();
        }

        // ------------------------------------------------------------------ contents

        private GearProgressionSettings Settings => work.Settings;
        private ItemDefinition Definition(int cell) => cell >= 0 ? inventory.GetDefinition(cell) : null;

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

        private void Refresh()
        {
            if (!IsOpen || work == null || inventory == null) return;
            RefreshPurse();
            ItemDefinition selected = Definition(selectedCell);
            if (selected == null || !Listed(selected, GearItemState.Of(inventory.Get(selectedCell)))) selectedCell = -1;

            items.Clear();
            int shown = 0;
            for (int cell = 0; cell < PlayerInventory.Capacity; cell++)
            {
                ItemStack stack = inventory.Get(cell);
                if (stack.IsEmpty) continue;
                ItemDefinition item = inventory.GetDefinition(cell);
                GearItemState state = GearItemState.Of(stack);
                if (!Listed(item, state)) continue;
                if (selectedCell < 0) selectedCell = cell;
                items.Add(ItemRow(cell, item, state));
                shown++;
            }
            if (shown == 0)
            {
                var empty = new Label(tab == Tab.Temper ? "There is no gear in your Pack. Worn gear must be taken off to be worked on."
                    : tab == Tab.Reforge ? "Only an untouched Rare can be reforged. There is none in your Pack."
                    : "This needs Rare or Epic gear in your Pack.");
                empty.AddToClassList("forge-empty");
                items.Add(empty);
            }
            RefreshDetail();
        }

        private VisualElement ItemRow(int cell, ItemDefinition item, GearItemState state)
        {
            var row = new VisualElement();
            row.AddToClassList("forge-item");
            row.AddToClassList("kn-card");
            row.EnableInClassList("selected", cell == selectedCell);
            row.EnableInClassList("material", cell == materialCell);

            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("forge-item-icon");
            if (item.icon != null) icon.style.backgroundImage = new StyleBackground(item.icon);
            row.Add(icon);

            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("forge-item-text");
            var title = new Label(item.displayName) { pickingMode = PickingMode.Ignore };
            title.AddToClassList("forge-item-title");
            GearUIController.ApplyRarityText(title, item.rarity);
            var sub = new Label($"{KindOf(item)}  ·  LEVEL {item.itemLevel}") { pickingMode = PickingMode.Ignore };
            sub.AddToClassList("forge-item-sub");
            text.Add(title);
            text.Add(sub);
            row.Add(text);

            var tag = new Label(state.ToString()) { pickingMode = PickingMode.Ignore };
            tag.AddToClassList("forge-item-tag");
            tag.style.color = GlowColor(state.Temper);
            row.Add(tag);

            row.RegisterCallback<ClickEvent>(_ =>
            {
                if (cell == selectedCell) return;
                selectedCell = cell;
                materialCell = -1;
                SetResult(string.Empty, null);
                Refresh();
            });
            return row;
        }

        private static string KindOf(ItemDefinition item) => item.weaponClass != WeaponClassId.None
            ? WeaponClasses.DisplayName(item.weaponClass).ToUpperInvariant()
            : item.equipSlot.ToString().ToUpperInvariant();

        private Color GlowColor(int temper) =>
            GearProgressionRules.TryGetGlow(Settings.temper, temper, out TemperGlowBand band) ? band.color : new Color(0.57f, 0.64f, 0.7f);

        private void RefreshPurse()
        {
            if (work == null) return;
            purse.text = $"CREDS {work.Creds:N0}   ·   RENOWN {work.Renown:N0}   ·   LUCK {work.Luck:0}";
        }

        // ------------------------------------------------------------------ the chosen piece

        private void RefreshDetail()
        {
            options.Clear();
            reset.style.display = DisplayStyle.None;
            ItemDefinition item = Definition(selectedCell);
            if (item == null)
            {
                itemName.text = "Choose a piece";
                itemState.text = string.Empty;
                optionsTitle.text = string.Empty;
                odds.text = string.Empty;
                reason.text = string.Empty;
                act.SetEnabled(false);
                return;
            }
            ItemStack stack = inventory.Get(selectedCell);
            GearItemState state = GearItemState.Of(stack);
            itemName.text = item.displayName;
            GearUIController.ApplyRarityText(itemName, item.rarity);
            itemState.text = state.IsClean ? "Untouched" : state.ToString();
            itemState.style.color = GlowColor(state.Temper);

            string blocked;
            switch (tab)
            {
                case Tab.Fusion: blocked = FusionDetail(item, stack, state); break;
                case Tab.Tuning: blocked = TuningDetail(item, stack, state); break;
                case Tab.Reforge: blocked = ReforgeDetail(item, state); break;
                default: blocked = TemperDetail(item, stack, state); break;
            }
            reason.text = blocked ?? string.Empty;
            act.SetEnabled(blocked == null);
        }

        private string TemperDetail(ItemDefinition item, ItemStack stack, GearItemState state)
        {
            TemperSettings temper = Settings.temper;
            optionsTitle.text = "ALLOY AND FAILSAFE";
            // The Alloy that fits where the piece is: Prime Alloy from the soft cap, the others below it.
            bool primeInUse = GearProgressionRules.PrimeAlloyInUse(temper);
            bool wantPrime = primeInUse && state.Temper >= temper.softCap;
            GearProgressionRules.TryFindAlloy(temper, alloyItemId, out AlloyGrade alloy);
            if (alloy.itemId == 0 || alloy.prime != wantPrime || inventory.CountItem(alloy.itemId) == 0)
            {
                alloy = default;
                foreach (AlloyGrade candidate in temper.alloys)
                    if (candidate.prime == wantPrime && inventory.CountItem(candidate.itemId) > 0) { alloy = candidate; break; }
                alloyItemId = alloy.itemId;
            }
            foreach (AlloyGrade candidate in temper.alloys)
            {
                if (candidate.prime && !primeInUse) continue; // the Alloy of a later system: not offered until it has a use
                AlloyGrade choice = candidate;
                int have = inventory.CountItem(choice.itemId);
                Option($"{NameOf(choice.itemId)} +{choice.step}%  x{have}", choice.itemId == alloyItemId, have > 0,
                    () => { alloyItemId = choice.itemId; RefreshDetail(); });
            }

            if (protectionItemId != 0 && inventory.CountItem(protectionItemId) == 0) protectionItemId = 0;
            Option("No Failsafe", protectionItemId == 0, true, () => { protectionItemId = 0; RefreshDetail(); });
            foreach (ushort id in new[] { temper.failsafeItemId, temper.luckyFailsafeItemId, temper.advancedFailsafeItemId })
            {
                ushort choice = id;
                int have = inventory.CountItem(choice);
                Option($"{NameOf(choice)}  x{have}", choice == protectionItemId, have > 0, () => { protectionItemId = choice; RefreshDetail(); });
            }

            TemperProtection protection = GearProgressionRules.ProtectionOf(temper, protectionItemId);
            string blocked = GearProgressionRules.TemperBlockedReason(Settings, item, state, alloy, protection);
            long cost = GearProgressionRules.TemperCost(Settings, item, state.Temper);
            var text = new StringBuilder();
            if (alloy.itemId != 0)
            {
                float chance = GearProgressionRules.TemperChance(temper, state.Temper, alloy.step, work.Luck, protection);
                float breaks = GearProgressionRules.BreakChance(temper, state.Temper);
                float luck = GearProgressionRules.LuckBonus(work.Luck, temper.luckPerPercent, temper.maxLuckBonus);
                int reach = Mathf.Min(alloy.prime ? temper.hardCap : Mathf.Min(temper.hardCap, Mathf.Max(temper.softCap, state.Temper)), state.Temper + alloy.step);
                text.Append($"+{state.Temper}% to +{reach}%   ·   Success {chance:0.#}%");
                if (luck > 0f) text.Append($" (Luck adds {luck:0.#}%)");
                text.AppendLine();
                if (protection == TemperProtection.Advanced) text.AppendLine("A fail costs nothing: the Advanced Failsafe takes it.");
                else
                {
                    text.Append($"A fail takes {temper.failLoss}% back");
                    if (breaks > 0f)
                        text.Append(protection == TemperProtection.None
                            ? $"   ·   <color=#F06058>{breaks:0.#}% of fails break the piece</color>"
                            : $"   ·   the Failsafe stops a break ({breaks:0.#}% of fails)");
                    text.AppendLine();
                }
                string gain = Gain(item, stack, state.WithTemper(reach));
                if (gain.Length > 0) text.AppendLine("On a success: " + gain);
            }
            text.Append($"Cost: {cost:N0} Creds and one Alloy");
            odds.text = text.ToString();
            if (blocked == null && work.Creds < cost) blocked = "You don't have enough Creds.";
            return blocked;
        }

        private string FusionDetail(ItemDefinition item, ItemStack stack, GearItemState state)
        {
            FusionSettings fusion = Settings.fusion;
            optionsTitle.text = "MATERIAL";
            int needed = GearProgressionRules.FusionMaterialLevel(Settings.statTable, item);
            int found = 0;
            for (int cell = 0; cell < PlayerInventory.Capacity; cell++)
            {
                if (cell == selectedCell) continue;
                ItemStack candidate = inventory.Get(cell);
                ItemDefinition candidateItem = candidate.IsEmpty ? null : inventory.GetDefinition(cell);
                if (candidateItem == null || !candidateItem.IsLadderGear) continue;
                if (GearProgressionRules.FusionBlockedReason(Settings, item, new GearItemState(GearItemState.MaxTemper, state.Fusion, 0),
                        candidateItem, GearItemState.Of(candidate)) != null) continue;
                int choice = cell;
                if (materialCell < 0) materialCell = cell;
                found++;
                if (found <= 6)
                    Option($"{candidateItem.displayName} L{candidateItem.itemLevel}", cell == materialCell, true, () => { materialCell = choice; RefreshDetail(); });
            }
            if (found == 0)
            {
                materialCell = -1;
                var none = new Label($"No untouched Rare {KindOf(item).ToLowerInvariant()} of item level {needed} in your Pack.");
                none.AddToClassList("forge-empty");
                options.Add(none);
            }

            ItemStack material = materialCell >= 0 ? inventory.Get(materialCell) : ItemStack.Empty;
            ItemDefinition materialItem = materialCell >= 0 ? inventory.GetDefinition(materialCell) : null;
            string blocked = GearProgressionRules.FusionBlockedReason(Settings, item, state, materialItem, GearItemState.Of(material));
            int next = Mathf.Min(fusion.maxStage, state.Fusion + 1);
            long cost = GearProgressionRules.FusionCost(fusion, next);
            int catalysts = inventory.CountItem(fusion.catalystItemId);
            var text = new StringBuilder();
            text.AppendLine($"FS{state.Fusion} to FS{next}   ·   Success {GearProgressionRules.FusionChance(fusion, next, work.Luck):0.#}%");
            text.AppendLine($"A success takes {fusion.temperCostOnSuccess}% Temper off the piece" +
                            (fusion.failConsumesMaterial ? "   ·   a fail destroys the material" : string.Empty));
            string gain = Gain(item, stack, GearProgressionRules.ResolveFusion(fusion, state));
            if (gain.Length > 0) text.AppendLine("On a success: " + gain);
            text.Append($"Cost: {cost:N0} Creds, the material and one {NameOf(fusion.catalystItemId)} (you have {catalysts})");
            odds.text = text.ToString();
            if (blocked == null && catalysts < 1) blocked = $"You have no {NameOf(fusion.catalystItemId)}.";
            if (blocked == null && work.Creds < cost) blocked = "You don't have enough Creds.";
            return blocked;
        }

        private string TuningDetail(ItemDefinition item, ItemStack stack, GearItemState state)
        {
            TuningSettings tuning = Settings.tuning;
            optionsTitle.text = string.Empty;
            string blocked = GearProgressionRules.TuneBlockedReason(Settings, item, state);
            bool canReset = state.Tune > 0 && inventory.CountItem(tuning.resetChipItemId) > 0;
            reset.style.display = canReset ? DisplayStyle.Flex : DisplayStyle.None;
            int next = Mathf.Min(tuning.maxStage, state.Tune + 1);
            if (!GearProgressionRules.TryGetBracket(tuning, next, out TuningBracket bracket))
            {
                odds.text = string.Empty;
                return blocked;
            }
            int chips = inventory.CountItem(bracket.chipItemId);
            var text = new StringBuilder();
            text.AppendLine($"Tune {state.Tune} to Tune {next}   ·   Success {GearProgressionRules.TuneChance(tuning, next):0.#}%");
            text.AppendLine(state.Tune >= tuning.failDropsFromStage ? "A fail drops the piece one stage." : "A fail costs the materials only.");
            string gain = Gain(item, stack, state.WithTune(next));
            if (gain.Length > 0) text.AppendLine("On a success: " + gain);
            text.Append($"Cost: {bracket.chips} {NameOf(bracket.chipItemId)} (you have {chips}), {bracket.renown} Renown, {bracket.creds:N0} Creds");
            odds.text = text.ToString();
            if (blocked == null && chips < bracket.chips) blocked = $"You need {bracket.chips} {NameOf(bracket.chipItemId)}.";
            if (blocked == null && work.Renown < bracket.renown) blocked = $"You need {bracket.renown} Renown.";
            if (blocked == null && work.Creds < bracket.creds) blocked = "You don't have enough Creds.";
            return blocked;
        }

        private string ReforgeDetail(ItemDefinition item, GearItemState state)
        {
            ReforgeSettings reforge = Settings.reforge;
            optionsTitle.text = string.Empty;
            string blocked = GearProgressionRules.ReforgeBlockedReason(Settings, item, state);
            long cost = GearProgressionRules.ReforgeCost(reforge, item.itemLevel);
            odds.text = $"Becomes another Rare of item level {item.itemLevel} that you can wear, picked at random.\n" +
                        $"Success {GearProgressionRules.ReforgeChance(reforge, work.Luck):0.#}%   ·   a fail costs only the Creds\n" +
                        $"Cost: {cost:N0} Creds";
            if (blocked == null && work.Creds < cost) blocked = "You don't have enough Creds.";
            return blocked;
        }

        private void Option(string label, bool chosen, bool enabled, Action choose)
        {
            var button = new Button(choose) { text = label };
            button.AddToClassList("forge-option");
            button.EnableInClassList("chosen", chosen);
            button.SetEnabled(enabled);
            options.Add(button);
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

        // ------------------------------------------------------------------ acting

        private void Act()
        {
            if (work == null || selectedCell < 0) return;
            SetResult(string.Empty, null);
            switch (tab)
            {
                case Tab.Temper: work.RequestTemper(selectedCell, alloyItemId, protectionItemId); break;
                case Tab.Fusion: if (materialCell >= 0) work.RequestFuse(selectedCell, materialCell); break;
                case Tab.Tuning: work.RequestTune(selectedCell); break;
                case Tab.Reforge: work.RequestReforge(selectedCell); break;
            }
        }

        private void OnFinished(GearWorkResult finished)
        {
            GearItemState was = GearItemState.Of(finished.before), now = GearItemState.Of(finished.after);
            string text;
            switch (finished.kind)
            {
                case GearWorkKind.Fusion:
                    text = finished.outcome == GearWorkOutcome.Success ? $"Fused to FS{now.Fusion}."
                        : "The Fusion failed. The piece is unharmed.";
                    break;
                case GearWorkKind.Tuning:
                    text = finished.outcome == GearWorkOutcome.Success ? $"Tuned to stage {now.Tune}."
                        : now.Tune < was.Tune ? $"Tuning failed. It fell to stage {now.Tune}." : "Tuning failed.";
                    break;
                case GearWorkKind.TuningReset:
                    text = "Tuning reset to stage 0.";
                    break;
                case GearWorkKind.Reforge:
                    text = finished.outcome == GearWorkOutcome.Success
                        ? $"Reforged into {NameOf(finished.after.itemId)}." : "The Reforge failed. Only the Creds are gone.";
                    break;
                default:
                    text = finished.outcome == GearWorkOutcome.Success ? $"Tempered to +{now.Temper}%."
                        : finished.outcome == GearWorkOutcome.Broken ? "The piece broke."
                        : finished.outcome == GearWorkOutcome.FailProtected
                            ? (now.Temper == was.Temper ? "Failed. The Failsafe took it: nothing lost." : $"Failed. The Failsafe stopped a break: Temper +{now.Temper}%.")
                        : $"Failed. Temper fell to +{now.Temper}%.";
                    break;
            }
            SetResult(text, finished.outcome == GearWorkOutcome.Success ? "success" : finished.outcome == GearWorkOutcome.Broken ? "broken" : "fail");
            if (finished.outcome == GearWorkOutcome.Broken) selectedCell = -1;
            materialCell = -1;
            Refresh();
        }

        private void SetResult(string text, string tone)
        {
            result.text = text;
            foreach (string name in new[] { "success", "fail", "broken" }) result.EnableInClassList(name, name == tone);
        }
    }
}
