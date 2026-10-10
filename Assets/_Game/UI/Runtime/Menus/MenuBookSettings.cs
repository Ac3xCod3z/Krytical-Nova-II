using UnityEngine;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// How the menus' comic book turns its pages (<see cref="MenuBook"/>, <see cref="MenuTransition"/>). The shape
    /// of the turn itself, how the paper lifts and curls, is MegaBook's: it is set on the book prefab's MegaBook
    /// Builder, with MegaBook's own inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/UI/Menu Book Settings", fileName = "MenuBookSettings")]
    public sealed class MenuBookSettings : ScriptableObject
    {
        [Header("The turn")]
        [Tooltip("Seconds a page takes to turn.")]
        [Min(0.1f)] public float turnSeconds = 0.7f;
        [Tooltip("How far the page has turned (0 flat, 1 over) across the turn's time (0 to 1).")]
        public AnimationCurve turn = new AnimationCurve(new Keyframe(0f, 0f, 0f, 1.9f), new Keyframe(1f, 1f, 0.5f, 0f));
        [Tooltip("Frames the picture of the last screen lies flat over the next one before its page turns: the next screen draws for the first time under it.")]
        [Range(1, 10)] public int holdFrames = 2;

        [Header("The way into the world")]
        [Tooltip("The material that cuts the Loading page into panels and throws them clear (KryticalNova/UI/PanelShatter). None: the page turns into the world like any other.")]
        public Material reveal;
        [Tooltip("Seconds from the first ink line to the last panel gone.")]
        [Min(0.2f)] public float revealSeconds = 1.25f;
        [Tooltip("How many panels the page is cut into, least and most. A different cut every time.")]
        [Range(3, 8)] public int panelsFewest = 5;
        [Range(3, 8)] public int panelsMost = 7;
        [Tooltip("How far a panel is thrown, in views (1 = the height or width of the screen), least and most.")]
        public Vector2 panelThrow = new Vector2(1.15f, 1.6f);
        [Tooltip("How far a panel turns as it goes, in degrees, least and most. Each turns one way or the other.")]
        public Vector2 panelSpin = new Vector2(10f, 32f);
        [Tooltip("The world is ready to be shown once this many frames in a row have each been drawn faster than the time below.")]
        [Range(1, 30)] public int settledFrames = 6;
        [Tooltip("Seconds: a frame slower than this is the world still starting up.")]
        [Range(0.02f, 0.5f)] public float settledFrameSeconds = 0.15f;
        [Tooltip("The picture never waits longer than this for the world, in seconds.")]
        [Min(1f)] public float revealTimeout = 12f;

        [Header("The view of the page")]
        [Tooltip("The book camera's field of view. Narrow: the page turns flat, like a diagram. Wide: its lifted edge sweeps towards the reader.")]
        [Range(5f, 40f)] public float fieldOfView = 18f;
        [Tooltip("Where in the world the book is stood while a page turns: far from anything a game camera looks at.")]
        public Vector3 stage = new Vector3(0f, -500f, 0f);

        [Header("The book at rest")]
        [Tooltip("A menu screen is shown on the book's right-hand page: this much of the view, the whole screen made that much smaller. 1: the screen fills the view and there is no book round it.")]
        [Range(0.7f, 1f)] public float pageScale = 0.88f;
        [Tooltip("The share of the view's width left of the page: the gutter and the edge of the page before.")]
        [Range(0f, 0.2f)] public float leftStrip = 0.085f;
        [Tooltip("What the book lies on.")]
        public Color table = new Color32(0x0C, 0x0B, 0x17, 0xFF);
        [Tooltip("The paper of the pages seen round the screen, and its shaded side.")]
        public Color paperColour = new Color32(0xEE, 0xE6, 0xD2, 0xFF), paperShade = new Color32(0xB9, 0xAE, 0x98, 0xFF);
        [Tooltip("The ink line round the page.")]
        public Color inkColour = new Color32(0x14, 0x12, 0x2B, 0xFF);
        [Tooltip("How many page edges show under the right and bottom sides of the page.")]
        [Range(0, 8)] public int edgeLeaves = 5;
        [Tooltip("The book's cover board, seen past the pages along the top, the outer side and the foot, and the lighter line of its edge.")]
        public Color cover = new Color32(0x2B, 0x2C, 0x6E, 0xFF), coverEdge = new Color32(0x4C, 0x52, 0xBF, 0xFF);
        [Tooltip("How far the cover stands out past the pages, as a share of the view's width. 0: no cover is drawn.")]
        [Range(0f, 0.02f)] public float coverMargin = 0.007f;

        [Header("The paper")]
        [Tooltip("The dots on the back of a page take the accent colour of the skin the menus wear. Off: the colour below.")]
        public bool tintWithTheSkin = true;
        public Color backTint = new Color32(0x4C, 0x52, 0xBF, 0xFF);
    }
}
