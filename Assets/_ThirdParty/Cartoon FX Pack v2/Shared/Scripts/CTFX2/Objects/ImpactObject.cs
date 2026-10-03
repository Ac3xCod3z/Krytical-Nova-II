using UnityEngine;
using PSD.Creator.Objects;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Objects
{
    [CreateAssetMenu(fileName = "Impact", menuName = "CTFX2/Components/Impact")]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class ImpactObject : BillboardObject
    {
        [Category("Position")]
        public Vector2 offset;
    }
}
