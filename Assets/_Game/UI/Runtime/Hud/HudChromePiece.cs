using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// One uGUI piece of a faction's HUD (composed from Synty's prefabs by FactionHudBuilder), standing on one UI
    /// Toolkit element. Every frame it puts itself on that element's rectangle and shows what the element shows.
    /// The root of a piece has the size it was composed at, with everything in it centred on its pivot.
    /// </summary>
    public abstract class HudChromePiece : MonoBehaviour
    {
        private RectTransform rect;
        private CanvasGroup group;
        private Vector2 composedSize;

        /// <summary>The element this piece stands on.</summary>
        protected VisualElement Anchor { get; private set; }
        /// <summary>The size the piece was composed at, in the packs' canvas units.</summary>
        protected Vector2 ComposedSize => composedSize;
        /// <summary>False once the element has left its panel (the HUD's tree was rebuilt).</summary>
        public bool IsAttached => Anchor != null && Anchor.panel != null;

        /// <summary>
        /// Finds the piece's elements under the HUD's root and takes its place. <paramref name="unitsToPanel"/> is
        /// how many panel pixels one canvas unit of the piece becomes. False when the HUD has no place for it.
        /// </summary>
        public bool Attach(VisualElement hudRoot, float unitsToPanel)
        {
            rect = (RectTransform)transform;
            composedSize = rect.rect.size;
            if (!TryGetComponent(out group)) group = gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            Anchor = OnAttach(hudRoot, unitsToPanel);
            if (Anchor == null) return false;
            Anchor.RegisterCallback<GeometryChangedEvent>(OnGeometry);
            Follow();
            return true;
        }

        public void Detach()
        {
            if (Anchor != null) Anchor.UnregisterCallback<GeometryChangedEvent>(OnGeometry);
            OnDetach();
            Anchor = null;
        }

        /// <summary>True when a rectangle of the panel lies over this piece.</summary>
        public bool Covers(Rect panelRect)
        {
            return Anchor != null && group != null && group.alpha > 0f && Anchor.worldBound.Overlaps(panelRect);
        }

        /// <summary>Binds the piece's elements; returns the element it stands on, or null.</summary>
        protected abstract VisualElement OnAttach(VisualElement hudRoot, float unitsToPanel);

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
            rect.position = new Vector3(pixels.center.x, pixels.center.y, 0f);
            float scale = pixels.width / Mathf.Max(1f, composedSize.x);
            rect.localScale = new Vector3(scale, scale, 1f);
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
