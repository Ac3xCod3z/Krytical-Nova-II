using KryticalNova.Player;
using KryticalNova.Player.Entities.Presentation;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// A client body's jump as the ghost flies it (M3b): the game's own warped jump clip and feel (<see cref="PlayerJumpPresentation"/>),
    /// played in place; the follow puts the root on the ghost's arc and Motion Warping never moves it (owner answer Q3 a).
    /// </summary>
    public sealed class GhostJumpView : MonoBehaviour, IGhostMotionView
    {
        PlayerJumpPresentation m_Presentation;

        // on the body's "Visuals" child in Player.prefab, not the root (GUI run 3, 31 Oct: looked for on the root, no clip ever played)
        PlayerJumpPresentation Presentation => m_Presentation != null ? m_Presentation
            : m_Presentation = GetComponentInChildren<PlayerJumpPresentation>(true);

        public void BeginJump(float clipSeconds) => Presentation?.BeginWarpedJump(clipSeconds);

        public void JumpTookOff() => Presentation?.WarpedTakeOff();

        public void JumpLanded() => Presentation?.WarpedLand();

        public void JumpCancelled() => Presentation?.ResetPresentation();

        PlayerAnimancerMotor m_Animancer;
        PlayerVaultMotor m_Vault;
        PlayerAnimancerMotor Animancer => m_Animancer != null ? m_Animancer : m_Animancer = GetComponentInChildren<PlayerAnimancerMotor>(true);
        PlayerVaultMotor Vault => m_Vault != null ? m_Vault : m_Vault = GetComponentInChildren<PlayerVaultMotor>(true);

        /// <summary>
        /// The vault's clip in place, as NGO's <c>PlayerVaultMotor.Begin</c> plays it: the body's own style for the height (faction and body
        /// type), its window stretched over the air time; the follow puts the root on the leg's curve (M3c).
        /// </summary>
        public void BeginVault(byte heightClass, float elapsedSeconds, float airSeconds)
        {
            var motor = Vault;
            var animancer = Animancer;
            if (motor == null || animancer == null || airSeconds <= 0f || !motor.TryStyleOf((VaultHeight)heightClass, out VaultStyle style) || style.clip == null) return;
            float window = Mathf.Max(0.05f, style.window.y - style.window.x);
            float speed = window / airSeconds;
            animancer.PlayWarpedAction(style.clip, style.window.x + elapsedSeconds * speed, speed, motor.FadeIn);
            var feel = KryticalNova.Core.ClientPresentationRegistry.Feel;
            if (feel != null)
            {
                feel.JumpTakeoff(transform, KryticalNova.Core.BodyRoleTag.IsMine(this, true));
                feel.Airborne(transform, true);
            }
        }

        public void EndVault(float speedMps)
        {
            var motor = Vault;
            var animancer = Animancer;
            if (animancer != null) animancer.EndWarpedAction(motor != null ? motor.FadeOut : 0.15f, speedMps);
            var feel = KryticalNova.Core.ClientPresentationRegistry.Feel;
            if (feel != null)
            {
                feel.Airborne(transform, false);
                feel.Landed(transform, KryticalNova.Core.BodyRoleTag.IsMine(this, true));
            }
        }
    }
}
