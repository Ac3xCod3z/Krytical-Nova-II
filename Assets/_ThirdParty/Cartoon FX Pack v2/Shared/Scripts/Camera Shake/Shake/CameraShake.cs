using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.CameraShake
{
    [Serializable]
    public class CameraShake
    {
        [SerializeField] public CameraShakeObject cameraShake;

        public bool HasValue() => cameraShake != null;

        private IShake shake;

        /// <summary>
        /// Perform Camera shake.
        /// </summary>
        /// <param name="transform">The transform to apply the shake to.</param>
        public void Shake(Transform transform)
        {
            Vector3 direction = UnityEngine.Random.insideUnitSphere;
            Vector3 rotation = UnityEngine.Random.insideUnitSphere;

            shake = cameraShake.GetShake(direction, rotation);
            shake?.Init(transform);
        }

        public bool Step() => shake?.Step() ?? false;
        public void Dispose() => shake?.Dispose();
    }
}