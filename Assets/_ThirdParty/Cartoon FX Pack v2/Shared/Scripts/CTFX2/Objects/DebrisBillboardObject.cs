using UnityEngine;
using PSD.Creator.Objects;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Objects
{
    [CreateAssetMenu(fileName = "Debris Billboard", menuName = "CTFX2/Components/Debris/Debris Billboard")]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class DebrisBillboardObject : DebrisObject
    {
        [Category("Flipbook")]
        [Range(0, 20)] public float speed = 1;
    }
}
