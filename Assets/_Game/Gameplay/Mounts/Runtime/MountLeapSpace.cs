using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// The leap off a mount happens in the player's own space (owner, 8 Nov 2026 leap proof run 1: running, the body fell
    /// up to 6.6 m behind the player and caught up). Kinemation's warp writes a world pose and holds the body on world
    /// points, while the player's root runs on at riding speed under it. So the warp moves a stand-in under a still
    /// space placed where the root was when the leap began, and every frame after the warp the body takes the stand-in's
    /// pose relative to that space - relative to the root, wherever the root has gone. Standing, nothing changes.
    /// </summary>
    [DefaultExecutionOrder(31000)] // after the warp's LateUpdate has placed the stand-in
    public sealed class MountLeapSpace : MonoBehaviour
    {
        private Transform root, body, space, stand;
        private bool following;

        /// <summary>What the warp moves.</summary>
        public Transform Stand => stand;
        public bool Following => following;

        public static MountLeapSpace Create(GameObject holder, Transform root, Transform body)
        {
            if (holder == null || root == null || body == null) return null;
            var leap = holder.AddComponent<MountLeapSpace>();
            leap.root = root;
            leap.body = body;
            leap.space = new GameObject("Mount Leap Space (" + root.name + ")").transform; // in no parent: it stays still
            leap.stand = new GameObject("Mount Leap Stand-in").transform;
            leap.stand.SetParent(leap.space, false);
            return leap;
        }

        /// <summary>The leap starts: the space is put where the root is, the stand-in where the body is.</summary>
        public void Begin()
        {
            space.SetPositionAndRotation(root.position, root.rotation);
            stand.SetPositionAndRotation(body.position, body.rotation);
            following = true;
        }

        public void End() => following = false;

        /// <summary>A world point now, in the space (it moves on with the root from here).</summary>
        public Vector3 SpacePoint(Vector3 world) => space.TransformPoint(root.InverseTransformPoint(world));
        public Quaternion SpaceRotation(Quaternion world) => space.rotation * (Quaternion.Inverse(root.rotation) * world);

        /// <summary>The body takes the stand-in's pose, relative to the root.</summary>
        public void Follow()
        {
            body.SetPositionAndRotation(root.TransformPoint(space.InverseTransformPoint(stand.position)),
                root.rotation * (Quaternion.Inverse(space.rotation) * stand.rotation));
        }

        private void LateUpdate()
        {
            if (following && root != null && body != null) Follow();
        }

        private void OnDestroy()
        {
            if (space == null) return;
            if (Application.isPlaying) Destroy(space.gameObject);
            else DestroyImmediate(space.gameObject); // EditMode tests
        }
    }
}
