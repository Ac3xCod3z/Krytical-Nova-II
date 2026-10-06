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

        [Header("Looks")]
        [Tooltip("Looping effect that rides on an evolved pet, tinted with the stage's glow colour. Built by the game feel builder.")]
        public GameObject auraPrefab;

        public PetTuning Tuning => new PetTuning(xpToFullGrowth, xpPerHostileKill, xpPerEventWin, xpPerEventLoss, hungerDrainPercentPerMinute);
    }
}
