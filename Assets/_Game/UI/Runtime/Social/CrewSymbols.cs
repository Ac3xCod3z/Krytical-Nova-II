using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// How a Crew's symbol looks. A Crew stores only a number; this turns it into one of a few shapes in one of a
    /// few colours, so the same symbol reads the same in every list and, later, over a player's head. Real icon art
    /// can replace this in one place.
    /// </summary>
    public static class CrewSymbols
    {
        private static readonly Color[] Colours =
        {
            new Color(0.94f, 0.33f, 0.31f), new Color(1f, 0.72f, 0.25f), new Color(0.45f, 0.87f, 0.77f),
            new Color(0.36f, 0.62f, 1f), new Color(0.73f, 0.45f, 1f), new Color(0.93f, 0.95f, 0.97f),
        };

        /// <summary>Shapes times colours: every index below this is a different symbol.</summary>
        public const int Count = 12;

        /// <summary>Styles an element as the symbol with this index: even numbers are squares, odd ones diamonds.</summary>
        public static void Apply(VisualElement element, int index)
        {
            if (element == null) return;
            index = Mathf.Abs(index) % Count;
            element.style.backgroundColor = Colours[(index / 2) % Colours.Length];
            element.style.rotate = new Rotate(index % 2 == 0 ? 0f : 45f);
        }
    }
}
