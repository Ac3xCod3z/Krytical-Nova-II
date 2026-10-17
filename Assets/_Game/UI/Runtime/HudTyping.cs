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
