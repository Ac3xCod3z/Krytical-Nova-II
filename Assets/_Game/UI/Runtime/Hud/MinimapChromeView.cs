using UnityEngine.UIElements;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The minimap's dial in the faction's Synty art. The map itself, its markers, the north mark and the zoom
    /// buttons stay the HUD's own (they are drawn over this); the dial is the frame round them, and it stands on
    /// the minimap's element by its glass, so the map fills the glass and the frame reaches out round it.
    /// </summary>
    public sealed class MinimapChromeView : HudChromePiece
    {
        private const string PanelName = "minimap";

        protected override VisualElement OnAttach(VisualElement hudRoot) => hudRoot.Q(PanelName);

        protected override void Mirror() { }
    }
}
