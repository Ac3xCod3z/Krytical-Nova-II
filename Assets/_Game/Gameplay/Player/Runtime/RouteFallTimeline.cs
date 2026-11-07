using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// What a roof-edge drop shows from its fall leg on (owner v4, 7 Nov 2026; presentation only): the flip finishes, then the freefall
    /// (lying flat) until <c>routeFallLoopLead</c> before touchdown, then the game's fall loop, then the landing started
    /// <c>routeLandLead</c> before touchdown so its feet meet the ground on time. A freefall window under <c>routeFreefallMinSeconds</c> is
    /// skipped. The flip plays at its own speed; only when what is left of it would not end before the landing does it speed up, at most
    /// <c>routeFlipMaxSpeedUp</c>. Times are seconds from the start of the fall leg; -1 = that phase is skipped.
    /// </summary>
    /// <remarks>
    /// Worked out when the fall leg starts (a view learns the fall's length only from that leg's pose), so a short drop's speed-up covers
    /// the flip's second half: the flip still ends where the landing starts (2 m: 1.10x over the second half).
    /// </remarks>
    public readonly struct RouteFallTimeline
    {
        public readonly float FlipSpeed;
        public readonly float FlipEnds;
        public readonly float FreefallAt;
        public readonly float LoopAt;
        public readonly float LandAt;

        private RouteFallTimeline(float flipSpeed, float flipEnds, float freefallAt, float loopAt, float landAt)
        {
            FlipSpeed = flipSpeed;
            FlipEnds = flipEnds;
            FreefallAt = freefallAt;
            LoopAt = loopAt;
            LandAt = landAt;
        }

        /// <param name="flipLeft">Seconds of the flip still to play at its own speed when the fall leg starts (0 = none playing).</param>
        /// <param name="fallSeconds">The fall leg's length: the time left to touchdown.</param>
        /// <param name="hasFreefall">False when this body has no freefall clip: the fall loop follows the flip.</param>
        public static RouteFallTimeline Plan(float flipLeft, float fallSeconds, bool hasFreefall, float maxSpeedUp, float freefallMinSeconds,
            float loopLead, float landLead)
        {
            float fall = Mathf.Max(0f, fallSeconds);
            float landAt = Mathf.Max(0f, fall - landLead);
            float speed = 1f;
            if (flipLeft > 0f) speed = landAt > 1e-4f ? Mathf.Clamp(flipLeft / landAt, 1f, Mathf.Max(1f, maxSpeedUp)) : Mathf.Max(1f, maxSpeedUp);
            float flipEnds = flipLeft > 0f ? flipLeft / speed : 0f;
            landAt = Mathf.Max(landAt, flipEnds);   // never before the flip has ended

            float loopFrom = Mathf.Max(flipEnds, fall - loopLead);
            float freefallAt = -1f, loopAt;
            if (hasFreefall && loopFrom - flipEnds >= freefallMinSeconds && loopFrom - flipEnds > 0f)
            {
                freefallAt = flipEnds;
                loopAt = loopFrom;
            }
            else loopAt = flipEnds;
            if (loopAt >= landAt - 1e-4f) loopAt = -1f;   // no time for the fall loop: straight into the landing
            return new RouteFallTimeline(speed, flipEnds, freefallAt, loopAt, landAt);
        }

        /// <summary>The same, with the numbers of the settings asset.</summary>
        public static RouteFallTimeline Plan(float flipLeft, float fallSeconds, bool hasFreefall, WallstrideSettings settings) =>
            settings != null
                ? Plan(flipLeft, fallSeconds, hasFreefall, settings.routeFlipMaxSpeedUp, settings.routeFreefallMinSeconds, settings.routeFallLoopLead,
                    settings.routeLandLead)
                : Plan(flipLeft, fallSeconds, hasFreefall, 1.25f, 0.12f, 0.40f, 0.133f);
    }
}
