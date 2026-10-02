using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Vegetation.Grass
{
    /// <summary>
    /// Runtime "no grass here" zone with a soft edge (spawn points, quest areas, building sites). Applied by the GPU
    /// every frame, so moving or toggling it takes effect immediately; no re-bake needed. Static roads and buildings
    /// are already handled by the baked mask.
    /// </summary>
    [ExecuteAlways]
    public sealed class GrassExclusionVolume : MonoBehaviour
    {
        public enum Shape { Box, Sphere }

        public Shape shape = Shape.Box;
        [Tooltip("Box: half size on X/Z. Sphere: x = radius.")]
        public Vector2 size = new Vector2(2f, 2f);
        [Tooltip("Metres over which grass fades back in outside the volume.")]
        [Min(0f)] public float falloff = 1.5f;

        internal static readonly List<GrassExclusionVolume> Active = new List<GrassExclusionVolume>();

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        /// <summary>(center xz, yaw radians, shape) and (half size xz / radius, falloff, 0).</summary>
        internal void Pack(out Vector4 a, out Vector4 b)
        {
            var p = transform.position;
            float yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
            var scale = transform.lossyScale;
            a = new Vector4(p.x, p.z, yaw, shape == Shape.Sphere ? 1f : 0f);
            b = shape == Shape.Sphere
                ? new Vector4(size.x * Mathf.Max(scale.x, scale.z), 0f, falloff, 0f)
                : new Vector4(size.x * scale.x, size.y * scale.z, falloff, 0f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one);
            var s = transform.lossyScale;
            if (shape == Shape.Box) Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x * 2f * s.x, 0.5f, size.y * 2f * s.z));
            else Gizmos.DrawWireSphere(Vector3.zero, size.x * Mathf.Max(s.x, s.z));
        }
    }
}
