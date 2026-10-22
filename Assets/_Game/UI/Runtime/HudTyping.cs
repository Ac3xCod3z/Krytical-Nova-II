using KryticalNova.Items;
using KryticalNova.Progression;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Text fields on the HUD: while one has the cursor, the Hotbar keys (1-0, Q W E R T) and the window keys must
    /// not fire. Every window with a text field guards it here instead of repeating the same focus handling.
    /// </summary>
    public static class HudTyping
    {
        /// <summary>The Hotbar stops listening while this field has the cursor.</summary>
        public static void Guard(TextField field)
        {
            if (field == null) return;
            field.RegisterCallback<FocusInEvent>(_ => SetSuppressed(true));
            field.RegisterCallback<FocusOutEvent>(_ => SetSuppressed(false));
        }

        /// <summary>
        /// Keyboard navigation never puts the cursor in a text field of this panel (owner's laptop report, 22 Oct 2026:
        /// skill keys typed into chat with the Skills window open). The UI's Navigate action reads W A S D and the
        /// arrows, and W is a Hotbar key: after a click on a window's button, W walked the focus into the chat line,
        /// and every key after it was typed there. A field is still entered by a click on it or by the game
        /// (Enter for chat, a rename box): only a focus that came by navigation is let go at once.
        /// </summary>
        public static void NoKeyboardFocus(VisualElement root)
        {
            if (root == null) return;
            root.UnregisterCallback<FocusInEvent>(LetGoOfNavigatedField, TrickleDown.TrickleDown);
            root.RegisterCallback<FocusInEvent>(LetGoOfNavigatedField, TrickleDown.TrickleDown);
        }

        private static void LetGoOfNavigatedField(FocusInEvent evt)
        {
            if (evt.direction == FocusChangeDirection.unspecified || evt.direction == FocusChangeDirection.none) return;
            for (VisualElement element = evt.target as VisualElement; element != null; element = element.parent)
            {
                if (!(element is TextField field)) continue;
                evt.StopImmediatePropagation();
                field.Blur();
                return;
            }
        }

        /// <summary>True while the cursor is in any text field of this panel: window hotkeys should wait.</summary>
        public static bool IsTyping(VisualElement root)
        {
            var focused = root?.panel?.focusController?.focusedElement as VisualElement;
            for (VisualElement element = focused; element != null; element = element.parent)
                if (element is TextField) return true;
            return false;
        }

        private static void SetSuppressed(bool suppressed)
        {
            PlayerInventory local = PlayerInventory.Local;
            if (local != null && local.TryGetComponent(out PlayerSkillHotbar hotbar)) hotbar.InputSuppressed = suppressed;
        }
    }
}
