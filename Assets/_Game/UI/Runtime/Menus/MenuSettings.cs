using System;
using KryticalNova.Factions;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Settings as the menus open it (character select's SETTINGS): the same window as in the game, the same pages
    /// (<see cref="SettingsPages"/>) over the same stores, so what is set here is set there. It wears the skin the
    /// menus wear and follows the Menu Theme setting as it is changed. What needs a character in the world (the HUD
    /// layout editor) is hidden. Closing it gives the keyboard back to the screen that opened it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MenuSettings : MonoBehaviour
    {
        [Tooltip("The game's mixer: the Audio page's sliders drive its exposed volumes.")]
        [SerializeField] private AudioMixer mixer;

        private readonly SettingsPages pages = new SettingsPages();
        private VisualElement root, window, shade;
        private Action closed;
        private bool built;

        /// <summary>The scene's menu Settings, or null where a scene has none.</summary>
        public static MenuSettings Current { get; private set; }

        public bool IsOpen => window != null && window.resolvedStyle.display != DisplayStyle.None;

        /// <summary>The pages, for feedback hooks.</summary>
        public SettingsPages Pages => pages;

        private void OnEnable()
        {
            Current = this;
            root = GetComponent<UIDocument>().rootVisualElement;
            if (root == null) return;
            window = root.Q("settings-window");
            shade = root.Q("menu-settings-shade");
            if (window == null)
            {
                Debug.LogError("MenuSettings needs a 'settings-window' element in its UXML.", this);
                return;
            }
            if (!built)
            {
                built = true;
                AudioPreferences.Bind(mixer);
                // No character, so no skill keys to list: the window keys and the fixed keys are shown.
                pages.Build(root, () => null, _ => { }, false);
                Button close = root.Q<Button>("close-settings");
                if (close != null) close.clicked += Close;
            }
            Show(false);
            MenuSkin.Changed += WearSkin;
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            MenuSkin.Changed -= WearSkin;
        }

        private void Update()
        {
            if (!IsOpen) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            // The key that set (or cancelled) a binding belongs to the Controls page, not to the window.
            ControlsSettingsTab controls = pages.Controls;
            if (controls != null && (controls.IsListening || controls.ListeningEndedFrame == Time.frameCount)) return;
            if (keyboard.escapeKey.wasPressedThisFrame) Close();
        }

        /// <summary>Opens the window; <paramref name="onClosed"/> runs when it closes.</summary>
        public void Open(Action onClosed = null)
        {
            if (window == null || IsOpen) return;
            closed = onClosed;
            WearSkin();
            Show(true);
            pages.Opened();
        }

        public void Close()
        {
            if (!IsOpen) return;
            pages.Controls?.Cancel();
            Show(false);
            Action done = closed;
            closed = null;
            done?.Invoke();
        }

        private void Show(bool visible)
        {
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            window.style.display = display;
            if (shade != null) shade.style.display = display;
        }

        // The class goes on the panel's own root, as the game's skin does (FactionSkin), which clears it again when a
        // scene loads: it is set whenever the window opens and whenever the menus' skin changes.
        private void WearSkin()
        {
            if (root == null) return;
            VisualElement top = root.panel != null ? root.panel.visualTree : root;
            FactionId skin = MenuSkin.Current;
            top.EnableInClassList(FactionSkin.HegemonyClass, skin == FactionId.Hegemony);
            top.EnableInClassList(FactionSkin.NullClass, skin == FactionId.Null);
            top.EnableInClassList(FactionSkin.CovenantClass, skin == FactionId.Covenant);
        }
    }
}
