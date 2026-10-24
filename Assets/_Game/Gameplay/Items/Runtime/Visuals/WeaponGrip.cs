using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Where a weapon is held, measured on the model (synty-blender weapon_pass.py, the "HK" grip data, owner
    /// 23 Oct 2026). The main hand is the prefab's origin, where the hand socket holds it. A two-handed weapon also
    /// says where the other hand may close on it: a stretch of the shaft, from <see cref="offHandFrom"/> to
    /// <see cref="offHandTo"/> in the prefab's own space, with <see cref="offHandAt"/> the place it was measured
    /// for. <see cref="OffHandGripIK"/> puts the left hand there while the weapon is drawn.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponGrip : MonoBehaviour
    {
        [Tooltip("The other hand closes on the weapon (a glaive's shaft).")]
        public bool twoHanded;
        [Tooltip("Where the other hand was measured to hold, in this prefab's space.")]
        public Vector3 offHandAt;
        [Tooltip("The stretch of shaft the other hand may slide along, nearest the main hand first.")]
        public Vector3 offHandFrom;
        public Vector3 offHandTo;

        /// <summary>The point of the shaft nearest <paramref name="worldPoint"/>, in world space.</summary>
        public Vector3 NearestOnShaft(Vector3 worldPoint)
        {
            Vector3 a = transform.TransformPoint(offHandFrom), b = transform.TransformPoint(offHandTo);
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-8f ? Mathf.Clamp01(Vector3.Dot(worldPoint - a, ab) / ab.sqrMagnitude) : 0f;
            return a + ab * t;
        }
    }
}
