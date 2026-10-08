using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Every number of the comic icon treatment (<see cref="ComicIconTreatment"/>): how a painted pack icon is redrawn
    /// to match the game's comic look. Change a value here, then Krytical Nova ▸ Icons ▸ Bake Every Assigned Icon.
    /// Sizes are in pixels of the finished icon.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Icons/Icon Treatment Settings", fileName = "IconTreatmentSettings")]
    public sealed class IconTreatmentSettings : ScriptableObject
    {
        public const string AssetPath = "Assets/_Game/Icons/Data/Assets/IconTreatmentSettings.asset";

        [Header("Size")]
        [Tooltip("Pixels along one side of a finished icon. The other icons of the game are 256.")]
        [Min(32)] public int size = 256;
        [Tooltip("The icon is drawn this many times larger and reduced, so its edges come out smooth.")]
        [Range(1, 4)] public int supersample = 2;

        [Header("The sticker: outline, keyline and shadow, as on the game's other icons")]
        [Tooltip("Clear room round the picture for the outline, the keyline and the shadow.")]
        [Min(0f)] public float margin = 18f;
        [Tooltip("How round the corners of a square (skill) icon are.")]
        [Min(0f)] public float cornerRadius = 30f;
        [Tooltip("Width of the ink outline round the icon.")]
        [Min(0f)] public float outlineWidth = 7f;
        [Tooltip("Width of the paper line outside the ink, which keeps the icon clear of the dark HUD.")]
        [Min(0f)] public float keylineWidth = 3.5f;
        [Tooltip("How far down and to the right the hard shadow falls.")]
        [Min(0f)] public float shadowOffset = 5f;
        [Range(0f, 1f)] public float shadowAlpha = 0.55f;
        [Tooltip("The Colour Bible's ink.")]
        public Color ink = new Color32(0x14, 0x12, 0x2B, 0xFF);
        public Color paper = new Color32(0xF4, 0xEE, 0xDF, 0xFF);

        [Header("Flat colour: soft paint gathered into a few flat tones")]
        [Tooltip("How far round a pixel the paint is evened out. Larger = flatter, simpler shapes.")]
        [Range(1, 8)] public int flattenRadius = 4;
        [Range(0, 4)] public int flattenPasses = 2;
        [Tooltip("How different two colours may be and still be blended. Smaller keeps more detail and more ink lines.")]
        [Range(0.02f, 0.5f)] public float flattenTolerance = 0.14f;
        [Tooltip("How many flat tones a picture is drawn in, darkest to lightest.")]
        [Range(2, 8)] public int tones = 5;
        [Tooltip("0 = the lines between tones are fixed; 1 = they follow the picture, so a dark painting still gets every tone.")]
        [Range(0f, 1f)] public float toneAdapt = 0.35f;
        [Tooltip("How bright the darkest tone is drawn.")]
        [Range(0f, 1f)] public float toneLow = 0.24f;
        [Tooltip("How bright the lightest tone is drawn.")]
        [Range(0f, 1f)] public float toneHigh = 0.95f;
        [Tooltip("0 = each tone keeps the brightness the painting had there; 1 = the tones are spread evenly from dark to light.")]
        [Range(0f, 1f)] public float toneContrast = 0.55f;
        [Tooltip("How many times stray specks of one tone inside another are cleared away.")]
        [Range(0, 4)] public int toneSettle = 1;

        [Header("Ink lines inside the picture")]
        [Tooltip("How large a step in colour between two flat patches gets an ink line (0.24 = the two sides differ by about a quarter).")]
        [Range(0f, 1f)] public float inkEdge = 0.24f;
        [Tooltip("Width of the ink lines. About 5 reads as a 1 pixel line at hotbar size.")]
        [Min(0f)] public float inkWidth = 5f;
        [Tooltip("Ink marks smaller than this many pixels are dropped.")]
        [Min(0)] public int inkSpeck = 30;
        [Tooltip("The lightest tones are never inked over, so thin bright strokes (lightning, a blade's edge) keep their light. This is how many tones are spared.")]
        [Range(0, 3)] public int inkSpareTones = 1;

        [Header("Ben-Day dots in the shade")]
        [Tooltip("Distance between dots, on a 45 degree grid. 9 is what the game's other icons use.")]
        [Min(2f)] public float dotCell = 9f;
        [Tooltip("Size of a dot as a share of its cell.")]
        [Range(0.05f, 0.7f)] public float dotRadius = 0.36f;
        [Tooltip("How strongly the dots stand out from their ground. 0 = no dots to be seen, 1 = the darkest tone over the next lighter one.")]
        [Range(0f, 1f)] public float dotContrast = 0.5f;

        [Header("Colour, pulled toward the Colour Bible")]
        [Tooltip("How much stronger the colours are made.")]
        [Range(0.5f, 2f)] public float saturation = 1.4f;
        [Tooltip("How far every hue is moved toward the nearest colour of the palette below.")]
        [Range(0f, 1f)] public float palettePull = 0.35f;
        [Tooltip("The Colour Bible's colours and the factions' colours.")]
        public Color[] palette =
        {
            new Color32(0x4F, 0xE3, 0xF0, 0xFF), // neon cyan (Hegemony)
            new Color32(0xE0, 0x4F, 0xB8, 0xFF), // neon magenta
            new Color32(0xFF, 0xC9, 0x3C, 0xFF), // signal yellow
            new Color32(0xFF, 0x9E, 0x66, 0xFF), // sunset coral
            new Color32(0x4C, 0x52, 0xBF, 0xFF), // shadow indigo
            new Color32(0x1F, 0x6E, 0xCC, 0xFF), // sky blue
            new Color32(0xD8, 0x34, 0x3C, 0xFF), // Null red
            new Color32(0x7A, 0x4F, 0xE0, 0xFF), // Null violet
            new Color32(0xE8, 0xB0, 0x4A, 0xFF), // Covenant gold
            new Color32(0x3F, 0x8F, 0x5A, 0xFF), // Covenant green
        };
        [Tooltip("How far the darkest tone leans toward the shade colour, as the world's comic shadows do.")]
        [Range(0f, 1f)] public float shadeTint = 0.45f;
        [Tooltip("The Colour Bible's shadow indigo.")]
        public Color shadeColor = new Color32(0x4C, 0x52, 0xBF, 0xFF);

        /// <summary>The treatment's numbers for one set of icons (a set may have its colour moved).</summary>
        public ComicIconLook ToLook(IconLibrary.Set set)
        {
            var packed = new uint[palette != null ? palette.Length : 0];
            for (int i = 0; i < packed.Length; i++) packed[i] = Pack(palette[i]);
            bool move = set != null && set.moveHue;
            return new ComicIconLook
            {
                size = size, supersample = supersample,
                margin = margin, cornerRadius = cornerRadius, outlineWidth = outlineWidth, keylineWidth = keylineWidth,
                shadowOffset = shadowOffset, shadowAlpha = shadowAlpha,
                flattenRadius = flattenRadius, flattenPasses = flattenPasses, flattenTolerance = flattenTolerance,
                tones = tones, toneAdapt = toneAdapt, toneLow = toneLow, toneHigh = toneHigh, toneContrast = toneContrast, toneSettle = toneSettle,
                inkEdge = inkEdge, inkWidth = inkWidth, inkSpeck = inkSpeck, inkSpareTones = inkSpareTones,
                dotCell = dotCell, dotRadius = dotRadius, dotContrast = dotContrast,
                saturation = saturation, palettePull = palettePull, palette = packed,
                shadeTint = shadeTint, shadeColor = Pack(shadeColor), ink = Pack(ink), paper = Pack(paper),
                hueFrom = move ? set.hueFrom : 0f, hueTo = move ? set.hueTo : 0f, hueBand = move ? set.hueBand : 0f,
            };
        }

        private static uint Pack(Color colour)
        {
            Color32 c = colour;
            return ((uint)c.r << 16) | ((uint)c.g << 8) | c.b;
        }
    }
}
