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

        [Tooltip("The plates a founder chooses the Crew tag's background from (owner, 22 Oct 2026). The tag's letters take one of the " +
                 "symbol colours. Choice 0 of each keeps the plain look: the plate in the symbol's colour, letters that read on it.")]
        public Color[] tagBackgrounds =
        {
            new Color(0.06f, 0.06f, 0.08f), new Color(0.22f, 0.24f, 0.28f), new Color(0.08f, 0.14f, 0.32f), new Color(0.10f, 0.30f, 0.62f),
            new Color(0.05f, 0.34f, 0.38f), new Color(0.10f, 0.32f, 0.16f), new Color(0.36f, 0.34f, 0.10f), new Color(0.38f, 0.10f, 0.12f),
            new Color(0.70f, 0.12f, 0.16f), new Color(0.30f, 0.12f, 0.40f), new Color(0.55f, 0.36f, 0.16f), new Color(0.92f, 0.90f, 0.84f),
        };

        /// <summary>The tag's plate: the background chosen (1 and up), or the symbol's colour for choice 0.</summary>
        public Color TagPlate(int background, int symbolColour) =>
            tagBackgrounds != null && background >= 1 && background <= tagBackgrounds.Length ? tagBackgrounds[background - 1] : SymbolColour(symbolColour);

        /// <summary>True for a tag colour choice there is a colour for (1 and up); 0 is the letters that read on the plate.</summary>
        public bool HasTagInk(int tagColour) => symbolColours != null && tagColour >= 1 && tagColour <= symbolColours.Length;

        /// <summary>The tag's letters for a choice of 1 and up (the symbol colours).</summary>
        public Color TagInk(int tagColour) => HasTagInk(tagColour) ? symbolColours[tagColour - 1] : Color.white;
        [Tooltip("Seconds a Crew invitation waits for an answer.")]
        [Min(5f)] public float inviteSeconds = 30f;
    }
}
