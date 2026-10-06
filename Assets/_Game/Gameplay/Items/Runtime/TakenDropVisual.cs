using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// What is left of a drop for a moment after someone takes it: the parcel waits while the hand comes down,
    /// then goes to the taker and is gone. Local to each machine; the item is already in the Pack.
    /// </summary>
    public sealed class TakenDropVisual : MonoBehaviour
    {
        private const float HandHeight = 0.55f; // where a crouched character's hand is

        private Transform taker;
        private Vector3 from;
        private Vector3 fullScale;
        private float wait;
        private float flight;
        private float elapsed;

        public void Begin(Transform takenBy, float waitSeconds, float flightSeconds)
        {
            taker = takenBy;
            from = transform.position;
            fullScale = transform.localScale;
            wait = Mathf.Max(0f, waitSeconds);
            flight = Mathf.Max(0.01f, flightSeconds);
        }

        private void Update()
        {
            if (taker == null)
            {
                Destroy(gameObject);
                return;
            }
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01((elapsed - wait) / flight);
            if (t <= 0f) return;

            float eased = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(from, taker.position + Vector3.up * HandHeight, eased);
            transform.localScale = fullScale * (1f - eased);
            if (t >= 1f) Destroy(gameObject);
        }
    }
}
