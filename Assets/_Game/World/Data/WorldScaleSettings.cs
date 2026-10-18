using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>Which measurement of a thing its kind is judged by.</summary>
    public enum ScaleAxis : byte
    {
        /// <summary>How tall it stands.</summary>
        Height = 0,
        /// <summary>The longer of its width and depth.</summary>
        Length = 1,
        /// <summary>The largest of its three measurements.</summary>
        Largest = 2,
    }

    /// <summary>
    /// One kind of thing and the size it has in the world: the real measurement it is rooted in, and the range a
    /// model of it may be within before it is brought back into it.
    /// </summary>
    [Serializable]
    public struct ScaleKind
    {
        [Tooltip("What builders call it (chair, table, tree).")]
        public string id;
        public ScaleAxis axis;
        [Tooltip("The real thing's measurement in metres.")]
        public float real;
        [Tooltip("The smallest and largest a model of it may be, in metres.")]
        public float min, max;
        [Tooltip("Where the real measurement comes from.")]
        public string root;
        [Tooltip("The most a model of this kind may be multiplied by; 0 = the settings' limit. The packs draw some trees at a third of their real height, so a field tree may need more than the furniture limit.")]
        public float maxFactor;
    }

    /// <summary>
    /// The world's measure: one unit is one metre, a person is <see cref="personHeight"/> tall, and every kind of
    /// made or grown thing has a real size it is held to (`_Docs/Design/16_world_scale.md` is the same table in
    /// words). Builders that bring vendor models into the game ask <see cref="Fit"/> what to multiply a model by;
    /// most are already true and get 1. What is meant to be grand is grand by a stated ratio to a person, and what
    /// is small is small for the same reason: nothing is a size by accident.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/World Scale")]
    public sealed class WorldScaleSettings : ScriptableObject
    {
        [Tooltip("How tall a person stands, in metres: the figure everything else is read against.")]
        [Min(0.5f)] public float personHeight = 1.8f;
        [Tooltip("How far over its real size a made thing may be drawn before it is brought down: the packs draw furniture a little heavy so that it reads from the camera's height.")]
        [Range(1f, 1.6f)] public float allowance = 1.25f;
        [Tooltip("How far under its real size a made thing may be before it is brought up.")]
        [Range(0.5f, 1f)] public float shortfall = 0.85f;
        [Tooltip("A model within this part of 1 is left exactly as its maker made it.")]
        [Range(0f, 0.2f)] public float leaveAlone = 0.05f;
        [Tooltip("The least and the most a model is ever multiplied by. Outside this it is the wrong model, not the wrong size.")]
        public Vector2 factorLimits = new Vector2(0.35f, 2.5f);

        [Header("How grand is grand (a ratio to a person)")]
        [Tooltip("At least this many times a person's height: what is built to impress (a Hall's door, a throne room, a statue of a hero).")]
        [Min(1f)] public float heroicRatio = 1.5f;
        [Tooltip("At least this many times a person's height: what towers (a Hall's roof, a gate, a great tree).")]
        [Min(1f)] public float monumentalRatio = 3f;
        [Tooltip("At least this many times a person's height: what is seen across a zone (the Storm Sovereign, a citadel).")]
        [Min(1f)] public float colossalRatio = 10f;
        [Tooltip("At most this part of a person's height: what is held in a hand (a cup, a candle, a book).")]
        [Range(0.01f, 1f)] public float tinyRatio = 1f / 6f;

        [Header("The kinds")]
        public List<ScaleKind> kinds = new List<ScaleKind>();

        public bool TryGet(string id, out ScaleKind kind)
        {
            if (!string.IsNullOrEmpty(id))
                for (int n = 0; n < kinds.Count; n++)
                    if (kinds[n].id == id) { kind = kinds[n]; return true; }
            kind = default;
            return false;
        }

        public static float Measure(ScaleAxis axis, Vector3 size)
        {
            switch (axis)
            {
                case ScaleAxis.Height: return size.y;
                case ScaleAxis.Length: return Mathf.Max(size.x, size.z);
                default: return Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            }
        }

        /// <summary>
        /// What a model of a kind is multiplied by to be within that kind's range: 1 when it already is, or when
        /// the kind is not known.
        /// </summary>
        public float Fit(string kindId, Vector3 size)
        {
            if (!TryGet(kindId, out ScaleKind kind)) return 1f;
            float measured = Measure(kind.axis, size);
            if (measured <= 0.0001f) return 1f;
            float factor = measured < kind.min ? kind.min / measured : measured > kind.max ? kind.max / measured : 1f;
            if (Mathf.Abs(factor - 1f) <= leaveAlone) return 1f;
            return Mathf.Clamp(factor, factorLimits.x, kind.maxFactor > 0f ? kind.maxFactor : factorLimits.y);
        }
    }
}
