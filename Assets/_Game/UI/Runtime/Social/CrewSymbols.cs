using KryticalNova.Factions;
using KryticalNova.Social;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// How a Crew's symbol looks. A Crew stores only a number; its faction's list in the
    /// <see cref="CrewSymbolLibrary"/> turns that into a picture from the faction's own interface pack, so the same
    /// symbol reads the same in the Crew window and over a member's head.
    /// </summary>
    public static class CrewSymbols
    {
        /// <summary>Shows a faction's symbol with this number on an element, or nothing when there is no picture for it.</summary>
        public static void Apply(VisualElement element, CrewSymbolLibrary library, FactionId faction, int index)
        {
            if (element == null) return;
            var picture = library != null ? library.Get(faction, index) : null;
            element.style.backgroundImage = picture != null ? new StyleBackground(picture) : StyleKeyword.Null;
        }

        /// <summary>
        /// The same, drawn in one of the Crew colours. The pictures are white, so the colour is all the colour there
        /// is: a Leader's choice shows the same in the window and over every member's head.
        /// </summary>
        public static void Apply(VisualElement element, CrewSettings settings, FactionId faction, int index, int colour)
        {
            if (element == null) return;
            Apply(element, settings != null ? settings.symbols : null, faction, index);
            element.style.unityBackgroundImageTintColor = settings != null ? settings.SymbolColour(colour) : Color.white;
        }
    }
}
