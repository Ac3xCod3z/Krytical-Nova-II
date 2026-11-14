using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// The five distance bands a townsperson is drawn in (owner 14 Nov 2026), edges per Crowd Detail tier in <see cref="TownsfolkSettings"/>:
    /// CLOSE (whole mesh, every frame, head-look, shadows), MEDIUM (LOD1, every 2nd frame, shadows), FAR (LOD2, every 4th frame, no shadow),
    /// REALLY FAR (the picture card) and TOO FAR (not drawn; the server still keeps it where it is). Pure rules, tested.
    /// </summary>
    public static class TownsfolkBands
    {
        public const int Close = 0, Medium = 1, Far = 2, Card = 3, Hidden = 4;

        /// <summary>Metres the LOD groups measure a body as (a townsperson stands about this tall).</summary>
        public const float GroupSize = 2f;

        /// <summary>A tier's band edges in metres, each at least a little past the one before.</summary>
        public readonly struct Edges : System.IEquatable<Edges>
        {
            public readonly float Close, Medium, Far, Cards;

            public Edges(float close, float medium, float far, float cards)
            {
                Close = Mathf.Max(1f, close);
                Medium = Mathf.Max(Close + 1f, medium);
                Far = Mathf.Max(Medium + 1f, far);
                Cards = Mathf.Max(Far + 1f, cards);
            }

            public static Edges Of(TownsfolkSettings s, int tier) => new Edges(
                TownsfolkSettings.ByTier(s.closeWithinByTier, tier, 25f), TownsfolkSettings.ByTier(s.mediumWithinByTier, tier, 55f),
                TownsfolkSettings.ByTier(s.farWithinByTier, tier, 85f), TownsfolkSettings.ByTier(s.cardsWithinByTier, tier, 220f));

            public float EdgeAfter(int band) => band == TownsfolkBands.Close ? Close : band == TownsfolkBands.Medium ? Medium : band == TownsfolkBands.Far ? Far : Cards;

            public bool Equals(Edges o) => Close == o.Close && Medium == o.Medium && Far == o.Far && Cards == o.Cards;
            public override bool Equals(object o) => o is Edges e && Equals(e);
            public override int GetHashCode() => (Close, Medium, Far, Cards).GetHashCode();
        }

        /// <summary>
        /// The band at <paramref name="distance"/> metres. With a band it was in (<paramref name="previous"/>, 255 = none) it stays in it
        /// until <paramref name="hysteresis"/> metres past that band's edges (no flicker on a line).
        /// </summary>
        public static int BandOf(float distance, in Edges e, int previous, float hysteresis)
        {
            int plain = distance <= e.Close ? Close : distance <= e.Medium ? Medium : distance <= e.Far ? Far : distance <= e.Cards ? Card : Hidden;
            if (previous < Close || previous > Hidden || plain == previous || hysteresis <= 0f) return plain;
            if (plain > previous)   // going out: only once past the old band's outer edge plus the hysteresis
                return previous < Hidden && distance <= e.EdgeAfter(previous) + hysteresis ? previous : plain;
            // coming in: only once inside the old band's inner edge less the hysteresis
            float inner = previous == Close ? 0f : e.EdgeAfter(previous - 1);
            return distance >= inner - hysteresis ? previous : plain;
        }

        /// <summary>
        /// The LOD group's switch height (share of the screen) at which a <see cref="GroupSize"/> m body changes level exactly at
        /// <paramref name="metres"/>, for a camera of <paramref name="fieldOfView"/> degrees and Unity's LOD bias.
        /// </summary>
        public static float ScreenHeight(float metres, float fieldOfView, float lodBias)
        {
            float half = Mathf.Tan(Mathf.Clamp(fieldOfView, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
            return Mathf.Clamp(GroupSize * Mathf.Max(0.01f, lodBias) / (2f * Mathf.Max(0.1f, metres) * half), 0.0001f, 1f);
        }
    }

    /// <summary>
    /// A CLOSE townsperson turning its head to the player (owner 14 Nov 2026: head-look only near, only the nearest few). Lives beside the
    /// Animator (Unity sends OnAnimatorIK there; the shared controller's IK pass is on), ticked by the townsfolk view: no Update of its own.
    /// </summary>
    public sealed class TownsfolkHeadLook : MonoBehaviour
    {
        public Animator Animator;
        float m_Weight;
        Vector3 m_At;

        /// <summary>Eases toward looking (or away) at <paramref name="at"/>; a released body snaps back at once (<paramref name="dt"/> large).</summary>
        public void Tick(bool look, Vector3 at, float dt)
        {
            if (look) m_At = at;
            m_Weight = Mathf.MoveTowards(m_Weight, look ? 1f : 0f, dt * 2.5f);
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (Animator == null || layerIndex != 0 || m_Weight <= 0f) return;
            Animator.SetLookAtWeight(m_Weight * 0.75f, 0.1f, 0.9f, 0.5f, 0.5f);
            Animator.SetLookAtPosition(m_At);
        }
    }
}
