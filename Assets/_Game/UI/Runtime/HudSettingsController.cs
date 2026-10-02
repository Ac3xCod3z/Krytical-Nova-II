using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Settings window (O / menu button) and the HUD layout editor. Gathers movable elements from every UIDocument
    /// that shares this document's PanelSettings, so the NPC dialogue and zone toast can be placed alongside the HUD.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudSettingsController : MonoBehaviour, IPointerBlockQuery
    {
        [Tooltip("Optional. Its key bindings are suppressed while the layout editor is open. Defaults to the ProgressionHUD's hotbar.")]
        [SerializeField] private PlayerSkillHotbar hotbar;

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
            if (settingsWindow == null)
            {
                Debug.LogError("HudSettingsController needs a 'settings-window' element in its UXML.", this);
                return;
            }
            hud = GetComponent<ProgressionHUD>();

            if (boundRoot != root)
            {
                boundRoot = root;
                BindControls();
            }

            HudPreferences.Changed += ApplyPreferences;

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
            SetHotbarSuppressed(false);
            HudPreferences.Changed -= ApplyPreferences;
            layout.EditingChanged -= OnEditingChanged;
            root.UnregisterCallback<GeometryChangedEvent>(OnRootGeometryChanged);
            initialized = false;
        }

        private void Update()
        {
            if (!initialized) return;
            var keyboard = Keyboard.current;
            if (keyboard == null || IsTyping()) return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                if (layout.IsEditing) EndLayoutEdit();
                else if (IsSettingsOpen) CloseSettings();
                return;
            }
            if (keyboard.oKey.wasPressedThisFrame && !layout.IsEditing) ToggleSettings();
        }

        // ------------------------------------------------------------------ settings window

        private void ToggleSettings()
        {
            bool show = !IsSettingsOpen;
            SetVisible(settingsWindow, show);
            if (show) settingsWindow.BringToFront();
        }
        private void CloseSettings() => SetVisible(settingsWindow, false);

        private void ApplyPreferences()
        {
            SetVisible(controlsHint, HudPreferences.ShowControlsHint);
        }

        // ------------------------------------------------------------------ layout editing

        private void BeginLayoutEdit()
        {
            CollectMovables();
            layout.BeginEdit();
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
            documentRoots.Clear();
            documentRoots.Add(root);
            foreach (var other in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
            {
                if (other == document || other.rootVisualElement == null) continue;
                if (other.panelSettings != document.panelSettings) continue;
                documentRoots.Add(other.rootVisualElement);
            }
            layout.Collect(documentRoots);
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
            BindToggle("setting-move-marker", HudPreferences.ShowMoveMarker, v => HudPreferences.ShowMoveMarker = v);
            BindToggle("setting-target-ring", HudPreferences.ShowTargetRing, v => HudPreferences.ShowTargetRing = v);
            BindToggle("setting-nameplates", HudPreferences.ShowNameplates, v => HudPreferences.ShowNameplates = v);
            BindToggle("setting-controls-hint", HudPreferences.ShowControlsHint, v => HudPreferences.ShowControlsHint = v);
            BindToggle("setting-minimap-rotate", HudPreferences.MinimapRotates, v => HudPreferences.MinimapRotates = v);
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
