using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// Adds the decorative Synty layers a single USS background can't provide: a chamfered outline (.kn-frame)
    /// over each panel and hotbar slot, and a hexagon pip in panel corners. Both ignore picking and are absolutely
    /// positioned, so they never change layout or steal clicks.
    /// </summary>
    public static class SyntySkin
    {
        public const string FrameClass = "kn-frame";
        public const string PipClass = "kn-pip";

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
            target.hierarchy.Add(CreateLayer(FrameClass));
        }

        private static VisualElement CreateLayer(string className)
        {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.AddToClassList(className);
            return layer;
        }
    }
}
