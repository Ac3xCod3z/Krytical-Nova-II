using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>How long a toast stays, how long it takes to come and go, and how many may wait behind it.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/UI/Toast Settings", fileName = "ToastSettings")]
    public sealed class ToastSettings : ScriptableObject
    {
        [Tooltip("Seconds a toast is on screen, its fade in included.")]
        [Min(1f)] public float showSeconds = 5f;
        [Tooltip("Seconds a toast takes to fade in, and to fade out before the next one comes up.")]
        [Range(0f, 2f)] public float fadeSeconds = 0.35f;
        [Tooltip("Toasts that may wait behind the one on screen. One more than this and the oldest waiting is dropped.")]
        [Range(1, 30)] public int maxWaiting = 8;
    }
}
