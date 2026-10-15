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
    /// after it is made; the choices are saved with the character. Nothing in the window is typed: every choice
    /// is a switch, a step or a slider, so there is no name to get wrong.
    /// The window is built in code, but out of the same parts as the Settings window: its frame and header, and
    /// the rows of <see cref="SettingsRows"/> (the pack's arrows, value boxes, sliders and ON / OFF switches).
    /// So it wears each faction's skin exactly as Settings does, with no style of its own.
    /// The Autobuff lives here too (owner, 15 Oct 2026): the time its licence has left, the AUTOBUFF tile that goes
    /// on a Hotbar key, and the eight cells a learned buff skill is dragged into. It shows with or without Autopilot
    /// time, since the Autobuff is a licence of its own.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class AutopilotWindowController : MonoBehaviour
    {
        private const float SaveDelay = 0.6f;
        private const float RadiusStep = 5f;
        private static readonly string[] PackFullNames = { "Keep hunting", "Stop" };
        private static readonly string[] StimsOutNames = { "Stop", "Keep hunting" };
        private static readonly string[] AttackedNames = { "Switch off", "Fight back" };
        private static readonly string[] AreaNames = { "Whole zone", "Radius" };

        private VisualElement root, window, notice;
        private Label status, noticeText, keyLabel;
        private Button switchButton;
        private readonly List<Action> refresh = new List<Action>();
        private string[] slotNames = { "None" };
        private string[] radiusNames = { "25 m" };
        private string[] gatherNames = { "1" };

        private PlayerAutopilot autopilot;
        private PlayerAutobuff autobuff;
        private PlayerCombatSkills combatSkills;
        private CharacterBuffManager buffs;
        private ProgressionHUD hud;
        private Label autobuffStatus;
        private VisualElement autobuffTile;
        private readonly VisualElement[] autobuffCells = new VisualElement[AutobuffRules.Slots];
        private readonly VisualElement[] autobuffCellIcons = new VisualElement[AutobuffRules.Slots];
        private readonly Label[] autobuffCellNames = new Label[AutobuffRules.Slots];
        private readonly Label[] autobuffCellRanks = new Label[AutobuffRules.Slots];
        private string[] workingIds = new string[AutobuffRules.Slots];
        private AutopilotPreferences working = new AutopilotPreferences();
        private bool filled;
        private float saveAt = -1f;
        private float nextRefresh;

        /// <summary>True while the window is on screen.</summary>
        public bool IsOpen => window != null && window.style.display != DisplayStyle.None;
        // Where the window stands: its width, and the gaps to the right and top edges of the screen.
        private const float WindowWidth = 540f, WindowRight = 24f, WindowTop = 64f;
        private const float WindowMargin = 20f; // the skin's frame and close button stand outside the window's own box
        private FloatingFit windowFit;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;
            hud = GetComponent<ProgressionHUD>(); // the same document: one space for drags between its windows and this one
            BuildNotice();
            PlayerInventory.LocalChanged += OnLocalChanged;
        }

        private void OnDisable()
        {
            PlayerInventory.LocalChanged -= OnLocalChanged;
            if (hud != null) foreach (VisualElement cell in autobuffCells) if (cell != null) hud.UnregisterDropTarget(cell);
            Bind(null);
            window?.RemoveFromHierarchy();
            notice?.RemoveFromHierarchy();
            window = notice = null;
        }

        private void OnLocalChanged() => Bind(null);

        private void Bind(PlayerAutopilot next)
        {
            if (autopilot != null) autopilot.Changed -= OnChanged;
            if (autobuff != null) autobuff.Changed -= OnChanged;
            autopilot = next;
            autobuff = next != null ? next.GetComponent<PlayerAutobuff>() : null;
            combatSkills = next != null ? next.GetComponent<PlayerCombatSkills>() : null;
            buffs = next != null ? next.GetComponent<CharacterBuffManager>() : null;
            if (autopilot != null) autopilot.Changed += OnChanged;
            if (autobuff != null) autobuff.Changed += OnChanged;
            filled = false;
        }

        private void OnChanged()
        {
            // The server's copy arrived or changed: what the window shows follows, unless the player is mid-edit.
            if (saveAt < 0f) filled = false;
        }

        private void Update()
        {
            if (notice == null) return;
            if (autopilot == null)
            {
                PlayerAutopilot local = PlayerAutopilot.Local;
                if (local != null) Bind(local);
            }
            if (autopilot != null && !HudTyping.IsTyping(root))
            {
                if (KeyBindings.Pressed(KeyBindings.Autopilot)) autopilot.RequestSetOn(!autopilot.IsOn);
                if (KeyBindings.Pressed(KeyBindings.AutopilotSettings)) SetOpen(!IsOpen);
            }

            bool on = autopilot != null && autopilot.IsOn;
            notice.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (autopilot == null) { if (window != null) window.style.display = DisplayStyle.None; return; }

            if (saveAt >= 0f && Time.unscaledTime >= saveAt)
            {
                saveAt = -1f;
                autopilot.SavePreferences(AutopilotPreferences.FromJson(working.ToJson(), autopilot.Settings));
            }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;

            string time = PlayerAutopilot.FormatTime(autopilot.SecondsLeft);
            string key = KeyBindings.Label(KeyBindings.Autopilot);
            if (on) noticeText.text = $"AUTOPILOT  ·  {time}  ·  {key} to stop";
            if (!IsOpen) return;
            status.text = on ? $"Running. Time left: {time}."
                : autopilot.SecondsLeft > 0f ? $"Off. Time left: {time}."
                : "Off. No Autopilot time left: use an Autopilot Chip.";
            switchButton.text = on ? $"STOP  [{key}]" : $"START  [{key}]";
            if (!filled) Fill();
            RefreshAutobuff();
        }

        /// <summary>Shows or hides the window. It is built the first time it is shown.</summary>
        public void SetOpen(bool open)
        {
            if (open && autopilot == null) return;
            if (open && window == null) BuildWindow();
            if (window == null) return;
            window.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (!open) return;
            window.BringToFront();
            // Against the right edge under the menu, and wholly on screen whatever the screen and interface size.
            VisualElement screen = window.panel != null ? window.panel.visualTree : null;
            if (screen != null) windowFit.At(new Vector2(screen.worldBound.xMax - WindowRight - WindowWidth, screen.worldBound.yMin + WindowTop));
            filled = false;
            nextRefresh = 0f;
        }

        // ------------------------------------------------------------------ building

        private void BuildNotice()
        {
            notice = new VisualElement { name = "autopilot-notice", pickingMode = PickingMode.Ignore };
            notice.AddToClassList("panel");
            notice.style.position = Position.Absolute;
            notice.style.top = 86f;
            notice.style.left = Length.Percent(50f);
            notice.style.translate = new Translate(Length.Percent(-50f), 0f);
            notice.style.paddingLeft = notice.style.paddingRight = 14f;
            notice.style.paddingTop = notice.style.paddingBottom = 4f;
            notice.style.display = DisplayStyle.None;
            noticeText = new Label { pickingMode = PickingMode.Ignore };
            noticeText.AddToClassList("accent");
            notice.Add(noticeText);
            root.Add(notice);
        }

        private void BuildWindow()
        {
            AutopilotSettings settings = autopilot.Settings;
            RefreshSlotNames();
            var radii = new List<string>();
            float low = settings != null ? settings.huntRadius.x : 10f, high = settings != null ? settings.huntRadius.y : 40f;
            for (float r = low; r <= high + 0.01f; r += RadiusStep) radii.Add($"{r:0} m");
            radiusNames = radii.ToArray();
            int most = settings != null ? settings.maxGatherCount : 8;
            gatherNames = new string[most];
            for (int i = 0; i < most; i++) gatherNames[i] = (i + 1).ToString();

            // The frame of the Settings window: the same classes, so the same skin.
            window = new VisualElement { name = "autopilot-window" };
            foreach (string c in new[] { "panel", "window", "blocks-world" }) window.AddToClassList(c);
            window.style.width = WindowWidth;
            window.style.display = DisplayStyle.None;
            windowFit = new FloatingFit(window, 0f, WindowMargin);

            var header = new VisualElement();
            header.AddToClassList("row");
            header.AddToClassList("kn-header");
            var title = new Label("AUTOPILOT & MAGNET");
            title.AddToClassList("heading");
            header.Add(title);
            keyLabel = new Label($"[{KeyBindings.Label(KeyBindings.AutopilotSettings)}]");
            keyLabel.AddToClassList("muted");
            keyLabel.AddToClassList("header-key");
            header.Add(keyLabel);
            var close = new Button(() => SetOpen(false)) { name = "close-autopilot" };
            close.AddToClassList("close");
            var glyph = new VisualElement { pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("kn-close-glyph");
            close.Add(glyph);
            header.Add(close);
            window.Add(header);

            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "autopilot-body", horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            scroll.AddToClassList("window-body");
            scroll.style.maxHeight = 470f;
            window.Add(scroll);
            var page = new VisualElement();
            page.AddToClassList("settings-page");
            page.style.display = DisplayStyle.Flex;
            scroll.Add(page);

            refresh.Clear();
            status = SettingsRows.Help(page, string.Empty);

            SettingsRows.Header(page, "WHERE");
            Keep(SettingsRows.Choice(page, "Hunting ground", AreaNames, () => working.wholeZone ? 0 : 1, i => Change(() => working.wholeZone = i == 0)));
            Keep(SettingsRows.Choice(page, "Radius round the start", radiusNames,
                () => Mathf.RoundToInt((working.huntRadius - low) / RadiusStep), i => Change(() => working.huntRadius = low + i * RadiusStep)));

            SettingsRows.Header(page, "WHICH HOSTILES");
            Keep(SettingsRows.Toggle(page, "Skip Hostiles another player holds", () => working.skipTagged, v => Change(() => working.skipTagged = v)));
            Keep(SettingsRows.Toggle(page, "Skip grey names (no XP)", () => working.skipGrey, v => Change(() => working.skipGrey = v)));
            Keep(SettingsRows.Toggle(page, "Skip red names (dangerous)", () => working.skipRed, v => Change(() => working.skipRed = v)));
            Keep(SettingsRows.Toggle(page, "Include Bruisers", () => working.includeBruisers, v => Change(() => working.includeBruisers = v)));

            SettingsRows.Header(page, "SKILLS (HOTBAR SLOTS)");
            Keep(SettingsRows.Choice(page, "Attack skill", slotNames, () => working.attackSlot + 1, i => Change(() => working.attackSlot = i - 1)));
            Keep(SettingsRows.Choice(page, "Area skill", slotNames, () => working.areaSlot + 1, i => Change(() => working.areaSlot = i - 1)));
            Keep(SettingsRows.Choice(page, "Gather before the area skill", gatherNames, () => working.gatherCount - 1, i => Change(() => working.gatherCount = i + 1)));

            SettingsRows.Header(page, "STIMS");
            Keep(SettingsRows.Slider(page, "Use an HP Stim under", 0f, 0.95f, () => working.stimHealthPercent / 100f,
                v => Change(() => working.stimHealthPercent = Mathf.Round(v * 100f))));
            Keep(SettingsRows.Slider(page, "Use a Flux Stim under", 0f, 0.95f, () => working.stimFluxPercent / 100f,
                v => Change(() => working.stimFluxPercent = Mathf.Round(v * 100f))));

            BuildAutobuff(page);

            SettingsRows.Header(page, "MAGNET");
            SettingsRows.Help(page, "Picks up what you walk over while you have Autopilot time, with Autopilot on or off. Creds are always taken.");
            Keep(SettingsRows.Toggle(page, "Magnet", () => working.magnetOn, v => Change(() => working.magnetOn = v)));
            Keep(SettingsRows.Toggle(page, "Take Common gear", () => working.takeCommon, v => Change(() => working.takeCommon = v)));
            Keep(SettingsRows.Toggle(page, "Take Uncommon gear", () => working.takeUncommon, v => Change(() => working.takeUncommon = v)));
            Keep(SettingsRows.Toggle(page, "Take Rare gear", () => working.takeRare, v => Change(() => working.takeRare = v)));
            Keep(SettingsRows.Toggle(page, "Take Epic gear", () => working.takeEpic, v => Change(() => working.takeEpic = v)));
            Keep(SettingsRows.Toggle(page, "Take Stims", () => working.magnetStims, v => Change(() => working.magnetStims = v)));
            Keep(SettingsRows.Toggle(page, "Take materials and other items", () => working.magnetMaterials, v => Change(() => working.magnetMaterials = v)));

            SettingsRows.Header(page, "WHEN THINGS GO WRONG");
            Keep(SettingsRows.Choice(page, "The Pack is full", PackFullNames, () => (int)working.whenPackFull, i => Change(() => working.whenPackFull = (WhenPackFull)i)));
            Keep(SettingsRows.Choice(page, "Out of HP Stims", StimsOutNames, () => (int)working.whenStimsOut, i => Change(() => working.whenStimsOut = (WhenStimsOut)i)));
            Keep(SettingsRows.Choice(page, "A player attacks you", AttackedNames, () => (int)working.whenAttacked, i => Change(() => working.whenAttacked = (WhenAttacked)i)));

            var footer = new VisualElement();
            footer.AddToClassList("settings-footer");
            var gap = new VisualElement();
            gap.AddToClassList("settings-footer-gap");
            footer.Add(gap);
            switchButton = new Button(() => { if (autopilot != null) autopilot.RequestSetOn(!autopilot.IsOn); }) { name = "autopilot-switch" };
            switchButton.AddToClassList("settings-footer-button");
            switchButton.AddToClassList("kn-button-primary");
            footer.Add(switchButton);
            window.Add(footer);

            root.Add(window);
            // The layers a skin draws on (the window's frame and crown, the bar under each heading, the cards):
            // the HUD's own windows were given theirs when the HUD was built; this one was not there yet.
            SyntySkin.Decorate(window.parent);
        }

        /// <summary>
        /// The AUTOBUFF section: the licence's time left, the tile that goes on a Hotbar key, and two rows of four
        /// cells for the buffs it casts. The tile and the cells are Hotbar slots in all but name, so the skin draws
        /// them as it draws the Hotbar.
        /// </summary>
        private void BuildAutobuff(VisualElement page)
        {
            SettingsRows.Header(page, "AUTOBUFF");
            autobuffStatus = SettingsRows.Help(page, string.Empty);
            SettingsRows.Help(page, "Drag learned buff skills from the Arsenal or the Hotbar into the cells. Drag the AUTOBUFF tile to a Hotbar key: one press casts every buff that is down, in turn, and Autopilot keeps them up. Right-click a cell to clear it.");

            var row = new VisualElement();
            row.AddToClassList("autobuff-row");
            autobuffTile = MakeCell("autobuff-tile", "AUTOBUFF");
            autobuffTile.AddToClassList("autobuff-tile");
            row.Add(autobuffTile);
            var grid = new VisualElement();
            grid.AddToClassList("autobuff-grid");
            for (int line = 0; line < 2; line++)
            {
                var cells = new VisualElement();
                cells.AddToClassList("autobuff-cells");
                for (int column = 0; column < 4; column++)
                {
                    int i = line * 4 + column;
                    VisualElement cell = MakeCell($"autobuff-cell-{i}", "—");
                    cell.AddToClassList("autobuff-cell");
                    autobuffCells[i] = cell;
                    autobuffCellIcons[i] = cell.Q(className: "slot-icon");
                    autobuffCellNames[i] = cell.Q<Label>(className: "slot-name");
                    autobuffCellRanks[i] = cell.Q<Label>(className: "slot-rank");
                    cells.Add(cell);
                }
                grid.Add(cells);
            }
            row.Add(grid);
            page.Add(row);

            if (hud != null)
            {
                hud.BindExternalDragSource(autobuffTile);
                autobuffTile.RegisterCallback<PointerDownEvent>(evt => { if (evt.button == 0) hud.BeginExternalAutobuffDrag(autobuffTile, evt); });
                for (int i = 0; i < autobuffCells.Length; i++)
                {
                    int cellIndex = i;
                    VisualElement cell = autobuffCells[i];
                    hud.BindExternalDragSource(cell);
                    hud.RegisterDropTarget(cell, s => AutobuffRules.Accepts(s) && combatSkills != null && combatSkills.IsLearned(s), s => SetCell(cellIndex, s.skillId));
                    cell.RegisterCallback<PointerDownEvent>(evt =>
                    {
                        if (evt.button == 1) { SetCell(cellIndex, string.Empty); evt.StopPropagation(); return; }
                        if (evt.button != 0) return;
                        SkillDefinitionSO held = combatSkills != null && !string.IsNullOrEmpty(workingIds[cellIndex]) ? combatSkills.GetSkill(workingIds[cellIndex]) : null;
                        if (held != null) hud.BeginExternalSkillDrag(cell, held, evt);
                    });
                }
            }
        }

        // A tile in the Hotbar's own clothes: the icon layer under the name and the small line, as the HUD builds its slots.
        private static VisualElement MakeCell(string name, string label)
        {
            var cell = new VisualElement { name = name };
            cell.AddToClassList("hotbar-slot");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("slot-icon");
            icon.style.display = DisplayStyle.None;
            cell.Add(icon);
            var text = new Label(label) { pickingMode = PickingMode.Ignore };
            text.AddToClassList("slot-name");
            cell.Add(text);
            var rank = new Label { pickingMode = PickingMode.Ignore };
            rank.AddToClassList("slot-rank");
            cell.Add(rank);
            return cell;
        }

        /// <summary>A cell takes a skill (or is cleared); the same skill elsewhere moves, and the cells are kept at once.</summary>
        private void SetCell(int index, string skillId)
        {
            if (autobuff == null || index < 0 || index >= workingIds.Length) return;
            if (!string.IsNullOrEmpty(skillId))
                for (int i = 0; i < workingIds.Length; i++)
                    if (i != index && workingIds[i] == skillId) workingIds[i] = string.Empty;
            workingIds[index] = skillId ?? string.Empty;
            autobuff.SaveSlots(workingIds);
            workingIds = (string[])autobuff.SkillIds.Clone();
            RefreshAutobuff();
        }

        private void RefreshAutobuff()
        {
            if (autobuffStatus == null || !IsOpen) return;
            if (autobuff == null)
            {
                autobuffStatus.text = "Autobuff is not set up on this character.";
                return;
            }
            bool active = autobuff.IsActive;
            autobuffStatus.text = autobuff.TimeLeftLine;
            if (autobuffTile != null)
            {
                autobuffTile.EnableInClassList("on-cooldown", !active);
                Sprite icon = autopilot != null && autopilot.GetComponent<IHotbarAutobuff>() is IHotbarAutobuff tile ? tile.Icon : null;
                VisualElement tileIcon = autobuffTile.Q(className: "slot-icon");
                if (tileIcon != null)
                {
                    tileIcon.style.backgroundImage = icon != null ? new StyleBackground(icon) : StyleKeyword.None;
                    tileIcon.style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;
                }
                autobuffTile.EnableInClassList("has-icon", icon != null);
                Label rank = autobuffTile.Q<Label>(className: "slot-rank");
                if (rank != null) rank.text = $"{AutobuffRules.Count(workingIds)} / {AutobuffRules.Slots}";
            }
            for (int i = 0; i < autobuffCells.Length; i++)
            {
                VisualElement cell = autobuffCells[i];
                if (cell == null) continue;
                string id = i < workingIds.Length ? workingIds[i] : string.Empty;
                SkillDefinitionSO skill = combatSkills != null && !string.IsNullOrEmpty(id) ? combatSkills.GetSkill(id) : null;
                bool learned = skill != null && combatSkills.IsLearned(skill);
                Sprite icon = skill != null ? skill.icon : null;
                if (autobuffCellIcons[i] != null)
                {
                    autobuffCellIcons[i].style.backgroundImage = icon != null ? new StyleBackground(icon) : StyleKeyword.None;
                    autobuffCellIcons[i].style.display = icon != null ? DisplayStyle.Flex : DisplayStyle.None;
                }
                cell.EnableInClassList("has-icon", icon != null);
                if (autobuffCellNames[i] != null) autobuffCellNames[i].text = skill != null ? skill.displayName.ToUpperInvariant() : string.IsNullOrEmpty(id) ? "—" : id.ToUpperInvariant();
                if (autobuffCellRanks[i] != null)
                    autobuffCellRanks[i].text = skill == null ? string.Empty : !learned ? "UNLEARNED" : buffs != null && buffs.HasBuff(skill.skillId) ? "UP" : $"R{combatSkills.GetRank(skill)}";
                cell.EnableInClassList("on-cooldown", skill != null && !learned);
                cell.EnableInClassList("selected", skill != null && learned && buffs != null && buffs.HasBuff(skill.skillId));
                cell.tooltip = skill != null ? $"{skill.displayName}. Right-click to clear." : "Drop a learned buff skill here.";
            }
        }

        private void Keep(Action show) => refresh.Add(show);

        /// <summary>A change made by hand: applied to the working copy and saved a moment later.</summary>
        private void Change(Action apply)
        {
            apply();
            saveAt = Time.unscaledTime + SaveDelay;
        }

        // ------------------------------------------------------------------ showing

        private void Fill()
        {
            filled = true;
            working = AutopilotPreferences.FromJson(autopilot.Preferences.ToJson(), autopilot.Settings);
            if (autobuff != null) workingIds = (string[])autobuff.SkillIds.Clone();
            RefreshSlotNames();
            foreach (Action show in refresh) show();
        }

        /// <summary>The Hotbar as the skill rows step through it: "None", then every slot with what is on it.</summary>
        private void RefreshSlotNames()
        {
            var hotbar = autopilot != null ? autopilot.GetComponent<PlayerSkillHotbar>() : null;
            if (slotNames.Length != PlayerSkillHotbar.SlotCount + 1) slotNames = new string[PlayerSkillHotbar.SlotCount + 1];
            slotNames[0] = "None";
            for (int i = 0; i < PlayerSkillHotbar.SlotCount; i++)
            {
                string holds = "empty";
                if (hotbar != null)
                {
                    HotbarSlotEntry entry = hotbar.GetSlotEntry(i);
                    if (entry.itemType == HotbarItemType.CombatSkill && entry.combatSkill != null) holds = entry.combatSkill.displayName;
                    else if (!entry.IsEmpty) holds = entry.itemType.ToString();
                }
                slotNames[i + 1] = $"{PlayerSkillHotbar.GetKeyLabel(i)}: {holds}";
            }
        }
    }
}
