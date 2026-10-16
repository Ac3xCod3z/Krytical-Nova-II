using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// The sums of the news ticker (<see cref="ToastHudController"/>): a line of text crosses a band from its right
    /// edge to past its left edge a set number of times, at a set speed, so a player who missed it the first time
    /// reads it on the next pass (owner, 15 Oct 2026). Pure, so it is the same in a test.
    /// </summary>
    public static class TickerRules
    {
        // The colours of the line (owner, 15 Oct 2026). An Epic piece tempered to the top is red, a Rare one orange;
        // an Epic find is the Epic rarity's own purple (the item card's), a Legendary one its ember.
        public const string Gold = "#FFB83C", Orange = "#FF851F", Red = "#FF4A4A", Purple = "#C880FF", Ember = "#FF9640";
        private const byte Epic = 3, Legendary = 4; // ItemRarity, as the news carries it

        /// <summary>The colour of the tag and of what the news is about: a GM line gold, a tempered piece by its rarity, a find by its rarity.</summary>
        public static string TintOf(AnnouncementKind kind, byte rarity)
        {
            switch (kind)
            {
                case AnnouncementKind.Temper: return rarity >= Epic ? Red : Orange;
                case AnnouncementKind.Drop: return rarity >= Legendary ? Ember : Purple;
                default: return Gold;
            }
        }

        /// <summary>How long one pass takes: the text travels the band's width and its own, in seconds.</summary>
        public static float PassSeconds(float bandWidth, float textWidth, float pixelsPerSecond) =>
            (Mathf.Max(0f, bandWidth) + Mathf.Max(0f, textWidth)) / Mathf.Max(1f, pixelsPerSecond);

        /// <summary>How long the message is on the band for all its passes, in seconds.</summary>
        public static float ShowSeconds(float bandWidth, float textWidth, float pixelsPerSecond, int passes) =>
            PassSeconds(bandWidth, textWidth, pixelsPerSecond) * Mathf.Max(1, passes);

        /// <summary>
        /// Where the text's left edge stands this many seconds into the show, in the band's own pixels: it starts at
        /// the band's right edge, runs left until it is wholly past the left edge, and comes round again.
        /// </summary>
        public static float Left(float elapsed, float bandWidth, float textWidth, float pixelsPerSecond)
        {
            float pass = PassSeconds(bandWidth, textWidth, pixelsPerSecond);
            if (pass <= 0f) return bandWidth;
            float into = Mathf.Repeat(Mathf.Max(0f, elapsed), pass);
            return bandWidth - into * Mathf.Max(1f, pixelsPerSecond);
        }
    }
}
