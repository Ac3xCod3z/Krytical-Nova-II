using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using laplahce.Watermark.Widgets;
using laplahce.Watermark.Theming;

namespace laplahce.Watermark.Core
{
    public sealed class WatermarkWindow : EditorWindow
    {
        private readonly List<WatermarkTab> tabs = new List<WatermarkTab>();
        private readonly GradientTextureCache textureCache = new GradientTextureCache();

        private ButtonStyleFactory buttonStyles;
        private WidgetContext widgetContext;
        private HeaderBanner headerBanner;
        private TabBar tabBar;
        private WindowFooter footer;

        private WatermarkWindowConfig config;
        private bool dontShowAgain;
        private Vector2 scroll;

        public static WatermarkWindow Create(WatermarkWindowConfig config)
        {
            var window = CreateInstance<WatermarkWindow>();
            window.config = config;
            window.titleContent = new GUIContent(config.PackName);
            window.minSize = config.Size;
            window.dontShowAgain = EditorPrefs.GetBool(config.DontShowAgainKey, false);
            return window;
        }

        public WatermarkWindow Tab(string label)
        {
            tabs.Add(new WatermarkTab(label));
            return this;
        }

        public WatermarkWindow Section(string title, Color? color = null) => Add(new SubheaderWidget(title, color));
        public WatermarkWindow Text(string text, int fontSize = 11, Color? color = null) => Add(new LabelWidget(text, fontSize, color));
        public WatermarkWindow Space(float height = 8f) => Add(new SpacerWidget(height));
        public WatermarkWindow Line() => Add(new SeparatorWidget());
        public WatermarkWindow Button(string label, System.Action onClick, float height = 22f, bool usePipelinePalette = false)
            => Add(new GradientButtonWidget(label, onClick, height, usePipelinePalette));
        public WatermarkWindow ButtonRow(params IWidget[] buttons) => Add(new ButtonRowWidget(buttons));
        public WatermarkWindow ImportButton(string label, string dialogTitle, string dialogMessage, string packageGuid)
            => Add(new PackageImportButtonWidget(label, dialogTitle, dialogMessage, packageGuid, config.PackName));
        public WatermarkWindow LinkCard(string title, string buttonLabel, string url, string description = null, Color? accentColor = null)
            => Add(new LinkCardWidget(title, buttonLabel, url, description, accentColor));
        public WatermarkWindow LinkCardToTab(string title, string buttonLabel, string targetTab, string description = null, Color? accentColor = null)
            => Add(LinkCardWidget.ToTab(title, buttonLabel, targetTab, description, accentColor));
        public WatermarkWindow ReviewCard(string subtitle, string buttonLabel, string reviewUrl) => Add(new ReviewCardWidget(subtitle, buttonLabel, reviewUrl));
        public WatermarkWindow Widget(IWidget widget) => Add(widget);

        public WatermarkWindow Launch()
        {
            ShowUtility();
            return this;
        }

        private WatermarkWindow Add(IWidget widget)
        {
            tabs[tabs.Count - 1].Widgets.Add(widget);
            return this;
        }

        private void OnEnable()
        {
            dontShowAgain = config != null && EditorPrefs.GetBool(config.DontShowAgainKey, false);
            buttonStyles = new ButtonStyleFactory(textureCache);
            headerBanner = new HeaderBanner();
            tabBar = new TabBar();
            footer = new WindowFooter();
            widgetContext = new WidgetContext(textureCache, buttonStyles, NavigateToTab);
        }

        private void OnDisable()
        {
            textureCache.Dispose();
        }

        private void NavigateToTab(string label)
        {
            if (!tabBar.SetActive(tabs, label)) return;
            scroll = Vector2.zero;
            Repaint();
        }

        private void OnGUI()
        {
            if (config == null || tabs.Count == 0) return;

            DrawAccentBar();
            headerBanner.Draw(widgetContext, config.PackName, config.Tagline, config.PackVersion);
            DrawSeparator();

            if (tabBar.Draw(tabs)) scroll = Vector2.zero;
            DrawSeparator();

            DrawActiveTabContent();

            footer.Draw(widgetContext, ref dontShowAgain, config.DontShowAgainKey, Close);
        }

        private void DrawAccentBar()
        {
            var rect = GUILayoutUtility.GetRect(0f, 3f, GUILayout.ExpandWidth(true));
            var texture = textureCache.Get(WatermarkColors.BtnGradientLeftNormal, WatermarkColors.BtnGradientRightNormal);
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill);
        }

        private void DrawSeparator()
        {
            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, WatermarkColors.Separator);
        }

        private void DrawActiveTabContent()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            EditorGUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(10);
                using (new EditorGUILayout.VerticalScope())
                {
                    var activeTab = tabs[tabBar.ActiveIndex];
                    foreach (var widget in activeTab.Widgets) widget.Draw(widgetContext);
                }
                GUILayout.Space(10);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }
    }
}