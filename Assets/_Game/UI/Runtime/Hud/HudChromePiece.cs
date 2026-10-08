using UnityEngine;
using UnityEngine.UIElements;
using Slider = UnityEngine.UI.Slider;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// One uGUI piece of a faction's HUD (composed from Synty's prefabs by FactionHudBuilder), standing on one UI
    /// Toolkit element. Every frame it puts itself on that element's rectangle and shows what the element shows.
    /// The root of a piece has the size it was composed at, with everything in it centred on its pivot.
    /// </summary>
    public abstract class HudChromePiece : MonoBehaviour
    {
        [Tooltip("How large the piece is drawn: panel pixels (of the 1280 x 720 interface) per canvas unit of the packs (drawn for 3840 x 2160; 0.333 is the size Synty shows them at). The builder sets it from the width the piece is given, so every faction's piece is as wide as the others.")]
        [SerializeField, Range(0.05f, 1f)] private float unitsToPanel = 0.3f;
        [Tooltip("The part of the piece that lies on the HUD's element, in the packs' units from the piece's middle. Empty: the whole piece. A dial stands on its element by its glass only, and its frame reaches out round it.")]
        [SerializeField] private Rect standOn;

        private RectTransform rect;
        private CanvasGroup group;
        private Vector2 composedSize;
        private Rect stand;

        /// <summary>The element this piece stands on.</summary>
        protected VisualElement Anchor { get; private set; }
        /// <summary>The size the piece was composed at, in the packs' canvas units.</summary>
        protected Vector2 ComposedSize => composedSize;
        /// <summary>The part of the piece that lies on the element, in the packs' units from the piece's middle.</summary>
        protected Rect Stand => stand;
        /// <summary>False once the element has left its panel (the HUD's tree was rebuilt).</summary>
        public bool IsAttached => Anchor != null && Anchor.panel != null;
        /// <summary>How wide the piece is drawn, in panel pixels.</summary>
        public float PanelWidth => ((RectTransform)transform).rect.width * unitsToPanel;
        /// <summary>The size of the HUD's element under the piece, in panel pixels.</summary>
        public Vector2 StandPanelSize => (standOn.width > 0f ? standOn.size : ((RectTransform)transform).rect.size) * unitsToPanel;
        /// <summary>Panel pixels per unit of the packs' canvas.</summary>
        protected float UnitsToPanel => unitsToPanel;

        /// <summary>
        /// Finds the piece's elements under the HUD's root and takes its place. False when the HUD has no place for it.
        /// </summary>
        public bool Attach(VisualElement hudRoot)
        {
            rect = (RectTransform)transform;
            composedSize = rect.rect.size;
            if (!TryGetComponent(out group)) group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            stand = standOn.width > 0f ? standOn : new Rect(-composedSize * 0.5f, composedSize);
            Anchor = OnAttach(hudRoot);
            if (Anchor == null) return false;
            // The stand-in takes the size of the part that stands on it, whatever room its parent has.
            Anchor.style.width = stand.width * unitsToPanel;
            Anchor.style.height = stand.height * unitsToPanel;
            Anchor.style.flexShrink = 0f;
            Anchor.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            Follow();
            return true;
        }

        public void Detach()
        {
            if (Anchor != null) Anchor.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            OnDetach();
            if (Anchor != null)
            {
                Anchor.style.width = StyleKeyword.Null;
                Anchor.style.height = StyleKeyword.Null;
                Anchor.style.flexShrink = StyleKeyword.Null;
            }
            Anchor = null;
        }

        /// <summary>True when a rectangle of the panel lies over this piece.</summary>
        public bool Covers(Rect panelRect)
        {
            return Anchor != null && group != null && group.alpha > 0f && Anchor.worldBound.Overlaps(panelRect);
        }

        /// <summary>Binds the piece's elements; returns the element it stands on, or null.</summary>
        protected abstract VisualElement OnAttach(VisualElement hudRoot);

        protected virtual void OnDetach() { }

        /// <summary>Shows what the elements show. Called every frame after the piece is placed.</summary>
        protected abstract void Mirror();

        private void OnGeometry(GeometryChangedEvent evt) => Follow();

        private void LateUpdate() => Tick();

        /// <summary>Takes the element's place and shows what it shows. Once a frame.</summary>
        public void Tick()
        {
            if (Anchor == null) return;
            Follow();
            Mirror();
        }

        private void Follow()
        {
            Rect pixels = default;
            bool seen = Anchor.panel != null && Shown() && HudChrome.TryScreenRect(Anchor, out pixels);
            group.alpha = seen ? 1f : 0f;
            if (!seen) return;
            // The canvas has no scaler: its units are screen pixels, and the piece's pivot is its centre.
            float scale = pixels.width / Mathf.Max(1f, stand.width);
            rect.position = new Vector3(pixels.center.x - stand.center.x * scale, pixels.center.y - stand.center.y * scale, 0f);
            rect.localScale = new Vector3(scale, scale, 1f);
        }

        /// <summary>Fills a pack's bar as far as the HUD's own bar is full.</summary>
        protected static void Fill(Slider slider, ProgressBar bar)
        {
            if (slider == null || bar == null) return;
            float share = Mathf.InverseLerp(bar.lowValue, bar.highValue, bar.value);
            if (!Mathf.Approximately(slider.normalizedValue, share)) slider.normalizedValue = share;
        }

        /// <summary>What a line says from its first digit on: "LEVEL 112" is "112", "HP 40 / 200" is "40 / 200".</summary>
        protected static string FromFirstDigit(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            int start = 0;
            while (start < text.Length && !char.IsDigit(text[start])) start++;
            return start < text.Length ? text.Substring(start) : text;
        }

        // An element hidden by a parent still has a size, but it is not drawn.
        private bool Shown()
        {
            for (VisualElement each = Anchor; each != null; each = each.parent)
                if (each.resolvedStyle.display == DisplayStyle.None || each.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }
    }
}
