using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Slider = UnityEngine.UI.Slider;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The player's plate in the corner of the screen, in the faction's Synty art: a live picture of the character
    /// (<see cref="HudChrome.Portrait"/>), the level, and the HP and Flux bars with their numbers. Under them, the
    /// two lines the old plate carried: how the character is moving, and the points left to spend. It shows what
    /// the HUD's own status panel shows and stands on that panel, which the layout editor moves and sizes.
    /// </summary>
    public sealed class VitalsChromeView : HudChromePiece
    {
        [SerializeField] private RawImage portrait;
        [SerializeField] private Slider health, flux;
        [SerializeField] private TMP_Text healthLabel, fluxLabel, level, mode, points;
        [Tooltip("How the level is written: {0} is the number.")]
        [SerializeField] private string levelFormat = "{0}";

        private const string PanelName = "summary";

        private ProgressBar healthBar, fluxBar;
        private Label levelLabel, modeLabel, pointsLabel;
        private string shownHealth, shownFlux, shownLevel, shownMode, shownPoints;

        protected override VisualElement OnAttach(VisualElement hudRoot)
        {
            VisualElement panel = hudRoot.Q(PanelName);
            if (panel == null) return null;
            healthBar = hudRoot.Q<ProgressBar>("hp-bar");
            fluxBar = hudRoot.Q<ProgressBar>("chi-bar");
            levelLabel = hudRoot.Q<Label>("level-label");
            modeLabel = hudRoot.Q<Label>("mode-label");
            pointsLabel = hudRoot.Q<Label>("points-summary");
            shownHealth = shownFlux = shownLevel = shownMode = shownPoints = null;
            return panel;
        }

        protected override void Mirror()
        {
            if (portrait != null)
            {
                Texture picture = HudChrome.Portrait;
                if (portrait.texture != picture) portrait.texture = picture;
                if (portrait.enabled != (picture != null)) portrait.enabled = picture != null;
            }

            Fill(health, healthBar);
            Fill(flux, fluxBar);
            // The old bars said "HP 40 / 200" on themselves; the pack's bars are coloured and say the numbers.
            if (healthBar != null) Say(healthLabel, ref shownHealth, healthBar.title, true);
            if (fluxBar != null) Say(fluxLabel, ref shownFlux, fluxBar.title, true);
            if (levelLabel != null && level != null && shownLevel != levelLabel.text)
            {
                shownLevel = levelLabel.text;
                level.text = string.Format(levelFormat, FromFirstDigit(shownLevel));
            }
            if (modeLabel != null) Say(mode, ref shownMode, modeLabel.text, false);
            if (pointsLabel != null) Say(points, ref shownPoints, pointsLabel.text, false);
        }

        private static void Say(TMP_Text label, ref string shown, string text, bool numbersOnly)
        {
            if (label == null || shown == text) return;
            shown = text;
            label.text = numbersOnly ? FromFirstDigit(text) : text;
        }
    }
}
