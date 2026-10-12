using Unity.Cinemachine;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>
    /// The view from above while a Hall is arranged: a camera looking straight down on the lawns or the room, with
    /// the gate at the foot of the screen. It is one more Cinemachine camera with the say over the player's own, so
    /// the game is still seen through the same camera and everything that asks for it keeps working. It is moved
    /// across the ground and brought nearer or further by whoever holds it.
    /// </summary>
    public sealed class ArrangeOverhead
    {
        private const int Priority = 500;

        private CinemachineCamera rig;
        private Vector3 focus;
        private float height;
        private Vector2 heightRange;
        private float panSpeed, startHeight;
        private Vector2 reach;
        private float middleX, middleZ;

        public bool Active => rig != null;

        /// <summary>Looks down on a place. <paramref name="halfReach"/> is how far from it the view may be moved.</summary>
        public void Begin(Vector3 middle, Vector2 halfReach, HousingSettings settings, Camera through)
        {
            End();
            focus = middle;
            middleX = middle.x;
            middleZ = middle.z;
            reach = halfReach;
            startHeight = height = settings != null ? settings.overheadHeight : 34f;
            heightRange = settings != null ? settings.overheadHeightRange : new Vector2(10f, 70f);
            panSpeed = settings != null ? settings.overheadPanSpeed : 22f;

            var holder = new GameObject("Arrange Overhead Camera");
            rig = holder.AddComponent<CinemachineCamera>();
            if (through != null) rig.Lens = LensSettings.FromCamera(through);
            rig.Priority = new PrioritySettings { Enabled = true, Value = Priority };
            Apply();
        }

        public void End()
        {
            if (rig != null) Object.Destroy(rig.gameObject);
            rig = null;
        }

        /// <summary>Moves the view: across the ground (each of x and y from -1 to 1) and nearer or further (the wheel).</summary>
        public void Move(Vector2 across, float nearer, float seconds)
        {
            if (rig == null) return;
            height = Mathf.Clamp(height - nearer * height * 0.12f, heightRange.x, heightRange.y);
            // The same sweep of the screen whatever the height.
            float speed = panSpeed * height / Mathf.Max(1f, startHeight);
            focus.x = Mathf.Clamp(focus.x + across.x * speed * seconds, middleX - reach.x, middleX + reach.x);
            focus.z = Mathf.Clamp(focus.z + across.y * speed * seconds, middleZ - reach.y, middleZ + reach.y);
            Apply();
        }

        private void Apply()
        {
            rig.transform.SetPositionAndRotation(focus + Vector3.up * height, Quaternion.Euler(90f, 0f, 0f));
        }
    }
}
