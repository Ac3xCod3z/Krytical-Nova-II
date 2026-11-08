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
    /// It moves the stand-in of a <see cref="MountLeapSpace"/> (the player's own space), never the body itself.
    /// </summary>
    public static class MountLeapWarp
    {
        public static MotionWarping Create(Transform root, Transform body, out MountLeapSpace space)
        {
            space = null;
            if (root == null || body == null) return null;
            var holder = new GameObject("Mount Leap Warp");
            holder.transform.SetParent(root, false);
            space = MountLeapSpace.Create(holder, root, body);
            var warp = holder.AddComponent<MotionWarping>();
            warp.onWarpStarted = new UnityEvent();
            warp.onWarpEnded = new UnityEvent();
            const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(MotionWarping);
            var target = type.GetField("transformToWarp", Private);
            if (target == null)
            {
                Debug.LogError("[Mounts] The Motion Warping package changed (no transformToWarp): the leap is off.");
                Destroy(holder);
                space = null;
                return null;
            }
            target.SetValue(warp, space.Stand);
            type.GetField("scalePlayRate", Private)?.SetValue(warp, false);
            type.GetField("playAnimator", Private)?.SetValue(warp, false);
            return warp;
        }

        private static void Destroy(GameObject go)
        {
            if (Application.isPlaying) Object.Destroy(go);
            else Object.DestroyImmediate(go);
        }

        /// <summary>
        /// A warp point at a world pose (the leap's points are taken into the still leap space first, so they are
        /// fixed there). Owner, 7 Nov 2026 (leap proof run): a point that FOLLOWS a transform is read by the vendor in
        /// that transform's own space (transform.TransformPoint(position), transform.rotation * rotation); the mark's
        /// world position was passed there and the body flew ~11 m / ~63 m. The leap uses only fixed points now.
        /// </summary>
        public static Kinemation.MotionWarping.Runtime.Utility.WarpPoint At(Vector3 position, Quaternion rotation)
        {
            return new Kinemation.MotionWarping.Runtime.Utility.WarpPoint { transform = null, position = position, rotation = rotation };
        }
    }
}
