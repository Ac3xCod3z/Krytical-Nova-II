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

        private static readonly List<ActorDrawOrder> Actors = new List<ActorDrawOrder>(64);
        /// <summary>Every actor that is switched on, for whoever has to know which things a Nova can hide (<see cref="NovaCull"/>).</summary>
        public static IReadOnlyList<ActorDrawOrder> All => Actors;

        private readonly List<Renderer> found = new List<Renderer>();
        private float nextScan;
        private bool withScenery;
        // A ball round the solid models, kept in the root's own space so it travels with the actor between two looks.
        private Vector3 ballCentre;
        private float ballRadius;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Begin() => Actors.Clear();

        /// <summary>A ball in the world that holds all of this actor's solid models. False while it has none.</summary>
        public bool TryGetBall(out Vector3 centre, out float radius)
        {
            centre = transform.TransformPoint(ballCentre);
            radius = ballRadius;
            return ballRadius > 0f;
        }

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

        private void OnEnable()
        {
            if (!Actors.Contains(this)) Actors.Add(this);
            Apply();
        }

        private void OnDisable() => Actors.Remove(this);

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
            bool any = false;
            Bounds ball = default;
            foreach (Renderer body in found)
            {
                Material material = body.sharedMaterial;
                if (material == null || material.renderQueue >= FirstBlendedQueue) continue;
                if (body.enabled && body.gameObject.activeInHierarchy && (body.sortingOrder == Scenery || body.sortingOrder == Actor))
                {
                    if (any) ball.Encapsulate(body.bounds);
                    else ball = body.bounds;
                    any = true;
                }
                // Only what this put there, or nothing, is changed: a renderer with an order of its own keeps it.
                if (body.sortingOrder == order || (body.sortingOrder != Scenery && body.sortingOrder != Actor)) continue;
                body.sortingOrder = order;
            }
            found.Clear();
            ballCentre = any ? transform.InverseTransformPoint(ball.center) : Vector3.zero;
            ballRadius = any ? ball.extents.magnitude : 0f;
        }
    }
}
