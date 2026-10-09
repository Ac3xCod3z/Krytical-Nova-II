using UnityEngine;

namespace KryticalNova.Pets
{
    /// <summary>
    /// Every number of the pet system in one place. The defaults are the design values of
    /// <c>_Docs/Design/12_pet_evolution_system.md</c>; change them here, never in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Pets/Pet Settings")]
    public sealed class PetSettings : ScriptableObject
    {
        [Header("Growth")]
        [Tooltip("Pet XP that takes the growth meter from 0 % to 100 %.")]
        [Min(1f)] public float xpToFullGrowth = 100_000f;
        [Tooltip("Pet XP for each Hostile the owner kills.")]
        [Min(0f)] public float xpPerHostileKill = 0.04f;
        [Tooltip("Pet XP for each pet on a winning team of a PvP event (wars). Not awarded by anything yet.")]
        [Min(0f)] public float xpPerEventWin = 50f;
        [Tooltip("Pet XP for each pet on a losing team of a PvP event.")]
        [Min(0f)] public float xpPerEventLoss = 25f;

        [Header("Hunger")]
        [Tooltip("Percent of the hunger meter lost per minute while the owner is online and the pet is out.")]
        [Min(0f)] public float hungerDrainPercentPerMinute = 0.05f;
        [Tooltip("The owner is warned once when hunger falls to this percent.")]
        [Range(0f, 100f)] public float hungerWarningPercent = 20f;
        [Tooltip("Seconds between the server writing the pet's hunger back to the item. Play shorter than this is not counted when the pet is put away.")]
        [Min(1f)] public float hungerWriteSeconds = 10f;

        [Header("Looks: the glow of an evolved pet (how strong at each stage is on the pet's own stages)")]
        [Tooltip("Soft sparkles that come off an evolved pet's body, tinted with the stage's glow colour. Built by the game feel builder.")]
        public GameObject auraPrefab;
        [Tooltip("Brightness of the halo of light round an evolved pet's outline, at the first evolved stage (x) and at the last (y). Above 1 the inner edge burns towards white.")]
        public Vector2 rimIntensity = new Vector2(0.7f, 1.7f);
        [Tooltip("How far the halo reaches from the pet's outline, in metres: first evolved stage (x), last (y).")]
        public Vector2 rimWidth = new Vector2(0.05f, 0.11f);
        [Tooltip("How deep the halo breathes, as the share of its brightness lost at the low point of a breath: first evolved stage (x), last (y).")]
        public Vector2 rimPulse = new Vector2(0.35f, 0.55f);
        [Tooltip("Breaths per second: first evolved stage (x), last (y).")]
        public Vector2 rimPulseSpeed = new Vector2(0.45f, 0.75f);
        [Tooltip("How much wider the halo is at the top of a breath, as a share of its width: first evolved stage (x), last (y).")]
        public Vector2 rimPulseWidth = new Vector2(0.2f, 0.4f);
        [Tooltip("Sparkles per second at the first evolved stage (x) and at the last (y).")]
        public Vector2 sparkleRate = new Vector2(3f, 16f);

        [Header("Animation: a pet whose model has animations of its own (today the Kitsune Spirit)")]
        [Tooltip("Such a pet plays its animations in turn, one every this many seconds, and rests in between. Keep it longer than the longest animation (2.5 s today). 0 = it only rests.")]
        [Min(0f)] public float animationCycleSeconds = 10f;

        [Header("Follow: where a pet hovers beside its owner (which side and how high is the pet item's own offset)")]
        [Tooltip("Half the width of the owner's head and hair at the height pets hover, in metres from the owner's centre line. The clear air below starts here.")]
        [Min(0f)] public float ownerHalfWidth = 0.18f;
        [Tooltip("Clear air between the owner and the nearest edge of the pet, in metres. The same at every growth stage: a pet that grows moves out and up, never in or down. Each pet can add its own adjustment.")]
        public float followGap = 0.07f;
        [Tooltip("Extra clear air for each unit a pet has grown beyond its base size (a pet at 1.35x gets 0.35 of this), on top of the reach of its glow, which always counts as part of the pet. A big, bright pet stands further off.")]
        [Min(0f)] public float followGapGrowth = 0.25f;
        [Tooltip("A pet nearer to the camera than this (metres, from its near side) shrinks out of view until the camera moves away, so it never fills the screen or cuts through the lens. 0 = never.")]
        [Min(0f)] public float cameraClearance = 0.6f;

        public PetTuning Tuning => new PetTuning(xpToFullGrowth, xpPerHostileKill, xpPerEventWin, xpPerEventLoss, hungerDrainPercentPerMinute);
    }
}
