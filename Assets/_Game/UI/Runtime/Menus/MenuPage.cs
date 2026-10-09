using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// A menu screen at rest, as a page of the comic book: the whole screen (its canvases and what its cameras
    /// draw) is made a little smaller and set on the book's right-hand page, and round it the book is seen: the
    /// table it lies on, the edge of the page before with the gutter's shadow, and the edges of the pages under
    /// this one. Nothing of the screen is rearranged: it is the same layout, smaller, and it is clicked and typed
    /// into as before. <see cref="MenuTransition"/> makes one for its scene and turns the pages over it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuPage : MonoBehaviour
    {
        private const string PageName = "Book Page";
        private const int UnderOrder = -1000;

        /// <summary>Where the screen is shown, as shares of the view (0 to 1). The whole view while there is no book.</summary>
        public static Rect Area { get; private set; } = new Rect(0f, 0f, 1f, 1f);

        private MenuBookSettings settings;
        private Canvas ownCanvas, under, over;
        private readonly List<Canvas> canvases = new List<Canvas>();
        private readonly List<Camera> cameras = new List<Camera>();
        private readonly List<Transform> strays = new List<Transform>();
        private Texture2D ramp;
        private Vector2Int fittedFor;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Area = new Rect(0f, 0f, 1f, 1f);

        /// <summary>The page in pixels of the screen, from its bottom-left corner.</summary>
        public static RectInt Pixels
        {
            get
            {
                int x = Mathf.RoundToInt(Area.x * Screen.width), y = Mathf.RoundToInt(Area.y * Screen.height);
                return new RectInt(x, y, Mathf.Min(Screen.width - x, Mathf.RoundToInt(Area.width * Screen.width)), Mathf.Min(Screen.height - y, Mathf.RoundToInt(Area.height * Screen.height)));
            }
        }

        /// <summary>Stretches a rectangle of a full-screen canvas over a share of the view.</summary>
        public static void Place(RectTransform rect, Rect share)
        {
            rect.anchorMin = share.min;
            rect.anchorMax = share.max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        /// <summary>Sets this scene's screen on the page. <paramref name="transitionCanvas"/> is the one canvas left as it is.</summary>
        public void Open(MenuBookSettings bookSettings, Canvas transitionCanvas)
        {
            settings = bookSettings;
            ownCanvas = transitionCanvas;
            float scale = Mathf.Clamp(settings.pageScale, 0.5f, 1f);
            float left = Mathf.Clamp(settings.leftStrip, 0f, 1f - scale);
            Area = new Rect(left, (1f - scale) * 0.5f, scale, scale);
            BuildBook();
            Fit();
        }

        /// <summary>Gives the whole view back (the page is turning into the world): the book round the screen goes.</summary>
        public void Close()
        {
            Area = new Rect(0f, 0f, 1f, 1f);
            if (under != null) Destroy(under.gameObject);
            if (over != null) Destroy(over.gameObject);
            foreach (Camera each in cameras)
                if (each != null) each.rect = new Rect(0f, 0f, 1f, 1f);
            canvases.Clear();
            cameras.Clear();
            enabled = false;
        }

        private void OnDestroy()
        {
            if (ramp != null) Destroy(ramp);
        }

        private void LateUpdate() => Fit();

        // A screen makes canvases, cameras and popups as it goes: whatever is there now goes on the page.
        private void Fit()
        {
            if (settings == null) return;
            foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
                if (canvas == ownCanvas || canvas == under || canvas == over) continue;
                Shrink(canvas);
            }
            foreach (Camera each in Camera.allCameras)
            {
                if (each.targetTexture != null) continue; // a picture being made, not the screen
                if (each.rect != Area) each.rect = Area;
                if (!cameras.Contains(each)) cameras.Add(each);
            }
        }

        private void Shrink(Canvas canvas)
        {
            var whole = (RectTransform)canvas.transform;
            Transform found = whole.Find(PageName);
            RectTransform page;
            if (found != null) page = (RectTransform)found;
            else
            {
                var go = new GameObject(PageName, typeof(RectTransform)) { layer = canvas.gameObject.layer };
                page = (RectTransform)go.transform;
                page.SetParent(whole, false);
                page.SetAsFirstSibling();
                canvases.Add(canvas);
            }
            // The canvas's own size, made smaller about its bottom-left corner and moved to the page's.
            Vector2 size = whole.rect.size;
            page.anchorMin = page.anchorMax = page.pivot = Vector2.zero;
            if (page.sizeDelta != size) page.sizeDelta = size;
            Vector2 at = Vector2.Scale(size, Area.min);
            if (page.anchoredPosition != at) page.anchoredPosition = at;
            var scale = new Vector3(Area.width, Area.height, 1f);
            if (page.localScale != scale) page.localScale = scale;

            strays.Clear();
            foreach (Transform child in whole)
                if (child != page) strays.Add(child);
            foreach (Transform child in strays) child.SetParent(page, false);
        }

        // ------------------------------------------------------------------ the book round the page

        private void BuildBook()
        {
            under = NewCanvas("Book Under", UnderOrder);
            over = NewCanvas("Book Over", ownCanvas != null ? ownCanvas.sortingOrder - 1 : 30000);
            ramp = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Book Shade" };
            for (int x = 0; x < 64; x++)
            {
                float t = x / 63f;
                ramp.SetPixel(x, 0, new Color(1f, 1f, 1f, (1f - t) * (1f - t)));
            }
            ramp.Apply();

            Rect page = Area;
            float aspect = Screen.height > 0 ? Screen.width / (float)Screen.height : 16f / 9f;
            // The table, wherever the page is not: four strips, so the page itself is left open for the cameras.
            Block(under, "Table Left", new Rect(0f, 0f, page.xMin, 1f), settings.table);
            Block(under, "Table Right", new Rect(page.xMax, 0f, 1f - page.xMax, 1f), settings.table);
            Block(under, "Table Top", new Rect(page.xMin, page.yMax, page.width, 1f - page.yMax), settings.table);
            Block(under, "Table Bottom", new Rect(page.xMin, 0f, page.width, page.yMin), settings.table);

            // The pages under this one: each a sliver further right and further down.
            float step = 0.0022f;
            for (int i = settings.edgeLeaves; i >= 1; i--)
            {
                float dx = i * step, dy = i * step * aspect;
                Color paper = i % 2 == 0 ? settings.paperColour : settings.paperShade;
                Block(under, "Leaf Side " + i, new Rect(page.xMax, page.yMin - dy, dx, page.height), paper);
                Block(under, "Leaf Foot " + i, new Rect(page.xMin + dx, page.yMin - dy, page.width, dy), paper);
            }

            // The page before, what shows of it left of the gutter, darker towards the fold.
            float gutter = page.xMin;
            Block(under, "Page Before", new Rect(0f, page.yMin, gutter, page.height), settings.paperColour);
            Shade(under, "Page Before Shade", new Rect(gutter * 0.25f, page.yMin, gutter * 0.75f, page.height), new Color(0f, 0f, 0f, 0.55f), true);

            // Over the screen: the fold's shadow on this page, and the ink line round it.
            Shade(over, "Fold Shade", new Rect(page.xMin, page.yMin, page.width * 0.035f, page.height), new Color(0f, 0f, 0f, 0.5f), false);
            float line = 0.0016f, lineY = line * aspect;
            Block(over, "Line Left", new Rect(page.xMin - line * 0.5f, page.yMin, line, page.height), settings.inkColour);
            Block(over, "Line Right", new Rect(page.xMax - line * 0.5f, page.yMin, line, page.height), settings.inkColour);
            Block(over, "Line Top", new Rect(page.xMin, page.yMax - lineY * 0.5f, page.width, lineY), settings.inkColour);
            Block(over, "Line Bottom", new Rect(page.xMin, page.yMin - lineY * 0.5f, page.width, lineY), settings.inkColour);
        }

        private Canvas NewCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = gameObject.layer };
            go.transform.SetParent(transform, false);
            Place((RectTransform)go.transform, new Rect(0f, 0f, 1f, 1f));
            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
            return canvas;
        }

        private static RawImage Block(Canvas canvas, string name, Rect share, Color colour)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = canvas.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(canvas.transform, false);
            Place(rect, share);
            var image = go.AddComponent<RawImage>();
            image.color = colour;
            image.raycastTarget = false;
            return image;
        }

        // A shadow that is darkest on one side and gone at the other.
        private void Shade(Canvas canvas, string name, Rect share, Color colour, bool darkOnTheRight)
        {
            RawImage image = Block(canvas, name, share, colour);
            image.texture = ramp;
            image.uvRect = darkOnTheRight ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
        }
    }
}
