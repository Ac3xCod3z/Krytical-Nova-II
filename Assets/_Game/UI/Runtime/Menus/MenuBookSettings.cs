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

        [Header("The paper")]
        [Tooltip("The dots on the back of a page take the accent colour of the skin the menus wear. Off: the colour below.")]
        public bool tintWithTheSkin = true;
        public Color backTint = new Color32(0x4C, 0x52, 0xBF, 0xFF);
    }
}
