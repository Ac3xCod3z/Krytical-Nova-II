using System;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>The shapes a swing push can take (day list item 20).</summary>
    public enum FoliagePushKind
    {
        /// <summary>An arc in front of a swing: combo steps 1-2.</summary>
        Arc = 0,
        /// <summary>A wider, stronger arc: the finisher (step 3) and attack skills.</summary>
        Finisher = 1,
        /// <summary>A ring that runs outwards from the caster: an area skill.</summary>
        Ring = 2,
        /// <summary>A wave that runs outwards from where a big hit landed (a critical, a finisher, a Shatterpoint).</summary>
        Wave = 3,
        /// <summary>A Hostile's swing, sized by its body.</summary>
        Hostile = 4,
    }

    /// <summary>
    /// How swings move the grass (owner's day list 13 Nov 2026, item 20; plan <c>Plans/swing-grass-push-plan.md</c>): a swing writes a
    /// short-lived push that bends the grass away and springs back in about half a second. A look only, per client, from what every
    /// client is already shown; nothing is sent over the network. Every number of the effect lives here.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Foliage Push Settings", fileName = "FoliagePush_Default")]
    public sealed class FoliagePushSettings : ScriptableObject
    {
        [Serializable]
        public struct Shape
        {
            [Tooltip("How far it reaches, metres (a ring's or a wave's last front; a Hostile's is times its body scale).")]
            public float radius;
            [Tooltip("Arcs: the whole arc in front, degrees. Rings and waves: unused.")]
            public float arcDegrees;
            [Tooltip("Rings and waves: how wide the moving front is, metres. Arcs: unused.")]
            public float band;
            [Tooltip("Rings and waves: how fast the front runs out, metres a second (0 = an arc, no front).")]
            public float frontSpeed;
            [Tooltip("How far the grass leans, times the grass's own bend (0-1.2).")]
            public float strength;
            [Tooltip("Seconds until it has sprung back and its slot is free.")]
            public float springSeconds;
        }

        [Tooltip("Master switch: off and no swing moves the grass.")]
        public bool enabled = true;

        [Header("Shapes")]
        public Shape arc = new Shape { radius = 2.2f, arcDegrees = 120f, strength = 0.55f, springSeconds = 0.45f };
        public Shape finisher = new Shape { radius = 2.8f, arcDegrees = 150f, strength = 0.8f, springSeconds = 0.5f };
        public Shape ring = new Shape { radius = 10f, band = 1.2f, frontSpeed = 14f, strength = 0.8f, springSeconds = 0.5f };
        public Shape wave = new Shape { radius = 4f, band = 1.5f, frontSpeed = 10f, strength = 1.1f, springSeconds = 0.6f };
        public Shape hostile = new Shape { radius = 2.5f, arcDegrees = 140f, strength = 0.7f, springSeconds = 0.5f };

        [Header("Limits")]
        [Tooltip("Pushes alive at once per World Effects tier (Low, Medium, High, Ultra); never more than FoliagePushes.MaxPushes.")]
        public int[] pushesPerTier = { 4, 6, 8, 8 };
        [Tooltip("A push farther than this from the camera is not added, metres (the grass's trample map reaches 48 m ahead).")]
        public float cullDistance = 60f;
        [Tooltip("A Hostile's push never grows past this many times its shape's radius, however big the body.")]
        public float maxBodyScale = 4f;
        [Tooltip("The body radius, metres, of a Hostile whose push is its shape as set (a person-sized one); bigger bodies push wider.")]
        public float hostileReferenceRadius = 0.45f;

        [Header("Spring")]
        [Tooltip("How fast it swings back, radians a second.")]
        public float springRate = 14f;
        [Tooltip("How quickly the swing dies away (0 never, 1 no overshoot).")]
        public float springDamping = 0.45f;
        [Tooltip("Seconds from nothing to the full lean.")]
        public float riseSeconds = 0.05f;
        [Tooltip("Arcs: how much of the lean goes sideways with the swing (combo steps 1 and 2 lean opposite ways).")]
        public float sweepShare = 0.3f;
        [Tooltip("The most a blade is bent by swings and trampling together (1 = the trample map's flat).")]
        public float totalPushClamp = 1.3f;

        public Shape ShapeOf(FoliagePushKind kind)
        {
            switch (kind)
            {
                case FoliagePushKind.Finisher: return finisher;
                case FoliagePushKind.Ring: return ring;
                case FoliagePushKind.Wave: return wave;
                case FoliagePushKind.Hostile: return hostile;
                default: return arc;
            }
        }

        /// <summary>How much a Hostile with this body radius (metres, before its scale) and scale grows its push.</summary>
        public float BodyScale(float bodyRadius, float scale) => bodyRadius * Mathf.Max(0.01f, scale) / Mathf.Max(0.05f, hostileReferenceRadius);

        /// <summary>How many pushes may be alive at this World Effects tier (0 Low to 3 Ultra).</summary>
        public int PushesAt(int tier)
        {
            if (pushesPerTier == null || pushesPerTier.Length == 0) return FoliagePushes.MaxPushes;
            return Mathf.Clamp(pushesPerTier[Mathf.Clamp(tier, 0, pushesPerTier.Length - 1)], 0, FoliagePushes.MaxPushes);
        }
    }
}
