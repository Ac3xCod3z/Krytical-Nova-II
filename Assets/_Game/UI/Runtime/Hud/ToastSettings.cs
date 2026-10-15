using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// How the news ticker runs: how many times a message crosses the band and how fast, how long the band takes to
    /// come and go, and how many messages may wait behind the one running.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/UI/Toast Settings", fileName = "ToastSettings")]
    public sealed class ToastSettings : ScriptableObject
    {
        [Tooltip("Kept for older assets: a toast's seconds on screen before the ticker (15 Oct 2026). Not used.")]
        [HideInInspector] public float showSeconds = 5f;
        [Tooltip("How many times a message crosses the band before the next one comes up.")]
        [Range(1, 10)] public int passes = 3;
        [Tooltip("How fast the line runs, in pixels a second (at the HUD's own scale).")]
        [Min(20f)] public float speedPixelsPerSecond = 160f;
        [Tooltip("Seconds the band takes to fade in, and to fade out before the next message comes up.")]
        [Range(0f, 2f)] public float fadeSeconds = 0.35f;
        [Tooltip("Messages that may wait behind the one running. One more than this and the oldest waiting is dropped.")]
        [Range(1, 30)] public int maxWaiting = 8;
    }
}
