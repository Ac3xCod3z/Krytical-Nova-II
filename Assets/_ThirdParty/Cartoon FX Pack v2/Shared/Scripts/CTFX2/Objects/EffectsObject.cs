using UnityEngine;
using PSD.Creator.Objects;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Objects
{
    [CreateAssetMenu(fileName = "Effect", menuName = "CTFX2/Components/Effect")]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class EffectsObject : BillboardObject
    {
        [Category("Flipbook")]
        [Range(0, 20)] public float speed = 1;
        public bool randomStartFrame = true;
        
        [Category("Size")]
        [Range(0, 2)] public float sizeMultiplier = 1;
    }
}
