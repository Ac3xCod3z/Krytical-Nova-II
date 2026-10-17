using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The game's opening (owner, 16 Oct 2026; Unity's own splash is off): on a dark field the Ascension Studios crest
    /// fades in and out, then the Krytical Nova logo comes in with a red and cyan glitch, the split its art is drawn with,
    /// holds, and the whole layer fades to show the title underneath. It plays over the Title scene the first time that
    /// scene opens after the game starts, never when a player comes back to the title. Any key or click skips it, and
    /// the title then waits its own moment before it takes input, so the skipping key does not also continue.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OpeningIntro : MonoBehaviour
    {
        [Tooltip("The studio's crest, first.")]
        [SerializeField] private Sprite studioLogo;
        [Tooltip("The game's name logo, second.")]
        [SerializeField] private Sprite nameLogo;
        [Tooltip("The field both logos stand on.")]
        [SerializeField] private Color field = new Color32(0x0B, 0x0A, 0x14, 0xFF);
        [Tooltip("The crest's height, as a share of the screen's.")]
        [SerializeField, Range(0.2f, 1f)] private float studioHeight = 0.62f;
        [Tooltip("The name logo's width, as a share of the screen's.")]
        [SerializeField, Range(0.2f, 1f)] private float nameWidth = 0.6f;
        [SerializeField, Min(0f)] private float fadeSeconds = 0.6f;
        [SerializeField, Min(0f)] private float holdSeconds = 1.7f;
        [SerializeField, Min(0f)] private float glitchSeconds = 0.55f;
        [Tooltip("How far the red and cyan copies jump at the start of the glitch, at 1080 lines.")]
        [SerializeField, Min(0f)] private float glitchPixels = 22f;

        private const float ReferenceHeight = 1080f;
        private static readonly Color Red = new Color(1f, 0.16f, 0.27f, 1f);
        private static readonly Color Cyan = new Color(0.25f, 0.95f, 1f, 1f);

        /// <summary>True while the opening covers the title.</summary>
        public static bool Playing { get; private set; }

        private static bool played;

        private GameObject layer;
        private CanvasGroup group;
        private Image studio, title, red, cyan;
        private bool skip;

        // Enter Play Mode runs without a domain reload: every Play is a fresh start of the game.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Playing = false;
            played = false;
        }

        private void Awake()
        {
            if (played || studioLogo == null || nameLogo == null)
            {
                enabled = false;
                return;
            }
            played = true;
            Playing = true;
            Build();
        }

        private void Start()
        {
            if (Playing && layer != null) StartCoroutine(Run());
        }

        private void Update()
        {
            if (!Playing || skip) return;
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if ((keyboard != null && keyboard.anyKey.wasPressedThisFrame) ||
                (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)))
                skip = true;
        }

        private void OnDestroy()
        {
            if (layer != null) Playing = false;
        }

        // ---------------------------------------------------------------- the layer

        private void Build()
        {
            layer = new GameObject("Opening Intro", typeof(RectTransform));
            layer.layer = LayerMask.NameToLayer("UI");
            layer.transform.SetParent(transform, false);
            var canvas = layer.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 2000; // over the menus' page turn (1000)
            var scaler = layer.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, ReferenceHeight);
            scaler.matchWidthOrHeight = 1f;
            layer.AddComponent<GraphicRaycaster>(); // swallows clicks meant for the title underneath
            group = layer.AddComponent<CanvasGroup>();

            Image backdrop = Picture("Field", null, Vector2.zero);
            var full = (RectTransform)backdrop.transform;
            full.anchorMin = Vector2.zero;
            full.anchorMax = Vector2.one;
            full.sizeDelta = Vector2.zero;
            backdrop.color = field;
            backdrop.raycastTarget = true;

            float screenAspect = 1920f / ReferenceHeight;
            float studioH = ReferenceHeight * studioHeight;
            studio = Picture("Studio Logo", studioLogo, new Vector2(studioH * Aspect(studioLogo), studioH));
            float nameW = ReferenceHeight * screenAspect * nameWidth;
            Vector2 nameSize = new Vector2(nameW, nameW / Aspect(nameLogo));
            cyan = Picture("Name Logo (cyan)", nameLogo, nameSize);
            red = Picture("Name Logo (red)", nameLogo, nameSize);
            title = Picture("Name Logo", nameLogo, nameSize);
            SetAlpha(studio, 0f);
            SetAlpha(title, 0f);
            SetAlpha(red, 0f);
            SetAlpha(cyan, 0f);
        }

        private Image Picture(string label, Sprite sprite, Vector2 size)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.layer = layer.layer;
            go.transform.SetParent(layer.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private static float Aspect(Sprite sprite) => sprite.rect.height > 0f ? sprite.rect.width / sprite.rect.height : 1f;

        // ---------------------------------------------------------------- the sequence

        private IEnumerator Run()
        {
            yield return null; // the title's first frame is drawn under the field
            yield return Tween(fadeSeconds, t =>
            {
                SetAlpha(studio, t);
                studio.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, t);
            });
            yield return Tween(holdSeconds, t => studio.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.02f, t));
            yield return Tween(fadeSeconds, t => SetAlpha(studio, 1f - t));
            yield return Tween(0.25f, _ => { });

            yield return Tween(glitchSeconds, Glitch);
            if (!skip) Glitch(1f);
            yield return Tween(holdSeconds, t => title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.015f, t));

            // Out: the logo goes first, on the dark page, and only then the page lifts and the title is there (owner,
            // 16 Oct 2026: the cover never shows behind a logo). A skip does both quickly.
            bool skipped = skip;
            skip = false; // the last fades always run
            float logoFrom = title.color.a, studioFrom = studio.color.a;
            yield return Tween(skipped ? 0.15f : fadeSeconds, t =>
            {
                SetAlpha(title, Mathf.Lerp(logoFrom, 0f, t));
                SetAlpha(studio, Mathf.Lerp(studioFrom, 0f, t));
                SetAlpha(red, 0f);
                SetAlpha(cyan, 0f);
            });
            yield return Tween(skipped ? 0.2f : fadeSeconds, t => group.alpha = 1f - t);
            Playing = false;
            Destroy(layer);
            layer = null;
        }

        // The logo arrives in flickers, its red and cyan copies jumping beside it and settling into place.
        private void Glitch(float t)
        {
            float settle = 1f - t;
            bool flicker = t < 0.7f && UnityEngine.Random.value < 0.35f;
            SetAlpha(title, flicker ? UnityEngine.Random.Range(0.15f, 0.6f) : Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, t * 1.6f)));
            float jump = glitchPixels * settle;
            Place(red, new Vector2(-jump * UnityEngine.Random.Range(0.4f, 1f), UnityEngine.Random.Range(-0.3f, 0.3f) * jump), settle);
            Place(cyan, new Vector2(jump * UnityEngine.Random.Range(0.4f, 1f), UnityEngine.Random.Range(-0.3f, 0.3f) * jump), settle);
            title.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, Mathf.SmoothStep(0f, 1f, t));
        }

        private static void Place(Image copy, Vector2 offset, float strength)
        {
            copy.rectTransform.anchoredPosition = offset;
            SetAlpha(copy, 0.75f * strength);
        }

        // Runs a step every frame for the given time (t from 0 to 1); a skip ends it at once.
        private IEnumerator Tween(float seconds, Action<float> step)
        {
            float start = Time.unscaledTime;
            while (!skip)
            {
                float t = seconds > 0f ? Mathf.Clamp01((Time.unscaledTime - start) / seconds) : 1f;
                step(t);
                if (t >= 1f) yield break;
                yield return null;
            }
        }

        private static void SetAlpha(Image image, float alpha)
        {
            Color c = image.color;
            c.a = alpha;
            image.color = c;
        }

        private void LateUpdate()
        {
            // The copies keep their tint whatever the glitch set last.
            if (red != null) red.color = new Color(Red.r, Red.g, Red.b, red.color.a);
            if (cyan != null) cyan.color = new Color(Cyan.r, Cyan.g, Cyan.b, cyan.color.a);
        }
    }
}
