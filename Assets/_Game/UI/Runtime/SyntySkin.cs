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

        public static void Decorate(VisualElement root)
        {
            if (root == null) return;
            root.Query(className: "panel").ForEach(panel => AddLayers(panel, true));
            root.Query(className: "kn-panel").ForEach(panel => AddLayers(panel, true));
            root.Query(className: "hotbar-slot").ForEach(slot => AddLayers(slot, false));
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
