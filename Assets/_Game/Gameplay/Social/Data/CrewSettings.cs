using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// Every number of the Crew system in one asset. The founding terms come from the Twelve Sky reference (page 28:
    /// leader level 30, 10,000,000 Creds); the rest are proposals to be tuned. Nothing in code may hard-code one.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Social/Crew Settings")]
    public sealed class CrewSettings : ScriptableObject
    {
        [Header("Founding")]
        [Tooltip("The level a character must have reached to found a Crew.")]
        [Min(1)] public int founderLevel = 30;
        [Tooltip("Creds the founder pays. A Creds sink: check it against the income target in the Economy Settings.")]
        [Min(0)] public long foundingCost = 10_000_000;

        [Header("Size")]
        [Tooltip("Members a Crew may have at each Crew level (index 0 = level 1). Crews do not level yet, so only the first counts.")]
        public int[] memberCapByLevel = { 20, 30, 40, 50 };
        [Tooltip("How many Officers a Crew may have beside its Leader.")]
        [Min(0)] public int maxOfficers = 3;

        [Header("Text")]
        [Range(2, 8)] public int nameMinLength = 3;
        [Range(8, 24)] public int nameMaxLength = 16;
        [Range(4, 24)] public int titleMaxLength = 16;
        [Range(40, 300)] public int announcementMaxLength = 160;

        [Header("Other")]
        [Tooltip("How many preset symbols a Crew chooses from. Build Crew Symbols sets it to what every faction has a picture for.")]
        [Range(1, 24)] public int symbolCount = 12;
        [Tooltip("The picture of each symbol, in each faction's own interface art. Every picture is white, so it takes a colour.")]
        public CrewSymbolLibrary symbols;
        [Tooltip("The colours a Leader chooses the Crew's symbol from. The first is what a new Crew gets.")]
        public Color[] symbolColours =
        {
            new Color(1f, 1f, 1f), new Color(0.96f, 0.80f, 0.30f), new Color(0.95f, 0.52f, 0.20f), new Color(0.90f, 0.27f, 0.25f),
            new Color(0.93f, 0.40f, 0.68f), new Color(0.69f, 0.45f, 0.95f), new Color(0.36f, 0.56f, 0.98f), new Color(0.30f, 0.82f, 0.92f),
            new Color(0.32f, 0.86f, 0.62f), new Color(0.62f, 0.86f, 0.30f), new Color(0.72f, 0.60f, 0.46f), new Color(0.62f, 0.67f, 0.74f),
        };

        /// <summary>The colour with this number, or white for a number there is no colour for.</summary>
        public Color SymbolColour(int index) =>
            symbolColours != null && index >= 0 && index < symbolColours.Length ? symbolColours[index] : Color.white;
        [Tooltip("Seconds a Crew invitation waits for an answer.")]
        [Min(5f)] public float inviteSeconds = 30f;
    }
}
