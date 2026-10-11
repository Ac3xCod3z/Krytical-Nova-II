using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The namecard over a player in the faction's own Synty art: the pack's level badge, the name in the pack's
    /// lettering with the faction's crest before it, the Standing over it and the Crew under it, all on a panel of
    /// the pack's own (its small box and frame) that is as large as what the card says. The pack's health
    /// bar shows only where a player's health is the viewer's business (an enemy, a Squad member, the target):
    /// with it the name stands over the badge and bar, without it the badge, crest and name make one row.
    /// Composed by PlayerCardBuilder, one per faction, the same parts in each. A view only: what it says and where
    /// it stands is <see cref="PlayerNamecardController"/>'s.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCardView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameLabel, levelLabel, standingLabel, crewLabel;
        [SerializeField] private Image crest;
        [Tooltip("The Crew's symbol, before the Crew's name.")]
        [SerializeField] private Image crewSymbol;
        [SerializeField] private Slider health;
        [Tooltip("The pack's bar without its badge: switched off where health is not shown.")]
        [SerializeField] private GameObject bar;
        [SerializeField] private RectTransform badge;
        [Tooltip("The pack's panel behind the card: it takes the size of what the card says.")]
        [SerializeField] private RectTransform back;
        [Tooltip("Panel pixels (of the 1280 x 720 interface) one unit of this prefab is drawn as.")]
        [SerializeField] private float unitsToPanel = 0.25f;
        [SerializeField, HideInInspector] private float barUnits = 1f;

        // Measured by the builder, in this prefab's units: x from the middle, y up from the card's foot.
        [SerializeField, HideInInspector] private Vector2 badgeRest;   // where the pack put the badge (its own anchored position)
        [SerializeField, HideInInspector] private float badgeCentreX;  // the badge's middle while the bar shows
        [SerializeField, HideInInspector] private float badgeWidth, badgeHeight;
        [SerializeField, HideInInspector] private float rowY;          // the middle of the badge, and of the row without a bar
        [SerializeField, HideInInspector] private float barTop;        // the top of the badge and bar
        [SerializeField, HideInInspector] private float gap;
        [SerializeField, HideInInspector] private Vector2 backPadding; // the panel's margin round what the card says

        private RectTransform rect;
        private bool barShown = true, stale = true;

        /// <summary>How wide the badge and bar are drawn, in panel pixels: the same for every faction.</summary>
        public float PanelWidth => unitsToPanel * barUnits;

        private void Awake() => rect = (RectTransform)transform;

        public void Say(string playerName, int level)
        {
            if (nameLabel != null) nameLabel.text = playerName;
            if (levelLabel != null) levelLabel.text = level.ToString();
            stale = true;
        }

        public void Colour(Color nameColour)
        {
            if (nameLabel != null) nameLabel.color = nameColour;
        }

        public void Standing(string standing)
        {
            Line(standingLabel, standing);
            stale = true;
        }

        public void Crew(string crew)
        {
            Line(crewLabel, crew);
            stale = true;
        }

        private static void Line(TMP_Text label, string text)
        {
            if (label == null) return;
            bool any = !string.IsNullOrEmpty(text);
            if (any) label.text = text;
            if (label.gameObject.activeSelf != any) label.gameObject.SetActive(any);
        }

        /// <summary>The Crew's symbol before its name, or (with no picture) none.</summary>
        public void CrewSymbol(Sprite picture)
        {
            if (crewSymbol == null) return;
            if (picture != null) crewSymbol.sprite = picture;
            if (crewSymbol.gameObject.activeSelf != (picture != null)) crewSymbol.gameObject.SetActive(picture != null);
            stale = true;
        }

        public void Crest(Sprite picture)
        {
            if (crest == null) return;
            crest.sprite = picture;
            if (crest.gameObject.activeSelf != (picture != null)) crest.gameObject.SetActive(picture != null);
            stale = true;
        }

        /// <summary>Shows the health bar with this share, or (with <paramref name="shown"/> false) no bar.</summary>
        public void Health(bool shown, float share)
        {
            if (shown && health != null) health.normalizedValue = share;
            if (shown == barShown) return;
            barShown = shown;
            if (bar != null) bar.SetActive(shown);
            stale = true;
        }

        public void Show(bool on)
        {
            if (gameObject.activeSelf != on) gameObject.SetActive(on);
        }

        /// <summary>
        /// Stands the card on a point of the screen (pixels from the bottom-left corner), its foot in the middle.
        /// <paramref name="panelScale"/> is how many screen pixels a panel pixel is.
        /// </summary>
        public void Place(Vector2 screenPixels, float panelScale)
        {
            if (rect == null) rect = (RectTransform)transform;
            if (stale) Lay();
            rect.anchoredPosition = screenPixels;
            float scale = unitsToPanel * panelScale;
            rect.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Puts the badge, crest, name and Standing where this name's length and the bar leave them.</summary>
        private void Lay()
        {
            stale = false;
            if (nameLabel == null || badge == null) return;
            if (rect == null) rect = (RectTransform)transform;
            float half = rect.rect.height * 0.5f; // the parts are placed from the root's middle
            RectTransform nameRect = nameLabel.rectTransform;
            float nameWidth = nameLabel.preferredWidth, nameHeight = nameRect.sizeDelta.y;
            bool crested = crest != null && crest.gameObject.activeSelf;
            float crestWidth = crested ? crest.rectTransform.sizeDelta.x : 0f;
            float crestGap = crested ? gap : 0f;

            float left, y, standingAt, halfWidth;
            if (barShown)
            {
                // The name, with its crest, is centred over the badge and bar.
                badge.anchoredPosition = badgeRest;
                float pair = crestWidth + crestGap + nameWidth;
                left = -pair * 0.5f;
                y = barTop + nameHeight * 0.5f + gap * 0.3f;
                standingAt = y + nameHeight * 0.5f;
                halfWidth = Mathf.Max(barUnits, pair) * 0.5f;
            }
            else
            {
                // One row: badge, crest, name, centred as a whole.
                float total = badgeWidth + gap + crestWidth + crestGap + nameWidth;
                float rowLeft = -total * 0.5f;
                badge.anchoredPosition = badgeRest + new Vector2(rowLeft + badgeWidth * 0.5f - badgeCentreX, 0f);
                left = rowLeft + badgeWidth + gap;
                y = rowY;
                standingAt = rowY + Mathf.Max(badgeHeight, nameHeight) * 0.5f;
                halfWidth = total * 0.5f;
            }
            if (crested) crest.rectTransform.anchoredPosition = new Vector2(left + crestWidth * 0.5f, y - half);
            nameRect.anchoredPosition = new Vector2(left + crestWidth + crestGap + nameWidth * 0.5f, y - half);
            if (standingLabel != null) standingLabel.rectTransform.anchoredPosition = new Vector2(0f, standingAt - half);

            if (back == null) return;
            // The panel goes round everything that shows: the row or the bar, and the lines over and under it.
            bool standing = standingLabel != null && standingLabel.gameObject.activeSelf;
            bool crew = crewLabel != null && crewLabel.gameObject.activeSelf;
            float top = standing ? standingAt + standingLabel.rectTransform.sizeDelta.y : standingAt;
            float bottom = crew ? 0f : rowY - badgeHeight * 0.5f;
            if (standing) halfWidth = Mathf.Max(halfWidth, standingLabel.preferredWidth * 0.5f);
            if (crew)
            {
                // The symbol and the Crew's name are centred as a pair.
                RectTransform crewRect = crewLabel.rectTransform;
                bool symbol = crewSymbol != null && crewSymbol.gameObject.activeSelf;
                float symbolWidth = symbol ? crewSymbol.rectTransform.sizeDelta.x : 0f;
                float symbolGap = symbol ? gap * 0.6f : 0f;
                float crewWidth = crewLabel.preferredWidth, pair = symbolWidth + symbolGap + crewWidth;
                crewRect.anchoredPosition = new Vector2((symbolWidth + symbolGap) * 0.5f, crewRect.anchoredPosition.y);
                if (symbol)
                    crewSymbol.rectTransform.anchoredPosition = new Vector2(-pair * 0.5f + symbolWidth * 0.5f, crewRect.anchoredPosition.y - crewLabel.fontSize * 0.62f);
                halfWidth = Mathf.Max(halfWidth, pair * 0.5f);
            }
            back.sizeDelta = new Vector2(halfWidth * 2f + backPadding.x * 2f, top - bottom + backPadding.y * 2f);
            back.anchoredPosition = new Vector2(0f, (top + bottom) * 0.5f - half);
        }
    }
}
