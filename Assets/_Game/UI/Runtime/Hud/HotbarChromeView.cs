using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Image = UnityEngine.UI.Image;
using Slider = UnityEngine.UI.Slider;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The Hotbar as the faction's Synty action bar: slots 1 to 0 with the XP bar, and the small Q W E R bar
    /// beside it. It decides nothing. Each frame it reads what the HUD's own elements show (the slot's icon, key,
    /// cooldown sweep and text, and its state classes; the XP bar) and shows the same on the prefab's parts, so
    /// whatever the Hotbar does, this shows. HP, Flux and the level are on <see cref="VitalsChromeView"/>.
    ///
    /// The UI Toolkit slots are laid exactly over the prefab's slots and stay what the player clicks, drags and
    /// drops on. The parts below are wired by FactionHudBuilder when it composes the prefab.
    /// </summary>
    public sealed class HotbarChromeView : HudChromePiece
    {
        [Serializable]
        public sealed class Slot
        {
            [Tooltip("The slot's own rectangle: the UI Toolkit slot is laid over it.")]
            public RectTransform area;
            public Image icon;
            [Tooltip("Shown while the skill is on cooldown.")]
            public GameObject cooldown;
            [Tooltip("Radial fill: 1 is a cooldown just started.")]
            public Image cooldownFill;
            [Tooltip("Seconds left, charge, or how many of an item are left.")]
            public TMP_Text count;
            public TMP_Text key;
            public GameObject selected, highlighted, disabled;
        }

        [SerializeField] private Slot[] slots = Array.Empty<Slot>();
        [SerializeField] private Slider experience;
        [SerializeField] private TMP_Text experienceLabel;
        [Tooltip("Where the middle of the row of slots 1 to 0 is across the piece (0 its left edge, 1 its right edge). That point is put on the screen's middle.")]
        [SerializeField, Range(0f, 1f)] private float mainCentre = 0.5f;
        [Tooltip("How wide the main bar (slots 1 to 0 with its ends) is, in the packs' units. Every faction's is drawn as wide as the others'.")]
        [SerializeField, Min(0f)] private float mainWidth;

        private const string DockName = "hotbar";
        private const float ChargeSwell = 1.08f;

        private VisualElement[] slotElements, slotIcons, slotSweeps;
        private Label[] slotKeys, slotRanks;
        private bool[] hovered;
        private ProgressBar experienceBar;
        private string shownExperience;
        private VisualElement holder;
        private Translate centring;

        public int SlotCount => slots.Length;
        /// <summary>How wide the main bar is drawn, in panel pixels.</summary>
        public float MainBarPanelWidth => mainWidth * UnitsToPanel;

        protected override VisualElement OnAttach(VisualElement hudRoot)
        {
            VisualElement dock = hudRoot.Q(DockName);
            if (dock == null) return null;
            // The stand-in's slots take the places of the prefab's slots.
            Canvas.ForceUpdateCanvases();
            var root = (RectTransform)transform;
            int count = slots.Length;
            slotElements = new VisualElement[count];
            slotIcons = new VisualElement[count];
            slotSweeps = new VisualElement[count];
            slotKeys = new Label[count];
            slotRanks = new Label[count];
            hovered = new bool[count];
            var corners = new Vector3[4];
            for (int i = 0; i < count; i++)
            {
                VisualElement element = hudRoot.Q("slot-" + i);
                slotElements[i] = element;
                if (element == null || slots[i].area == null) continue;
                slotIcons[i] = element.Q(className: "slot-icon");
                slotSweeps[i] = element.Q(className: "slot-cooldown");
                slotKeys[i] = element.Q<Label>(className: "slot-key");
                slotRanks[i] = element.Q<Label>(className: "slot-rank");

                slots[i].area.GetWorldCorners(corners);
                // From the piece's bottom-left corner.
                Vector2 corner = root.rect.min;
                Vector2 min = (Vector2)root.InverseTransformPoint(corners[0]) - corner, max = (Vector2)root.InverseTransformPoint(corners[2]) - corner;
                element.style.position = Position.Absolute;
                element.style.left = Length.Percent(100f * min.x / ComposedSize.x);
                element.style.top = Length.Percent(100f * (1f - max.y / ComposedSize.y));
                element.style.width = Length.Percent(100f * (max.x - min.x) / ComposedSize.x);
                element.style.height = Length.Percent(100f * (max.y - min.y) / ComposedSize.y);

                int index = i;
                element.RegisterCallback<PointerEnterEvent>(_ => hovered[index] = true);
                element.RegisterCallback<PointerLeaveEvent>(_ => hovered[index] = false);
            }

            experienceBar = hudRoot.Q<ProgressBar>("xp-bar");
            holder = dock.parent;
            centring = new Translate(Length.Percent(-100f * mainCentre), 0f);
            Centre();
            return dock;
        }

        protected override void OnDetach()
        {
            if (holder != null && holder.style.translate.keyword == StyleKeyword.Undefined && holder.style.translate.value == centring)
                holder.style.translate = StyleKeyword.Null;
            holder = null;
            if (slotElements == null) return;
            foreach (VisualElement element in slotElements)
            {
                if (element == null) continue;
                element.style.position = StyleKeyword.Null;
                element.style.left = StyleKeyword.Null;
                element.style.top = StyleKeyword.Null;
                element.style.width = StyleKeyword.Null;
                element.style.height = StyleKeyword.Null;
            }
        }

        /// <summary>
        /// The dock centres itself on its place by its own middle; the piece's middle is not the main bar's (the
        /// Q W E R bar hangs off one end). While the player has not placed the dock (the layout editor then sets its
        /// own translate, and clears it on a reset), the dock is shifted so that the main bar is what is centred.
        /// </summary>
        private void Centre()
        {
            if (holder != null && holder.style.translate.keyword == StyleKeyword.Null) holder.style.translate = centring;
        }

        protected override void Mirror()
        {
            Centre();
            for (int i = 0; i < slots.Length; i++)
            {
                VisualElement element = slotElements[i];
                if (element == null) continue;
                Slot slot = slots[i];

                Sprite art = slotIcons[i] != null && slotIcons[i].style.display != DisplayStyle.None ? slotIcons[i].style.backgroundImage.value.sprite : null;
                if (slot.icon != null)
                {
                    if (slot.icon.sprite != art) slot.icon.sprite = art;
                    if (slot.icon.enabled != (art != null)) slot.icon.enabled = art != null;
                }

                bool cooling = element.ClassListContains("on-cooldown");
                bool charging = element.ClassListContains("charging");
                float left = slotSweeps[i] != null && slotSweeps[i].style.height.keyword == StyleKeyword.Undefined ? slotSweeps[i].style.height.value.value / 100f : 0f;
                if (slot.cooldown != null && slot.cooldown.activeSelf != (cooling && left > 0f)) slot.cooldown.SetActive(cooling && left > 0f);
                if (slot.cooldownFill != null) slot.cooldownFill.fillAmount = left;

                // Under the key the old slot said its rank; here it says only what changes: seconds, charge, how many.
                string text = slotRanks[i] != null ? slotRanks[i].text : null;
                bool worthSaying = !string.IsNullOrEmpty(text) && (cooling || charging || text[0] == 'x');
                if (slot.count != null)
                {
                    string say = worthSaying ? text : string.Empty;
                    if (slot.count.text != say) slot.count.text = say;
                }

                if (slot.key != null && slotKeys[i] != null && slot.key.text != slotKeys[i].text) slot.key.text = slotKeys[i].text;

                Show(slot.selected, element.ClassListContains("selected"));
                Show(slot.highlighted, hovered[i] || element.ClassListContains("drop-target") || element.ClassListContains("drop-hover"));
                Show(slot.disabled, element.ClassListContains("insufficient-chi") || (cooling && left <= 0f));
                if (slot.icon != null)
                {
                    float swell = charging ? ChargeSwell : 1f;
                    if (!Mathf.Approximately(slot.icon.rectTransform.localScale.x, swell)) slot.icon.rectTransform.localScale = new Vector3(swell, swell, 1f);
                }
            }

            Fill(experience, experienceBar);
            if (experienceLabel != null && experienceBar != null && shownExperience != experienceBar.title)
            {
                shownExperience = experienceBar.title;
                experienceLabel.text = shownExperience;
            }
        }

        private static void Show(GameObject part, bool on)
        {
            if (part != null && part.activeSelf != on) part.SetActive(on);
        }
    }
}
