using System;
using KryticalNova.CharacterCreation;
using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>The kinds of a route's jump (part 5b), each with its own clip in <see cref="LedgeJumpClips"/>.</summary>
    public enum LedgeJumpKind : byte
    {
        /// <summary>Over a roof's edge with no walk down (NGO <c>TryPlanDrop</c>).</summary>
        EdgeDrop,
        /// <summary>Across a gap between roofs, or down a drop between them (a NavMeshLink on the walk, NGO <c>CrossLinks</c>).</summary>
        Gap,
    }

    /// <summary>
    /// The clip each kind of a route's jump plays in the air, per body type, as <see cref="VaultAnimationMap"/> picks by the replicated body
    /// type so every machine plays the same clip. Played in place, fitted to the leg's air time; the leg's arc moves the body. A kind with no
    /// clip plays the game's own jump clip (owner, 2 Nov: the backflip only off a roof's edge; the gaps keep the normal jump).
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Player/Ledge Jump Clips", fileName = "LedgeJumpClips")]
    public sealed class LedgeJumpClips : ScriptableObject
    {
        [Serializable]
        public struct BodyClips
        {
            [Tooltip("Masculine bodies (an in-place clip: the jump's arc moves the body). Empty: the normal jump.")]
            public AnimationClip masculine;
            [Tooltip("Feminine bodies (an in-place clip). Empty: the masculine one, else the normal jump.")]
            public AnimationClip feminine;

            public AnimationClip For(BodyType body)
            {
                AnimationClip own = body == BodyType.Feminine ? feminine : masculine;
                return own != null ? own : body == BodyType.Feminine ? masculine : feminine;
            }
        }

        [Tooltip("Off a roof's edge with no walk down: our roof flip, a 3/4 backward turn that ends lying flat, ONE clip for every body " +
                 "(owner v4, 7 Nov 2026: no masculine / feminine versions; was the Synty Sporty backflip). Played at its own speed. Empty: the normal jump.")]
        public AnimationClip edgeDrop;
        [Tooltip("After the roof-edge flip on a tall drop: the freefall loop, lying flat, one clip for every body (owner v4, 7 Nov 2026). " +
                 "Empty: no freefall, the fall loop follows the flip.")]
        public AnimationClip edgeDropFreefall;
        [Tooltip("Across a gap or down a drop between roofs. Empty: the normal jump (owner, 2 Nov).")]
        public BodyClips gap;

        /// <summary>The clip for a kind and a body type (the edge drop has one for every body); null = the game's own jump clip.</summary>
        public AnimationClip For(LedgeJumpKind kind, BodyType body) => kind == LedgeJumpKind.EdgeDrop ? edgeDrop : gap.For(body);

        /// <summary>The roof-edge drop's freefall loop (every body); null = none (the fall loop follows the flip).</summary>
        public AnimationClip Freefall => edgeDropFreefall;
    }
}
