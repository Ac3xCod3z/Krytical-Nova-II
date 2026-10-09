using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The way from one menu scene to the next. The menus are the pages of a comic book (<see cref="MenuBook"/>):
    /// leaving a screen takes its picture, the next scene loads under that picture, and the picture's page turns
    /// away to show it. Forward through the menus the page turns one way, back it turns the other.
    ///
    /// Where there is no picture to turn (the game has just started, or the menus are come back to from the world)
    /// the screen starts under solid ink, and that ink is the book's cover: it turns away like any page. Without the
    /// book the older comic transition does it all: Ben-Day dots sweep across, merge into solid ink, and shrink away
    /// on the next screen. One per front-end scene, on its own top-most canvas.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuTransition : MonoBehaviour
    {
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int DirectionId = Shader.PropertyToID("_Direction");
        // A scene's first frames are long. Without a cap they would swallow the whole animation.
        private const float MaxStep = 1f / 30f;
        // The book makes its page on its first frame; if it has not after this many, the turn is given up.
        private const int FramesToWaitForTheBook = 20;
        private static readonly WaitForEndOfFrame EndOfFrame = new WaitForEndOfFrame();

        [Tooltip("Full-screen image using the BenDayWipe material.")]
        [SerializeField] private Image cover;
        [SerializeField, Min(0.05f)] private float duration = 0.45f;
        [SerializeField] private Vector2 direction = new Vector2(1f, 1f);

        [Header("The comic book")]
        [Tooltip("The book whose pages the menus are. Without it the dots above do every transition.")]
        [SerializeField] private MenuBook bookPrefab;
        [Tooltip("Full-screen picture of the screen being left, flat, while the next scene loads under it.")]
        [SerializeField] private RawImage still;
        [Tooltip("Full-screen view of the book's turning page.")]
        [SerializeField] private RawImage page;

        // The picture of the screen that was left, on its way to the scene that turns its page.
        private static Texture2D pendingPicture;
        private static bool pendingForward;
        // The last picture taken of each menu screen: turning back, the page that comes over from the left shows it.
        private static readonly Dictionary<string, Texture2D> PagesSeen = new Dictionary<string, Texture2D>();

        private Material material;
        private Coroutine running;
        private float progress = 1f;
        private bool opening, closing;
        private MenuBook turningBook;
        private Texture2D turningPicture;
        private MenuPage atRest; // this screen as a page of the open book, while it is one

        /// <summary>The transition of the scene that is currently open.</summary>
        public static MenuTransition Current { get; private set; }
        /// <summary>True while the screen is covered. A page that is turning away covers nothing: the screen under it is the player's.</summary>
        public bool IsBusy => opening || closing;
        /// <summary>True while a page of the book is turning.</summary>
        public bool IsTurning => turningBook != null;

        private bool HasBook => bookPrefab != null && still != null && page != null;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Current = null;
            pendingPicture = null;
            foreach (Texture2D picture in PagesSeen.Values)
                if (picture != null) Destroy(picture);
            PagesSeen.Clear();
        }

        private void Awake()
        {
            Current = this;
            if (still != null) still.enabled = false;
            if (page != null) page.enabled = false;
            MenuBookSettings bookSettings = HasBook ? bookPrefab.Settings : null;
            if (bookSettings != null && bookSettings.pageScale < 0.999f)
            {
                // At rest the screen is the right-hand page of the open book; the pictures of a turn lie on that
                // page, and the turning leaf is seen across both pages.
                atRest = gameObject.AddComponent<MenuPage>();
                atRest.Open(bookSettings, GetComponentInParent<Canvas>());
                Rect area = MenuPage.Area;
                MenuPage.Place(still.rectTransform, area);
                MenuPage.Place(page.rectTransform, new Rect(area.x - area.width, area.y, area.width * 2f, area.height));
                if (cover != null) MenuPage.Place(cover.rectTransform, area);
            }
            if (cover != null)
            {
                material = new Material(cover.material); // never animate the shared asset
                cover.material = material;
            }
            if (pendingPicture != null && HasBook)
            {
                // Arrived by a page turn: the last screen's picture lies over this one until its page lifts.
                ShowStill(pendingPicture);
                if (cover != null) SetCovering(false);
                opening = true;
            }
            else if (cover != null)
            {
                // Opened with no page to turn: the screen starts under ink (the book's cover, when there is a book).
                DropPending();
                SetProgress(1f);
                SetCovering(true);
                opening = true;
            }
        }

        private void Start()
        {
            if (HasBook && (pendingPicture != null || cover != null)) running = StartCoroutine(TurnPage());
            else if (cover != null) running = StartCoroutine(Open());
        }

        private void OnDestroy()
        {
            if (Current == this) Current = null;
            if (material != null) Destroy(material);
            EndTurn();
        }

        /// <summary>Goes to another scene. Which way the page turns follows from where the scene is in the book.</summary>
        public void LoadScene(string scene)
        {
            bool forward = FrontEnd.PageOf(scene) >= FrontEnd.PageOf(SceneManager.GetActiveScene().name);
            Close(() => SceneManager.LoadScene(scene), forward: forward);
        }

        /// <summary>
        /// Cover the screen, then run <paramref name="then"/> (load a scene, activate a preloaded one).
        /// With <paramref name="carryIntoNextScene"/> the transition survives the scene change and uncovers the next
        /// scene itself: for scenes that have no transition of their own (the world).
        /// </summary>
        public void Close(Action then, bool carryIntoNextScene = false, bool forward = true)
        {
            if (closing) return;
            bool interrupted = opening;
            closing = true;
            opening = false;
            if (running != null) StopCoroutine(running);
            EndTurn(); // a page still turning from the way in: the screen is left as it is now
            running = StartCoroutine(HasBook
                ? TurnAway(then, carryIntoNextScene, forward, interrupted)
                : CloseRoutine(then, carryIntoNextScene, interrupted));
        }

        // ------------------------------------------------------------------ the page turn

        /// <summary>Takes the picture of this screen and leaves: the scene that comes next turns its page.</summary>
        private IEnumerator TurnAway(Action then, bool carryIntoNextScene, bool forward, bool interruptedOpening)
        {
            yield return EndOfFrame; // the whole frame, interface and all, is drawn
            Texture2D picture = TakePicture();
            if (picture == null)
            {
                yield return CloseRoutine(then, carryIntoNextScene, interruptedOpening);
                yield break;
            }
            DropPending();
            pendingPicture = picture;
            pendingForward = forward;
            Remember(SceneManager.GetActiveScene().name, picture);
            ShowStill(picture);
            if (cover != null) SetCovering(false);

            if (!carryIntoNextScene)
            {
                then?.Invoke();
                yield break;
            }

            // The page turns into the world: the book is left behind, and the picture takes the whole view.
            if (atRest != null)
            {
                atRest.Close();
                atRest = null;
                var whole = new Rect(0f, 0f, 1f, 1f);
                MenuPage.Place(still.rectTransform, whole);
                MenuPage.Place(page.rectTransform, whole);
            }
            var from = SceneManager.GetActiveScene();
            transform.SetParent(null, false);
            DontDestroyOnLoad(gameObject);
            then?.Invoke();
            while (SceneManager.GetActiveScene() == from) yield return null;
            closing = false;
            opening = true;
            yield return TurnPage();
            Destroy(gameObject);
        }

        /// <summary>
        /// Lifts the picture of the last screen off this one as a page of the book, and turns it away. With no
        /// picture (the menus have just been opened) it is the ink this screen starts under that turns away, as
        /// the book's cover.
        /// </summary>
        private IEnumerator TurnPage()
        {
            opening = true;
            turningPicture = pendingPicture;
            bool forward = turningPicture == null || pendingForward;
            pendingPicture = null;
            turningBook = Instantiate(bookPrefab);
            turningBook.gameObject.SetActive(true);
            MenuBookSettings settings = turningBook.Settings;

            // On the open book a turn back is a leaf that comes over from the left and lands on this page, showing
            // this screen as it was last seen; the screen that was left stays on the page until the leaf covers it.
            // (Not seen before in this run, the leaf of the screen that was left turns away instead, as going forward.)
            bool spread = atRest != null;
            Texture2D landing = null;
            bool comingBack = spread && !forward && PagesSeen.TryGetValue(SceneManager.GetActiveScene().name, out landing) && landing != null;
            Texture2D onLeaf = comingBack ? landing : turningPicture;

            // This scene draws for the first time under the flat picture, and the book makes its page.
            int frames = 0;
            while (frames < (settings != null ? settings.holdFrames : 2) || !turningBook.Prepare(onLeaf, spread || forward, spread))
            {
                if (++frames > FramesToWaitForTheBook) break;
                yield return null;
            }
            if (!turningBook.Ready)
            {
                Debug.LogWarning("[Menus] The menu book did not make its page: the screen was shown without a page turn.", this);
                bool underInk = turningPicture == null && cover != null;
                EndTurn();
                if (underInk) yield return Open(); // the dots take the ink away instead
                opening = false;
                running = null;
                yield break;
            }
            if (comingBack) turningBook.SetTurn(1f); // it starts lying on the left-hand page
            yield return null; // the book's camera draws the page once where it starts

            page.texture = turningBook.Target;
            // Full-screen, turning back is the same turn in a mirror: the page leaves by the other edge. On the open
            // book nothing is mirrored: the leaf goes over to the left, or comes back from it.
            page.uvRect = spread || forward ? new Rect(0f, 0f, 1f, 1f) : new Rect(1f, 0f, -1f, 1f);
            page.enabled = true;
            if (!comingBack) still.enabled = false;
            if (cover != null) SetCovering(false); // the ink the screen started under is the cover page now
            // From here the screen underneath is the player's: the page takes no clicks and holds no keys back.
            opening = false;
            // The page lifts: the moment has its own feel, and the screen it uncovers is what answers it.
            var skins = FindAnyObjectByType<MenuSkinSwitch>();
            MenuMoments.Raise(forward ? MenuMoment.PageForward : MenuMoment.PageBack,
                skins != null && skins.ShowingRoot != null ? skins.ShowingRoot.transform : null);

            float seconds = Mathf.Max(0.05f, settings.turnSeconds);
            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxStep);
                float turned = settings.turn.Evaluate(Mathf.Clamp01(elapsed / seconds));
                turningBook.SetTurn(comingBack ? 1f - turned : turned);
                yield return null;
            }
            EndTurn();
            running = null;
        }

        /// <summary>Keeps the picture of a menu screen for the turn back to it. The one kept before for that screen goes.</summary>
        private static void Remember(string scene, Texture2D picture)
        {
            if (PagesSeen.TryGetValue(scene, out Texture2D before) && before != null && before != picture) Destroy(before);
            PagesSeen[scene] = picture;
        }

        private static bool IsKept(Texture2D picture) => picture != null && PagesSeen.ContainsValue(picture);

        /// <summary>The screen as it is shown, as a picture that shows the same colours again.</summary>
        private static Texture2D TakePicture()
        {
            Texture2D taken = ScreenCapture.CaptureScreenshotAsTexture();
            if (taken == null) return null;
            // On the open book the screen is the page only: the book round it is not part of the picture.
            RectInt area = MenuPage.Pixels;
            if (area.width > 16 && area.height > 16 && (area.width < taken.width || area.height < taken.height))
            {
                var cut = new Texture2D(area.width, area.height, taken.format, false, !GraphicsFormatUtility.IsSRGBFormat(taken.graphicsFormat));
                cut.SetPixels(taken.GetPixels(Mathf.Clamp(area.x, 0, taken.width - area.width), Mathf.Clamp(area.y, 0, taken.height - area.height), area.width, area.height));
                cut.Apply(false);
                Destroy(taken);
                taken = cut;
            }
            // What comes back holds the screen's own (sRGB) values. Where the project works in linear colour it can
            // be marked as holding linear ones, and would then be shown too bright: the same values, marked right.
            if (QualitySettings.activeColorSpace == ColorSpace.Linear && !GraphicsFormatUtility.IsSRGBFormat(taken.graphicsFormat))
            {
                var marked = new Texture2D(taken.width, taken.height, taken.format, false, false);
                marked.LoadRawTextureData(taken.GetRawTextureData<byte>());
                marked.Apply(false, true);
                Destroy(taken);
                taken = marked;
            }
            taken.name = "Menu Page Picture";
            taken.wrapMode = TextureWrapMode.Clamp;
            return taken;
        }

        private void ShowStill(Texture picture)
        {
            still.texture = picture;
            still.enabled = true;
            still.raycastTarget = true; // swallow clicks while the scene changes
        }

        /// <summary>Puts the book away: the page, its picture and the views of them.</summary>
        private void EndTurn()
        {
            if (page != null)
            {
                page.enabled = false;
                page.texture = null;
            }
            if (still != null && turningPicture != null && still.texture == turningPicture)
            {
                still.enabled = false;
                still.texture = null;
            }
            if (turningBook != null) Destroy(turningBook.gameObject);
            if (turningPicture != null && !IsKept(turningPicture)) Destroy(turningPicture);
            turningBook = null;
            turningPicture = null;
        }

        private static void DropPending()
        {
            if (pendingPicture != null && !IsKept(pendingPicture)) Destroy(pendingPicture);
            pendingPicture = null;
        }

        // ------------------------------------------------------------------ the dots

        private IEnumerator Open()
        {
            opening = true;
            // The previous scene closed towards one corner; open away from it so the sweep reads as one motion.
            material.SetVector(DirectionId, -direction);
            yield return null; // the new scene draws once under the ink
            yield return Animate(0f);
            SetCovering(false);
            opening = false;
            running = null;
        }

        private IEnumerator CloseRoutine(Action then, bool carryIntoNextScene, bool interruptedOpening)
        {
            if (cover != null)
            {
                SetCovering(true);
                // Closing over a half-finished opening keeps its direction: the dots simply grow back.
                if (!interruptedOpening) material.SetVector(DirectionId, direction);
                yield return Animate(1f);
            }
            if (!carryIntoNextScene || cover == null)
            {
                then?.Invoke();
                yield break;
            }

            var from = SceneManager.GetActiveScene();
            transform.SetParent(null, false);
            DontDestroyOnLoad(gameObject);
            then?.Invoke();
            while (SceneManager.GetActiveScene() == from) yield return null;
            // The next scene's first frames (spawning, the camera snapping into place) stay under the ink.
            yield return null;
            yield return null;
            closing = false;
            opening = true;
            material.SetVector(DirectionId, -direction);
            yield return Animate(0f);
            Destroy(gameObject);
        }

        private IEnumerator Animate(float to)
        {
            float from = progress;
            float time = duration * Mathf.Abs(to - from);
            float elapsed = 0f;
            while (elapsed < time)
            {
                elapsed += Mathf.Min(Time.unscaledDeltaTime, MaxStep);
                SetProgress(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, elapsed / time)));
                yield return null;
            }
            SetProgress(to);
        }

        private void SetProgress(float value)
        {
            progress = value;
            material.SetFloat(ProgressId, value);
        }

        private void SetCovering(bool value)
        {
            cover.enabled = value;
            cover.raycastTarget = value; // swallow clicks while the scene changes
        }
    }
}
