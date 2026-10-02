using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Marks something that parts grass as it moves through it (players, NPCs, mobs).
    /// Presentation only — remote players already have synced transforms, so nothing is networked.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoliageInteractor : MonoBehaviour
    {
        [Tooltip("Metres around the feet in which grass bends away.")]
        [Range(0.2f, 3f)] public float radius = 0.9f;

        internal static readonly List<FoliageInteractor> Active = new List<FoliageInteractor>(64);

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);
    }

    /// <summary>
    /// Publishes the interactors nearest the camera to the global <c>_KN_Interactors</c> array read by
    /// KryticalNova/Foliage Wind. One SetGlobalVectorArray per frame, no allocations.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class FoliageInteractorSystem : MonoBehaviour
    {
        public const int MaxInteractors = 8; // must match KN_MAX_INTERACTORS in KNWind.hlsl
        private const float MaxDistance = 70f;

        private static readonly int InteractorsId = Shader.PropertyToID("_KN_Interactors");
        private static readonly int CountId = Shader.PropertyToID("_KN_InteractorCount");

        private readonly Vector4[] packed = new Vector4[MaxInteractors];
        private readonly float[] distances = new float[MaxInteractors];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var go = new GameObject("FoliageInteractorSystem") { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<FoliageInteractorSystem>();
        }

        private void OnDisable() => Shader.SetGlobalInt(CountId, 0);

        private void LateUpdate()
        {
            var cam = Camera.main;
            Vector3 origin = cam != null ? cam.transform.position : Vector3.zero;
            int count = 0;

            // Keep the N nearest (insertion into a tiny sorted array).
            var list = FoliageInteractor.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var it = list[i];
                Vector3 p = it.transform.position;
                float d = (p - origin).sqrMagnitude;
                if (d > MaxDistance * MaxDistance) continue;
                if (count == MaxInteractors && d >= distances[count - 1]) continue;

                int slot = count < MaxInteractors ? count++ : MaxInteractors - 1;
                while (slot > 0 && distances[slot - 1] > d)
                {
                    distances[slot] = distances[slot - 1];
                    packed[slot] = packed[slot - 1];
                    slot--;
                }
                distances[slot] = d;
                packed[slot] = new Vector4(p.x, p.y, p.z, it.radius);
            }

            Shader.SetGlobalVectorArray(InteractorsId, packed);
            Shader.SetGlobalInt(CountId, count);
        }
    }
}
