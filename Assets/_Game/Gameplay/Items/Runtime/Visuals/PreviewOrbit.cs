using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The damped orbit used to look at a character: drag turns the character, the wheel zooms while the pivot rises
    /// from the body towards the face, and a pan slides the view. Every value eases with SmoothDamp, which is what
    /// gives the equipment page its feel; the character creator and character select use the same class so they
    /// move identically.
    /// </summary>
    public sealed class PreviewOrbit
    {
        public float minDistance = 1.5f, maxDistance = 4.1f;
        public float minPitch = -10f, maxPitch = 25f, defaultPitch = 6f;
        public float farPivotHeight = 0.95f, nearPivotHeight = 1.5f;
        public float panLimitX = 0.45f, panLimitDown = 0.8f, panLimitUp = 0.7f;
        public float smoothTime = 0.12f;
        public float fieldOfView = 30f;

        private float yaw, targetYaw, yawVelocity;
        private float pitch, targetPitch, pitchVelocity;
        private float distance, targetDistance, distanceVelocity;
        private Vector2 pan, targetPan, panVelocity; // metres in the camera plane (x right, y up)

        public float Distance => distance;
        /// <summary>0 at full-body framing, 1 fully zoomed in on the face.</summary>
        public float Zoom01 => Mathf.InverseLerp(maxDistance, minDistance, distance);

        public PreviewOrbit() => Snap();

        public void Rotate(float deltaYaw, float deltaPitch)
        {
            targetYaw += deltaYaw;
            targetPitch = Mathf.Clamp(targetPitch + deltaPitch, minPitch, maxPitch);
        }

        public void Zoom(float delta)
        {
            targetDistance = Mathf.Clamp(targetDistance + delta, minDistance, maxDistance);
            targetPan = ClampPan(targetPan, targetDistance); // zooming out pulls a pan back into frame
        }

        /// <summary>
        /// Slide the view by a drag of <paramref name="pixelDelta"/> (UI pixels, y down) on a viewport
        /// <paramref name="viewportHeightPixels"/> tall, so the model tracks the cursor 1:1.
        /// </summary>
        public void Pan(Vector2 pixelDelta, float viewportHeightPixels)
        {
            if (viewportHeightPixels <= 1f) return;
            float metresPerPixel = 2f * targetDistance * Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) / viewportHeightPixels;
            // Dragging right moves the model right, so the view pivot moves left.
            targetPan = ClampPan(targetPan + new Vector2(-pixelDelta.x, pixelDelta.y) * metresPerPixel, targetDistance);
        }

        public void ResetView()
        {
            targetYaw = Mathf.Round(yaw / 360f) * 360f; // shortest way back to facing the camera
            targetPitch = defaultPitch;
            targetDistance = maxDistance;
            targetPan = Vector2.zero;
        }

        /// <summary>Ease to a framing between full body (0) and face (1), re-centred, keeping the current turn.</summary>
        public void Focus(float zoom01)
        {
            targetDistance = Mathf.Lerp(maxDistance, minDistance, Mathf.Clamp01(zoom01));
            targetPitch = defaultPitch;
            targetPan = Vector2.zero;
        }

        /// <summary>Jump straight to the default view (no easing), e.g. when the view opens.</summary>
        public void Snap()
        {
            yaw = targetYaw = 0f;
            pitch = targetPitch = defaultPitch;
            distance = targetDistance = maxDistance;
            yawVelocity = pitchVelocity = distanceVelocity = 0f;
            pan = targetPan = panVelocity = Vector2.zero;
        }

        public void Tick(float deltaTime)
        {
            yaw = Mathf.SmoothDamp(yaw, targetYaw, ref yawVelocity, smoothTime, Mathf.Infinity, deltaTime);
            pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVelocity, smoothTime, Mathf.Infinity, deltaTime);
            distance = Mathf.SmoothDamp(distance, targetDistance, ref distanceVelocity, smoothTime, Mathf.Infinity, deltaTime);
            pan = Vector2.SmoothDamp(pan, targetPan, ref panVelocity, smoothTime, Mathf.Infinity, deltaTime);
        }

        /// <summary>
        /// The character's local rotation and the camera pose for the current view. <paramref name="origin"/> is the
        /// point the character stands on and <paramref name="basis"/> the stage's rotation (the character faces the
        /// camera along the stage's -Z).
        /// </summary>
        public void Evaluate(Vector3 origin, Quaternion basis, out Quaternion characterLocalRotation,
            out Vector3 cameraPosition, out Quaternion cameraRotation)
        {
            characterLocalRotation = Quaternion.Euler(0f, 180f + yaw, 0f); // face the camera
            Vector3 pivot = Vector3.up * Mathf.Lerp(farPivotHeight, nearPivotHeight, Zoom01);
            Quaternion orbit = Quaternion.Euler(pitch, 0f, 0f);
            pivot += orbit * new Vector3(pan.x, pan.y, 0f); // pan slides the pivot in the camera plane
            Vector3 offset = orbit * new Vector3(0f, 0f, -distance);
            cameraPosition = origin + basis * (pivot + offset);
            cameraRotation = basis * Quaternion.LookRotation(-offset, Vector3.up);
        }

        /// <summary>Pan range grows as you zoom in; at full-body framing there's almost none, so the model can't leave view.</summary>
        private Vector2 ClampPan(Vector2 value, float atDistance)
        {
            float zoom01 = Mathf.InverseLerp(maxDistance, minDistance, atDistance);
            float range = Mathf.Lerp(0.15f, 1f, zoom01);
            return new Vector2(Mathf.Clamp(value.x, -panLimitX * range, panLimitX * range),
                               Mathf.Clamp(value.y, -panLimitDown * range, panLimitUp * range));
        }
    }
}
