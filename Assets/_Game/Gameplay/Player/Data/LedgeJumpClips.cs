using KryticalNova.CharacterCreation;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>
    /// The clip a jump off a roof's edge or across a gap between roofs plays in the air (owner, 1 Nov: the Synty Sporty backflip), one per
    /// body type, as <see cref="VaultAnimationMap"/> picks by the replicated body type so every machine plays the same clip. Played in place,
    /// fitted to the leg's air time; the leg's arc moves the body.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Player/Ledge Jump Clips", fileName = "LedgeJumpClips")]
    public sealed class LedgeJumpClips : ScriptableObject
    {
        [Tooltip("Masculine bodies (an in-place clip: the jump's arc moves the body).")]
        public AnimationClip masculine;
        [Tooltip("Feminine bodies (an in-place clip).")]
        public AnimationClip feminine;

        /// <summary>The clip for a body type; the other one when it has none.</summary>
        public AnimationClip For(BodyType body)
        {
            AnimationClip own = body == BodyType.Feminine ? feminine : masculine;
            return own != null ? own : body == BodyType.Feminine ? masculine : feminine;
        }
    }
}
