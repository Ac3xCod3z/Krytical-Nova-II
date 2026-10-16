using System.Collections.Generic;
using KryticalNova.World;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KryticalNova.UI.Travel
{
    /// <summary>
    /// The screen shown while travelling between maps, drawn like a comic page: the destination region's concept
    /// painting in an ink panel, a black title strip ("NOW ENTERING ...") above it, a red strip with the level band
    /// and PvP rule below, a sound-effect burst, and in the bottom-right corner a yellow caption box with the lore.
    /// Clicking the caption flips the page to the next painting and its lore (once every <see cref="flipCooldown"/>
    /// seconds, so it cannot be spammed): the map's own region first, then the other regions
    /// (<see cref="ZoneCatalog.BuildDeck"/>). Every map scene carries one; the first lives on across loads and the
    /// others remove themselves, so the screen stays up while the old map unloads and the new one starts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TravelScreen : MonoBehaviour
    {
        private static readonly Color Ink = new Color32(14, 14, 16, 255);
        private static readonly Color Paper = new Color32(250, 246, 234, 255);
        private static readonly Color Caption = new Color32(255, 222, 74, 255);
        private static readonly Color Pow = new Color32(215, 38, 61, 255);

        [Tooltip("Headings, the burst word and the caption title (Bangers).")]
        [SerializeField] private Font titleFont;
        [Tooltip("The lore text.")]
        [SerializeField] private Font bodyFont;
        [SerializeField, Min(0.5f)] private float flipCooldown = 1.5f;
        [Tooltip("The screen stays at least this long, so the first page can be read.")]
        [SerializeField, Min(0f)] private float minimumSeconds = 3.5f;
        [Tooltip("After the new map is ready the screen waits this long past the last flip before it fades.")]
        [SerializeField, Min(0f)] private float lingerAfterFlip = 2.5f;
        [Tooltip("However much the player flips, the screen goes this long after the new map is ready.")]
        [SerializeField, Min(1f)] private float maximumLinger = 15f;

        private static TravelScreen living;

        private TMP_FontAsset titleAsset, bodyAsset;
        private CanvasGroup group;
        private RectTransform artFrame, caption, burst, progressFill;
        private RawImage artBack, artFront;
        private TextMeshProUGUI entering, title, region, band, burstWord, captionTitle, captionLore, captionPage, captionHint;
        private Image captionFill;

        private readonly List<TravelPage> deck = new List<TravelPage>();
        private int page;
        private float shownAt, lastFlip = -99f, arrivedAt = -1f;
        private float flipT = -1f, fadeT, artBlend = 1f;
        private bool visible, swapped;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => living = null;

        private void Awake()
        {
            if (living != null && living != this)
            {
                Destroy(gameObject);
                return;
            }
            living = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            Build();
            SetVisible(false);
            ZoneTravel.Departing += OnDeparting;
            ZoneTravel.Ended += OnEnded;
        }

        private void OnDestroy()
        {
            if (living != this) return;
            living = null;
            ZoneTravel.Departing -= OnDeparting;
            ZoneTravel.Ended -= OnEnded;
        }

        // ------------------------------------------------------------------ trip

        private void OnDeparting(ZoneEntry zone, ZoneCatalog catalog)
        {
            deck.Clear();
            if (catalog != null) deck.AddRange(catalog.BuildDeck(zone));
            ZoneRegion home = catalog != null ? catalog.Region(zone.region) : null;

            title.text = zone.displayName.ToUpperInvariant();
            region.text = home != null ? $"{home.name} · {home.faction}".ToUpperInvariant() : "NEUTRAL GROUND";
            band.text = $"{zone.LevelBand} · {RuleText(zone.pvp)}".ToUpperInvariant();
            burstWord.text = BurstWord(zone.kind);
            entering.text = zone.kind == ZoneKind.Run ? "NOW DESCENDING INTO" : "NOW ENTERING";

            page = 0;
            ShowPage(immediate: true);
            shownAt = Time.unscaledTime;
            arrivedAt = -1f;
            lastFlip = -99f;
            SetVisible(true);
        }

        private void OnEnded(ZoneEntry arrived)
        {
            // A failed trip closes at once; a finished one waits for the reader.
            arrivedAt = Time.unscaledTime;
            if (arrived == null) minimumSecondsOverride = 0f;
        }

        private float minimumSecondsOverride = -1f;

        private void Update()
        {
            float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;

            // Fade.
            fadeT = Mathf.MoveTowards(fadeT, visible ? 1f : 0f, dt / (visible ? 0.3f : 0.5f));
            group.alpha = fadeT;
            if (!visible && fadeT <= 0f) { if (group.gameObject.activeSelf) group.gameObject.SetActive(false); return; }

            // Indeterminate loading bar until the map is ready, then full.
            float progress = arrivedAt >= 0f ? 1f : 0.15f + 0.7f * (1f - Mathf.Exp(-(now - shownAt) * 0.6f));
            progressFill.anchorMax = new Vector2(Mathf.MoveTowards(progressFill.anchorMax.x, progress, dt * 1.5f), 1f);

            // Gentle drift of the painting and the burst's throb.
            artFrame.localScale = Vector3.one * (1.0f + 0.012f * Mathf.Sin((now - shownAt) * 0.35f));
            burst.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(now * 5f));

            // Painting crossfade.
            if (artBlend < 1f)
            {
                artBlend = Mathf.MoveTowards(artBlend, 1f, dt / 0.35f);
                artFront.color = new Color(1f, 1f, 1f, artBlend);
                if (artBlend >= 1f) artBack.texture = artFront.texture;
            }

            // Caption flip: squeeze, swap, spring back.
            if (flipT >= 0f)
            {
                flipT += dt;
                const float half = 0.14f;
                float sx = flipT < half ? 1f - flipT / half : Spring((flipT - half) / 0.26f);
                if (!swapped && flipT >= half) { swapped = true; ShowPage(immediate: false); }
                if (flipT >= half + 0.26f) { flipT = -1f; sx = 1f; }
                caption.localScale = new Vector3(Mathf.Max(0.001f, sx), caption.localScale.y, 1f);
            }

            bool ready = now - lastFlip >= flipCooldown && deck.Count > 1;
            captionHint.alpha = ready ? 1f : 0.35f;

            // Close once the new map is ready and the reader has had their time.
            if (visible && arrivedAt >= 0f)
            {
                float minimum = minimumSecondsOverride >= 0f ? minimumSecondsOverride : minimumSeconds;
                bool longEnough = now - shownAt >= minimum && now - lastFlip >= lingerAfterFlip;
                if (longEnough || now - arrivedAt >= maximumLinger)
                {
                    SetVisible(false);
                    minimumSecondsOverride = -1f;
                }
            }
        }

        private void Flip()
        {
            float now = Time.unscaledTime;
            if (!visible || deck.Count < 2 || now - lastFlip < flipCooldown || flipT >= 0f) return;
            lastFlip = now;
            page = (page + 1) % deck.Count;
            flipT = 0f;
            swapped = false;
        }

        private void ShowPage(bool immediate)
        {
            if (deck.Count == 0) return;
            TravelPage p = deck[page];
            captionTitle.text = (p.Title ?? string.Empty).ToUpperInvariant();
            captionLore.text = p.Lore ?? string.Empty;
            captionPage.text = $"PAGE {page + 1} / {deck.Count}";
            captionHint.text = deck.Count > 1 ? "CLICK TO FLIP »" : string.Empty;
            FitArt(p.Art);
            if (immediate || artBack.texture == null)
            {
                artBack.texture = p.Art;
                artFront.texture = p.Art;
                artFront.color = Color.white;
                artBlend = 1f;
            }
            else
            {
                artFront.texture = p.Art;
                artFront.color = new Color(1f, 1f, 1f, 0f);
                artBlend = 0f;
            }
            caption.localRotation = Quaternion.Euler(0f, 0f, page % 2 == 0 ? -1.6f : 1.2f);
        }

        /// <summary>The panel takes the painting's shape inside the space between the strips.</summary>
        private void FitArt(Texture art)
        {
            const float maxW = 1780f, maxH = 700f;
            float aspect = art != null && art.height > 0 ? (float)art.width / art.height : maxW / maxH;
            float w = maxW, h = maxW / aspect;
            if (h > maxH) { h = maxH; w = maxH * aspect; }
            artFrame.sizeDelta = new Vector2(w + 28f, h + 28f);
        }

        private void SetVisible(bool show)
        {
            visible = show;
            if (show) group.gameObject.SetActive(true);
            group.blocksRaycasts = show;
            group.interactable = show;
        }

        private static float Spring(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f + Mathf.Sin(t * Mathf.PI) * 0.12f * (1f - t) - (1f - t) * (1f - t) * (1f - t);
        }

        private static string RuleText(ZonePvpRule rule) => rule switch
        {
            ZonePvpRule.Neutral => "Neutral ground",
            ZonePvpRule.AlwaysOn => "Always-on PvP",
            _ => "Faction war zone",
        };

        private static string BurstWord(ZoneKind kind) => kind switch
        {
            ZoneKind.Run => "DOWN!",
            ZoneKind.HomeCity => "HOME!",
            ZoneKind.Scar => "WAR!",
            ZoneKind.StarterTown => "BACK!",
            _ => "WHOOSH!",
        };

        // ------------------------------------------------------------------ the page

        private void Build()
        {
            titleAsset = MakeFont(titleFont);
            bodyAsset = MakeFont(bodyFont) ?? titleAsset;

            var root = new GameObject("Travel Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            root.transform.SetParent(transform, false);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            group = root.GetComponent<CanvasGroup>();
            var page = (RectTransform)root.transform;

            // Ink ground and halftone dots.
            Stretch(Img("Ink", page, Ink));
            var dots = new GameObject("Halftone", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            dots.transform.SetParent(page, false);
            Stretch(dots.rectTransform);
            dots.texture = DotTexture();
            dots.uvRect = new Rect(0f, 0f, 1920f / 22f, 1080f / 22f);
            dots.color = new Color(1f, 1f, 1f, 0.07f);
            dots.raycastTarget = false;

            // The painting: ink frame, paper border, two layers for the crossfade.
            artFrame = Img("Panel", page, Ink).rectTransform;
            artFrame.anchoredPosition = new Vector2(0f, 6f);
            artFrame.localRotation = Quaternion.Euler(0f, 0f, 0.7f);
            var paper = Img("Paper", artFrame, Paper).rectTransform;
            Inset(paper, 6f);
            var window = Img("Window", paper, Ink).rectTransform;
            Inset(window, 8f);
            window.gameObject.AddComponent<RectMask2D>();
            artBack = Raw("Art Back", window);
            artFront = Raw("Art Front", window);

            // Top strip: black band, yellow edge, the title.
            var top = Img("Title Strip", page, Ink).rectTransform;
            Anchor(top, new Vector2(0.5f, 1f), new Vector2(2100f, 196f), new Vector2(0f, -122f), -1.8f);
            var topEdge = Img("Edge", top, Caption).rectTransform;
            topEdge.anchorMin = new Vector2(0f, 0f); topEdge.anchorMax = new Vector2(1f, 0f);
            topEdge.sizeDelta = new Vector2(0f, 10f); topEdge.anchoredPosition = new Vector2(0f, 5f);
            entering = Text("Entering", top, titleAsset, 34f, Caption, TextAlignmentOptions.TopLeft);
            Place(entering.rectTransform, new Vector2(150f, -30f), new Vector2(900f, 44f), new Vector2(0f, 1f));
            title = Text("Title", top, titleAsset, 104f, Paper, TextAlignmentOptions.BottomLeft);
            Place(title.rectTransform, new Vector2(150f, 12f), new Vector2(1500f, 120f), new Vector2(0f, 0f));
            title.outlineWidth = 0.18f; title.outlineColor = Ink;
            region = Text("Region", top, titleAsset, 36f, Caption, TextAlignmentOptions.BottomRight);
            Place(region.rectTransform, new Vector2(-150f, 22f), new Vector2(800f, 48f), new Vector2(1f, 0f));

            // Bottom strip: red band, the level band and rule, the loading bar.
            var bottom = Img("Band Strip", page, Pow).rectTransform;
            Anchor(bottom, new Vector2(0.5f, 0f), new Vector2(2100f, 130f), new Vector2(0f, 92f), 1.2f);
            var bottomEdge = Img("Edge", bottom, Ink).rectTransform;
            bottomEdge.anchorMin = new Vector2(0f, 1f); bottomEdge.anchorMax = new Vector2(1f, 1f);
            bottomEdge.sizeDelta = new Vector2(0f, 8f); bottomEdge.anchoredPosition = new Vector2(0f, -4f);
            band = Text("Band", bottom, titleAsset, 44f, Paper, TextAlignmentOptions.Left);
            Place(band.rectTransform, new Vector2(150f, 10f), new Vector2(1000f, 60f), new Vector2(0f, 0.5f));
            band.outlineWidth = 0.15f; band.outlineColor = Ink;
            var bar = Img("Loading", bottom, Ink).rectTransform;
            Place(bar, new Vector2(150f, -40f), new Vector2(560f, 16f), new Vector2(0f, 0.5f));
            progressFill = Img("Fill", bar, Caption).rectTransform;
            progressFill.anchorMin = Vector2.zero; progressFill.anchorMax = new Vector2(0f, 1f);
            progressFill.offsetMin = new Vector2(3f, 3f); progressFill.offsetMax = new Vector2(-3f, -3f);

            // Sound-effect burst over the painting's top-left corner.
            burst = new GameObject("Burst", typeof(RectTransform)).GetComponent<RectTransform>();
            burst.SetParent(page, false);
            Place(burst, new Vector2(220f, -300f), new Vector2(300f, 220f), new Vector2(0f, 1f));
            burst.localRotation = Quaternion.Euler(0f, 0f, -9f);
            var star = new GameObject("Star", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            star.transform.SetParent(burst, false);
            Stretch(star.rectTransform);
            star.texture = StarTexture();
            star.raycastTarget = false;
            burstWord = Text("Word", burst, titleAsset, 64f, Caption, TextAlignmentOptions.Center);
            Stretch(burstWord.rectTransform);
            burstWord.outlineWidth = 0.25f; burstWord.outlineColor = Ink;

            // The lore caption, bottom right; a click flips the page.
            caption = new GameObject("Lore Caption", typeof(RectTransform)).GetComponent<RectTransform>();
            caption.SetParent(page, false);
            caption.anchorMin = caption.anchorMax = new Vector2(1f, 0f);
            caption.pivot = new Vector2(1f, 0f);
            caption.anchoredPosition = new Vector2(-110f, 150f);
            caption.sizeDelta = new Vector2(600f, 270f);
            var shadow = Img("Shadow", caption, new Color(0f, 0f, 0f, 0.85f)).rectTransform;
            Stretch(shadow); shadow.anchoredPosition = new Vector2(12f, -12f);
            var border = Img("Border", caption, Ink).rectTransform;
            Stretch(border);
            captionFill = Img("Fill", border, Caption);
            Inset(captionFill.rectTransform, 5f);
            captionFill.raycastTarget = true;
            var button = caption.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = captionFill;
            button.onClick.AddListener(Flip);
            caption.gameObject.AddComponent<CaptionHover>();

            var body = captionFill.rectTransform;
            captionTitle = Text("Title", body, titleAsset, 40f, Ink, TextAlignmentOptions.TopLeft);
            Place(captionTitle.rectTransform, new Vector2(22f, -12f), new Vector2(556f, 48f), new Vector2(0f, 1f));
            captionLore = Text("Lore", body, bodyAsset, 21f, Ink, TextAlignmentOptions.TopLeft);
            captionLore.rectTransform.anchorMin = new Vector2(0f, 0f);
            captionLore.rectTransform.anchorMax = new Vector2(1f, 1f);
            captionLore.rectTransform.offsetMin = new Vector2(22f, 46f);
            captionLore.rectTransform.offsetMax = new Vector2(-22f, -62f);
            captionLore.enableAutoSizing = true;
            captionLore.fontSizeMin = 15f; captionLore.fontSizeMax = 21f;
            captionLore.lineSpacing = -4f;
            captionPage = Text("Page", body, titleAsset, 24f, Ink, TextAlignmentOptions.BottomLeft);
            Place(captionPage.rectTransform, new Vector2(22f, 12f), new Vector2(260f, 32f), new Vector2(0f, 0f));
            captionHint = Text("Hint", body, titleAsset, 26f, Pow, TextAlignmentOptions.BottomRight);
            Place(captionHint.rectTransform, new Vector2(-22f, 12f), new Vector2(300f, 32f), new Vector2(1f, 0f));
        }

        private TMP_FontAsset MakeFont(Font font)
        {
            if (font == null) return TMP_Settings.defaultFontAsset;
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);
            if (asset == null) return TMP_Settings.defaultFontAsset;
            asset.name = font.name + " (Travel)";
            if (TMP_Settings.defaultFontAsset != null)
                asset.fallbackFontAssetTable = new List<TMP_FontAsset> { TMP_Settings.defaultFontAsset };
            return asset;
        }

        private static Image Img(string name, Transform parent, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RawImage Raw(string name, Transform parent)
        {
            var raw = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            raw.transform.SetParent(parent, false);
            Stretch(raw.rectTransform);
            raw.raycastTarget = false;
            return raw;
        }

        private static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font, float size, Color color, TextAlignmentOptions align)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            text.transform.SetParent(parent, false);
            if (font != null) text.font = font;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private static void Stretch(Image image) => Stretch(image.rectTransform);

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Inset(RectTransform rect, float by)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(by, by);
            rect.offsetMax = new Vector2(-by, -by);
        }

        private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position, float degrees)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            rect.localRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size, Vector2 anchor)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        private static Texture2D DotTexture()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, name = "Travel Halftone" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f));
                    byte a = (byte)(Mathf.Clamp01(7f - d) * 255f);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>A comic burst: a jagged red star with an ink rim.</summary>
        private static Texture2D StarTexture()
        {
            const int w = 300, h = 220, points = 13;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Travel Burst" };
            var px = new Color32[w * h];
            var rng = new System.Random(7);
            var radius = new float[points * 2];
            for (int i = 0; i < radius.Length; i++) radius[i] = i % 2 == 0 ? 0.92f + (float)rng.NextDouble() * 0.08f : 0.62f + (float)rng.NextDouble() * 0.08f;
            Color32 pow = Pow, ink = Ink, clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float u = (x - w * 0.5f) / (w * 0.5f), v = (y - h * 0.5f) / (h * 0.5f);
                    float angle = Mathf.Atan2(v, u) / (2f * Mathf.PI) * radius.Length;
                    if (angle < 0f) angle += radius.Length;
                    int i0 = (int)angle % radius.Length, i1 = (i0 + 1) % radius.Length;
                    float edge = Mathf.Lerp(radius[i0], radius[i1], angle - Mathf.Floor(angle));
                    float r = Mathf.Sqrt(u * u + v * v);
                    px[y * w + x] = r <= edge - 0.06f ? pow : r <= edge ? ink : clear;
                }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        /// <summary>The caption leans in a little under the pointer, so it reads as something to click.</summary>
        private sealed class CaptionHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private bool over;
            public void OnPointerEnter(PointerEventData e) => over = true;
            public void OnPointerExit(PointerEventData e) => over = false;

            private void Update()
            {
                float target = over ? 1.04f : 1f;
                var s = transform.localScale;
                float y = Mathf.MoveTowards(s.y, target, Time.unscaledDeltaTime * 0.6f);
                transform.localScale = new Vector3(s.x, y, 1f);
            }
        }
    }
}
