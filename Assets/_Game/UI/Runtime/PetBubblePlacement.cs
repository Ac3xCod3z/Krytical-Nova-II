using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// Where a pet's speech bubble goes on the screen. It wants to sit to the left of the pet. If it would cover
    /// something that must stay clear there (the player, a namecard, an HP bar, a target marker, a HUD panel) it
    /// is shifted up or down, then tried on the pet's other side, then above it. Plain sums on rectangles (pixels,
    /// y running down), so they can be tested without a scene.
    /// </summary>
    public static class PetBubblePlacement
    {
        // How far, in bubble heights, a bubble is shifted when its first place is taken: up first, then down.
        private static readonly float[] Shifts = { 0f, -0.7f, -1.4f, -2.1f, 0.7f, 1.4f };

        /// <summary>
        /// The bubble's rectangle. <paramref name="tailOnRight"/> says which side its tail is on (toward the pet).
        /// <paramref name="clear"/> is true when the place covers nothing; when no place is clear the one that covers
        /// least is returned.
        /// </summary>
        public static Rect Place(Rect pet, Vector2 size, IReadOnlyList<Rect> blocked, Rect screen, float gap, float margin, out bool tailOnRight, out bool clear)
        {
            Rect best = default;
            float leastCovered = float.MaxValue;
            tailOnRight = true;
            clear = false;

            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                float x = left ? pet.xMin - gap - size.x : pet.xMax + gap;
                foreach (float shift in Shifts)
                {
                    var candidate = new Rect(x, pet.center.y - size.y * 0.5f + shift * size.y, size.x, size.y);
                    clear = Try(candidate, pet, blocked, screen, margin, left ? -1 : 1, ref best, ref leastCovered, out bool better);
                    if (better) tailOnRight = left;
                    if (clear) return best;
                }
            }
            // Above the pet, its tail on the side the pet's middle is.
            var above = new Rect(pet.center.x - size.x * 0.5f, pet.yMin - gap - size.y, size.x, size.y);
            clear = Try(above, pet, blocked, screen, margin, 0, ref best, ref leastCovered, out bool aboveIsBetter);
            if (aboveIsBetter) tailOnRight = best.center.x <= pet.center.x;
            return best;
        }

        /// <summary>
        /// True when the candidate, kept on the screen, covers nothing. <paramref name="better"/> says it is the
        /// best place seen so far (it covers least), which is the one handed back when no place is clear.
        /// <paramref name="side"/> is -1 for a place on the pet's left, 1 on its right, 0 above it.
        /// </summary>
        private static bool Try(Rect candidate, Rect pet, IReadOnlyList<Rect> blocked, Rect screen, float margin, int side,
            ref Rect best, ref float leastCovered, out bool better)
        {
            candidate = Clamp(candidate, screen);
            // Pushed back on to the screen it may now sit on the pet itself: that counts as covered too.
            float covered = Covered(candidate, pet, 0f);
            // A place beside the pet has to be beside it: pushed back over the pet's own column (at the screen's
            // edge) its tail would point away from the pet, so the other side is the better place.
            float over = side < 0 ? candidate.xMax - pet.xMin : side > 0 ? pet.xMax - candidate.xMin : 0f;
            if (over > 0f) covered += over * candidate.height;
            if (blocked != null)
                for (int i = 0; i < blocked.Count; i++) covered += Covered(candidate, blocked[i], margin);
            better = covered < leastCovered;
            if (better)
            {
                leastCovered = covered;
                best = candidate;
            }
            return covered <= 0f;
        }

        /// <summary>The rectangle moved (never resized) so that it lies on the screen.</summary>
        public static Rect Clamp(Rect rect, Rect screen)
        {
            float x = Mathf.Min(Mathf.Max(rect.x, screen.xMin), Mathf.Max(screen.xMin, screen.xMax - rect.width));
            float y = Mathf.Min(Mathf.Max(rect.y, screen.yMin), Mathf.Max(screen.yMin, screen.yMax - rect.height));
            return new Rect(x, y, rect.width, rect.height);
        }

        /// <summary>The area of <paramref name="rect"/> that lies over <paramref name="other"/> grown by <paramref name="margin"/>.</summary>
        public static float Covered(Rect rect, Rect other, float margin)
        {
            if (other.width <= 0f || other.height <= 0f) return 0f;
            float w = Mathf.Min(rect.xMax, other.xMax + margin) - Mathf.Max(rect.xMin, other.xMin - margin);
            float h = Mathf.Min(rect.yMax, other.yMax + margin) - Mathf.Max(rect.yMin, other.yMin - margin);
            return w > 0f && h > 0f ? w * h : 0f;
        }
    }
}
