using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Objects
{
    [CreateAssetMenu(fileName = "Debris Mesh", menuName = "CTFX2/Components/Debris/Debris Mesh")]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class DebrisMeshObject : DebrisObject
    {
        public Mesh[] meshes;
    }
}
