using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Keyboard shortcuts for a menu screen, and the keyboard glyphs on the prompts that advertise them. While a
    /// text field has focus only Enter and Escape get through, so typing a name never triggers a shortcut.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuHotkeys : MonoBehaviour
    {
        [Tooltip("Keyboard glyphs shown on the prompts. Without it prompts show the key's name as text.")]
        [SerializeField] private KeyGlyphSet glyphs;

        private readonly List<(Key key, Action action)> bindings = new List<(Key, Action)>();

        /// <summary>Set while a popup owns the keyboard.</summary>
        public bool Suspended { get; set; }

        public void Bind(Key key, Action action)
        {
            for (int i = 0; i < bindings.Count; i++)
                if (bindings[i].key == key) { bindings[i] = (key, action); return; }
            bindings.Add((key, action));
        }

        /// <summary>Binds <paramref name="key"/> and shows it on the prompt that advertises it.</summary>
        public void Bind(Key key, Action action, Transform prompt)
        {
            Bind(key, action);
            ShowKey(prompt, key);
        }

        /// <summary>Puts a keyboard glyph on a Synty hotkey prompt, replacing the gamepad button it ships with.</summary>
        public void ShowKey(Transform prompt, Key key)
        {
            if (prompt == null) return;
            if (glyphs != null) glyphs.Show(prompt, key);
            else MenuUi.SetText(prompt, "Input", KeyGlyphSet.NameOf(key));
        }

        /// <summary>Puts a mouse picture on a prompt that explains a mouse gesture (drag to turn, wheel to zoom).</summary>
        public void ShowMouse(Transform prompt, KeyGlyphSet.MouseGlyph button)
        {
            if (prompt != null && glyphs != null) glyphs.ShowMouse(prompt, button);
        }

        public void Clear() => bindings.Clear();

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (Suspended || keyboard == null) return;
            // Settings is open over this screen: every key is the window's.
            if (MenuSettings.Current != null && MenuSettings.Current.IsOpen) return;
            var transition = MenuTransition.Current;
            if (transition != null && transition.IsBusy) return;

            bool typing = IsTyping();
            // One action per frame, then stop: it may rebind keys or load another scene.
            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                var (key, action) = bindings[i];
                if (typing && key != Key.Enter && key != Key.NumpadEnter && key != Key.Escape) continue;
                if (!keyboard[key].wasPressedThisFrame) continue;
                action?.Invoke();
                return;
            }
        }

        private static bool IsTyping()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.TryGetComponent<TMP_InputField>(out var field) && field.isFocused;
        }
    }
}
