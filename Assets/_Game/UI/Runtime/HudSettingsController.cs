using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Settings window (O / menu button) and the HUD layout editor. The window has four pages: Graphics, Audio and
    /// Controls are built from their preference classes (see UI/Runtime/Settings), Interface holds the layout editor
    /// and the HUD toggles. Gathers movable elements from every UIDocument that shares this document's PanelSettings,
    /// so the NPC dialogue and zone toast can be placed alongside the HUD. Also keeps the key names shown on the
    /// HUD (menu buttons, window headers, hints) in step with the player's bindings.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudSettingsController : MonoBehaviour, IPointerBlockQuery
    {
        [Tooltip("Optional. Its key bindings are suppressed while the layout editor is open. Defaults to the ProgressionHUD's hotbar.")]
        [SerializeField] private PlayerSkillHotbar hotbar;
        [Tooltip("The game's mixer: the Audio page's sliders drive its exposed volumes. Assigned by the zone scene setup.")]
        [SerializeField] private AudioMixer mixer;

        private static readonly string[] Pages = { "graphics", "audio", "interface", "controls" };
        // Menu buttons that show the key that opens their window: button, its caption, the window key (null = a player-asset action).
        private static readonly (string Button, string Caption, string WindowKey, string PlayerAction)[] MenuKeys =
        {
            ("open-inventory", "PACK", KeyBindings.Pack, null),
            ("open-map", "MAP", KeyBindings.Map, null),
            ("open-equipment", "EQUIPMENT", KeyBindings.Equipment, null),
            ("open-character", "CHARACTER", null, "CharacterWindow"),
            ("open-skills", "SKILLS", null, "SkillsWindow"),
            ("open-settings", "SETTINGS", KeyBindings.Settings, null),
        };
        // Labels in window headers that show the key: label name, window key.
        private static readonly (string Label, string WindowKey)[] HeaderKeys =
        {
            ("key-settings", KeyBindings.Settings), ("key-pack", KeyBindings.Pack), ("key-equipment", KeyBindings.Equipment),
            ("key-map", KeyBindings.Map), ("key-kiosk", KeyBindings.Kiosk), ("key-contacts", KeyBindings.Contacts), ("key-crew", KeyBindings.Crew),
        };

        private readonly GraphicsSettingsTab graphicsTab = new GraphicsSettingsTab();
        private readonly AudioSettingsTab audioTab = new AudioSettingsTab();
        private ControlsSettingsTab controlsTab;
        private string activePage = Pages[0];
        private Vector2Int authoredReferenceResolution;
        private Button pickPanelButton, resetPanelButton;
        private ScrollView panelList;
        private const float PanelListGap = 6f;
        private readonly List<(string Name, string Label)> panels = new List<(string, string)>();
        private InputActionAsset shownPlayerActions;
        private readonly HudLayoutEditor layout = new HudLayoutEditor();
        private readonly List<VisualElement> documentRoots = new List<VisualElement>();
        private UIDocument document;
        private VisualElement root;
        private VisualElement settingsWindow;
        private VisualElement editBanner;
        private VisualElement controlsHint;
        private Button editLayoutButton;
        private bool initialized;
        private bool suppressingHotbar;
        private ProgressionHUD hud;
        // The HUD's hotbar follows the local player (offline avatar or networked player); fall back to the serialized one.
        private PlayerSkillHotbar CurrentHotbar => hud != null && hud.Hotbar != null ? hud.Hotbar : hotbar;
        private VisualElement boundRoot;

        public bool IsSettingsOpen => settingsWindow != null && settingsWindow.resolvedStyle.display != DisplayStyle.None;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            if (root == null) return;

            settingsWindow = root.Q("settings-window");
            editBanner = root.Q("hud-edit-banner");
            controlsHint = root.Q("controls-hint");
            editLayoutButton = root.Q<Button>("settings-edit-layout");
            pickPanelButton = root.Q<Button>("hud-edit-pick");
            resetPanelButton = root.Q<Button>("hud-edit-reset-one");
            panelList = root.Q<ScrollView>("hud-edit-list");
            // The banner grows when RESET THIS PANEL appears: the list follows its lower edge.
            editBanner?.RegisterCallback<GeometryChangedEvent>(_ => PlacePanelList());
            if (settingsWindow == null)
            {
                Debug.LogError("HudSettingsController needs a 'settings-window' element in its UXML.", this);
                return;
            }
            hud = GetComponent<ProgressionHUD>();
            if (controlsTab == null) controlsTab = new ControlsSettingsTab(PlayerActions, OnListeningChanged);
            if (document.panelSettings != null) authoredReferenceResolution = document.panelSettings.referenceResolution;
            AudioPreferences.Bind(mixer);

            if (boundRoot != root)
            {
                boundRoot = root;
                BindControls();
                BuildPages();
            }

            HudPreferences.Changed += ApplyPreferences;
            KeyBindings.Changed += RefreshKeyLabels;

            layout.EditingChanged += OnEditingChanged;
            root.RegisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            ClientPresentationRegistry.RegisterPointerBlock(this);

            SetVisible(settingsWindow, false);
            SetVisible(editBanner, false);
            ApplyPreferences();
            initialized = true;
        }

        private void Start()
        {
            if (!initialized) return;
            CollectMovables();
            layout.LoadAndApply();
        }

        private void OnDisable()
        {
            ClientPresentationRegistry.UnregisterPointerBlock(this);
            if (!initialized) return;
            layout.EndEdit();
            controlsTab?.Cancel();
            SetHotbarSuppressed(false);
            HudPreferences.Changed -= ApplyPreferences;
            KeyBindings.Changed -= RefreshKeyLabels;
            // The panel settings asset is shared project data: leave it the size it was authored at.
            if (document != null && document.panelSettings != null && authoredReferenceResolution.x > 0)
                document.panelSettings.referenceResolution = authoredReferenceResolution;
            layout.EditingChanged -= OnEditingChanged;
            root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            initialized = false;
        }

        private void Update()
        {
            if (!initialized) return;
            // The player's own actions arrive with the player (and change at the offline-to-online handoff).
            InputActionAsset actions = PlayerActions();
            if (actions != shownPlayerActions)
            {
                shownPlayerActions = actions;
                if (controlsTab != null && !controlsTab.IsListening) controlsTab.Build(root.Q("settings-page-controls"));
                RefreshKeyLabels();
            }

            var keyboard = Keyboard.current;
            if (keyboard == null || IsTyping()) return;
            // The key that set (or cancelled) a binding belongs to the Controls page, not to the window.
            if (controlsTab != null && (controlsTab.IsListening || controlsTab.ListeningEndedFrame == Time.frameCount)) return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (layout.IsEditing && IsPanelListOpen) SetVisible(panelList, false);
                else if (layout.IsEditing) EndLayoutEdit();
                else if (IsSettingsOpen) CloseSettings();
                return;
            }
            if (KeyBindings.Pressed(KeyBindings.Settings) && !layout.IsEditing) ToggleSettings();
        }

        // ------------------------------------------------------------------ settings window

        /// <summary>Opens the settings window (the game menu's SETTINGS).</summary>
        public void OpenSettings()
        {
            if (initialized && !IsSettingsOpen) ToggleSettings();
        }

        private void ToggleSettings()
        {
            bool show = !IsSettingsOpen;
            if (!show) controlsTab?.Cancel();
            SetVisible(settingsWindow, show);
            if (!show) return;
            // The window lives in a layer of its own: the layer, not the window, is what stacks against the others.
            settingsWindow.parent?.BringToFront();
            graphicsTab.Refresh();
        }

        private void CloseSettings()
        {
            controlsTab?.Cancel();
            SetVisible(settingsWindow, false);
        }

        private void ApplyPreferences()
        {
            SetVisible(controlsHint, HudPreferences.ShowControlsHint);
            // The whole interface grows or shrinks by drawing it against a smaller or larger reference screen.
            if (document.panelSettings != null && authoredReferenceResolution.x > 0)
            {
                float scale = HudPreferences.InterfaceScale;
                var reference = new Vector2Int(Mathf.RoundToInt(authoredReferenceResolution.x / scale), Mathf.RoundToInt(authoredReferenceResolution.y / scale));
                if (document.panelSettings.referenceResolution != reference) document.panelSettings.referenceResolution = reference;
            }
        }

        // ------------------------------------------------------------------ pages

        private InputActionAsset PlayerActions() => CurrentHotbar != null ? CurrentHotbar.Actions : null;

        private void BuildPages()
        {
            foreach (string page in Pages)
            {
                string name = page;
                Bind("settings-tab-" + name, () => ShowPage(name));
            }
            graphicsTab.Build(root.Q("settings-page-graphics"));
            audioTab.Build(root.Q("settings-page-audio"));
            controlsTab.Build(root.Q("settings-page-controls"));

            VisualElement layoutOptions = root.Q("settings-layout-options");
            if (layoutOptions != null)
            {
                string[] sizes = System.Array.ConvertAll(HudPreferences.GridSizes, size => size + " PX");
                SettingsRows.Choice(layoutOptions, "Grid size", sizes,
                    () => Mathf.Max(0, System.Array.IndexOf(HudPreferences.GridSizes, HudPreferences.GridSize)),
                    index => HudPreferences.GridSize = HudPreferences.GridSizes[index]);
                SettingsRows.Slider(layoutOptions, "Interface size", HudPreferences.MinInterfaceScale, HudPreferences.MaxInterfaceScale,
                    () => HudPreferences.InterfaceScale, value => HudPreferences.InterfaceScale = Mathf.Round(value * 20f) / 20f);
            }
            ShowPage(activePage);
            RefreshKeyLabels();
        }

        private void ShowPage(string page)
        {
            if (page != "controls") controlsTab?.Cancel();
            activePage = page;
            foreach (string name in Pages)
            {
                root.Q("settings-page-" + name)?.EnableInClassList("active", name == page);
                root.Q<Button>("settings-tab-" + name)?.EnableInClassList("active", name == page);
            }
        }

        private void OnListeningChanged(bool listening)
        {
            // While the Controls page waits for a key the Hotbar must not act on it (unless the layout editor holds it already).
            if (!layout.IsEditing) SetHotbarSuppressed(listening);
        }

        /// <summary>Writes the current keys into every place the HUD names one: menu buttons, window headers, hints.</summary>
        private void RefreshKeyLabels()
        {
            if (root == null) return;
            InputActionAsset actions = PlayerActions();
            foreach (var entry in MenuKeys)
            {
                Button button = root.Q<Button>(entry.Button);
                if (button == null) continue;
                string key = entry.WindowKey != null ? KeyBindings.Label(entry.WindowKey)
                    : KeyBindings.Label(actions?.FindAction("SkillCommands/" + entry.PlayerAction, false));
                button.text = string.IsNullOrEmpty(key) ? entry.Caption : $"{entry.Caption}  [{key}]";
            }

            CollectDocumentRoots();
            foreach (var entry in HeaderKeys)
                foreach (VisualElement documentRoot in documentRoots)
                {
                    Label label = documentRoot.Q<Label>(entry.Label);
                    if (label != null) label.text = $"[{KeyBindings.Label(entry.WindowKey)}]";
                }

            if (controlsHint is Label hint)
            {
                string first = PlayerSkillHotbar.GetKeyLabel(0), last = PlayerSkillHotbar.GetKeyLabel(PlayerSkillHotbar.NumberSlotCount - 1);
                string letters = string.Join(" ", PlayerSkillHotbar.GetKeyLabel(10), PlayerSkillHotbar.GetKeyLabel(11),
                    PlayerSkillHotbar.GetKeyLabel(12), PlayerSkillHotbar.GetKeyLabel(13));
                hint.text = $"{first}\u2013{last} / {letters} cast skill (hold to repeat or charge)   \u00B7   Drag to rearrange   \u00B7   " +
                            "Right-click a slot to clear   \u00B7   Esc closes windows";
            }
        }

        // ------------------------------------------------------------------ layout editing

        private void BeginLayoutEdit()
        {
            CloseSettings(); // the window would sit over the panels being placed; DONE on the banner ends the edit
            CollectMovables();
            layout.BeginEdit();
            ShowPanelList(); // nothing is in edit state until the player says what to adjust
        }

        private bool IsPanelListOpen => panelList != null && panelList.resolvedStyle.display != DisplayStyle.None;

        /// <summary>The choices of the layout editor: every panel at once, or one of them by name.</summary>
        private void ShowPanelList()
        {
            if (panelList == null) return;
            panelList.Clear();
            layout.GetPanels(panels);
            AddPanelChoice(HudLayoutEditor.AllPanels);
            foreach (var panel in panels) AddPanelChoice(panel.Name);
            SetVisible(panelList, true);
            panelList.BringToFront();
            PlacePanelList();
        }

        /// <summary>Puts the list just under the banner, whatever height the banner has now.</summary>
        private void PlacePanelList()
        {
            if (panelList == null || editBanner == null || panelList.parent == null) return;
            Rect banner = editBanner.worldBound;
            if (banner.height < 1f) return; // not laid out yet: the style sheet's place stands until it is
            panelList.style.top = panelList.parent.WorldToLocal(new Vector2(banner.xMin, banner.yMax)).y + PanelListGap;
        }

        private void AddPanelChoice(string name)
        {
            var choice = new Button(() =>
            {
                SetVisible(panelList, false);
                layout.Select(name);
            }) { text = layout.LabelOf(name) };
            choice.EnableInClassList("chosen", layout.Selection == name);
            panelList.Add(choice);
        }

        private void TogglePanelList()
        {
            if (IsPanelListOpen) SetVisible(panelList, false);
            else ShowPanelList();
        }

        /// <summary>A panel that sizes itself (the chat window) changed: keep it on screen and store its place.</summary>
        public void Replace(VisualElement element)
        {
            if (initialized) layout.Recommit(element);
        }

        private void EndLayoutEdit() => layout.EndEdit();

        private void ResetLayout()
        {
            CollectMovables();
            layout.ResetAll();
        }

        private void OnEditingChanged()
        {
            SetVisible(editBanner, layout.IsEditing);
            if (!layout.IsEditing) SetVisible(panelList, false);
            if (pickPanelButton != null) pickPanelButton.text = "PANEL: " + layout.LabelOf(layout.Selection);
            // One panel chosen: it can be put back on its own. With all or none chosen, RESET ALL is the button.
            SetVisible(resetPanelButton, layout.IsEditing && layout.Selection != null && layout.Selection != HudLayoutEditor.AllPanels);
            // The banner sits above everything else while editing.
            if (layout.IsEditing) editBanner?.BringToFront();
            SetHotbarSuppressed(layout.IsEditing);
            if (editLayoutButton != null) editLayoutButton.SetEnabled(!layout.IsEditing);
        }

        private void OnRootGeometryChanged(GeometryChangedEvent evt)
        {
            if (!layout.IsEditing) layout.ClampAllIntoView();
        }

        /// <summary>Finds movable elements in this document and every other document on the same panel.</summary>
        private void CollectMovables()
        {
            CollectDocumentRoots();
            layout.Collect(documentRoots);
        }

        private void CollectDocumentRoots()
        {
            documentRoots.Clear();
            documentRoots.Add(root);
            foreach (var other in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
            {
                if (other == document || other.rootVisualElement == null) continue;
                if (other.panelSettings != document.panelSettings) continue;
                documentRoots.Add(other.rootVisualElement);
            }
        }

        // ------------------------------------------------------------------ input

        public bool BlocksWorldPointer(Vector2 screenPosition) => initialized && layout.IsEditing;

        private bool IsTyping()
        {
            var focused = root.panel?.focusController?.focusedElement as VisualElement;
            for (VisualElement element = focused; element != null; element = element.parent)
                if (element is TextField) return true;
            return false;
        }

        private void SetHotbarSuppressed(bool suppressed)
        {
            var current = CurrentHotbar;
            if (current == null || suppressingHotbar == suppressed) return;
            suppressingHotbar = suppressed;
            current.InputSuppressed = suppressed;
        }

        // ------------------------------------------------------------------ helpers

        private void BindControls()
        {
            Bind("open-settings", ToggleSettings);
            Bind("close-settings", CloseSettings);
            Bind("settings-edit-layout", BeginLayoutEdit);
            Bind("settings-reset-layout", ResetLayout);
            Bind("hud-edit-done", EndLayoutEdit);
            Bind("hud-edit-reset", ResetLayout);
            Bind("hud-edit-pick", TogglePanelList);
            Bind("hud-edit-reset-one", () => layout.ResetSelected());
            BindToggle("setting-move-marker", HudPreferences.ShowMoveMarker, v => HudPreferences.ShowMoveMarker = v);
            BindToggle("setting-target-ring", HudPreferences.ShowTargetRing, v => HudPreferences.ShowTargetRing = v);
            BindToggle("setting-nameplates", HudPreferences.ShowNameplates, v => HudPreferences.ShowNameplates = v);
            BindToggle("setting-own-namecard", HudPreferences.ShowOwnNamecard, v => HudPreferences.ShowOwnNamecard = v);
            BindToggle("setting-pet-chatter", HudPreferences.PetChatter, v => HudPreferences.PetChatter = v);
            BindToggle("setting-controls-hint", HudPreferences.ShowControlsHint, v => HudPreferences.ShowControlsHint = v);
            BindToggle("setting-minimap-rotate", HudPreferences.MinimapRotates, v => HudPreferences.MinimapRotates = v);
            BindToggle("setting-screen-shake", HudPreferences.ScreenShake, v => HudPreferences.ScreenShake = v);
            BindToggle("setting-snap-grid", HudPreferences.SnapToGrid, v => HudPreferences.SnapToGrid = v);
        }

        private void Bind(string elementName, System.Action callback)
        {
            Button button = root.Q<Button>(elementName);
            if (button != null) button.clicked += callback;
        }

        private void BindToggle(string elementName, bool initialValue, System.Action<bool> onChanged)
        {
            Toggle toggle = root.Q<Toggle>(elementName);
            if (toggle == null) return;
            toggle.SetValueWithoutNotify(initialValue);
            toggle.RegisterValueChangedCallback(evt => onChanged(evt.newValue));
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
