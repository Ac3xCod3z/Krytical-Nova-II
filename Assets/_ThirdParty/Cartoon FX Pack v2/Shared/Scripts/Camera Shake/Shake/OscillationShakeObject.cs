using UnityEngine;

namespace CTFX2.CameraShake
{
    [CreateAssetMenu(fileName = "Oscillation Shake", menuName = "CTFX2/Camera Shake/Oscillation")]
    public class OscillationShakeObject : CameraShakeObject
    {
        public override IShake GetShake(Vector3 direction, Vector3 rotation) => new Oscillation(oscillation, direction, rotation);
        [SerializeField] private OscillationSettings oscillation;
    }
}