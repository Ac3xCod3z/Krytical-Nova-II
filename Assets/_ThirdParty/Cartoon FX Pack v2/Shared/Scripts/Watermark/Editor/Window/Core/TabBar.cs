using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using laplahce.Watermark.Theming;

namespace laplahce.Watermark.Core
{
    public sealed class TabBar
    {
        public int ActiveIndex { get; private set; }

        public bool Draw(IReadOnlyList<WatermarkTab> tabs)
        {
            bool changed = false;
            var style = new GUIStyle(EditorStyles.toolbarButton) { fontSize = 11, fixedHeight = 24 };

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.ExpandWidth(true)))
            {
                for (int i = 0; i < tabs.Count; i++)
                {
                    bool isActive = ActiveIndex == i;
                    var previousBackground = GUI.backgroundColor;
                    if (isActive) GUI.backgroundColor = WatermarkColors.Accent.WithAlpha(0.18f);

                    if (GUILayout.Button(tabs[i].Label, style, GUILayout.ExpandWidth(true)) && !isActive)
                    {
                        ActiveIndex = i;
                        changed = true;
                    }

                    if (isActive)
                    {
                        var lastRect = GUILayoutUtility.GetLastRect();
                        EditorGUI.DrawRect(new Rect(lastRect) { yMin = lastRect.yMax - 2f }, WatermarkColors.Accent);
                    }

                    GUI.backgroundColor = previousBackground;
                }
            }

            return changed;
        }

        public void SetActive(int index) => ActiveIndex = index;

        public bool SetActive(IReadOnlyList<WatermarkTab> tabs, string label)
        {
            for (int i = 0; i < tabs.Count; i++)
            {
                if (tabs[i].Label == label)
                {
                    ActiveIndex = i;
                    return true;
                }
            }
            return false;
        }
    }
}