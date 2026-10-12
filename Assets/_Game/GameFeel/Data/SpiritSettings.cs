using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The spirit that leaves a body when it dies: which effect for which kind of body, when and where it is let go,
    /// and how big. The effects are made by Krytical Nova ▸ GameFeel ▸ Rebuild Spirit Effects from the Cartoon FX
    /// Remaster pack's Souls Escape, in three colours taken from the Synty swatch.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/GameFeel/Spirit Settings")]
    public sealed class SpiritSettings : ScriptableObject
    {
        [Header("Effects")]
        [Tooltip("A creature: several spirits burst out of the body. Light green.")]
        public GameObject creature;
        [Tooltip("A person of flesh: one small spirit rises from the mouth. Light blue.")]
        public GameObject living;
        [Tooltip("A robot or a skeleton: one small spirit rises from the mouth. Reddish.")]
        public GameObject unliving;

        [Header("When")]
        [Tooltip("Seconds after a creature dies before its spirits burst out.")]
        [Min(0f)] public float creatureDelay = 0.35f;
        [Tooltip("Seconds after a person dies before the spirit leaves: long enough for the body to be down on the ground.")]
        [Min(0f)] public float bodyDownDelay = 1.6f;
        [Tooltip("Seconds an effect is kept before it is removed.")]
        [Min(1f)] public float lifetime = 5f;

        [Header("Where and how big")]
        [Tooltip("Size of a creature's spirits against the effect as built, for a body 1.8 m tall: a taller body lets go larger ones.")]
        [Min(0.05f)] public float creatureSize = 1f;
        [Tooltip("Size of the one spirit of a person against the effect as built: small enough to leave by the mouth.")]
        [Min(0.02f)] public float mouthSize = 1f;
        [Tooltip("How far in front of the head bone the mouth is, in metres along the way the face points.")]
        public float mouthAhead = 0.11f;
        [Tooltip("How far up the head from the head bone (which sits where the skull meets the neck) the mouth is, in metres.")]
        public float mouthUp = 0.06f;

        [Header("Lineages whose players are robots or skeletons (the species letters of the lineage)")]
        public string[] unlivingSpecies = { "RO", "SN" };
    }
}
