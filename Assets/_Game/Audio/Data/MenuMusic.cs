using UnityEngine;

namespace KryticalNova.Audio
{
    /// <summary>
    /// The music of the menus (Sound Bible, section 4; owner's picks of 21 Oct 2026). The title theme loops on the
    /// title page; pressing a key plays the outro sting while the theme fades, and the Selection theme follows. There
    /// is one Selection theme per menu skin (which faction gets which is not a story choice). On faction select the
    /// highlighted faction's coat takes over from it. The menu music player (UI) plays it; nothing here has a beat.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Audio/Menu Music")]
    public sealed class MenuMusic : ScriptableObject
    {
        public MusicTrack title;
        public MusicTrack titleOutro;

        [Header("Selection theme, one per menu skin")]
        public MusicTrack selectHegemony;
        public MusicTrack selectNull;
        public MusicTrack selectCovenant;

        [Header("Faction coats (faction select)")]
        public MusicTrack coatHegemony;
        public MusicTrack coatNull;
        public MusicTrack coatCovenant;

        [Header("Timing")]
        [Tooltip("Seconds for a theme to come in.")]
        [Min(0.05f)] public float fadeInSeconds = 1.5f;
        [Tooltip("Seconds for a theme to go out when another takes over.")]
        [Min(0.05f)] public float fadeOutSeconds = 1.2f;
        [Tooltip("Seconds the title theme takes to go out when the player presses a key.")]
        [Min(0.05f)] public float titleFadeOutSeconds = 2f;
        [Tooltip("Seconds after the key press before the Selection theme starts (under the outro's tail).")]
        [Min(0f)] public float afterTitleSeconds = 2f;
        [Tooltip("Seconds for the menu music to go out when the world starts loading.")]
        [Min(0.05f)] public float loadingFadeSeconds = 3f;
        [Range(0f, 1f)] public float volume = 0.8f;
    }
}
