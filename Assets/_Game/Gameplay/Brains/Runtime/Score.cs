using Unity.Mathematics;

namespace KryticalNova.Brains
{
    /// <summary>A response curve of a consideration (the "infinite axis" utility style): x and the result both in 0..1.</summary>
    public struct ScoreCurve
    {
        public byte Shape;
        public float M, K, B, C;
    }

    public static class ScoreShape
    {
        public const byte Linear = 0, Quadratic = 1, Logistic = 2, Logit = 3;
    }

    /// <summary>Scores of a mind's options (pure, Burst). An option's score is the product of its considerations, compensated for their count.</summary>
    public static class Score
    {
        /// <summary>
        /// Linear: M(x - C) + B. Quadratic: M max(x - C, 0)^K + B. Logistic: K / (1 + (1000 e M)^(C - x)) + B.
        /// Logit: M log10(z / (1 - z)) / 5 + 0.5 + B with z = x - C kept inside (0, 1).
        /// </summary>
        public static float Evaluate(in ScoreCurve c, float x)
        {
            x = math.saturate(x);
            float y;
            switch (c.Shape)
            {
                case ScoreShape.Quadratic: y = c.M * math.pow(math.max(x - c.C, 0f), c.K) + c.B; break;
                case ScoreShape.Logistic: y = c.K / (1f + math.pow(1000f * math.E * c.M, c.C - x)) + c.B; break;
                case ScoreShape.Logit:
                    float z = math.clamp(x - c.C, 1e-4f, 1f - 1e-4f);
                    y = c.M * math.log10(z / (1f - z)) / 5f + 0.5f + c.B;
                    break;
                default: y = c.M * (x - c.C) + c.B; break;
            }
            return math.saturate(y);
        }

        /// <summary>The product of <paramref name="count"/> considerations made up for their number (more factors would else always lose).</summary>
        public static float Compensate(float product, int count)
        {
            if (count <= 1) return product;
            float modification = 1f - 1f / count;
            return product + (1f - product) * modification * product;
        }

        /// <summary>
        /// Whether an option with this running product can still beat <paramref name="best"/>: its remaining factors are at most 1 and the
        /// compensation grows with the product, so when even the compensated running product cannot, the rest need not be scored.
        /// </summary>
        public static bool CanStillBeat(float runningProduct, int count, float best) => Compensate(runningProduct, count) > best;
    }
}
