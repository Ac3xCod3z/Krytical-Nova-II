using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// A gear part that moves by real cloth physics (owner, 24 Oct 2026, Hollow Warden's cloak: bone chains let the arms
    /// through it; "use real cloth physics"): it is drawn as its own piece with Unity's <see cref="Cloth"/>, pinned along
    /// its top (the drape on the shoulders) and free below, kept off the body by capsules on the arms, hands, trunk and
    /// legs. Sits in Resources/Meshes/PartCloth as the part's name plus "_Cloth", where <see cref="GearCloth"/> finds it.
    /// </summary>
    public sealed class GearClothLook : ScriptableObject
    {
        [Header("Where it is pinned (the part's own rest height, metres)")]
        [Tooltip("Cloth above this height stays where the skin puts it: the drape on the shoulders.")]
        public float pinAbove = 1.40f;
        [Tooltip("Below the pin line the cloth is let go over this many metres, so the drape eases into the free cloth.")]
        public float ease = 0.10f;
        [Tooltip("How far the lowest cloth may move from where the skin puts it, metres. Grows from 0 at the pin line.")]
        public float maxDrop = 0.45f;

        [Header("The cloth")]
        [Range(0f, 1f)] public float stretchingStiffness = 0.95f;
        [Range(0f, 1f)] public float bendingStiffness = 0.15f;
        [Range(0f, 1f)] public float damping = 0.15f;
        [Range(0f, 1f)] public float friction = 0.5f;
        [Tooltip("How much of the character's own movement the cloth takes along (1 = moves with it, 0 = trails behind).")]
        [Range(0f, 1f)] public float worldVelocityScale = 0.6f;
        [Range(0f, 1f)] public float worldAccelerationScale = 0.8f;
        [Tooltip("Gravity on the cloth, as a share of the world's.")]
        public float gravity = 1f;

        [Header("What it keeps off")]
        [Tooltip("Capsule radius on the upper arms, forearms and hands, metres.")]
        public float armRadius = 0.075f;
        [Tooltip("Capsule radius on the trunk (pelvis to neck), metres.")]
        public float trunkRadius = 0.16f;
        [Tooltip("Capsule radius on the thighs and calves, metres.")]
        public float legRadius = 0.09f;
    }
}
