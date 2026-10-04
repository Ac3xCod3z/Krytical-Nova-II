using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Keyboard glyphs for the menu prompts. The front end shows keyboard glyphs only, never gamepad buttons:
    /// a key that has a picture (Enter, Space, Tab, Backspace, arrows) shows it on the keycap, any other key shows
    /// its name. The Synty screens ship with their prompts switched to Xbox buttons; <see cref="Show"/> switches a
    /// prompt back to its keycap.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/UI/Key Glyph Set", fileName = "KeyGlyphs")]
    public sealed class KeyGlyphSet : ScriptableObject
    {
        [Serializable]
        public struct Glyph
        {
            public Key key;
            [Tooltip("Drawn on a keycap, tinted like the keycap text (the pack's _Clean sprites).")]
            public Sprite onKeycap;
            [Tooltip("Drawn on its own, with its shadow (the pack's _Underlay sprites).")]
            public Sprite standalone;
        }

        // Width of a picture on the pack's 80-unit keycap (the keycap prefab's own value; gamepad buttons use 80).
        private const float KeycapPictureWidth = 40f;
        private const float LetterSize = 52f, WordSize = 38f;

        public enum MouseGlyph
        {
            LeftButton,
            RightButton,
            MiddleButton
        }

        [SerializeField] private Glyph[] glyphs = Array.Empty<Glyph>();
        [Tooltip("Mouse pictures for prompts such as drag-to-rotate (the pack's MouseColor _Underlay sprites).")]
        [SerializeField] private Sprite mouseLeft, mouseRight, mouseMiddle;

        public void SetContents(Glyph[] value, Sprite leftButton, Sprite rightButton, Sprite middleButton)
        {
            glyphs = value ?? Array.Empty<Glyph>();
            mouseLeft = leftButton;
            mouseRight = rightButton;
            mouseMiddle = middleButton;
        }

        public bool TryGet(Key key, out Glyph glyph)
        {
            foreach (var g in glyphs)
                if (g.key == key) { glyph = g; return true; }
            glyph = default;
            return false;
        }

        /// <summary>
        /// Puts <paramref name="key"/> on a Synty hotkey prompt (Input_Hotkey_Prompt): keycap on, gamepad button
        /// gone, the key's picture or its name on the cap.
        /// </summary>
        public void Show(Transform prompt, Key key)
        {
            var hotkey = MenuUi.Find(prompt, "Hotkey");
            if (hotkey == null) return;
            // For its gamepad buttons the pack switches the keycap off (box and fitter), pins it to the button's
            // width and lets the button ignore the cap's layout. Undo all of it, so the cap fits its key again.
            foreach (var behaviour in hotkey.GetComponents<Behaviour>()) behaviour.enabled = true;
            if (hotkey.TryGetComponent<LayoutElement>(out var cap)) cap.preferredWidth = -1f;
            if (hotkey.TryGetComponent<ContentSizeFitter>(out var fitter)) fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            var label = MenuUi.Find<TMP_Text>(hotkey, "Input");
            var icon = MenuUi.Find<Image>(hotkey, "ICON");
            Glyph glyph = default;
            bool pictured = icon != null && TryGet(key, out glyph) && glyph.onKeycap != null;
            if (icon != null)
            {
                icon.gameObject.SetActive(pictured);
                if (pictured)
                {
                    icon.sprite = glyph.onKeycap;
                    icon.preserveAspect = true;
                    if (label != null) icon.color = label.color; // the pack tints cap pictures like cap text
                    if (icon.TryGetComponent<LayoutElement>(out var size))
                    {
                        size.ignoreLayout = false;
                        size.minWidth = -1f;
                        size.preferredWidth = KeycapPictureWidth;
                    }
                }
            }
            if (label != null)
            {
                string name = NameOf(key);
                label.gameObject.SetActive(!pictured);
                label.text = name;
                // The pack shrinks a word to fit a one-letter cap, which leaves "Esc" tiny. Widen the cap instead.
                label.enableAutoSizing = false;
                label.fontSize = name.Length > 1 ? WordSize : LetterSize;
            }
        }

        /// <summary>
        /// Puts a mouse picture on a Synty hotkey prompt. The prompt keeps the layout it ships with (a bare picture,
        /// no keycap): only the gamepad button is replaced.
        /// </summary>
        public void ShowMouse(Transform prompt, MouseGlyph button)
        {
            var hotkey = MenuUi.Find(prompt, "Hotkey");
            if (hotkey == null) return;
            var label = MenuUi.Find<TMP_Text>(hotkey, "Input");
            var icon = MenuUi.Find<Image>(hotkey, "ICON");
            Sprite sprite = button == MouseGlyph.LeftButton ? mouseLeft : button == MouseGlyph.RightButton ? mouseRight : mouseMiddle;
            if (label != null) label.gameObject.SetActive(false);
            if (icon == null) return;
            icon.gameObject.SetActive(sprite != null);
            icon.sprite = sprite;
            icon.color = Color.white;
            icon.preserveAspect = true;
        }

        /// <summary>The short name printed on a keycap.</summary>
        public static string NameOf(Key key)
        {
            switch (key)
            {
                case Key.Escape: return "Esc";
                case Key.Delete: return "Del";
                case Key.Enter:
                case Key.NumpadEnter: return "Enter";
                case Key.Backspace: return "Back";
                case Key.UpArrow: return "Up";
                case Key.DownArrow: return "Down";
                case Key.LeftArrow: return "Left";
                case Key.RightArrow: return "Right";
                case Key.LeftShift:
                case Key.RightShift: return "Shift";
                case Key.LeftCtrl:
                case Key.RightCtrl: return "Ctrl";
                case Key.LeftAlt:
                case Key.RightAlt: return "Alt";
            }
            if (key >= Key.Digit1 && key <= Key.Digit0) return key == Key.Digit0 ? "0" : ((int)(key - Key.Digit1) + 1).ToString();
            return key.ToString(); // letters, F-keys, Space, Tab
        }
    }
}
