using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// A titled grid of colour swatches built on the Fantasy Screens colour section: shows one swatch per colour
    /// handed to it and keeps the picked one lit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuSwatchGroup : MonoBehaviour
    {
        private readonly List<MenuRow> swatches = new List<MenuRow>();
        private readonly List<Image> fills = new List<Image>();
        private TMP_Text title;
        private Action<int> picked;
        private bool bound;

        public int Capacity
        {
            get
            {
                Bind();
                return swatches.Count;
            }
        }

        /// <summary><paramref name="selected"/> is the lit swatch, or -1 for none. <paramref name="onPick"/> gets the index clicked.</summary>
        public void Show(string label, IReadOnlyList<Color32> colours, int selected, Action<int> onPick)
        {
            Bind();
            picked = onPick;
            if (title != null) title.text = label;
            gameObject.SetActive(true);
            for (int i = 0; i < swatches.Count; i++)
            {
                bool used = i < colours.Count;
                swatches[i].gameObject.SetActive(used);
                if (!used) continue;
                var c = colours[i];
                if (fills[i] != null) fills[i].color = new Color32(c.r, c.g, c.b, 255);
            }
            Select(selected);
        }

        public void Hide()
        {
            picked = null;
            gameObject.SetActive(false);
        }

        public void Select(int index)
        {
            for (int i = 0; i < swatches.Count; i++) swatches[i].SetSelected(i == index);
        }

        private void Bind()
        {
            if (bound) return;
            bound = true;
            title = MenuUi.Find<TMP_Text>(transform, "Label_Header");
            var grid = MenuUi.Find(transform, "Grid_Colours");
            if (grid == null) return;
            for (int i = 0; i < grid.childCount; i++)
            {
                var swatch = grid.GetChild(i);
                if (!swatch.TryGetComponent<MenuRow>(out var row)) row = swatch.gameObject.AddComponent<MenuRow>();
                int index = swatches.Count;
                row.Clicked += () =>
                {
                    Select(index);
                    picked?.Invoke(index);
                    MenuMoments.Raise(MenuMoment.Toggle, swatch);
                };
                swatches.Add(row);
                fills.Add(MenuUi.Find<Image>(swatch, "COLOUR"));
            }
        }
    }
}
