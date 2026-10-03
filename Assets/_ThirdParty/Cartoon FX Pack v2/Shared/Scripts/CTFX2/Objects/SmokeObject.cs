using UnityEngine;
using PSD.Creator.Objects;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Objects
{
    [CreateAssetMenu(fileName = "Smoke", menuName = "CTFX2/Components/Smoke")]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class SmokeObject : BillboardObject
    {
        [Range(0, 1)] public float lightIntensity = 0.2f;
    }
}
