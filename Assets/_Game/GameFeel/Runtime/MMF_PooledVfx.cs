using KryticalNova.Core;
using KryticalNova.Networking;
using MoreMountains.Feedbacks;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// FEEL feedback: spawns a particle prefab through <see cref="NetworkCombatVFXPool"/>, so a moment's particles sit
    /// in the same MMF_Player as its camera impulse, screen pulse and sound, and are tuned in one place.
    /// The feedback plays at the position passed to the player; <see cref="GameFeelPresenter"/> also hands it the
    /// facing and (for effects that follow a character) the target before each play.
    /// </summary>
    [AddComponentMenu("")]
    [System.Serializable]
    [FeedbackPath("Particles/Pooled VFX (Krytical Nova)")]
    [FeedbackHelp("Spawns a pooled particle prefab at the feedback position, optionally snapped to the ground or riding on the target.")]
    public class MMF_PooledVfx : MMF_Feedback
    {
        public static bool FeedbackTypeAuthorized = true;
#if UNITY_EDITOR
        public override Color FeedbackColor => MMFeedbacksInspectorColors.ParticlesColor;
        public override bool EvaluateRequiresSetup() => Prefab == null;
        public override string RequiredTargetText => Prefab != null ? Prefab.name : string.Empty;
#endif

        [MMFInspectorGroup("Pooled VFX", true, 28)]
        public GameObject Prefab;
        [Tooltip("Offset in the effect's own facing (x = right, y = up, z = forward).")]
        public Vector3 Offset;
        [Tooltip("Drop the effect onto the visible ground under the position (dust, rings).")]
        public bool SnapToGround;
        [Tooltip("Parent the effect to the target so it follows the character while it plays.")]
        public bool FollowTarget;
        [Tooltip("Keep the effect upright instead of turning it with the play direction.")]
        public bool Upright;
        [Tooltip("Turn the effect's forward toward the camera (comic text and other flat, one-sided effects).")]
        public bool FaceCamera;
        [Tooltip("Uniform scale applied to the spawned instance.")]
        public float Scale = 1f;
        [Tooltip("Seconds before the instance returns to the pool. 0 = the prefab's own particle duration.")]
        public float Lifetime;

        /// <summary>Facing for the next play; set by the presenter, identity otherwise.</summary>
        [System.NonSerialized] public Quaternion Rotation = Quaternion.identity;
        /// <summary>Character the next play belongs to (needed by <see cref="FollowTarget"/>).</summary>
        [System.NonSerialized] public Transform Target;

        protected override void CustomPlayFeedback(Vector3 position, float feedbacksIntensity = 1f)
        {
            if (!Active || !FeedbackTypeAuthorized || Prefab == null) return;
            var pool = NetworkCombatVFXPool.Instance;
            if (pool == null) return;

            Quaternion rotation = Upright ? Quaternion.identity : Rotation;
            if (FaceCamera && Camera.main != null)
            {
                Vector3 toCamera = Camera.main.transform.position - position;
                if (toCamera.sqrMagnitude > 0.001f) rotation = Quaternion.LookRotation(toCamera);
            }
            GameObject instance;
            if (FollowTarget && Target != null)
            {
                Vector3 local = Target.InverseTransformPoint(Place(position, rotation));
                instance = pool.SpawnAttached(Prefab, Target, local, Quaternion.Inverse(Target.rotation) * rotation, Lifetime);
            }
            else
            {
                instance = pool.Spawn(Prefab, Place(position, rotation), rotation, Lifetime);
            }
            if (instance != null) instance.transform.localScale = Prefab.transform.localScale * Scale;
        }

        private Vector3 Place(Vector3 position, Quaternion rotation)
        {
            if (SnapToGround) position = GroundSnap.Snap(position, Target) + Vector3.up * 0.05f;
            return position + rotation * Offset;
        }
    }
}
