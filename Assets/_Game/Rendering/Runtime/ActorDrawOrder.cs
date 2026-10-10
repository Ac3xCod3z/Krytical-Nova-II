using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Rendering
{
    /// <summary>
    /// Has a character, a pet or a Hostile drawn after the scenery, so that something drawn between the two can hide
    /// it without hiding the world behind it: an Ascension Nova does (see NovaHalo and
    /// <see cref="NovaCull"/>). Put on the root; it looks its models over again now and then, because gear, outfits
    /// and growth stages swap them. Only solid models are touched: light and particles that blend keep their order.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ActorDrawOrder : MonoBehaviour
    {
        /// <summary>The order of the solid pass, lowest first. Scenery is whatever nothing has marked.</summary>
        public const int Scenery = 0, Mask = 1, Actor = 2, Unmask = 3;

        private const float RescanSeconds = 1f;
        private const int FirstBlendedQueue = 2501;

        private readonly List<Renderer> found = new List<Renderer>();
        private float nextScan;
        private bool withScenery;

        /// <summary>Marks everything solid under <paramref name="root"/>, now and as it changes.</summary>
        public static ActorDrawOrder On(GameObject root)
        {
            if (root == null || Application.isBatchMode) return null;
            if (!root.TryGetComponent(out ActorDrawOrder order)) order = root.AddComponent<ActorDrawOrder>();
            order.Apply();
            return order;
        }

        /// <summary>
        /// True = drawn with the scenery after all, so nothing drawn in between hides it. A character seen through
        /// its own Nova is: the Nova is its own, not something it stands behind.
        /// </summary>
        public bool DrawWithScenery
        {
            get => withScenery;
            set
            {
                if (withScenery == value) return;
                withScenery = value;
                Apply();
            }
        }

        private void OnEnable() => Apply();

        private void LateUpdate()
        {
            if (Time.unscaledTime >= nextScan) Apply();
        }

        private void OnDestroy()
        {
            withScenery = true;
            Apply();
        }

        private void Apply()
        {
            nextScan = Time.unscaledTime + RescanSeconds;
            int order = withScenery ? Scenery : Actor;
            GetComponentsInChildren(true, found);
            foreach (Renderer body in found)
            {
                // Only what this put there, or nothing, is changed: a renderer with an order of its own keeps it.
                if (body.sortingOrder == order || (body.sortingOrder != Scenery && body.sortingOrder != Actor)) continue;
                Material material = body.sharedMaterial;
                if (material == null || material.renderQueue >= FirstBlendedQueue) continue;
                body.sortingOrder = order;
            }
            found.Clear();
        }
    }
}
