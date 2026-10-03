using System.Collections.Generic;
using UnityEngine;
using PSD.Creator.Inspector.Utility;
using PSD.Preferences.Theming;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Inspector
{
#if UNITY_EDITOR
    public class PageManager
    {
        private readonly Dictionary<string, List<Page>> categories = new Dictionary<string, List<Page>>();
        private readonly List<string> categoryOrder = new();

        public int CurrentCategoryIndex { get; private set; } = 0;
        public int CurrentPageIndex { get; private set; } = 0;

        private string CurrentCategory =>
            categoryOrder.Count > 0 ? categoryOrder[CurrentCategoryIndex] : null;

        private List<Page> CurrentPages =>
            CurrentCategory != null && categories.TryGetValue(CurrentCategory, out var list)
                ? list
                : null;

        public Page CurrentPage =>
            CurrentPages != null &&
            CurrentPageIndex >= 0 &&
            CurrentPageIndex < CurrentPages.Count
                ? CurrentPages[CurrentPageIndex]
                : null;

        public void AddPage(Page page)
        {
            if (!categories.TryGetValue(page.Category, out var list))
            {
                list = new List<Page>();
                categories[page.Category] = list;
                categoryOrder.Add(page.Category);
            }
            list.Add(page);
        }

        public void RemovePage(Page page)
        {
            if (CurrentPages == null) return;
            int index = CurrentPages.IndexOf(page);
            if (index >= 0)
            {
                CurrentPages.RemoveAt(index);
                if (CurrentPageIndex >= CurrentPages.Count)
                    CurrentPageIndex = Mathf.Max(0, CurrentPages.Count - 1);
            }
        }

        public void ClearPages()
        {
            CurrentPages?.Clear();
            CurrentPageIndex = 0;
        }

        public void NextPage()
        {
            var pages = CurrentPages;
            if (pages == null || pages.Count == 0) return;

            CurrentPageIndex = (CurrentPageIndex + 1) % pages.Count;
        }

        public void PreviousPage()
        {
            var pages = CurrentPages;
            if (pages == null || pages.Count == 0) return;

            CurrentPageIndex = (CurrentPageIndex - 1 + pages.Count) % pages.Count;
        }

        public void GoToPage(int index)
        {
            if (CurrentPages == null) return;
            if (index >= 0 && index < CurrentPages.Count)
                CurrentPageIndex = index;
        }

        public void SetCategory(int categoryIndex, int pageIndex = 0)
        {
            if (categoryIndex < 0 || categoryIndex >= categoryOrder.Count) return;

            CurrentCategoryIndex = categoryIndex;

            var pages = CurrentPages;
            if (pages != null && pageIndex >= 0 && pageIndex < pages.Count)
                CurrentPageIndex = pageIndex;
            else
                CurrentPageIndex = 0;
        }

        public void DrawNavigation(SerializedObject target)
        {
            float viewWidth = EditorGUIUtility.currentViewWidth;
            bool isMobile = viewWidth < 350;

            EditorGUILayout.BeginHorizontal();
            {
                if (GUILayout.Button(EditorGUIUtility.IconContent("back"), EditorStyles.label, GUILayout.Width(25), GUILayout.Height(20)))
                    PreviousPage();

                if (isMobile)
                    DrawMobileCenter(target);
                else
                    DrawDesktopCenter(target, viewWidth - 80);

                if (GUILayout.Button(EditorGUIUtility.IconContent("forward"), EditorStyles.label, GUILayout.Width(25), GUILayout.Height(20)))
                    NextPage();
            }
            EditorGUILayout.EndHorizontal();

            Rect rect = EditorGUILayout.GetControlRect(false, 1);
            rect.y += 6;
            EditorGUI.DrawRect(rect, Theme.NavigationSeparator);
            EditorGUILayout.Space(10);
        }

        private void DrawMobileCenter(SerializedObject target)
        {
            var allPages = new List<(string category, Page page, int catIndex, int pageIndex)>();
            for (int ci = 0; ci < categoryOrder.Count; ci++)
            {
                var pages = categories[categoryOrder[ci]];
                for (int pi = 0; pi < pages.Count; pi++)
                    allPages.Add((categoryOrder[ci], pages[pi], ci, pi));
            }

            string[] titles = new string[allPages.Count];
            int currentFlat = 0;

            for (int i = 0; i < allPages.Count; i++)
            {
                var (category, page, ci, pi) = allPages[i];
                bool isEnabled = page.IsEnabled(target);
                bool isValid = !isEnabled || page.Validate(target);

                string prefix = !isEnabled ? "\u25CB " : !isValid ? "⚠️ " : "";
                string suffix = !isEnabled ? " (Disabled)" : "";
                titles[i] = $"{category} / {prefix}{pi + 1}. {page.Title}{suffix}";

                if (ci == CurrentCategoryIndex && pi == CurrentPageIndex)
                    currentFlat = i;
            }

            int newFlat = EditorGUILayout.Popup(currentFlat, titles, EditorStyles.toolbarPopup);
            if (newFlat != currentFlat)
            {
                var (_, _, ci, pi) = allPages[newFlat];
                CurrentCategoryIndex = ci;
                CurrentPageIndex = pi;
            }
        }

        private void DrawCategoryBar()
        {
            if (categoryOrder.Count == 0) return;

            float viewWidth = EditorGUIUtility.currentViewWidth;
            if (viewWidth < 350) return;

            var tab = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 12,
                fixedHeight = 24,
                padding = new RectOffset(14, 14, 0, 0),
                alignment = TextAnchor.MiddleCenter
            };

            Color defaultBg = GUI.backgroundColor;

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(4);

            for (int i = 0; i < categoryOrder.Count; i++)
            {
                bool selected = i == CurrentCategoryIndex;

                GUI.backgroundColor = selected ? Theme.CategoryTabSelected : defaultBg;

                var style = new GUIStyle(tab);
                style.normal.textColor = selected ? Color.white : Theme.CategoryTabText;
                style.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;

                if (GUILayout.Button(categoryOrder[i], style, GUILayout.ExpandWidth(false)))
                {
                    if (CurrentCategoryIndex != i)
                    {
                        CurrentCategoryIndex = i;
                        CurrentPageIndex = 0;
                        GUI.FocusControl(null);
                    }
                }

                if (selected)
                {
                    Rect last = GUILayoutUtility.GetLastRect();
                    float width = last.width * 1 - 2;
                    EditorGUI.DrawRect(new Rect(last.x + (last.xMax - last.x) / 2 - width / 2, last.yMax, width, 1), Theme.CategoryUnderline);
                }

                GUILayout.Space(2);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = defaultBg;
            EditorGUILayout.Space(6);

            Rect separatorRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(separatorRect, Theme.Separator);

            EditorGUILayout.Space(6);
        }

        private void DrawDesktopCenter(SerializedObject target, float availableWidth)
        {
            var pill = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                fixedHeight = 22,
                padding = new RectOffset(10, 10, 3, 3),
                alignment = TextAnchor.MiddleCenter
            };

            Color defaultBg = GUI.backgroundColor;
            Color defaultContent = GUI.contentColor;

            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();

            float currentX = 0;

            for (int i = 0; i < CurrentPages.Count; i++)
            {
                bool isEnabled = CurrentPages[i].IsEnabled(target);
                bool isValid = !isEnabled || CurrentPages[i].Validate(target);
                bool isActive = i == CurrentPageIndex;

                string prefix = !isEnabled ? "\u25CB " : !isValid ? "⚠ " : "";
                GUIContent content = new GUIContent(prefix + CurrentPages[i].Title);

                float buttonWidth = pill.CalcSize(content).x + 8;

                if (currentX + buttonWidth > availableWidth)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    currentX = 0;
                }

                if (isActive)
                    GUI.backgroundColor = isValid ? Theme.PillActive : Theme.PillActiveInvalid;
                else if (!isValid)
                    GUI.backgroundColor = Theme.PillInvalid;
                else
                    GUI.backgroundColor = defaultBg;

                GUI.contentColor = isEnabled ? defaultContent : Theme.PillDisabledContent;

                if (GUILayout.Button(content, pill, GUILayout.Width(buttonWidth)))
                {
                    CurrentPageIndex = i;
                    GUI.FocusControl(null);
                }

                currentX += buttonWidth;
            }

            GUI.backgroundColor = defaultBg;
            GUI.contentColor = defaultContent;

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        public void DrawCurrentPage(SerializedObject target) => CurrentPage?.DrawPage(target);

        public void DrawPageSystem(SerializedObject target)
        {
            if (categories.Count == 0)
            {
                EditorGUILayout.HelpBox("No pages available.", MessageType.Info);
                return;
            }

            DrawCategoryBar();
            DrawNavigation(target);
            DrawCurrentPage(target);
        }
    }
#endif
}