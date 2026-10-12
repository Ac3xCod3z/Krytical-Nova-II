using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>
    /// Every number of Crew Halls in one asset. All of them are first picks to be tuned once the building has been
    /// played with (`_Docs/Plans/crew-halls-plan.md`). Nothing in code may hard-code one. Sizes follow the world's
    /// measure (`_Docs/Design/16_world_scale.md`).
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Housing/Housing Settings")]
    public sealed class HousingSettings : ScriptableObject
    {
        [Header("The Hall sandbox (Stage 1)")]
        [Tooltip("Where the sandbox Lot is set down: far outside the starter town, beyond what its camera draws.")]
        public Vector3 sandboxOrigin = new Vector3(6500f, 0f, 0f);
        [Tooltip("The Lot that is set down there.")]
        public HousingLot lotPrefab;
        [Tooltip("Not used since Facades are looks in the catalog; kept so an older settings asset still loads.")]
        public GameObject[] facadeByFaction = new GameObject[4];

        [Header("The trip (to the Hall, through its door, and back)")]
        [Tooltip("The least a trip takes, in seconds. The place arrived at is made and warmed meanwhile, behind the curtain.")]
        [Min(0f)] public float travelSeconds = 1.2f;
        [Tooltip("Seconds the curtain takes to close, and to open again.")]
        [Min(0.01f)] public float curtainSeconds = 0.25f;
        [Tooltip("How many furnishings are made in one frame while a Hall is warmed.")]
        [Range(1, 200)] public int warmPiecesPerFrame = 24;

        [Header("The room inside (an instance: made when someone goes in, gone when they leave)")]
        [Tooltip("Where the room is made, from the Lot's middle. It is bigger on the inside: out of sight of the Lot, which is not drawn while a player is indoors.")]
        public Vector3 roomOffset = new Vector3(0f, -150f, 0f);
        [Tooltip("Width and depth of the room, by Hall tier (index 0 = tier 1), in metres. Multiples of 5: the packs build walls and floors in 2.5 m and 5 m pieces.")]
        public Vector2[] roomSizeByTier = { new Vector2(20f, 15f), new Vector2(25f, 20f), new Vector2(30f, 25f) };
        [Tooltip("Height of the room, by Hall tier: a hall is two storeys high, and the packs build a storey 3 m high.")]
        public float[] roomHeightByTier = { 6f, 6f, 9f };
        [Tooltip("Metres between one floor's ceiling and the floor above it. The three floors inside are made one above the other.")]
        [Min(0.1f)] public float floorGap = 1f;
        [Tooltip("Width and depth of the place a stair takes on a floor, in metres.")]
        [Min(1f)] public float stairSize = 3f;
        [Tooltip("The stair that stands where a floor's way up is, by faction (index = FactionId; 0 when a faction has none).")]
        public GameObject[] roomStairsByFaction = new GameObject[4];
        [Tooltip("Width and height of the Hall's door, inside and out.")]
        public Vector2 doorSize = new Vector2(2.4f, 3.2f);
        [Tooltip("How near the door a player goes through it, in metres.")]
        [Min(0.3f)] public float doorRadius = 1.2f;
        [Tooltip("How far inside the door a player arrives.")]
        [Min(0.5f)] public float doorStep = 2.5f;
        [Tooltip("The door that stands in the room's front wall, by faction (index = FactionId; 0 when a faction has none).")]
        public GameObject[] roomDoorByFaction = new GameObject[4];
        [Tooltip("What the room is made of where a Finish is missing, and the dark ground it stands on.")]
        public Material roomPlainMaterial;
        public Material roomSurroundMaterial;
        [Tooltip("Marks the way through a door.")]
        public Material doorMarkMaterial;

        [Header("Standing in the way")]
        [Tooltip("A piece lower than this is stepped over and stops nobody (a knee-high step is about 0.35 m).")]
        [Min(0.05f)] public float blockHeight = 0.35f;
        [Tooltip("The height a walker takes up: what of a piece is within it is what stands in the way (a tree's trunk, not its crown).")]
        [Min(0.5f)] public float walkBand = 2.1f;
        [Tooltip("How near a walker's middle a piece may be set down: nobody is built into a wardrobe.")]
        [Min(0f)] public float standClear = 0.45f;

        [Header("Lights")]
        [Tooltip("How many furnishing lights are lit at once indoors, and on the lawns at night: the ones nearest the camera.")]
        [Range(0, 24)] public int interiorLights = 10;
        [Range(0, 12)] public int lawnLights = 4;
        [Tooltip("Reach and strength of a furnishing's light.")]
        [Min(0.5f)] public float pieceLightRange = 7f;
        [Min(0f)] public float pieceLightIntensity = 9f;
        [Tooltip("The colour of a furnishing's light, by faction (index = FactionId).")]
        public Color[] lightColorByFaction =
        {
            new Color(1f, 0.82f, 0.6f), new Color(0.75f, 0.9f, 1f), new Color(0.72f, 0.62f, 1f), new Color(1f, 0.8f, 0.55f),
        };
        [Tooltip("The room's own light, so that an unfurnished room is not black at night: strength, and how many across and deep.")]
        [Min(0f)] public float roomLightIntensity = 14f;
        public Color roomLightColor = new Color(1f, 0.94f, 0.84f);

        [Header("Budgets")]
        [Tooltip("Budget points the front and back lawns of one Lot hold together.")]
        [Min(1)] public int lawnBudget = 200;
        [Tooltip("Budget points the inside of a Hall holds, all three floors together, by Hall tier (index 0 = tier 1).")]
        public int[] interiorBudgetByTier = { 400, 500, 600 };
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
        [Tooltip("Colour of the frame round a piece the cursor is on, and round the pieces picked together.")]
        public Color frameHover = new Color(1f, 1f, 1f, 0.8f);
        public Color framePicked = new Color(0.4f, 0.85f, 1f, 0.95f);

        [Header("Arrange: Advanced")]
        [Tooltip("The smallest and largest a piece may be made, as a part of its true size.")]
        [Range(0.25f, 1f)] public float minPieceScale = 0.5f;
        [Range(1f, 3f)] public float maxPieceScale = 2f;
        [Tooltip("What a press makes a piece larger or smaller by.")]
        [Range(0.015625f, 0.5f)] public float scaleStep = 0.125f;
        [Tooltip("What a press raises or lowers a piece by, in metres, and with the fine-step key.")]
        [Min(0.01f)] public float liftStep = 0.25f;
        [Min(0.01f)] public float fineLiftStep = 0.0625f;
        [Tooltip("Degrees a press tilts or leans a piece, and with the fine-step key.")]
        [Range(1f, 90f)] public float tiltStep = 15f;
        [Range(0.5f, 15f)] public float fineTiltStep = 5f;
        [Tooltip("The most pieces picked up together.")]
        [Range(2, 200)] public int maxGroup = 60;

        [Header("Arrange: the view from above")]
        [Tooltip("How high the camera starts, how low and high it goes, and how fast it is moved (metres a second at the starting height).")]
        [Min(5f)] public float overheadHeight = 34f;
        public Vector2 overheadHeightRange = new Vector2(10f, 70f);
        [Min(1f)] public float overheadPanSpeed = 22f;

        public GameObject RoomDoorFor(FactionId faction)
        {
            int index = (int)faction;
            if (roomDoorByFaction == null || roomDoorByFaction.Length == 0) return null;
            GameObject own = index >= 0 && index < roomDoorByFaction.Length ? roomDoorByFaction[index] : null;
            return own != null ? own : roomDoorByFaction[0];
        }

        public GameObject RoomStairsFor(FactionId faction)
        {
            int index = (int)faction;
            if (roomStairsByFaction == null || roomStairsByFaction.Length == 0) return null;
            GameObject own = index >= 0 && index < roomStairsByFaction.Length ? roomStairsByFaction[index] : null;
            return own != null ? own : roomStairsByFaction[0];
        }

        public Color LightColorFor(FactionId faction)
        {
            int index = (int)faction;
            if (lightColorByFaction == null || lightColorByFaction.Length == 0) return Color.white;
            return lightColorByFaction[Mathf.Clamp(index, 0, lightColorByFaction.Length - 1)];
        }

        public Vector2 RoomSize(int tier)
        {
            if (roomSizeByTier == null || roomSizeByTier.Length == 0) return new Vector2(16f, 12f);
            return roomSizeByTier[Mathf.Clamp(tier - 1, 0, roomSizeByTier.Length - 1)];
        }

        public float RoomHeight(int tier)
        {
            if (roomHeightByTier == null || roomHeightByTier.Length == 0) return 5f;
            return roomHeightByTier[Mathf.Clamp(tier - 1, 0, roomHeightByTier.Length - 1)];
        }

        public int InteriorBudget(int tier)
        {
            if (interiorBudgetByTier == null || interiorBudgetByTier.Length == 0) return lawnBudget;
            return interiorBudgetByTier[Mathf.Clamp(tier - 1, 0, interiorBudgetByTier.Length - 1)];
        }

        public int Budget(HomeArea area, int tier = 1) => HomeAreas.IsInside(area) ? InteriorBudget(tier) : lawnBudget;
    }
}
