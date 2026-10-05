using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The central plaza (lawns, walkways, the Elder hall, the hologram centrepiece), the vendor stalls around its
    /// perimeter walk, and the two quarters with NPCs: the NW training yard and the SE Exchange square.
    /// </summary>
    public static partial class StarterTownBuilder
    {
        private enum StallKind { Market, Forge, HoloBooth, Watch, Tailor, Guild, Gate, Collector }

        private struct VendorStall
        {
            public string NpcId;     // NPC_<Id> prefab, or a future slot when Slot is true
            public bool Slot;
            public StallKind Kind;
            public float X, Z, Yaw;  // stall centre; yaw faces the customers
            public VendorStall(string id, bool slot, StallKind kind, float x, float z, float yaw)
            { NpcId = id; Slot = slot; Kind = kind; X = x; Z = z; Yaw = yaw; }
        }

        // Around the plaza's perimeter walk (the reference map's ring of vendors), every stall facing the centre.
        private static readonly VendorStall[] PlazaStalls =
        {
            new VendorStall("MallManager", false, StallKind.HoloBooth, -22.5f, 12.5f, 90f),
            new VendorStall("Tailor", true, StallKind.Tailor, -22.5f, -12.5f, 90f),
            new VendorStall("GuardCaptain", false, StallKind.Watch, 22.5f, 12.5f, 270f),
            new VendorStall("HerbMaster", false, StallKind.Market, 22.5f, -12.5f, 270f),
            new VendorStall("Blacksmith", false, StallKind.Forge, -12.5f, -22.5f, 0f),
            new VendorStall("Collector", true, StallKind.Collector, 12.5f, -22.5f, 0f),
            new VendorStall("GuildDirector", true, StallKind.Guild, -14f, 22.5f, 180f),
            new VendorStall("GateMaster", true, StallKind.Gate, 14f, 22.5f, 180f),
        };

        private static readonly Vector2 ElderHall = new Vector2(0f, 18f);
        private static readonly Vector2 ElderHallFootprint = new Vector2(14f, 14f);
        private static readonly Rect TrainingYard = new Rect(-90f, 75f, 35f, 15f);   // NW block interior, open to the mid ring road
        private static readonly Rect ExchangeSquare = new Rect(50f, -90f, 40f, 15f);  // SE block interior, open to the mid ring road

        // ---------------------------------------------------------------- plaza

        private static void BuildPlaza(Transform parent)
        {
            var paving = Group(parent, "Paving");
            for (float x = -PlazaHalf + Tile * 1.5f; x < PlazaHalf - Tile; x += Tile)
            for (float z = -PlazaHalf + Tile * 1.5f; z < PlazaHalf - Tile; z += Tile)
            {
                float ax = Mathf.Abs(x), az = Mathf.Abs(z);
                bool centre = ax < Tile && az < Tile;
                bool walkway = ax < Tile || az < Tile || Mathf.Max(ax, az) > LawnHalf;
                if (centre) Paving(paving, "SM_Env_Ground_Tile_01", x, z, 0f);
                else if (walkway) Paving(paving, "SM_Env_Sidewalk_03_SF", x, z, 0f);
                // Lawn squares stay bare terrain (ZoneTerrainProfile.greenHalfSize); the GPU grass fills them (PlazaLawnRects).
            }

            // Centrepiece: landing pad podium + hologram beacon (the reference's bright centre square).
            Place(parent, "SM_Bld_LandingPad_01", 0f, 0f, 45f);
            var beacon = AssetDatabase.LoadAssetAtPath<GameObject>(PortalVfxBuilder.TownBeaconPath);
            if (beacon != null)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(beacon, parent);
                go.transform.position = new Vector3(0f, 1.25f, 0f);
                go.transform.localScale = Vector3.one * 1.5f;
            }

            // The Elder's hall closes the north walkway.
            var hall = Group(parent, "Elder Hall");
            Place(hall, "SM_Bld_Large_04", ElderHall.x, ElderHall.y, 180f);
            for (int sx = -1; sx <= 1; sx += 2)
            {
                Place(hall, "SM_Prop_Plant_Vase_02", sx * 3.2f, 10.6f, 0f);
                Place(hall, "SM_Prop_Paper_Banner_01", sx * 5.6f, 11f, 180f);
                Place(hall, "SM_Prop_Streetlight_01", sx * 7.5f, 9f, 180f, 0f, 1f, alignToBounds: false);
            }
            MarkRect(CenteredRect(ElderHall, ElderHallFootprint));

            var lawns = Group(parent, "Lawns");
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var c = new Vector2(sx * 12.5f, sz * 12.5f);
                bool north = sz > 0;
                // Hedges frame each lawn along the walkways, with gaps in the middle to walk through.
                Place(lawns, "SM_Env_Hedge_01", c.x - sx * 4f, sz * 5.6f, 0f);
                Place(lawns, "SM_Env_Hedge_01", sx * 5.6f, c.y + (north ? -4f : -sz * 4f), 90f);
                Place(lawns, "SM_Env_Hedge_01", c.x + sx * 4f, sz * 19.4f, 0f);
                Place(lawns, "SM_Env_Hedge_01", sx * 19.4f, c.y - sz * 4f, 90f);
                // Trees and benches; the north lawns keep clear of the hall.
                Place(lawns, "SM_Env_Tree_01", c.x + sx * (north ? 3f : 0f), c.y, Range(0f, 360f));
                Place(lawns, "SM_Env_Tree_03", c.x + sx * 5f, c.y - sz * 5f, Range(0f, 360f));
                Place(lawns, "SM_Prop_Bench_01", sx * 9f, sz * 6.4f, sz > 0 ? 180f : 0f);
                Place(lawns, "SM_Prop_TrashCan_01", sx * 6.6f, sz * 6.6f, 0f);
                Place(lawns, "SM_Prop_Streetlight_01", sx * 8.5f, sz * 20.6f, YawOf(new Vector2(0f, -sz)), 0f, 1f, alignToBounds: false);
                // The lawns are mown GPU grass now (GrassSystemBuilder bakes PlazaLawnRects), so the tall grass
                // patches are discarded. They are still placed first so the builder's random sequence, and every
                // building after this, stays the same.
                var patchA = Place(lawns, "SM_Env_Grass_Patch_01", c.x - sx * 4f, c.y + sz * 3f, Range(0f, 360f), 0f, 1f, true, false);
                var patchB = Place(lawns, "SM_Env_Grass_Patch_02", c.x + sx * 2f, c.y - sz * 3.5f, Range(0f, 360f), 0f, 1f, true, false);
                if (patchA != null) Object.DestroyImmediate(patchA);
                if (patchB != null) Object.DestroyImmediate(patchB);
            }
        }

        // ---------------------------------------------------------------- stalls

        private enum SignStyle { NeonPanel, WoodBoard, HoloPlate, FabricBanner, BladeSign, Lightbox }

        private struct StallLook
        {
            public string Title, Subtitle;
            public SignStyle Style;
            public string Font;      // SDF font asset path
            public Color Ink;        // text colour (HDR for glowing styles)
            public Color Board;      // board / panel colour
        }

        private const string FontOrbitron = "Assets/Synty/InterfaceSciFiSoldierHUD/Fonts/Orbitron/Orbitron-ExtraBold SDF.asset";
        private const string FontExo = "Assets/Synty/InterfaceSciFiSoldierHUD/Fonts/exo-2-0/Exo2.0-SemiBold SDF.asset";
        private const string FontLato = "Assets/_ThirdParty/Feel/MMTools/Demos/MMTween/Fonts/Lato/SDF/Lato-Black SDF.asset";

        /// <summary>Every stall gets its own sign: different style, typeface and colour from the colour bible.</summary>
        private static StallLook LookFor(VendorStall s)
        {
            switch (s.Kind)
            {
                case StallKind.Market: return new StallLook { Title = "APOTHECARY", Subtitle = "stims · recall tags", Style = SignStyle.WoodBoard, Font = FontLato, Ink = ColorBible.Hex("F2E6C8"), Board = ColorBible.Hex("6B4A30") };
                case StallKind.Forge: return new StallLook { Title = "ARMORER", Subtitle = "forge · tempering", Style = SignStyle.NeonPanel, Font = FontOrbitron, Ink = ColorBible.SunsetCoral * 2.2f, Board = ColorBible.Ink };
                case StallKind.HoloBooth: return new StallLook { Title = "NOVA STORE", Subtitle = "premium goods", Style = SignStyle.HoloPlate, Font = FontOrbitron, Ink = Color.white * 1.8f, Board = ColorBible.NeonCyan };
                case StallKind.Watch: return new StallLook { Title = "MARSHAL POST", Subtitle = "contracts · battles", Style = SignStyle.Lightbox, Font = FontExo, Ink = ColorBible.Ink, Board = ColorBible.Hex("F4EBDD") };
                case StallKind.Tailor: return new StallLook { Title = "OUTFITTER", Subtitle = "coming soon", Style = SignStyle.FabricBanner, Font = FontExo, Ink = Color.white, Board = ColorBible.NeonMagenta * 0.8f };
                case StallKind.Guild: return new StallLook { Title = "CREW HALL", Subtitle = "coming soon", Style = SignStyle.BladeSign, Font = FontLato, Ink = ColorBible.SignalYellow, Board = ColorBible.ShadowIndigo };
                case StallKind.Gate: return new StallLook { Title = "WAYFINDER", Subtitle = "coming soon", Style = SignStyle.NeonPanel, Font = FontExo, Ink = ColorBible.NeonCyan * 2f, Board = ColorBible.Ink };
                default: return new StallLook { Title = "COLLECTOR", Subtitle = "coming soon", Style = SignStyle.WoodBoard, Font = FontOrbitron, Ink = ColorBible.SignalYellow, Board = ColorBible.Hex("3E3A48") };
            }
        }

        private static void BuildStalls(Transform parent, Transform npcRoot)
        {
            var slots = Group(parent, "Future NPC Slots");
            Marker(slots, "Slot_Elder", ElderHall.x, ElderHall.y - 8.5f).transform.rotation = Quaternion.Euler(0f, 180f, 0f);

            foreach (var s in PlazaStalls)
            {
                var stall = Group(parent, $"Stall - {s.NpcId}{(s.Slot ? " (future)" : "")}");
                var origin = new Vector2(s.X, s.Z);
                Vector2 npcLocal = BuildStallKit(stall, s.Kind, origin, s.Yaw);
                BuildStallSign(stall, LookFor(s), origin, s.Yaw, dim: s.Slot);
                Vector2 npcPos = origin + Rotate(npcLocal, s.Yaw);
                if (s.Slot) Marker(slots, "Slot_" + s.NpcId, npcPos.x, npcPos.y).transform.rotation = Quaternion.Euler(0f, s.Yaw, 0f);
                else SpawnNpc(npcRoot, s.NpcId, npcPos, s.Yaw);
            }
        }

        // Stall frame, local frame (+Z = toward customers): back wall at z −1.6, canopy over z −1.4..+0.9, counter at
        // z +0.5, vendor at z −0.6. Every prop is either grounded (snapped to y 0) or hung from the measured canopy.
        private const float StallWallZ = -1.6f, StallCounterZ = 0.55f, StallVendorZ = -0.6f, StallWidth = 5f;
        private static float canopyUnderside = 2.3f;

        /// <summary>Builds one stall in its local frame and returns where the vendor stands.</summary>
        private static Vector2 BuildStallKit(Transform p, StallKind kind, Vector2 o, float yaw)
        {
            bool watch = kind == StallKind.Watch;
            if (watch)
            {
                // The guard post is a real watch station instead of a canvas stall.
                Grounded(p, "SM_Bld_Security_WatchStation_01", o, yaw, 0f, -1.6f);
                Grounded(p, "SM_Prop_Barrier_02", o, yaw, -3.1f, 1.4f);
                Grounded(p, "SM_Prop_Barrier_02", o, yaw, 3.1f, 1.4f);
                Grounded(p, "SM_Prop_MarketTable_02", o, yaw, 0f, 1.3f);
                OnCounter(p, "SM_Prop_Monitor_01", o, yaw, -0.5f, 1.3f, 180f);
                OnCounter(p, "SM_Prop_ControlPanel_01", o, yaw, 0.7f, 1.3f);
                canopyUnderside = 4.2f;
                MarkStall(o, yaw, 7.5f, 6f);
                return new Vector2(0f, 0.4f);
            }

            StallFrame(p, kind, o, yaw);
            switch (kind)
            {
                case StallKind.Market: // Herb Master: jars, plants and hanging herbs
                    Counter(p, "SM_Prop_MarketTable_04", o, yaw);
                    OnCounter(p, "SM_Prop_Jar_01", o, yaw, -1f, StallCounterZ);
                    OnCounter(p, "SM_Prop_Jar_02", o, yaw, -0.5f, StallCounterZ + 0.1f);
                    OnCounter(p, "SM_Prop_JarFull_01", o, yaw, 0.2f, StallCounterZ);
                    OnCounter(p, "SM_Prop_Jar_03", o, yaw, 0.8f, StallCounterZ - 0.1f);
                    Hung(p, "SM_Prop_Jar_Hanging_01", o, yaw, -1.3f, -0.4f);
                    Hung(p, "SM_Prop_Jar_Hanging_01", o, yaw, 1.2f, -0.2f);
                    Grounded(p, "SM_Prop_Plant_Vase_01", o, yaw, -2.9f, 1f);
                    Grounded(p, "SM_Prop_Plant_Vase_02", o, yaw, 2.9f, 0.9f);
                    break;
                case StallKind.Forge: // Blacksmith: forge drum, parts, tools
                    Counter(p, "SM_Prop_MarketTable_02", o, yaw);
                    OnCounter(p, "SM_Prop_Cog_01", o, yaw, -0.6f, StallCounterZ);
                    OnCounter(p, "SM_Prop_Spanner_01", o, yaw, 0.5f, StallCounterZ, 30f);
                    Grounded(p, "SM_Prop_Cylinder_01", o, yaw, -2f, -1.1f);
                    Grounded(p, "SM_Prop_Box_RobotParts_01", o, yaw, 2f, -1f);
                    Grounded(p, "SM_Prop_Crate_Military_01", o, yaw, 2.9f, 0.8f, 15f);
                    Grounded(p, "SM_Prop_Crate_Military_02", o, yaw, -2.9f, 0.8f, -10f);
                    break;
                case StallKind.HoloBooth: // Mall Manager: VR booths either side, hologram on the counter
                    Counter(p, "SM_Prop_MarketTable_05", o, yaw);
                    OnCounter(p, "SM_Prop_ControlPanel_01", o, yaw, 0.6f, StallCounterZ);
                    Grounded(p, "SM_Prop_VRBooth_01", o, yaw, -3.4f, -0.6f, 90f);
                    Grounded(p, "SM_Prop_VRBooth_01", o, yaw, 3.4f, -0.6f, 270f);
                    Grounded(p, "SM_Prop_Hologram_Bottle_02", o, yaw, -0.8f, -1f, 0f, false);
                    Grounded(p, "SM_Prop_VendingMachine_Soda_01", o, yaw, 1.8f, -1.2f);
                    break;
                case StallKind.Tailor: // future Tailor: banners, boxes of cloth
                    Counter(p, "SM_Prop_MarketTable_01", o, yaw);
                    OnCounter(p, "SM_Prop_Box_Empty_01", o, yaw, -0.6f, StallCounterZ);
                    Grounded(p, "SM_Prop_Paper_Banner_01", o, yaw, -2.9f, 0.6f);
                    Grounded(p, "SM_Prop_Paper_Banner_01", o, yaw, 2.9f, 0.6f);
                    Grounded(p, "SM_Prop_Box_Supplies_01", o, yaw, 1.8f, -1.1f);
                    break;
                case StallKind.Guild: // future Guild Director: notice pillar, ledgers
                    Counter(p, "SM_Prop_MarketTable_01", o, yaw);
                    OnCounter(p, "SM_Prop_Books_01", o, yaw, 0.4f, StallCounterZ);
                    Grounded(p, "SM_Prop_Advertisement_Pillar_01", o, yaw, 3.3f, 0.4f);
                    Grounded(p, "SM_Prop_Box_Supplies_01", o, yaw, -1.8f, -1.1f);
                    break;
                case StallKind.Gate: // future Gate Master: teleport pad in front, scanner arch behind
                    Counter(p, "SM_Prop_MarketTable_02", o, yaw);
                    OnCounter(p, "SM_Prop_ControlPanel_01", o, yaw, 0f, StallCounterZ);
                    Grounded(p, "SM_Prop_SciFi_Pad_01", o, yaw, 0f, 2.2f, 0f, false);
                    Grounded(p, "SM_Prop_ChargingStation_01", o, yaw, -2.9f, 0.2f, 90f);
                    Grounded(p, "SM_Prop_ChargingStation_01", o, yaw, 2.9f, 0.2f, 270f);
                    break;
                default: // Collector: crates and curios
                    Counter(p, "SM_Prop_MarketTable_01", o, yaw);
                    OnCounter(p, "SM_Prop_Jar_01", o, yaw, -0.6f, StallCounterZ);
                    OnCounter(p, "SM_Prop_Bottle_02", o, yaw, 0.3f, StallCounterZ);
                    OnCounter(p, "SM_Prop_JarFull_01", o, yaw, 0.8f, StallCounterZ);
                    Grounded(p, "SM_Prop_Crate_Large_01", o, yaw, -1.8f, -1.1f);
                    Grounded(p, "SM_Prop_Crate_02", o, yaw, 2.9f, 0.8f, 25f);
                    break;
            }
            MarkStall(o, yaw, StallWidth + 2f, 4.5f);
            return new Vector2(0f, StallVendorZ);
        }

        /// <summary>Back wall, a canopy stretched to the stall width, side crates and canopy lanterns.</summary>
        private static void StallFrame(Transform p, StallKind kind, Vector2 o, float yaw)
        {
            Grounded(p, "SM_Env_Wall_Generic_01", o, yaw, 0f, StallWallZ);
            string cover = "SM_Prop_MarketCover_01";
            float coverWidth = Footprint(cover).size.x;
            var c = Grounded(p, cover, o, yaw, 0f, -0.35f);
            if (c != null)
            {
                c.transform.localScale = new Vector3(StallWidth / coverWidth, 1f, 1f);
                if (TryGetBounds(c, out var cb)) canopyUnderside = cb.max.y - 0.55f;
            }
            Grounded(p, kind == StallKind.Forge ? "SM_Prop_Crate_03" : "SM_Prop_Crate_01", o, yaw, -2.7f, -1f, 10f);
            Grounded(p, "SM_Prop_Box_Supplies_01", o, yaw, 2.6f, -1.2f);
            for (int s = -1; s <= 1; s += 2)
                Hung(p, s < 0 ? "SM_Prop_Paper_Lantern_01" : "SM_Prop_Paper_Lantern_02", o, yaw, s * 2.1f, 0.7f);
        }

        private static GameObject Counter(Transform p, string table, Vector2 o, float yaw) => Grounded(p, table, o, yaw, 0f, StallCounterZ);

        /// <summary>A prop standing on the ground (its lowest point snapped to y 0).</summary>
        private static GameObject Grounded(Transform p, string prefab, Vector2 o, float yaw, float lx, float lz, float localYaw = 0f, bool collider = true)
        {
            var go = PlaceLocal(p, prefab, o, yaw, lx, lz, localYaw, 0f, collider);
            SnapBottom(go, 0f);
            return go;
        }

        /// <summary>A prop resting on the counter top (snapped to the counter's measured top).</summary>
        private static GameObject OnCounter(Transform p, string prefab, Vector2 o, float yaw, float lx, float lz, float localYaw = 0f)
        {
            var go = PlaceLocal(p, prefab, o, yaw, lx, lz, localYaw, 0f, false);
            SnapBottom(go, CounterTop(p, o, yaw, lz));
            return go;
        }

        /// <summary>A prop hanging under the canopy (its top snapped to the canopy underside).</summary>
        private static GameObject Hung(Transform p, string prefab, Vector2 o, float yaw, float lx, float lz)
        {
            var go = PlaceLocal(p, prefab, o, yaw, lx, lz, 0f, 0f, false, false);
            if (go != null && TryGetBounds(go, out var b)) go.transform.position += new Vector3(0f, canopyUnderside - b.max.y, 0f);
            return go;
        }

        private static float CounterTop(Transform stall, Vector2 o, float yaw, float lz)
        {
            // Highest table top under the counter line in this stall.
            Vector2 at = o + Rotate(new Vector2(0f, lz), yaw);
            float top = 0.85f;
            foreach (Transform t in stall)
                if (t.name.Contains("MarketTable") && TryGetBounds(t.gameObject, out var b) &&
                    Vector2.Distance(new Vector2(b.center.x, b.center.z), at) < 2f) top = b.max.y;
            return top;
        }

        private static void MarkStall(Vector2 o, float yaw, float width, float depth)
        {
            bool turned = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad)) > 0.5f;
            MarkRect(CenteredRect(o, turned ? new Vector2(depth, width) : new Vector2(width, depth)));
        }

        // ---------------------------------------------------------------- stall signs

        /// <summary>The stall's name sign, in its own style: board geometry plus world-space TextMeshPro.</summary>
        private static void BuildStallSign(Transform stall, StallLook look, Vector2 o, float yaw, bool dim)
        {
            var root = new GameObject("Sign " + look.Title);
            root.transform.SetParent(stall, false);
            float top = canopyUnderside + 0.75f;
            Color ink = dim ? look.Ink * 0.6f : look.Ink;
            ink.a = 1f;
            Vector3 Local(float lx, float y, float lz) { var v = o + Rotate(new Vector2(lx, lz), yaw); return new Vector3(v.x, y, v.y); }
            Quaternion facing = Quaternion.Euler(0f, yaw, 0f);

            switch (look.Style)
            {
                case SignStyle.NeonPanel:
                    // Dark panel on the canopy front with a glowing tube underline.
                    SignBoard(root, Local(0f, top, 0.95f), facing, new Vector3(3.6f, 0.8f, 0.08f), ColorBible.SignMaterial("Panel_Ink", look.Board, 0f));
                    SignBoard(root, Local(0f, top - 0.48f, 0.98f), facing, new Vector3(3.2f, 0.05f, 0.05f), ColorBible.SignMaterial("Tube_" + ColorUtility.ToHtmlStringRGB(look.Ink), look.Ink, 2.5f));
                    SignText(root, look, Local(0f, top + 0.08f, 1.0f), facing, new Vector2(3.4f, 0.6f), ink, 0.3f);
                    break;
                case SignStyle.WoodBoard:
                    // A plank board hanging on two chains from the canopy edge.
                    SignBoard(root, Local(0f, top - 0.15f, 1.05f), facing, new Vector3(2.8f, 0.7f, 0.1f), ColorBible.SignMaterial("Wood_" + ColorUtility.ToHtmlStringRGB(look.Board), look.Board, 0f));
                    for (int s = -1; s <= 1; s += 2)
                        SignBoard(root, Local(s * 1.1f, top + 0.35f, 1.05f), facing, new Vector3(0.04f, 0.35f, 0.04f), ColorBible.SignMaterial("Chain", ColorBible.Hex("2A2830"), 0f));
                    SignText(root, look, Local(0f, top - 0.1f, 1.11f), facing, new Vector2(2.6f, 0.55f), ink, 0.25f);
                    break;
                case SignStyle.HoloPlate:
                    // A translucent cyan plate floating just above the counter roof (hologram projector below it).
                    SignBoard(root, Local(0f, top + 0.1f, 0.9f), facing, new Vector3(3.2f, 0.9f, 0.02f), ColorBible.SignMaterial("Holo", look.Board, 1.4f, true));
                    SignText(root, look, Local(0f, top + 0.15f, 0.93f), facing, new Vector2(3f, 0.7f), ink, 0.25f);
                    break;
                case SignStyle.FabricBanner:
                    // A cloth banner stretched across the canopy front.
                    SignBoard(root, Local(0f, top - 0.2f, 1.0f), facing, new Vector3(4.6f, 0.75f, 0.03f), ColorBible.SignMaterial("Fabric_" + ColorUtility.ToHtmlStringRGB(look.Board), look.Board, 0f));
                    SignText(root, look, Local(0f, top - 0.15f, 1.03f), facing, new Vector2(4.2f, 0.6f), ink, 0.28f);
                    break;
                case SignStyle.BladeSign:
                    // A vertical blade on its own post at the stall corner, readable from both directions along the walk.
                    var post = Local(-StallWidth * 0.5f - 0.5f, 0f, 1f);
                    SignBoard(root, post + new Vector3(0f, 1.8f, 0f), facing, new Vector3(0.12f, 3.6f, 0.12f), ColorBible.SignMaterial("Post", ColorBible.Hex("2A2830"), 0f));
                    var bladeRot = facing * Quaternion.Euler(0f, 90f, 0f);
                    SignBoard(root, post + new Vector3(0f, 2.6f, 0f), bladeRot, new Vector3(0.7f, 1.9f, 0.08f), ColorBible.SignMaterial("Blade_" + ColorUtility.ToHtmlStringRGB(look.Board), look.Board, 0f));
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vector3 n = bladeRot * Vector3.forward * (0.05f * side);
                        SignText(root, look, post + new Vector3(0f, 2.6f, 0f) + n, bladeRot * Quaternion.Euler(0f, side < 0 ? 0f : 180f, 0f) * Quaternion.Euler(0f, 0f, 90f),
                                 new Vector2(1.8f, 0.6f), ink, 0f, flipFacing: false);
                    }
                    break;
                default: // Lightbox
                    SignBoard(root, Local(0f, top, 0.95f), facing, new Vector3(3.4f, 0.8f, 0.25f), ColorBible.SignMaterial("Lightbox", look.Board, 0.6f));
                    SignText(root, look, Local(0f, top + 0.05f, 1.09f), facing, new Vector2(3.2f, 0.6f), ink, 0.28f);
                    break;
            }
        }

        private static void SignBoard(GameObject root, Vector3 position, Quaternion rotation, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Board";
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEditor.GameObjectUtility.SetStaticEditorFlags(go, UnityEditor.StaticEditorFlags.BatchingStatic);
        }

        /// <summary>
        /// World-space TextMeshPro, title plus an optional small subtitle line. TMP reads correctly when viewed along +Z,
        /// so by default it is turned to face the frame's customers (+Z of <paramref name="facing"/>).
        /// </summary>
        private static void SignText(GameObject root, StallLook look, Vector3 position, Quaternion facing, Vector2 box, Color ink, float subtitleShare, bool flipFacing = true)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(look.Font);
            var go = new GameObject("Text");
            go.transform.SetParent(root.transform, false);
            var tmp = go.AddComponent<TMPro.TextMeshPro>();
            if (font != null)
            {
                tmp.font = font;
                tmp.fontSharedMaterial = ColorBible.SignTextMaterial(font, ink);
            }
            tmp.text = subtitleShare > 0f && !string.IsNullOrEmpty(look.Subtitle)
                ? $"{look.Title}\n<size={Mathf.RoundToInt(subtitleShare * 100f)}%>{look.Subtitle.ToUpperInvariant()}</size>"
                : look.Title;
            tmp.alignment = TMPro.TextAlignmentOptions.Center;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.5f;
            tmp.fontSizeMax = 6f;
            tmp.color = Color.white;
            tmp.rectTransform.sizeDelta = box;
            go.transform.SetPositionAndRotation(position, flipFacing ? facing * Quaternion.Euler(0f, 180f, 0f) : facing);
        }

        private static void SpawnNpc(Transform parent, string id, Vector2 position, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{NpcPrefabFolder}/NPC_{id}.prefab");
            if (prefab == null) { Debug.LogWarning($"[StarterTown] Missing NPC prefab NPC_{id}; run Zone01 setup."); return; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.SetPositionAndRotation(new Vector3(position.x, 0f, position.y), Quaternion.Euler(0f, yaw, 0f));
        }

        // ---------------------------------------------------------------- quarters

        private static void BuildQuarters(Transform parent, Transform npcRoot)
        {
            // NW training yard: an open drill ground facing the mid ring road, fenced on the far sides.
            var yard = Group(parent, "NW Training Yard");
            Rect y = TrainingYard;
            MarkRect(y);
            for (float x = y.xMin + Tile * 0.5f; x < y.xMax; x += Tile)
            for (float z = y.yMin + Tile * 0.5f; z < y.yMax; z += Tile)
                Paving(yard, "SM_Env_Ground_Tile_02", x, z, 0f);
            for (float x = y.xMin + 1f; x < y.xMax - 1f; x += 2.2f)
                Place(yard, "SM_Prop_Barrier_01", x, y.yMax - 0.6f, 90f);
            for (float z = y.yMin + 1f; z < y.yMax - 1f; z += 2.2f)
                Place(yard, "SM_Prop_Barrier_01", y.xMin + 0.6f, z, 0f);
            Place(yard, "SM_Bld_Security_WatchStation_01", y.xMin + 4f, y.yMax - 4.5f, 135f);
            Place(yard, "SM_Prop_Crate_Military_01", y.xMax - 3f, y.yMax - 2.5f, 10f);
            Place(yard, "SM_Prop_Crate_Military_03", y.xMax - 5f, y.yMax - 2.2f, -20f);
            Place(yard, "SM_Prop_Crate_04", y.xMax - 2.5f, y.yMax - 4.5f, 90f);
            Place(yard, "SM_Prop_Flags_01", y.center.x, y.yMax - 0.8f, 0f, 3.5f, 2f, true, false);
            Place(yard, "SM_Prop_Streetlight_01", y.xMin + 10f, y.yMin + 1f, 0f, 0f, 1f, alignToBounds: false);
            Place(yard, "SM_Prop_Streetlight_01", y.xMax - 6f, y.yMin + 1f, 0f, 0f, 1f, alignToBounds: false);
            SpawnNpc(npcRoot, "TrainerBlade", new Vector2(y.xMin + 12f, y.yMax - 6f), 180f);
            SpawnNpc(npcRoot, "TrainerFist", new Vector2(y.xMin + 22f, y.yMax - 6f), 180f);

            // SE Exchange square: the Trader's bank, booth and ATMs, and the Agent's kiosk, facing the mid ring road.
            var ex = Group(parent, "SE Exchange Square");
            Rect e = ExchangeSquare;
            MarkRect(e);
            for (float x = e.xMin + Tile * 0.5f; x < e.xMax; x += Tile)
            for (float z = e.yMin + Tile * 0.5f; z < e.yMax; z += Tile)
                Paving(ex, Chance(0.7) ? "SM_Env_Ground_Tile_01" : "SM_Env_Ground_Tile_03", x, z, 0f);
            Place(ex, "SM_Bld_Bank_01", e.xMax - 8.2f, e.yMin + 8.5f, 270f);
            Place(ex, "SM_Prop_Bank_Booth_01", e.xMax - 22f, e.yMin + 9f, 0f);
            Place(ex, "SM_Prop_Bank_Booth_Pillar_01", e.xMax - 24f, e.yMin + 9f, 0f);
            Place(ex, "SM_Prop_Bank_Booth_Pillar_01", e.xMax - 20f, e.yMin + 9f, 0f);
            for (int i = 0; i < 3; i++) Place(ex, "SM_Prop_Atm_01", e.xMax - 17f, e.yMin + 3f + i * 1.4f, 270f);
            Place(ex, "SM_Bld_Shop_Open_01", e.xMin + 6f, e.yMin + 4f, 0f);
            Place(ex, "SM_Prop_Monitor_01", e.xMin + 9.5f, e.yMin + 7.5f, 0f);
            Place(ex, "SM_Prop_Hologram_Bottle_03", e.xMin + 13f, e.yMin + 6f, 0f, 0f, 1f, true, false);
            Place(ex, "SM_Prop_Bench_01", e.xMin + 20f, e.yMax - 3f, 180f);
            Place(ex, "SM_Prop_Plant_Vase_02", e.xMin + 24f, e.yMax - 3f, 0f);
            SpawnNpc(npcRoot, "Trader", new Vector2(e.xMax - 22f, e.yMin + 8f), 0f);
            SpawnNpc(npcRoot, "Agent", new Vector2(e.xMin + 6f, e.yMin + 8.2f), 0f);
        }
    }
}
