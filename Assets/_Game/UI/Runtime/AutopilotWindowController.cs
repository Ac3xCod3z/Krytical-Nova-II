using System;
using System.Collections.Generic;
using KryticalNova.Autopilot;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Autopilot and Magnet window: every choice of <see cref="AutopilotPreferences"/>, the time left, and
    /// the switch. It also puts the "AUTOPILOT" notice on the screen while Autopilot runs, and owns the two keys
    /// (on / off, and this window) so that neither fires while the player is typing. A change is kept a moment
    /// after it is made; the choices are saved with the character. The window is built in code from the shared
    /// classes, so it wears the faction skin like any other.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class AutopilotWindowController : MonoBehaviour
    {
        private const float SaveDelay = 0.6f;
        private const string None = "None";
        private static readonly List<string> RarityNames = new List<string> { "Common", "Uncommon", "Rare", "Epic", "Legendary" };
        private static readonly List<string> PackFullNames = new List<string> { "Keep hunting", "Stop" };
        private static readonly List<string> StimsOutNames = new List<string> { "Stop", "Keep hunting" };
        private static readonly List<string> AttackedNames = new List<string> { "Switch off and alert", "Fight back" };

        private VisualElement root, window, notice;
        private Label status, noticeText;
        private Button switchButton;
        private Toggle wholeZone, skipTagged, skipGrey, skipRed, bruisers, magnetOn, magnetStims;
        private Slider radius, stimHealth, stimFlux;
        private SliderInt gather;
        private DropdownField attack, area, buffA, buffB, rarity, packFull, stimsOut, attacked;
        private TextField always, never;

        private PlayerAutopilot autopilot;
        private AutopilotPreferences working;
        private readonly List<string> slotChoices = new List<string>();
        private bool filling;
        private float saveAt = -1f;
        private float nextRefresh;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;
            Build();
            PlayerInventory.LocalChanged += OnLocalChanged;
        }

        private void OnDisable()
        {
            PlayerInventory.LocalChanged -= OnLocalChanged;
            Bind(null);
            window?.RemoveFromHierarchy();
            notice?.RemoveFromHierarchy();
            window = notice = null;
        }

        private void OnLocalChanged() => Bind(null);

        private void Bind(PlayerAutopilot next)
        {
            if (autopilot != null) autopilot.Changed -= OnChanged;
            autopilot = next;
            if (autopilot != null) autopilot.Changed += OnChanged;
            working = null;
        }

        private void OnChanged()
        {
            // The server's copy arrived or changed: what the window shows follows, unless the player is mid-edit.
            if (saveAt < 0f) working = null;
        }

        private void Update()
        {
            if (window == null) return;
            if (autopilot == null)
            {
                PlayerAutopilot local = PlayerAutopilot.Local;
                if (local != null) Bind(local);
            }
            bool typing = HudTyping.IsTyping(root);
            if (autopilot != null && !typing)
            {
                if (KeyBindings.Pressed(KeyBindings.Autopilot)) autopilot.RequestSetOn(!autopilot.IsOn);
                if (KeyBindings.Pressed(KeyBindings.AutopilotSettings)) SetOpen(window.style.display == DisplayStyle.None);
            }

            bool on = autopilot != null && autopilot.IsOn;
            notice.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (autopilot == null) { window.style.display = DisplayStyle.None; return; }

            if (saveAt >= 0f && Time.unscaledTime >= saveAt)
            {
                saveAt = -1f;
                autopilot.SavePreferences(working);
            }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;

            string time = PlayerAutopilot.FormatTime(autopilot.SecondsLeft);
            if (on) noticeText.text = $"AUTOPILOT  ·  {time}  ·  {KeyBindings.Label(KeyBindings.Autopilot)} to stop";
            if (window.style.display == DisplayStyle.None) return;
            status.text = on ? $"Running. Time left: {time}." : autopilot.SecondsLeft > 0f ? $"Off. Time left: {time}." : "Off. No Autopilot time: use an Autopilot Chip.";
            switchButton.text = (on ? "STOP" : "START") + "  [" + KeyBindings.Label(KeyBindings.Autopilot) + "]";
            if (working == null) Fill();
        }

        private void SetOpen(bool open)
        {
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (open) { working = null; nextRefresh = 0f; }
        }

        // ------------------------------------------------------------------ building

        private void Build()
        {
            notice = new VisualElement { pickingMode = PickingMode.Ignore };
            notice.AddToClassList("kn-panel");
            notice.style.position = Position.Absolute;
            notice.style.top = 86f;
            notice.style.left = Length.Percent(50f);
            notice.style.translate = new Translate(Length.Percent(-50f), 0f);
            notice.style.paddingLeft = notice.style.paddingRight = 14f;
            notice.style.paddingTop = notice.style.paddingBottom = 4f;
            notice.style.display = DisplayStyle.None;
            noticeText = new Label { pickingMode = PickingMode.Ignore };
            noticeText.AddToClassList("kn-accent");
            noticeText.AddToClassList("kn-bold");
            notice.Add(noticeText);
            root.Add(notice);

            window = new VisualElement { name = "autopilot-window" };
            foreach (string c in new[] { "panel", "window", "blocks-world", "kn-panel" }) window.AddToClassList(c);
            window.style.right = 24f;
            window.style.top = 120f;
            window.style.width = 360f;
            window.style.display = DisplayStyle.None;

            var header = new VisualElement();
            header.AddToClassList("kn-header");
            header.style.flexDirection = FlexDirection.Row;
            header.style.justifyContent = Justify.SpaceBetween;
            var title = new Label("AUTOPILOT & MAGNET");
            title.AddToClassList("kn-title");
            header.Add(title);
            var close = new Button(() => SetOpen(false)) { text = "X" };
            close.AddToClassList("kn-close");
            header.Add(close);
            window.Add(header);

            status = new Label();
            status.AddToClassList("kn-muted");
            status.style.whiteSpace = WhiteSpace.Normal;
            window.Add(status);
            switchButton = new Button(() => { if (autopilot != null) autopilot.RequestSetOn(!autopilot.IsOn); });
            switchButton.AddToClassList("kn-button-primary");
            window.Add(switchButton);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.maxHeight = 430f;
            window.Add(scroll);
            VisualElement body = scroll.contentContainer;

            Heading(body, "WHERE");
            wholeZone = AddToggle(body, "Hunt the whole Hostile Zone", v => working.wholeZone = v);
            radius = AddSlider(body, "Or this many metres round the start", 10f, 40f, v => working.huntRadius = v);

            Heading(body, "WHICH HOSTILES");
            skipTagged = AddToggle(body, "Skip Hostiles another player holds", v => working.skipTagged = v);
            skipGrey = AddToggle(body, "Skip grey names (no XP)", v => working.skipGrey = v);
            skipRed = AddToggle(body, "Skip red names (dangerous)", v => working.skipRed = v);
            bruisers = AddToggle(body, "Include Bruisers", v => working.includeBruisers = v);

            Heading(body, "SKILLS (Hotbar slots)");
            attack = AddDropdown(body, "Attack skill", slotChoices, i => working.attackSlot = i - 1);
            area = AddDropdown(body, "Area skill", slotChoices, i => working.areaSlot = i - 1);
            gather = new SliderInt("Gather before the area skill", 1, 8) { showInputField = true };
            gather.RegisterValueChangedCallback(e => Change(() => working.gatherCount = e.newValue));
            body.Add(gather);
            buffA = AddDropdown(body, "Buff to keep up", slotChoices, i => working.buffSlotA = i - 1);
            buffB = AddDropdown(body, "Second buff", slotChoices, i => working.buffSlotB = i - 1);

            Heading(body, "STIMS");
            stimHealth = AddSlider(body, "Use an HP Stim under (%)", 0f, 95f, v => working.stimHealthPercent = v);
            stimFlux = AddSlider(body, "Use a Flux Stim under (%)", 0f, 95f, v => working.stimFluxPercent = v);

            Heading(body, "MAGNET (works with Autopilot off too)");
            magnetOn = AddToggle(body, "Magnet on", v => working.magnetOn = v);
            magnetStims = AddToggle(body, "Take Stims", v => working.magnetStims = v);
            rarity = AddDropdown(body, "Take gear from", RarityNames, i => working.minGearRarity = (ItemRarity)i);
            always = AddNames(body, "Always take (names, comma between)", names => working.alwaysTake = names);
            never = AddNames(body, "Never take (names, comma between)", names => working.neverTake = names);

            Heading(body, "WHEN THINGS GO WRONG");
            packFull = AddDropdown(body, "Pack full", PackFullNames, i => working.whenPackFull = (WhenPackFull)i);
            stimsOut = AddDropdown(body, "Out of HP Stims", StimsOutNames, i => working.whenStimsOut = (WhenStimsOut)i);
            attacked = AddDropdown(body, "A player attacks", AttackedNames, i => working.whenAttacked = (WhenAttacked)i);

            root.Add(window);
        }

        private static void Heading(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList("kn-eyebrow");
            label.style.marginTop = 8f;
            parent.Add(label);
        }

        /// <summary>A change made by hand: applied to the working copy and saved a moment later.</summary>
        private void Change(Action apply)
        {
            if (filling || working == null) return;
            apply();
            saveAt = Time.unscaledTime + SaveDelay;
        }

        private Toggle AddToggle(VisualElement parent, string label, Action<bool> set)
        {
            var toggle = new Toggle(label);
            toggle.AddToClassList("kn-toggle");
            toggle.RegisterValueChangedCallback(e => Change(() => set(e.newValue)));
            parent.Add(toggle);
            return toggle;
        }

        private Slider AddSlider(VisualElement parent, string label, float low, float high, Action<float> set)
        {
            var slider = new Slider(label, low, high) { showInputField = true };
            slider.RegisterValueChangedCallback(e => Change(() => set(Mathf.Round(e.newValue))));
            parent.Add(slider);
            return slider;
        }

        private DropdownField AddDropdown(VisualElement parent, string label, List<string> choices, Action<int> set)
        {
            var dropdown = new DropdownField(label, choices, 0);
            dropdown.RegisterValueChangedCallback(e => Change(() => set(Mathf.Max(0, dropdown.index))));
            parent.Add(dropdown);
            return dropdown;
        }

        private TextField AddNames(VisualElement parent, string label, Action<string[]> set)
        {
            var heading = new Label(label);
            heading.AddToClassList("kn-muted");
            parent.Add(heading);
            var field = new TextField { multiline = false };
            field.AddToClassList("kn-textfield");
            HudTyping.Guard(field);
            field.RegisterValueChangedCallback(e => Change(() => set(e.newValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))));
            parent.Add(field);
            return field;
        }

        // ------------------------------------------------------------------ showing

        private void Fill()
        {
            working = AutopilotPreferences.FromJson(autopilot.Preferences.ToJson(), autopilot.Settings);
            filling = true;
            try
            {
                AutopilotSettings settings = autopilot.Settings;
                if (settings != null)
                {
                    radius.lowValue = settings.huntRadius.x;
                    radius.highValue = settings.huntRadius.y;
                    gather.highValue = settings.maxGatherCount;
                }
                RefreshSlots();
                wholeZone.SetValueWithoutNotify(working.wholeZone);
                radius.SetValueWithoutNotify(working.huntRadius);
                skipTagged.SetValueWithoutNotify(working.skipTagged);
                skipGrey.SetValueWithoutNotify(working.skipGrey);
                skipRed.SetValueWithoutNotify(working.skipRed);
                bruisers.SetValueWithoutNotify(working.includeBruisers);
                attack.index = working.attackSlot + 1;
                area.index = working.areaSlot + 1;
                gather.SetValueWithoutNotify(working.gatherCount);
                buffA.index = working.buffSlotA + 1;
                buffB.index = working.buffSlotB + 1;
                stimHealth.SetValueWithoutNotify(working.stimHealthPercent);
                stimFlux.SetValueWithoutNotify(working.stimFluxPercent);
                magnetOn.SetValueWithoutNotify(working.magnetOn);
                magnetStims.SetValueWithoutNotify(working.magnetStims);
                rarity.index = Mathf.Clamp((int)working.minGearRarity, 0, RarityNames.Count - 1);
                always.SetValueWithoutNotify(string.Join(", ", working.alwaysTake));
                never.SetValueWithoutNotify(string.Join(", ", working.neverTake));
                packFull.index = (int)working.whenPackFull;
                stimsOut.index = (int)working.whenStimsOut;
                attacked.index = (int)working.whenAttacked;
            }
            finally
            {
                filling = false;
            }
        }

        /// <summary>The Hotbar as the four skill dropdowns list it: "None", then every slot with what is on it.</summary>
        private void RefreshSlots()
        {
            slotChoices.Clear();
            slotChoices.Add(None);
            var hotbar = autopilot.GetComponent<PlayerSkillHotbar>();
            for (int i = 0; i < PlayerSkillHotbar.SlotCount; i++)
            {
                string on = "empty";
                if (hotbar != null)
                {
                    HotbarSlotEntry entry = hotbar.GetSlotEntry(i);
                    if (entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill != null) on = entry.combatSkill.displayName;
                    else if (!entry.IsEmpty) on = entry.itemType.ToString();
                }
                slotChoices.Add($"{PlayerSkillHotbar.GetKeyLabel(i)}: {on}");
            }
            foreach (DropdownField dropdown in new[] { attack, area, buffA, buffB }) dropdown.choices = slotChoices;
        }
    }
}
