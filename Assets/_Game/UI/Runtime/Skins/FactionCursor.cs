using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// The mouse pointer in a faction's own art (the pointer of its Synty interface pack, kept on its
    /// <see cref="UiSkin"/>). In the game it is the character's faction (HudChrome), in the menus the faction whose
    /// skin the menus wear (MenuSkinSwitch). With no picture the system's own pointer comes back.
    /// </summary>
    public static class FactionCursor
    {
        private static Texture2D shown;

        // Enter Play Mode runs without a domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => shown = null;

        /// <param name="hotspot">Where the pointer points, in the picture's pixels from its top-left corner.</param>
        public static void Show(Texture2D picture, Vector2 hotspot)
        {
            if (picture == shown) return;
            shown = picture;
            Cursor.SetCursor(picture, picture != null ? hotspot : Vector2.zero, CursorMode.Auto);
        }
    }
}
