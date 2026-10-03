using System.Collections.Generic;
using Animancer;
using BoingKit;
using KryticalNova.Core;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>Animancer owns flip rotation; Boing owns scale on a separate visual transform. Navigation never flips.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerJumpPresentation : MonoBehaviour, IAnimationClipSource
    {
        private const float MinimumScale = 0.25f;
        private const float MaximumScale = 1.75f;
        private const float MinimumDuration = 0.01f;
        private const float DefaultVisualPivotHeight = 1f;
        [SerializeField] private JumpMotionData motionData;
        [SerializeField] private AnimancerComponent animancer;
        [SerializeField] private Transform squashRoot;
        [SerializeField] private PlayerAnimancerMotor animancerMotor;
        [Tooltip("Distance from the visual pivot to its feet. Keeps takeoff/landing squash planted on the ground.")]
        [SerializeField, Min(0f)] private float visualPivotHeight = DefaultVisualPivotHeight;
        private Vector3Spring scaleSpring;
        private Vector3 targetScale = Vector3.one;
        private Vector3 originalScale = Vector3.one;
        private Vector3 originalPosition;
        private AnimancerState flipState;
        public bool IsReady => (animancerMotor != null) || (motionData != null && animancer != null && squashRoot != null
            && motionData.frontFlip != null && motionData.restPose != null);

        private void Awake()
        {
            if (animancerMotor == null)
            {
                animancerMotor = GetComponentInParent<PlayerAnimancerMotor>();
            }

            if (!IsReady)
            {
                Debug.LogError("Jump presentation needs Animancer, a squash root, and flip/rest clips in JumpMotionData.", this);
                enabled = false;
                return;
            }

            if (squashRoot != null)
            {
                originalScale = squashRoot.localScale;
                originalPosition = squashRoot.localPosition;
            }
            scaleSpring.Reset(Vector3.one);
        }

        private void LateUpdate()
        {
            if (!IsReady || squashRoot == null || motionData == null) return;
            Vector3 scale = scaleSpring.TrackHalfLife(targetScale, motionData.springFrequency, motionData.springHalfLife, Time.deltaTime);
            scale = Vector3.Min(Vector3.one * MaximumScale, Vector3.Max(Vector3.one * MinimumScale, scale));
            squashRoot.localScale = Vector3.Scale(originalScale, scale);
            squashRoot.localPosition = originalPosition + Vector3.up * ((scale.y - 1f) * visualPivotHeight);
        }

        /// <summary>Sets a squashed anticipation target before takeoff.</summary>
        public void Anticipate()
        {
            if (!IsReady) return;
            if (animancerMotor != null)
            {
                // Humanoid jump starts on takeoff
                return;
            }
            PlayRestPose();
            if (motionData != null) targetScale = motionData.anticipationScale;
        }

        /// <summary>Stretches the body at launch, then lets the Boing spring settle naturally.</summary>
        public void TakeOff()
        {
            if (!IsReady) return;
            ClientPresentationRegistry.Feel?.JumpTakeoff(transform, IsLocalPlayer());
            if (animancerMotor != null)
            {
                animancerMotor.PlayJumpTakeoff();
                return;
            }
            if (motionData != null) scaleSpring.Reset(motionData.takeoffScale);
            targetScale = Vector3.one;
        }

        /// <summary>Starts the full forward flip exactly at the apex, fitting it into the remaining flight time.</summary>
        public void PlayApexFlip(float availableDuration)
        {
            if (!IsReady) return;
            if (animancerMotor != null)
            {
                animancerMotor.PlayJumpAirborne();
                return;
            }
            if (motionData != null && animancer != null && motionData.frontFlip != null)
            {
                float duration = Mathf.Max(MinimumDuration, Mathf.Min(motionData.flipDuration, availableDuration));
                flipState = animancer.Play(motionData.frontFlip);
                flipState.Time = 0f;
                flipState.Speed = motionData.frontFlip.length / duration;
            }
        }

        /// <summary>Returns to an upright pose and kicks the Boing spring into a landing bounce.</summary>
        public void Land()
        {
            if (!IsReady) return;
            ClientPresentationRegistry.Feel?.Landed(transform, IsLocalPlayer());
            if (animancerMotor != null)
            {
                animancerMotor.PlayJumpLanding();
                return;
            }
            PlayRestPose();
            targetScale = Vector3.one;
            if (motionData != null) scaleSpring.Reset(motionData.landingScale, motionData.landingScaleVelocity);
        }

        // ---- Warped jump: a chain of in-place clips, the motor moves the body with Motion Warping ----

        /// <summary>The humanoid motor has the jump clip chain, so the jump can be driven by root-motion warping.</summary>
        public bool SupportsWarpedJump => animancerMotor != null && animancerMotor.HasWarpedJump;

        /// <summary>Starts the clip at its crouch; the motor starts warping on the same frame.</summary>
        public void BeginWarpedJump()
        {
            if (SupportsWarpedJump) animancerMotor.PlayWarpedJump();
        }

        /// <summary>The feet leave the ground.</summary>
        public void WarpedTakeOff()
        {
            ClientPresentationRegistry.Feel?.JumpTakeoff(transform, IsLocalPlayer());
        }

        /// <summary>The feet touch down; the landing clip's crouch and recovery play out in place.</summary>
        public void WarpedLand()
        {
            ClientPresentationRegistry.Feel?.Landed(transform, IsLocalPlayer());
            if (animancerMotor != null) animancerMotor.PlayWarpedJumpRecovery();
        }

        private bool IsLocalPlayer()
        {
            var net = GetComponentInParent<Unity.Netcode.NetworkObject>();
            return net == null || !net.IsSpawned || net.IsOwner;
        }

        /// <summary>Restores visual pose and scale after an interrupted jump.</summary>
        public void ResetPresentation()
        {
            if (!IsReady) return;
            if (animancerMotor != null)
            {
                animancerMotor.ResetJumpAnimation();
                return;
            }
            PlayRestPose();
            targetScale = Vector3.one;
            scaleSpring.Reset(Vector3.one);
            if (squashRoot != null)
            {
                squashRoot.localScale = originalScale;
                squashRoot.localPosition = originalPosition;
            }
        }

        private void PlayRestPose()
        {
            // Never recreate an Animancer graph while its object is being disabled or destroyed.
            if (!animancer.isActiveAndEnabled)
            {
                flipState = null;
                return;
            }
            flipState?.Stop();
            flipState = null;
            animancer.Play(motionData.restPose).Time = 0f;
        }

        /// <summary>Exposes the data-owned clips to Unity and Animancer animation previews.</summary>
        public void GetAnimationClips(List<AnimationClip> results)
        {
            if (motionData == null || results == null) return;
            if (motionData.frontFlip != null) results.Add(motionData.frontFlip);
            if (motionData.restPose != null) results.Add(motionData.restPose);
        }

        private void OnDisable()
        {
            if (Application.isPlaying) ResetPresentation();
        }
    }
}
