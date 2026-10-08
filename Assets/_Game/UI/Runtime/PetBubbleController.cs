using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The speech bubbles of pets: a comic bubble (<see cref="ComicBubble"/>) beside a pet, with a line of text or an
    /// emoji in it. A bubble sits to the left of its pet and follows it; it never lies over the player, a namecard, a
    /// HUD panel (the HP bar among them) or the target and its marker: it is shifted or moved to the pet's other side
    /// instead (<see cref="PetBubblePlacement"/>). What a pet says, and when, is decided elsewhere; this only shows it.
    /// Purely local: nothing is sent for a bubble.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PetBubbleController : MonoBehaviour
    {
        private const string LayerName = "pet-bubbles";
        private const string NamecardLayer = "player-namecards";
        private const string ShownClass = "comic-bubble--shown";
        private const string TextClass = "pet-bubble-text";
        private const string EmojiClass = "pet-bubble-emoji";

        [Tooltip("Pixels of air between a pet and its bubble.")]
        [SerializeField, Min(0f)] private float gap = 8f;
        [Tooltip("Pixels a bubble keeps clear of everything it must not cover.")]
        [SerializeField, Min(0f)] private float margin = 6f;
        [Tooltip("How fast a bubble follows its pet across the screen (per second). Higher sticks closer.")]
        [SerializeField, Min(1f)] private float follow = 14f;
        [Tooltip("The box a character is taken to fill when bubbles keep off it: half its width, and how far it reaches below and above its root, in metres.")]
        [SerializeField] private Vector3 characterBox = new Vector3(0.45f, 1.05f, 0.95f);
        [Tooltip("Seconds a bubble takes to fade once its time is up.")]
        [SerializeField, Min(0f)] private float fadeSeconds = 0.18f;

        private sealed class Bubble
        {
            public ComicBubble Root;
            public Label Text;
            public VisualElement Emoji;
            public Transform Pet, Owner;
            public Renderer[] Renderers;
            public Bounds Body;
            public bool HasBody;
            public float Until;
            public Vector2 At;
            public bool Placed;
        }

        public static PetBubbleController Instance { get; private set; }

        private readonly List<Bubble> bubbles = new List<Bubble>(2);
        private readonly List<Rect> blocked = new List<Rect>(24);
        private readonly List<VisualElement> panels = new List<VisualElement>(24);
        private static readonly Vector3[] Corners = new Vector3[8];
        private VisualElement root, layer, namecards;
        private Camera targetCamera;
        private WorldMarkerPresenter markers;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            Instance = this;
            root = GetComponent<UIDocument>().rootVisualElement;
            layer = root?.Q(LayerName);
            if (layer == null) Debug.LogError("[Pets] ProgressionHUD.uxml has no '" + LayerName + "' element.", this);
            namecards = root?.Q(NamecardLayer);
        }

        private void OnDisable()
        {
            Clear();
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Shows a bubble beside a pet for a while: a line of text, or an emoji when <paramref name="emoji"/> is
        /// given. A pet has one bubble: what it says next replaces what it was saying. <paramref name="body"/> is
        /// the pet's own box in its transform's space; without it the bubble keeps clear of every mesh on the pet.
        /// </summary>
        public void Say(Transform pet, Transform owner, string text, Sprite emoji, float seconds, Bounds? body = null)
        {
            if (layer == null || pet == null || !HudPreferences.PetChatter) return;
            Bubble bubble = bubbles.Find(b => b.Pet == pet);
            if (bubble == null)
            {
                bubble = new Bubble { Root = new ComicBubble(), Text = new Label { pickingMode = PickingMode.Ignore }, Emoji = new VisualElement { pickingMode = PickingMode.Ignore } };
                bubble.Text.AddToClassList(TextClass);
                bubble.Emoji.AddToClassList(EmojiClass);
                bubble.Root.Add(bubble.Text);
                bubble.Root.Add(bubble.Emoji);
                layer.Add(bubble.Root);
                bubbles.Add(bubble);
            }
            bubble.Pet = pet;
            bubble.Owner = owner;
            bubble.HasBody = body.HasValue;
            bubble.Body = body.GetValueOrDefault();
            bubble.Renderers = bubble.HasBody ? null : pet.GetComponentsInChildren<Renderer>(false);
            bubble.Until = Time.unscaledTime + Mathf.Max(0.2f, seconds);
            bubble.Placed = false;
            bool showEmoji = emoji != null;
            bubble.Text.text = showEmoji ? string.Empty : text ?? string.Empty;
            bubble.Text.style.display = showEmoji ? DisplayStyle.None : DisplayStyle.Flex;
            bubble.Emoji.style.display = showEmoji ? DisplayStyle.Flex : DisplayStyle.None;
            bubble.Emoji.style.backgroundImage = showEmoji ? new StyleBackground(emoji) : StyleKeyword.None;
            bubble.Root.RemoveFromClassList(ShownClass); // it pops in again once it has been placed
        }

        /// <summary>True while this pet has a bubble up.</summary>
        public bool IsSpeaking(Transform pet) => pet != null && bubbles.Exists(b => b.Pet == pet && Time.unscaledTime < b.Until);

        public void Clear()
        {
            foreach (Bubble bubble in bubbles) bubble.Root.RemoveFromHierarchy();
            bubbles.Clear();
        }

        private void LateUpdate()
        {
            if (layer == null || root.panel == null || bubbles.Count == 0) return;
            if (!HudPreferences.PetChatter) { Clear(); return; }
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            float now = Time.unscaledTime;
            float ease = 1f - Mathf.Exp(-follow * Time.unscaledDeltaTime);
            var screen = new Rect(0f, 0f, root.resolvedStyle.width, root.resolvedStyle.height);
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                Bubble bubble = bubbles[i];
                if (bubble.Pet == null || now >= bubble.Until + fadeSeconds)
                {
                    bubble.Root.RemoveFromHierarchy();
                    bubbles.RemoveAt(i);
                    continue;
                }
                if (now >= bubble.Until) bubble.Root.RemoveFromClassList(ShownClass); // fading out, still following

                var size = new Vector2(bubble.Root.resolvedStyle.width, bubble.Root.resolvedStyle.height);
                if (float.IsNaN(size.x) || size.x < 1f || size.y < 1f) continue; // not laid out yet: next frame
                if (!TryRectOf(bubble, out Rect pet))
                {
                    bubble.Root.RemoveFromClassList(ShownClass); // the pet is behind the camera
                    bubble.Placed = false;
                    continue;
                }

                Gather(bubble);
                Rect place = PetBubblePlacement.Place(pet, size, blocked, screen, gap, margin, out bool tailOnRight, out _);
                bool flipped = bubble.Root.TailOnRight != tailOnRight;
                bubble.Root.TailOnRight = tailOnRight;
                bubble.At = bubble.Placed && !flipped ? Vector2.Lerp(bubble.At, place.position, ease) : place.position;
                bubble.Root.style.left = bubble.At.x;
                bubble.Root.style.top = bubble.At.y;
                if (!bubble.Placed)
                {
                    bubble.Placed = true;
                    if (now < bubble.Until) bubble.Root.AddToClassList(ShownClass);
                }
            }
        }

        // ---------------------------------------------------------------- what a bubble keeps clear of

        private void Gather(Bubble bubble)
        {
            blocked.Clear();
            // The player.
            if (bubble.Owner != null && TryRectOfCharacter(bubble.Owner, out Rect owner)) blocked.Add(owner);

            // The target and the marker under it.
            if (markers == null) markers = FindAnyObjectByType<WorldMarkerPresenter>();
            if (markers != null && markers.TryGetTargetMarker(out Transform marked, out Vector3 ring, out float diameter))
            {
                if (marked != null && TryRectOfCharacter(marked, out Rect target)) blocked.Add(target);
                var ringBounds = new Bounds(ring, new Vector3(diameter, 0.05f, diameter));
                if (TryRectOf(ringBounds, out Rect ringRect)) blocked.Add(ringRect);
            }

            // Namecards.
            if (namecards != null)
            {
                int count = namecards.childCount;
                for (int i = 0; i < count; i++) AddIfSeen(namecards[i]);
            }

            // The HUD's panels: the HP bar, the Hotbar, the chat, the map, any open window.
            panels.Clear();
            root.Query(className: "hud-movable").ToList(panels);
            foreach (VisualElement panel in panels) AddIfSeen(panel);
            panels.Clear();
            root.Query(className: "blocks-world").ToList(panels);
            foreach (VisualElement panel in panels) AddIfSeen(panel);
        }

        private void AddIfSeen(VisualElement element)
        {
            if (element == null || element.resolvedStyle.display == DisplayStyle.None || element.resolvedStyle.visibility == Visibility.Hidden
                || element.resolvedStyle.opacity <= 0.01f) return;
            Rect bound = element.worldBound;
            if (float.IsNaN(bound.width) || bound.width < 1f || bound.height < 1f) return;
            blocked.Add(root.WorldToLocal(bound));
        }

        // ---------------------------------------------------------------- the world on the panel

        private bool TryRectOf(Bubble bubble, out Rect rect)
        {
            // Its own box, turned as the pet is: closer to what is seen than a box squared to the world.
            if (bubble.HasBody) return TryRectOf(bubble.Pet.localToWorldMatrix, bubble.Body, out rect);
            bool any = false;
            Bounds bounds = default;
            foreach (Renderer each in bubble.Renderers)
            {
                // The body only: a pet's sparkles reach far beyond it.
                if (each == null || !each.enabled || !(each is MeshRenderer || each is SkinnedMeshRenderer)) continue;
                if (any) bounds.Encapsulate(each.bounds);
                else { bounds = each.bounds; any = true; }
            }
            if (!any) bounds = new Bounds(bubble.Pet.position, Vector3.one * 0.5f);
            return TryRectOf(bounds, out rect);
        }

        private bool TryRectOfCharacter(Transform character, out Rect rect)
        {
            float height = characterBox.y + characterBox.z;
            var bounds = new Bounds(character.position + Vector3.up * (characterBox.z - characterBox.y) * 0.5f,
                new Vector3(characterBox.x * 2f, height, characterBox.x * 2f));
            return TryRectOf(bounds, out rect);
        }

        private bool TryRectOf(Bounds bounds, out Rect rect) => TryRectOf(Matrix4x4.identity, bounds, out rect);

        /// <summary>The rectangle a box covers on the HUD, or false when any of it is behind the camera.</summary>
        private bool TryRectOf(Matrix4x4 toWorld, Bounds bounds, out Rect rect)
        {
            rect = default;
            Vector3 min = bounds.min, max = bounds.max;
            Corners[0] = new Vector3(min.x, min.y, min.z); Corners[1] = new Vector3(max.x, min.y, min.z);
            Corners[2] = new Vector3(min.x, max.y, min.z); Corners[3] = new Vector3(max.x, max.y, min.z);
            Corners[4] = new Vector3(min.x, min.y, max.z); Corners[5] = new Vector3(max.x, min.y, max.z);
            Corners[6] = new Vector3(min.x, max.y, max.z); Corners[7] = new Vector3(max.x, max.y, max.z);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 screen = targetCamera.WorldToScreenPoint(toWorld.MultiplyPoint3x4(Corners[i]));
                if (screen.z <= 0.01f) return false;
                Vector2 point = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
                if (point.x < x0) x0 = point.x;
                if (point.x > x1) x1 = point.x;
                if (point.y < y0) y0 = point.y;
                if (point.y > y1) y1 = point.y;
            }
            rect = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }
    }
}
