using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>
    /// Every number of Crew Halls in one asset. All of them are first picks to be tuned once the building has been
    /// played with (`_Docs/Plans/crew-halls-plan.md`). Nothing in code may hard-code one.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Housing/Housing Settings")]
    public sealed class HousingSettings : ScriptableObject
    {
        [Header("The Hall sandbox (Stage 1)")]
        [Tooltip("Where the sandbox Lot is set down: far outside the starter town, beyond what its camera draws.")]
        public Vector3 sandboxOrigin = new Vector3(6500f, 0f, 0f);
        [Tooltip("The Lot that is set down there.")]
        public HousingLot lotPrefab;
        [Tooltip("The Hall that stands on the Lot's pad, by faction (index = FactionId; 0 is used when a faction has none).")]
        public GameObject[] facadeByFaction = new GameObject[4];

        [Header("Budgets")]
        [Tooltip("Budget points the front and back lawns of one Lot hold together.")]
        [Min(1)] public int lawnBudget = 200;
        [Tooltip("Budget points the inside of a Hall holds, by Hall tier (index 0 = tier 1).")]
        public int[] interiorBudgetByTier = { 300, 450, 600 };
        [Tooltip("The most of one furnishing a Furnishing Crate holds.")]
        [Min(1)] public int crateStackCap = 9999;

        [Header("Arrange")]
        [Tooltip("The grid a piece snaps to, in metres.")]
        [Min(0.05f)] public float gridStep = 0.5f;
        [Tooltip("The finer grid while the fine-step key is held.")]
        [Min(0.01f)] public float fineGridStep = 0.125f;
        [Tooltip("Degrees a piece turns at a press.")]
        [Range(1f, 90f)] public float rotateStep = 15f;
        [Tooltip("How many edits can be undone.")]
        [Range(1, 200)] public int undoSteps = 50;
        [Tooltip("How deep pieces may stand on pieces (a vase on a book on a table is 2).")]
        [Range(0, 4)] public int maxStackDepth = 3;
        [Tooltip("What the preview of a piece being set down is drawn with: a see-through, unlit material whose colour is set below.")]
        public Material ghostMaterial;
        [Tooltip("Colour of the preview where a piece may be set down, and where it may not.")]
        public Color ghostAllowed = new Color(0.35f, 1f, 0.55f, 0.55f);
        public Color ghostRefused = new Color(1f, 0.3f, 0.25f, 0.55f);

        public GameObject FacadeFor(FactionId faction)
        {
            int index = (int)faction;
            if (facadeByFaction == null || facadeByFaction.Length == 0) return null;
            GameObject own = index >= 0 && index < facadeByFaction.Length ? facadeByFaction[index] : null;
            return own != null ? own : facadeByFaction[0];
        }

        public int InteriorBudget(int tier)
        {
            if (interiorBudgetByTier == null || interiorBudgetByTier.Length == 0) return lawnBudget;
            return interiorBudgetByTier[Mathf.Clamp(tier - 1, 0, interiorBudgetByTier.Length - 1)];
        }

        public int Budget(HomeArea area, int tier = 1) => area == HomeArea.Interior ? InteriorBudget(tier) : lawnBudget;
    }
}
