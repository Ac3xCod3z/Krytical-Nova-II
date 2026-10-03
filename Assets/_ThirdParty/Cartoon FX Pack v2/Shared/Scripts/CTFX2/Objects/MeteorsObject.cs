using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Objects
{
    [CreateAssetMenu(fileName = "Meteors", menuName = "CTFX2/Components/Meteors")]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class MeteorsObject : DebrisMeshObject
    {
    }
}
