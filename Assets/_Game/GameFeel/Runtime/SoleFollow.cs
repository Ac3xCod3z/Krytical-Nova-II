using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// Keeps an effect under one foot (owner, 18 Oct 2026: the wallstride effects "directly under each foot"): at the
    /// foot's sole as it steps, turned with the body rather than the foot, so a ring laid flat stays flat on the wall
    /// while the foot swings.
    /// </summary>
    public sealed class SoleFollow : MonoBehaviour
    {
        [Tooltip("How far under the foot's bone the sole is (metres).")]
        public float below = 0.07f;

        private Transform foot, actor;

        public void Follow(Transform footBone, Transform body)
        {
            foot = footBone;
            actor = body;
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (foot == null || actor == null) return;
            transform.SetPositionAndRotation(foot.position - actor.up * below, actor.rotation);
        }
    }
}
