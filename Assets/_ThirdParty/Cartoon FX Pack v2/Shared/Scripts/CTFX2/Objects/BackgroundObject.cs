using UnityEngine;
using PSD.Creator.Objects;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Objects
{
    [CreateAssetMenu(fileName = "Background", menuName = "CTFX2/Components/Background")]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class BackgroundObject : BillboardObject
    { }
}
