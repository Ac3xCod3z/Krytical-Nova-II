using System.Collections.Generic;
using KryticalNova.Factions;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Housing
{
    /// <summary>
    /// The room inside a Hall. It is its own place, bigger than the building that stands on the Lot, so a Facade is
    /// changed without touching it. Its own space has the origin in the middle of the floor, x across, z from the
    /// door towards the back. The walls, the floor and the ceiling are each covered by one Finish: a panel or tile
    /// repeated to fill them, so one item changes the whole of one of them. Walls the camera would have to look
    /// through, and the ceiling when the camera is above it, are not drawn (they still keep the sun out), which is
    /// what lets the room be seen and furnished from outside it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HallRoom : MonoBehaviour
    {
        private const string GroundLayerName = "Ground";
        private const float WallThickness = 0.5f, SurroundSize = 400f, SurroundDrop = 0.6f;
        private const float LightSpacing = 8f, LightHeightPart = 0.8f;
        private const float DoorMarkDepth = 1.4f, DoorMarkLift = 0.02f;
        private const float StairStep = 2.8f; // metres from a stair to where a player coming off it stands

        /// <summary>A stair on this floor: where it is, in the room's space, and the floor it leads to.</summary>
        public struct Stair
        {
            public Vector3 spot;
            public HomeArea leadsTo;
        }

        private sealed class Wall
        {
            public Vector3 middle, normal;   // in the room's space; the normal points into the room
            public float length;
            public Transform holder;
            public BoxCollider solid;
            public readonly List<Renderer> drawn = new List<Renderer>();
            public bool hidden;
        }

        private readonly Wall[] walls = new Wall[4];
        private readonly List<Renderer> ceilingDrawn = new List<Renderer>();
        private readonly Dictionary<Collider, Wall> wallOf = new Dictionary<Collider, Wall>();
        private readonly List<Stair> stairs = new List<Stair>(2);
        private HomeArea floor;
        private Vector2 size;
        private float height;
        private Vector2 doorSize;
        private float doorStep;
        private Material plain;
        private Transform floorHolder, ceilingHolder, pieceRoot, entry, lights;
        private bool ceilingHidden;

        public Vector2 Size => size;
        /// <summary>Which of the three floors inside this is.</summary>
        public HomeArea Floor => floor;
        public IReadOnlyList<Stair> Stairs => stairs;
        public float Height => height;
        public Transform PieceRoot => pieceRoot;
        /// <summary>Where a player stands on coming in: inside the door on the ground floor, by the stair on the others.</summary>
        public Transform Entry => entry;
        /// <summary>The middle of the doorway, on the floor, in the world.</summary>
        public Vector3 DoorPoint => transform.TransformPoint(new Vector3(0f, 0f, -size.y * 0.5f + 0.4f));

        /// <summary>The shape of one floor of a Hall of a tier, as the rules see it: known before the floor is made.</summary>
        public static AreaShape ShapeFor(HousingSettings settings, HomeArea floor, int tier = 1)
        {
            Vector2 size = settings.RoomSize(tier);
            var kept = new List<Rect>(3);
            // The way in is kept clear, and so is every stair with the place a player steps off it.
            if (floor == HomeArea.Interior)
                kept.Add(new Rect(-settings.doorSize.x * 0.5f - 0.3f, -size.y * 0.5f, settings.doorSize.x + 0.6f, settings.doorStep + 0.5f));
            foreach (Stair stair in StairsOf(settings, floor, tier))
            {
                float half = settings.stairSize * 0.5f;
                kept.Add(new Rect(stair.spot.x - half, stair.spot.z - half - StairStep, settings.stairSize, settings.stairSize + StairStep));
            }
            return new AreaShape { halfSize = size * 0.5f, height = settings.RoomHeight(tier), hasWalls = true, blocked = kept.ToArray() };
        }

        /// <summary>
        /// The stairs of a floor: up in the back left corner, down in the back right, so that the two ends of a
        /// stair are one above the other.
        /// </summary>
        public static List<Stair> StairsOf(HousingSettings settings, HomeArea floor, int tier = 1)
        {
            Vector2 size = settings.RoomSize(tier);
            float inset = settings.stairSize * 0.5f + 0.25f;
            var up = new Vector3(-size.x * 0.5f + inset, 0f, size.y * 0.5f - inset);
            var down = new Vector3(size.x * 0.5f - inset, 0f, size.y * 0.5f - inset);
            var list = new List<Stair>(2);
            switch (floor)
            {
                case HomeArea.Interior:
                    list.Add(new Stair { spot = up, leadsTo = HomeArea.Upper });
                    list.Add(new Stair { spot = down, leadsTo = HomeArea.Basement });
                    break;
                case HomeArea.Upper: list.Add(new Stair { spot = up, leadsTo = HomeArea.Interior }); break;
                case HomeArea.Basement: list.Add(new Stair { spot = down, leadsTo = HomeArea.Interior }); break;
            }
            return list;
        }

        /// <summary>How far above (or below) the ground floor a floor is made.</summary>
        public static Vector3 FloorOffset(HousingSettings settings, HomeArea floor, int tier = 1)
        {
            float storey = settings.RoomHeight(tier) + settings.floorGap;
            return Vector3.up * (floor == HomeArea.Upper ? storey : floor == HomeArea.Basement ? -storey : 0f);
        }

        /// <summary>Where, in the world, a player coming off the stair from another floor stands. The room's middle when no stair leads there.</summary>
        public Vector3 StairArrival(HomeArea cameFrom)
        {
            foreach (Stair stair in stairs)
                if (stair.leadsTo == cameFrom) return transform.TransformPoint(stair.spot + Vector3.back * StairStep);
            return transform.position;
        }

        /// <summary>The floor the stair under a pair of feet leads to, when they are on one.</summary>
        public bool TryStairAt(Vector3 worldFeet, float reach, out HomeArea leadsTo)
        {
            Vector3 feet = transform.InverseTransformPoint(worldFeet);
            foreach (Stair stair in stairs)
            {
                if (Vector2.Distance(new Vector2(feet.x, feet.z), new Vector2(stair.spot.x, stair.spot.z)) > reach) continue;
                leadsTo = stair.leadsTo;
                return true;
            }
            leadsTo = floor;
            return false;
        }

        /// <summary>How near the nearest stair a pair of feet is, across the floor.</summary>
        public float StairDistance(Vector3 worldFeet)
        {
            Vector3 feet = transform.InverseTransformPoint(worldFeet);
            float nearest = float.MaxValue;
            foreach (Stair stair in stairs)
                nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(feet.x, feet.z), new Vector2(stair.spot.x, stair.spot.z)));
            return nearest;
        }

        /// <summary>
        /// Makes an empty room at a place in the world: its floor to walk on, its walls to hang things on, a door,
        /// its own light. It wears nothing until <see cref="Wear"/> is called for each of its three Finishes.
        /// </summary>
        public static HallRoom Create(Vector3 position, Quaternion rotation, HousingSettings settings, FactionId faction,
            HomeArea floor = HomeArea.Interior, int tier = 1)
        {
            var holder = new GameObject("Hall " + HomeAreas.Name(floor));
            holder.transform.SetPositionAndRotation(position, rotation);
            var room = holder.AddComponent<HallRoom>();
            room.floor = floor;
            room.Build(settings, faction, tier);
            return room;
        }

        private void Build(HousingSettings settings, FactionId faction, int tier)
        {
            stairs.AddRange(StairsOf(settings, floor, tier));
            size = settings.RoomSize(tier);
            height = settings.RoomHeight(tier);
            doorSize = settings.doorSize;
            doorStep = settings.doorStep;
            plain = settings.roomPlainMaterial;
            int ground = LayerMask.NameToLayer(GroundLayerName);

            // The floor that is walked on and clicked on. What is seen of it is the Finish laid over it.
            var walked = new GameObject("Floor") { layer = ground >= 0 ? ground : 0 };
            walked.transform.SetParent(transform, false);
            var slab = walked.AddComponent<BoxCollider>();
            slab.center = new Vector3(0f, -0.5f, 0f);
            slab.size = new Vector3(size.x, 1f, size.y);

            floorHolder = Child("Floor Finish", Vector3.zero);
            ceilingHolder = Child("Ceiling Finish", new Vector3(0f, height, 0f));
            pieceRoot = Child("Pieces", Vector3.zero);
            entry = Child("Entry", floor == HomeArea.Interior || stairs.Count == 0
                ? new Vector3(0f, 0f, -size.y * 0.5f + doorStep)
                : stairs[0].spot + Vector3.back * StairStep);

            float halfX = size.x * 0.5f, halfZ = size.y * 0.5f;
            walls[0] = MakeWall("Wall Front", new Vector3(0f, 0f, -halfZ), Vector3.forward, size.x);
            walls[1] = MakeWall("Wall Back", new Vector3(0f, 0f, halfZ), Vector3.back, size.x);
            walls[2] = MakeWall("Wall Left", new Vector3(-halfX, 0f, 0f), Vector3.right, size.y);
            walls[3] = MakeWall("Wall Right", new Vector3(halfX, 0f, 0f), Vector3.left, size.y);

            if (settings.roomSurroundMaterial != null)
            {
                GameObject surround = Plain("Surround", transform, new Vector3(0f, -SurroundDrop - 0.5f, 0f),
                    new Vector3(SurroundSize, 1f, SurroundSize), settings.roomSurroundMaterial);
                foreach (Renderer renderer in surround.GetComponentsInChildren<Renderer>())
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }

            if (floor == HomeArea.Interior) MakeDoor(settings, faction);
            MakeStairs(settings, faction);
            MakeLights(settings);
        }

        // A stair is a marked place on the floor: up has the faction's stair standing on it, down a dark well.
        private void MakeStairs(HousingSettings settings, FactionId faction)
        {
            Transform holder = Child("Stairs", Vector3.zero);
            foreach (Stair stair in stairs)
            {
                bool up = stair.leadsTo == HomeArea.Upper || (floor == HomeArea.Basement && stair.leadsTo == HomeArea.Interior);
                var size3 = new Vector3(settings.stairSize, 0.02f, settings.stairSize);
                if (!up && settings.roomSurroundMaterial != null)
                    Plain("Stair Well", holder, stair.spot + Vector3.up * 0.015f, size3, settings.roomSurroundMaterial);
                if (settings.doorMarkMaterial != null)
                    Plain("Stair Mark", holder, stair.spot + new Vector3(0f, DoorMarkLift + 0.01f, -settings.stairSize * 0.5f - 0.5f),
                        new Vector3(settings.stairSize, 0.02f, 1f), settings.doorMarkMaterial);
                GameObject prefab = up ? settings.RoomStairsFor(faction) : null;
                if (prefab == null) continue;
                GameObject model = Instantiate(prefab, holder, false);
                model.name = "Stair";
                Strip(model);
                // Stood in its corner by its own box, rising towards the back wall, and no wider than its place.
                Bounds box = LocalBox(model, holder);
                float fit = Mathf.Min(1f, settings.stairSize / Mathf.Max(0.1f, Mathf.Max(box.size.x, box.size.z)));
                model.transform.localScale *= fit;
                box = LocalBox(model, holder);
                model.transform.localPosition += new Vector3(stair.spot.x - box.center.x, -box.min.y, stair.spot.z - box.center.z);
            }
        }

        private Transform Child(string childName, Vector3 localPosition)
        {
            var child = new GameObject(childName).transform;
            child.SetParent(transform, false);
            child.localPosition = localPosition;
            return child;
        }

        private Wall MakeWall(string wallName, Vector3 middle, Vector3 normal, float length)
        {
            var wall = new Wall { middle = middle, normal = normal, length = length };
            // The holder's own space: x along the wall, y up, z out of the wall into the room, the wall's face at z = 0.
            wall.holder = Child(wallName, middle);
            wall.holder.localRotation = Quaternion.LookRotation(normal, Vector3.up);
            wall.solid = wall.holder.gameObject.AddComponent<BoxCollider>();
            wall.solid.center = new Vector3(0f, height * 0.5f, -WallThickness * 0.5f);
            wall.solid.size = new Vector3(length + WallThickness * 2f, height, WallThickness);
            wallOf[wall.solid] = wall;
            return wall;
        }

        private void MakeDoor(HousingSettings settings, FactionId faction)
        {
            Transform front = walls[0].holder;
            GameObject prefab = settings.RoomDoorFor(faction);
            if (prefab != null)
            {
                GameObject door = Instantiate(prefab, front, false);
                door.name = "Door";
                Strip(door);
                // Stood against the wall's face, in its middle, on the floor: by its own box, whatever its pivot.
                Bounds box = LocalBox(door, front);
                door.transform.localPosition += new Vector3(-box.center.x, -box.min.y, -box.min.z + 0.02f);
            }
            else
            {
                Plain("Door", front, new Vector3(0f, doorSize.y * 0.5f, 0.06f), new Vector3(doorSize.x, doorSize.y, 0.12f), settings.doorMarkMaterial);
            }
            if (settings.doorMarkMaterial != null)
                Plain("Door Mark", front, new Vector3(0f, DoorMarkLift, DoorMarkDepth * 0.5f + 0.1f),
                    new Vector3(doorSize.x, 0.02f, DoorMarkDepth), settings.doorMarkMaterial);
        }

        private void MakeLights(HousingSettings settings)
        {
            if (settings.roomLightIntensity <= 0f) return;
            int across = Mathf.Max(1, Mathf.RoundToInt(size.x / LightSpacing)), deep = Mathf.Max(1, Mathf.RoundToInt(size.y / LightSpacing));
            Transform holder = lights = Child("Lights", Vector3.zero);
            for (int ix = 0; ix < across; ix++)
            for (int iz = 0; iz < deep; iz++)
            {
                var lamp = new GameObject("Room Light").AddComponent<Light>();
                lamp.transform.SetParent(holder, false);
                lamp.transform.localPosition = new Vector3((ix + 0.5f) / across * size.x - size.x * 0.5f, height * LightHeightPart,
                    (iz + 0.5f) / deep * size.y - size.y * 0.5f);
                lamp.type = LightType.Point;
                lamp.shadows = LightShadows.None;
                lamp.color = settings.roomLightColor;
                lamp.intensity = settings.roomLightIntensity;
                lamp.range = Mathf.Max(LightSpacing * 1.5f, height * 2.2f);
            }
        }

        /// <summary>
        /// Whether this is the floor its player is on. The others of the Hall stay where they are, one above the
        /// other, neither drawn nor lit: only one floor is ever looked into.
        /// </summary>
        public void SetShown(bool shown)
        {
            enabled = shown;
            if (lights != null) lights.gameObject.SetActive(shown);
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true)) renderer.forceRenderingOff = !shown;
            // A floor that is not looked into is not clicked into either.
            foreach (Wall wall in walls) wall.solid.enabled = shown && !wall.hidden;
        }

        // ------------------------------------------------------------------ Finishes

        /// <summary>Covers every wall, the floor or the ceiling with a Finish. Null leaves it plain.</summary>
        public void Wear(HomeLook slot, HallLookDefinition look)
        {
            switch (slot)
            {
                case HomeLook.Wall:
                    foreach (Wall wall in walls)
                    {
                        Empty(wall.holder, keep: wall == walls[0] ? "Door" : null);
                        wall.drawn.Clear();
                        if (look != null && look.prefab != null) Tile(wall.holder, look, wall.length, height, upright: true, wall.drawn);
                        else Keep(Plain("Plain", wall.holder, new Vector3(0f, height * 0.5f, -0.05f), new Vector3(wall.length, height, 0.1f), plain), wall.drawn);
                        Show(wall.drawn, !wall.hidden);
                    }
                    break;
                case HomeLook.Floor:
                    Empty(floorHolder, null);
                    if (look != null && look.prefab != null) Tile(floorHolder, look, size.x, size.y, upright: false, null, faceUp: true);
                    else Plain("Plain", floorHolder, new Vector3(0f, -0.05f, 0f), new Vector3(size.x, 0.1f, size.y), plain);
                    break;
                case HomeLook.Ceiling:
                    Empty(ceilingHolder, null);
                    ceilingDrawn.Clear();
                    if (look != null && look.prefab != null) Tile(ceilingHolder, look, size.x, size.y, upright: false, ceilingDrawn, faceUp: false);
                    else Keep(Plain("Plain", ceilingHolder, new Vector3(0f, 0.05f, 0f), new Vector3(size.x, 0.1f, size.y), plain), ceilingDrawn);
                    Show(ceilingDrawn, !ceilingHidden);
                    break;
            }
        }

        // Lays a panel side by side (and row on row) until it covers a width and a height, stretched by the little it
        // takes to end exactly at the edges. A wall's panels stand with their face at the holder's z = 0; a floor's
        // lie with their top at y = 0; a ceiling's hang with their underside at y = 0.
        private void Tile(Transform holder, HallLookDefinition look, float width, float span, bool upright, List<Renderer> into, bool faceUp = true)
        {
            Vector3 box = Vector3.Max(look.boundsSize, Vector3.one * 0.01f);
            float across = box.x, along = upright ? box.y : box.z;
            int columns = Mathf.Max(1, Mathf.RoundToInt(width / across)), rows = Mathf.Max(1, Mathf.RoundToInt(span / along));
            float stretchX = width / (columns * across), stretchY = span / (rows * along);
            Vector3 prefabScale = look.prefab.transform.localScale * Mathf.Max(0.01f, look.modelScale);

            for (int column = 0; column < columns; column++)
            for (int row = 0; row < rows; row++)
            {
                var tile = new GameObject("Tile").transform;
                tile.SetParent(holder, false);
                float x = -width * 0.5f + (column + 0.5f) * across * stretchX - look.boundsCenter.x * stretchX;
                if (upright)
                {
                    tile.localScale = new Vector3(stretchX, stretchY, 1f);
                    float y = row * along * stretchY - (look.boundsCenter.y - box.y * 0.5f) * stretchY;
                    tile.localPosition = new Vector3(x, y, -(look.boundsCenter.z + box.z * 0.5f));
                }
                else
                {
                    tile.localScale = new Vector3(stretchX, 1f, stretchY);
                    float z = -span * 0.5f + (row + 0.5f) * along * stretchY - look.boundsCenter.z * stretchY;
                    float y = faceUp ? -(look.boundsCenter.y + box.y * 0.5f) : -(look.boundsCenter.y - box.y * 0.5f);
                    tile.localPosition = new Vector3(x, y, z);
                }

                GameObject model = Instantiate(look.prefab, tile, false);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.Euler(look.modelEuler);
                model.transform.localScale = prefabScale;
                Strip(model);
                if (look.material != null)
                    foreach (MeshRenderer renderer in model.GetComponentsInChildren<MeshRenderer>())
                    {
                        var worn = new Material[renderer.sharedMaterials.Length];
                        for (int n = 0; n < worn.Length; n++) worn[n] = look.material;
                        renderer.sharedMaterials = worn;
                    }
                if (into != null) into.AddRange(model.GetComponentsInChildren<Renderer>());
            }
        }

        private static void Keep(GameObject made, List<Renderer> into) => into.AddRange(made.GetComponentsInChildren<Renderer>());

        // ------------------------------------------------------------------ what the camera looks through

        private void LateUpdate()
        {
            Camera view = Camera.main;
            if (view == null) return;
            Vector3 eye = transform.InverseTransformPoint(view.transform.position);

            bool above = eye.y > height - 0.05f;
            if (above != ceilingHidden)
            {
                ceilingHidden = above;
                Show(ceilingDrawn, !above);
            }
            foreach (Wall wall in walls)
            {
                // Behind the wall's face: the camera is outside it, and it would be looked through.
                bool hide = Vector3.Dot(eye - wall.middle, wall.normal) < 0f;
                if (hide == wall.hidden) continue;
                wall.hidden = hide;
                Show(wall.drawn, !hide);
                // What cannot be seen cannot be clicked: the floor behind it stays within reach.
                wall.solid.enabled = !hide;
            }
        }

        // Not drawn, but still between the sun and the room.
        private static void Show(List<Renderer> renderers, bool show)
        {
            ShadowCastingMode mode = show ? ShadowCastingMode.On : ShadowCastingMode.ShadowsOnly;
            for (int n = 0; n < renderers.Count; n++)
                if (renderers[n] != null) renderers[n].shadowCastingMode = mode;
        }

        /// <summary>
        /// Whether a collider is one of the room's walls, and if so where the middle of its foot is and how it is
        /// turned, in the room's space: x along the wall, y up, z out of its face into the room.
        /// </summary>
        public bool TryGetWall(Collider collider, out Vector3 middle, out Quaternion turn)
        {
            if (collider != null && wallOf.TryGetValue(collider, out Wall wall))
            {
                middle = wall.middle;
                turn = wall.holder.localRotation;
                return true;
            }
            middle = Vector3.zero;
            turn = Quaternion.identity;
            return false;
        }

        // ------------------------------------------------------------------ helpers

        private static void Empty(Transform holder, string keep)
        {
            for (int n = holder.childCount - 1; n >= 0; n--)
            {
                GameObject child = holder.GetChild(n).gameObject;
                if (keep != null && (child.name == keep || child.name == "Door Mark")) continue;
                Remove(child);
            }
        }

        private static GameObject Plain(string name, Transform parent, Vector3 localPosition, Vector3 localSize, Material material)
        {
            GameObject slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = name;
            slab.transform.SetParent(parent, false);
            slab.transform.localPosition = localPosition;
            slab.transform.localScale = localSize;
            if (material != null) slab.GetComponent<MeshRenderer>().sharedMaterial = material;
            Remove(slab.GetComponent<Collider>());
            return slab;
        }

        private static void Strip(GameObject model)
        {
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) Remove(collider);
        }

        // A model's box in a parent's space, from its meshes.
        private static Bounds LocalBox(GameObject model, Transform parent)
        {
            bool any = false;
            Bounds box = default;
            foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                Bounds mesh = filter.sharedMesh.bounds;
                Matrix4x4 toParent = parent.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = mesh.center + Vector3.Scale(mesh.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    point = toParent.MultiplyPoint3x4(point);
                    if (any) box.Encapsulate(point); else box = new Bounds(point, Vector3.zero);
                    any = true;
                }
            }
            return box;
        }

        // The room is also made in the Editor, outside Play, to be pictured.
        private static void Remove(Object thing)
        {
            if (thing == null) return;
            if (Application.isPlaying) Destroy(thing); else DestroyImmediate(thing);
        }
    }
}
