using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The plate over a Hostile in the faction's own Synty art: the pack's enemy plate (level badge, health bar,
    /// name) with two lines of ours, one over it for a Bruiser and one under it for a Hostile another player holds
    /// the kill of. Composed by HostilePlateBuilder, one per faction, the same parts in each. A view only: what it
    /// says and where it stands is <see cref="HostileNameplateController"/>'s.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HostilePlateView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameLabel, levelLabel, tierLabel, takenLabel;
        [SerializeField] private Slider health;
        [Tooltip("Panel pixels (of the 1280 x 720 interface) one unit of this prefab is drawn as.")]
        [SerializeField] private float unitsToPanel = 0.25f;

        private RectTransform rect;

        /// <summary>How wide the badge and bar are drawn, in panel pixels: the same for every faction.</summary>
        public float PanelWidth => unitsToPanel * BarUnits;
        [SerializeField, HideInInspector] private float barUnits = 1f;
        private float BarUnits => barUnits;

        private void Awake() => rect = (RectTransform)transform;

        public void Say(string hostileName, int level)
        {
            if (nameLabel != null) nameLabel.text = hostileName;
            if (levelLabel != null) levelLabel.text = level.ToString();
        }

        public void Colour(Color nameColour)
        {
            if (nameLabel != null) nameLabel.color = nameColour;
        }

        public void Health(float share)
        {
            if (health != null) health.normalizedValue = share;
        }

        public void Tier(bool bruiser)
        {
            if (tierLabel != null && tierLabel.gameObject.activeSelf != bruiser) tierLabel.gameObject.SetActive(bruiser);
        }

        public void Taken(bool byAnother)
        {
            if (takenLabel != null && takenLabel.gameObject.activeSelf != byAnother) takenLabel.gameObject.SetActive(byAnother);
        }

        public void Show(bool on)
        {
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
        }

        /// <summary>
        /// Stands the plate on a point of the screen (pixels from the bottom-left corner), its foot in the middle.
        /// <paramref name="panelScale"/> is how many screen pixels a panel pixel is, so the plate follows the
        /// screen's size and the Interface size setting like the rest of the HUD.
        /// </summary>
        public void Place(Vector2 screenPixels, float panelScale)
        {
            if (rect == null) rect = (RectTransform)transform;
            rect.anchoredPosition = screenPixels;
            float scale = unitsToPanel * panelScale;
            rect.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
