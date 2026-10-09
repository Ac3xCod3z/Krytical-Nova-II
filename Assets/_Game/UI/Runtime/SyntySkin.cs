using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Adds the decorative Synty layers a single USS background can't provide: an outline (.kn-frame) over each
    /// panel and hotbar slot, a hexagon pip in panel corners, and on windows the two ornaments of the top edge
    /// (.kn-crown over the frame, .kn-crown-back under it). All ignore picking and are absolutely positioned, so
    /// they never change layout or steal clicks. What each one shows is the faction skin's business (the sheets).
    /// </summary>
    public static class SyntySkin
    {
        public const string FrameClass = "kn-frame";
        public const string PipClass = "kn-pip";
        public const string CrownClass = "kn-crown";
        public const string CrownBackClass = "kn-crown-back";
        private const string WindowClass = "window";
        /// <summary>The pack's ornate frame over a card (a section of a page, a skill's card).</summary>
        public const string CardFrameClass = "kn-card-frame";
        /// <summary>The pack's slot frame over a skill's tile.</summary>
        public const string TokenFrameClass = "kn-token-frame";
        /// <summary>The pack's bar under the heading of a section: two halves that meet in the middle.</summary>
        public const string SectionBarClass = "kn-section-bar";

        public static void Decorate(VisualElement root)
        {
            if (root == null) return;
            root.Query(className: "panel").ForEach(panel => AddLayers(panel, true));
            root.Query(className: "kn-panel").ForEach(panel => AddLayers(panel, true));
            root.Query(className: "hotbar-slot").ForEach(slot => AddLayers(slot, false));
            // The pages' parts that every window shares: cards, skill tiles, and the headings of sections.
            root.Query(className: "kn-card").ForEach(card => AddLayer(card, CardFrameClass));
            root.Query(className: "skill-token").ForEach(token => AddLayer(token, TokenFrameClass));
            root.Query(className: "eyebrow").ForEach(AddSectionBar);
        }

        private static void AddLayer(VisualElement target, string className)
        {
            foreach (VisualElement child in target.hierarchy.Children())
                if (child.ClassListContains(className)) return;
            target.hierarchy.Add(CreateLayer(className));
        }

        // The bar goes right after the heading, in whatever the heading is laid out in.
        private static void AddSectionBar(VisualElement heading)
        {
            VisualElement parent = heading.hierarchy.parent;
            if (parent == null) return;
            // A heading in a row (beside a value or a button) keeps the row to itself.
            if (parent.resolvedStyle.flexDirection == FlexDirection.Row || parent.ClassListContains("row")) return;
            int at = parent.hierarchy.IndexOf(heading);
            if (at + 1 < parent.hierarchy.childCount && parent.hierarchy[at + 1].ClassListContains(SectionBarClass)) return;
            VisualElement bar = CreateLayer(SectionBarClass);
            bar.AddToClassList("doll-bar");
            bar.AddToClassList("doll-bar--top");
            foreach (string side in new[] { "doll-bar-left", "doll-bar-right" })
            {
                VisualElement half = CreateLayer("doll-bar-half");
                half.AddToClassList(side);
                bar.Add(half);
            }
            parent.hierarchy.Insert(at + 1, bar);
        }

        private static void AddLayers(VisualElement target, bool withPip)
        {
            foreach (VisualElement child in target.hierarchy.Children())
                if (child.ClassListContains(FrameClass)) return;

            if (withPip) target.hierarchy.Add(CreateLayer(PipClass));
            if (target.ClassListContains(WindowClass)) target.hierarchy.Add(CreateLayer(CrownBackClass));
            target.hierarchy.Add(CreateLayer(FrameClass));
            if (target.ClassListContains(WindowClass)) target.hierarchy.Add(CreateLayer(CrownClass));
        }

        private static VisualElement CreateLayer(string className)
        {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList(className);
            return layer;
        }
    }
}
