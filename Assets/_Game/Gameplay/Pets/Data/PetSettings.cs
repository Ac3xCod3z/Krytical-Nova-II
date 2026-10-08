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
        [Tooltip("The rim of light around an evolved pet's outline (the Temper glow material). Each pet works on a copy.")]
        public Material glowMaterial;
        [Tooltip("Rim brightness at the first evolved stage (x) and at the last (y). Above 1 it blooms as well.")]
        public Vector2 rimIntensity = new Vector2(0.9f, 2.4f);
        [Tooltip("Sparkles per second at the first evolved stage (x) and at the last (y).")]
        public Vector2 sparkleRate = new Vector2(3f, 16f);

        [Header("Follow: where a pet hovers beside its owner (which side and how high is the pet item's own offset)")]
        [Tooltip("Half the width of the owner's head and hair at the height pets hover, in metres from the owner's centre line. The clear air below starts here.")]
        [Min(0f)] public float ownerHalfWidth = 0.18f;
        [Tooltip("Clear air between the owner and the nearest edge of the pet, in metres. The same at every growth stage: a pet that grows moves out and up, never in or down. Each pet can add its own adjustment.")]
        public float followGap = 0.07f;
        [Tooltip("A pet nearer to the camera than this (metres, from its near side) shrinks out of view until the camera moves away, so it never fills the screen or cuts through the lens. 0 = never.")]
        [Min(0f)] public float cameraClearance = 0.6f;

        public PetTuning Tuning => new PetTuning(xpToFullGrowth, xpPerHostileKill, xpPerEventWin, xpPerEventLoss, hungerDrainPercentPerMinute);
    }
}
