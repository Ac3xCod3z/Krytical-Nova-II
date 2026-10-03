using UnityEngine;
using PSD.Creator.Objects;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Objects
{
    [CreateAssetMenu(fileName = "Halftone", menuName = "CTFX2/Components/Halftone")]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class HalftoneObject : BillboardObject
    { }
}
