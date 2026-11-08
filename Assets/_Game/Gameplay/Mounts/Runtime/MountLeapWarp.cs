using Kinemation.MotionWarping.Runtime.Core;
using UnityEngine;
using UnityEngine.Events;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// The Motion Warping component the leap off a mount uses, made in code (owner, 7 Nov 2026 retest 3: one made with
    /// AddComponent had null UnityEvents, so Play threw after it had started, the warp never ended and Stop threw every
    /// frame - the body flew off, the roll never came and the ride locked). Made here with everything the vendor
    /// component expects from its inspector: its events, what it moves, no play-rate scaling, no Animator play.
    /// </summary>
    public static class MountLeapWarp
    {
        public static MotionWarping Create(Transform parent, Transform warped)
        {
            if (parent == null || warped == null) return null;
            var holder = new GameObject("Mount Leap Warp");
            holder.transform.SetParent(parent, false);
            var warp = holder.AddComponent<MotionWarping>();
            warp.onWarpStarted = new UnityEvent();
            warp.onWarpEnded = new UnityEvent();
            const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(MotionWarping);
            var target = type.GetField("transformToWarp", Private);
            if (target == null)
            {
                Debug.LogError("[Mounts] The Motion Warping package changed (no transformToWarp): the leap is off.");
                Object.Destroy(holder);
                return null;
            }
            target.SetValue(warp, warped);
            type.GetField("scalePlayRate", Private)?.SetValue(warp, false);
            type.GetField("playAnimator", Private)?.SetValue(warp, false);
            return warp;
        }

        /// <summary>
        /// The leap's landing: a warp point that follows <paramref name="mark"/> (so a running body is caught). Its
        /// position and rotation are in the MARK'S OWN space - the vendor reads a point with a transform as
        /// transform.TransformPoint(position) and transform.rotation * rotation. Owner, 7 Nov 2026 (leap proof run):
        /// the mark's world position was passed there, so the target lay as far again from the world's origin as the
        /// player (~11 m standing at z -12, ~63 m running) and faced backwards; the body flew there and snapped back.
        /// </summary>
        public static Kinemation.MotionWarping.Runtime.Utility.WarpPoint LandingPoint(Transform mark)
        {
            return new Kinemation.MotionWarping.Runtime.Utility.WarpPoint
            {
                transform = mark,
                position = Vector3.zero,
                rotation = Quaternion.identity,
            };
        }
    }
}
