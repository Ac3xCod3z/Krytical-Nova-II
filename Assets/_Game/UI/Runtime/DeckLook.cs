using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// The maths of the Deck's lively looks (build 17, 5.5c), kept apart so it can be tested: the colour of a bar by its
    /// level (the Beat Ring's low / mid / high language) and the peak cap that rests on a loud bar and falls back slowly.
    /// </summary>
    public static class DeckLook
    {
        /// <summary>Quiet bars are <paramref name="low"/>, half-loud <paramref name="mid"/>, full ones <paramref name="high"/>.</summary>
        public static Color BeatColour(float level01, Color low, Color mid, Color high)
        {
            float t = Mathf.Clamp01(level01);
            return t < 0.5f ? Color.Lerp(low, mid, t * 2f) : Color.Lerp(mid, high, (t - 0.5f) * 2f);
        }

        /// <summary>A cap jumps up to a louder bar at once and falls back at <paramref name="fallPerSecond"/>.</summary>
        public static float Peak(float peak, float level01, float fallPerSecond, float deltaSeconds)
        {
            float level = Mathf.Clamp01(level01);
            return level >= peak ? level : Mathf.Max(level, peak - Mathf.Max(0f, fallPerSecond) * Mathf.Max(0f, deltaSeconds));
        }
    }
}
