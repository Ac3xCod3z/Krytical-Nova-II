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
    }
}
