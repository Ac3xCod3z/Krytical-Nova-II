using UnityEngine;
using PSD.Creator.Objects;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CTFX2.Creator.Objects
{
    [CreateAssetMenu(fileName = "Smoke Trail", menuName = "CTFX2/Components/Smoke Trail")]
#if UNITY_EDITOR
    [CanEditMultipleObjects]
#endif
    public class SmokeTrailObject : ParticleObject
    {
    }
}
