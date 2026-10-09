using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// What is left of a drop for a moment after someone takes it: the parcel waits while the hand comes down,
    /// then goes to the taker and is gone. Local to each machine; the item is already in the Pack. The parcel is
    /// the drop's own, and drops are pooled: when the flight is over it goes back onto its drop, hidden, as it
    /// was, ready for the drop's next use.
    /// </summary>
    public sealed class TakenDropVisual : MonoBehaviour
    {
        private const float HandHeight = 0.55f; // where a crouched character's hand is

        private Transform taker;
        private Transform home;
        private Vector3 from;
        private Vector3 fullScale;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Vector3 homeScale;
        private float wait;
        private float flight;
        private float elapsed;

        /// <param name="returnTo">The drop the parcel belongs to. It was a child of it until now.</param>
        public void Begin(Transform takenBy, float waitSeconds, float flightSeconds, Transform returnTo)
        {
            taker = takenBy;
            home = returnTo;
            // Read before the parcel is taken off its drop: this is how it goes back.
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            homeScale = transform.localScale;
            transform.SetParent(null, true);
            from = transform.position;
            fullScale = transform.localScale;
            wait = Mathf.Max(0f, waitSeconds);
            flight = Mathf.Max(0.01f, flightSeconds);
        }

        private void Update()
        {
            if (taker == null) { ComeHome(); return; }
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01((elapsed - wait) / flight);
            if (t <= 0f) return;
            float eased = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(from, taker.position + Vector3.up * HandHeight, eased);
            transform.localScale = fullScale * (1f - eased);
            if (t >= 1f) ComeHome();
        }

        /// <summary>Ends the flight now and puts the parcel back on its drop, hidden. Called early when the drop is needed again.</summary>
        public void ComeHome()
        {
            if (home == null)
            {
                Destroy(gameObject); // the drop is gone for good (its scene was unloaded)
                return;
            }
            transform.SetParent(home, false);
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
            transform.localScale = homeScale;
            gameObject.SetActive(false);
            Destroy(this);
        }
    }
}
