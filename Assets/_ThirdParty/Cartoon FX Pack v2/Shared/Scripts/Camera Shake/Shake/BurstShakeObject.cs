using UnityEngine;

namespace CTFX2.CameraShake
{
    [CreateAssetMenu(fileName = "Burst Shake", menuName = "CTFX2/Camera Shake/Burst")]
    public class BurstShakeObject : CameraShakeObject
    {
        public override IShake GetShake(Vector3 direction, Vector3 rotation) => new Burst(burst, direction, rotation);
        [SerializeField] private BurstSettings burst;
    }
}