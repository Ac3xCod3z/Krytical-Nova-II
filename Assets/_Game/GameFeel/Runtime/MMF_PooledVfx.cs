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
        [Tooltip("Colour the effect like the dust of the ground it plays on (pale on paving, grey on roads, earthy on grass and dirt).")]
        public bool TintWithGroundDust;
        [Tooltip("Colour the effect with the colour of the moment it plays for (a Nova tier's), handed over by the presenter. " +
                 "Use it on effects of their own only: a pooled instance keeps the last colour it was given.")]
        public bool TintWithMoment;
        [Tooltip("A comic effect (ComicFx) is drawn in the colour ramp of the moment (a faction's, a rarity's) when the presenter hands one over. Without one it is drawn in the colours it was built in.")]
        public bool RampWithMoment;
        [Tooltip("Copies of this effect made ready before any fight, so none has to be created during one.")]
        [Min(1)] public int Warm = 2;

        /// <summary>Facing for the next play; set by the presenter, identity otherwise.</summary>
        [System.NonSerialized] public Quaternion Rotation = Quaternion.identity;
        /// <summary>Character the next play belongs to (needed by <see cref="FollowTarget"/>).</summary>
        [System.NonSerialized] public Transform Target;
        /// <summary>Colour for the next play of a <see cref="TintWithMoment"/> effect; set by the presenter.</summary>
        [System.NonSerialized] public Color MomentTint = Color.white;
        /// <summary>Ramp for the next play of a <see cref="RampWithMoment"/> effect; set by the presenter, and used once.</summary>
        [System.NonSerialized] public ComicRamp MomentRamp;
        [System.NonSerialized] public bool HasMomentRamp;
        /// <summary>
        /// For the next play only: the effect is shown if a copy lies ready in the pool and left out if none does,
        /// instead of being created. Set by the presenter for fights the local player is no part of, so that a
        /// crowd cannot make the pool grow in the middle of a fight.
        /// </summary>
        [System.NonSerialized] public bool OnlyIfReady;

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
            bool onlyIfReady = OnlyIfReady;
            OnlyIfReady = false;
            GameObject instance;
            if (FollowTarget && Target != null)
            {
                Vector3 local = Target.InverseTransformPoint(Place(position, rotation));
                Quaternion localRotation = Quaternion.Inverse(Target.rotation) * rotation;
                instance = onlyIfReady ? pool.TrySpawnAttached(Prefab, Target, local, localRotation, Lifetime)
                    : pool.SpawnAttached(Prefab, Target, local, localRotation, Lifetime);
            }
            else
            {
                instance = onlyIfReady ? pool.TrySpawn(Prefab, Place(position, rotation), rotation, Lifetime)
                    : pool.Spawn(Prefab, Place(position, rotation), rotation, Lifetime);
            }
            bool ramped = RampWithMoment && HasMomentRamp;
            HasMomentRamp = false;
            if (instance == null) return;
            instance.transform.localScale = Prefab.transform.localScale * Scale;
            // A comic effect keeps the colours it was last drawn in, so it is always told which to wear.
            if (instance.TryGetComponent(out ComicFx comic))
            {
                if (ramped) comic.Apply(MomentRamp);
                else comic.ApplyAuthored();
            }
            if (TintWithGroundDust)
                GroundSurfaces.Tint(instance.GetComponentsInChildren<ParticleSystem>(), GroundSurfaces.DustAt(position, Target));
            else if (TintWithMoment)
                GroundSurfaces.Tint(instance.GetComponentsInChildren<ParticleSystem>(), MomentTint);
        }

        private Vector3 Place(Vector3 position, Quaternion rotation)
        {
            if (SnapToGround) position = GroundSnap.Snap(position, Target) + Vector3.up * 0.05f;
            // An effect turned to the camera is still lifted straight up. Along its own up, a camera that looks
            // down would carry a word over a character's head back behind them instead.
            if (FaceCamera) rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
            return position + rotation * Offset;
        }
    }
}
