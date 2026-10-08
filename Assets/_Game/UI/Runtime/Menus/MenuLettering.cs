using TMPro;
using UnityEngine;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Sets a screen in a skin's lettering when the screen is not of the skin's own pack (the character creator:
    /// none of the menu packs has one, so every skin uses Fantasy Screens' layout). The display face goes where the
    /// screen used one, the reading face everywhere else. Done as the screen wakes, not in the scene file: a face
    /// that draws its letters on demand has no sheet to cut them from until the game runs.
    /// </summary>
    public sealed class MenuLettering : MonoBehaviour
    {
        [SerializeField] private TMP_FontAsset display;
        [SerializeField] private TMP_FontAsset reading;
        [Tooltip("Faces of the screen's own pack that are display faces (part of the font asset's name).")]
        [SerializeField] private string[] displayFaces = { "LTMuseum", "Ortica" };

        private bool lettered;

        public void Set(TMP_FontAsset displayFace, TMP_FontAsset readingFace)
        {
            display = displayFace;
            reading = readingFace;
        }

        private void Awake()
        {
            if (lettered) return;
            lettered = true;
            foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font == null) continue;
                TMP_FontAsset face = IsDisplay(text.font.name) ? display : reading;
                if (face != null && text.font != face) text.font = face;
            }
        }

        private bool IsDisplay(string faceName)
        {
            foreach (string each in displayFaces)
                if (faceName.Contains(each)) return true;
            return false;
        }
    }
}
