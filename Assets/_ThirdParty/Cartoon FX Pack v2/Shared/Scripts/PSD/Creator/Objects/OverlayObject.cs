using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Objects
{
    [CreateAssetMenu(fileName = "Overlay", menuName = "CTFX2/Components/Overlay", order = 1)]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class OverlayObject : BillboardObject
    {
        [Range(0, 10)] public float tiling = 1;
        public bool randomizeOffset = true;
    }
}
