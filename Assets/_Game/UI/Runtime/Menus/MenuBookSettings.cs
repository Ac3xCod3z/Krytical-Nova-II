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

        [Header("The paper")]
        [Tooltip("The dots on the back of a page take the accent colour of the skin the menus wear. Off: the colour below.")]
        public bool tintWithTheSkin = true;
        public Color backTint = new Color32(0x4C, 0x52, 0xBF, 0xFF);
    }
}
