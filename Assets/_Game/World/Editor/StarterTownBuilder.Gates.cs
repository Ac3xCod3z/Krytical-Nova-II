using TMPro;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// City gatehouses, modelled on the POLYGON Sci-Fi City demo's road gate (Demo.unity ≈ (-35, 0, 90)): two big wall
    /// towers flank a 10 m road that runs under a two-storey lintel block with side wings, drive-through watch-station
    /// gantries outside, industrial corridor walls, fenced and barriered approach, a lit street lamp, and a big framed
    /// sign above — here a blue nameplate that reads the gate's direction ("NORTH GATE"…) on both faces.
    ///
    /// Offsets are the demo's, measured from its tower line (demo dz = 4.24) with the demo's front (-z) mapped to our
    /// outward direction, so <c>Gate(dx, y, dz)</c> places a piece exactly as the demo does on any of the four sides.
    /// Pieces that would close the road (the demo's Door_06 doors, the Industrial_01 slab) are left out.
    /// </summary>
    public static partial class StarterTownBuilder
    {
        public const float GateTowerAcross = 10.2f;     // demo towers at ±10.2 from the road centre line
        private const float GateTowerScale = 1.69f;
        private const float DemoTowerLine = 4.24f;       // demo dz of the towers = our wall line
        private const string NameplateMaterialPath = "Assets/_Game/Environment/Buildings/Materials/BuildingPalette/Signs/M_Sign_GateNameplate.mat";
        private static readonly string[] GateNames = { "NORTH GATE", "SOUTH GATE", "EAST GATE", "WEST GATE" };
        private static readonly string[] GateGroupNames = { "North Gate", "South Gate", "East Gate", "West Gate" };

        /// <summary>True for anything inside one of the four gatehouses (their pieces carry prefab names, not "Gate").</summary>
        public static bool IsInGate(Transform piece)
        {
            for (Transform t = piece; t != null; t = t.parent)
                if (System.Array.IndexOf(GateGroupNames, t.name) >= 0) return true;
            return false;
        }

        private static void BuildGates(Transform parent)
        {
            for (int side = 0; side < 4; side++)
            {
                Vector2 out2 = WallOutward(side);
                var gate = Group(parent, GateGroupNames[side]);
                BuildGate(gate, side, new Vector3(out2.x, 0f, out2.y));
            }
        }

        private static void BuildGate(Transform parent, int side, Vector3 outward)
        {
            Vector3 origin = outward * WallHalf;
            // Demo frame -> world: demo -z (its front) becomes outward.
            Quaternion frame = Quaternion.LookRotation(-outward, Vector3.up);

            GameObject Piece(string prefab, float dx, float y, float dz, float yaw, Vector3? scale = null, bool collider = true)
            {
                Vector3 p = origin + frame * new Vector3(dx, 0f, dz - DemoTowerLine);
                float ground = Mathf.Max(0f, GroundHeight(p.x, p.z));
                return Spawn(parent, prefab, new Vector3(p.x, ground + y, p.z), frame * Quaternion.Euler(0f, yaw, 0f),
                    scale ?? Vector3.one, alignToBounds: false, collider: collider);
            }

            // Towers on the wall line.
            for (int s = -1; s <= 1; s += 2)
                Piece("SM_Bld_City_Wall_Tower_01", s * GateTowerAcross, -1.06f, DemoTowerLine, 0f, Vector3.one * GateTowerScale);

            // Gatehouse: lintel over the road (open below), wings with doors, roof blocks.
            foreach (float dx in new[] { -5f, 0f })
            {
                Piece("SM_Bld_Section_Wall_01", dx, 3f, 0f, 180f);
                Piece("SM_Bld_Section_Wall_03", dx, 6f, 0f, 180f);
            }
            foreach (float dx in new[] { -10f, 5f })
            {
                Piece("SM_Bld_Section_Wall_05", dx, 0f, -2.66f, 180f);
                Piece("SM_Bld_Section_Wall_05", dx, 3f, -2.66f, 180f);
            }
            for (int s = -1; s <= 1; s += 2)
            {
                Piece("SM_Prop_Door_02", s * 7.5f, 0f, -2.87f, 180f);
                Piece("SM_Bld_Large_Part_01", s * 7.5f, 5.96f, 2.6f, 180f, Vector3.one * 0.61f);
            }

            // Drive-through watch stations outside, roof decks on top (no ground collider: the lanes stay open).
            foreach (float dx in new[] { -4.99f, 0.01f })
            {
                Piece("SM_Bld_Security_WatchStation_01", dx, 0f, -4.81f, 180f, collider: false);
                Piece("SM_Env_Ground_Tile_02", dx + 0.11f, 6.01f, -4.63f, 180f);
            }

            // Industrial corridor walls and the fenced, barriered approach.
            Piece("SM_Bld_Industrial_02", -9.77f, 0f, -9.14f, 90f, Vector3.one * 2f);
            Piece("SM_Bld_Industrial_02", 9.83f, 0f, -9.14f, 90f, new Vector3(2f, 2f, -2f));
            foreach (float dx in new[] { -8f, -5.8f, 5.79f, 8f })
                Piece("SM_Prop_Fence_01", dx, 0f, -9.68f, -90f);
            foreach (float dx in new[] { -7.7f, -5.6f, 5.6f, 7.7f })
                Piece("SM_Prop_Barrier_02", dx, 0f, -9.9f, 90f);
            Piece(Streetlight, 9.98f, 0f, -13.17f, 180f);
            Piece(Streetlight, -9.98f, 0f, -13.17f, 180f);

            // Paved two-lane approach out past the fences (the city grid stops at the wall line).
            Vector3 across = frame * Vector3.right;
            for (float d = 2.5f; d < 34f; d += 5f)
                for (int lane = -1; lane <= 1; lane += 2)
                {
                    Vector3 p = origin + outward * d + across * (lane * 2.5f);
                    float laneYaw = Mathf.Abs(outward.z) > 0.5f ? 0f : 90f;
                    Paving(parent, RoadLines, p.x, p.z, laneYaw, Mathf.Max(0f, GroundHeight(p.x, p.z)) + PavingHeight);
                }

            // Direction nameplate, readable from outside and from inside the city.
            BuildNameplate(parent, origin + frame * new Vector3(0f, 0f, 2.91f - DemoTowerLine), frame, GateNames[side]);
            BuildNameplate(parent, origin + frame * new Vector3(0f, 0f, 2.91f - DemoTowerLine + 0.9f), frame * Quaternion.Euler(0f, 180f, 0f), GateNames[side]);
        }

        /// <summary>The demo's big billboard frame with our own glowing blue panel and the gate's name in white.</summary>
        private static void BuildNameplate(Transform parent, Vector3 basePos, Quaternion facing, string text)
        {
            const float Height = 11.63f, Scale = 6.41f;
            var root = new GameObject($"Nameplate ({text})");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(basePos + Vector3.up * Height, facing * Quaternion.Euler(0f, 180f, 0f));

            var frameGo = Spawn(root.transform, "SM_Sign_Billboard_Border_03", root.transform.position, root.transform.rotation,
                Vector3.one * Scale, alignToBounds: false, collider: false);
            // Centre the panel on the frame's visual centre (pivot sits at its base).
            Vector3 centre = root.transform.position;
            if (frameGo != null && TryGetBounds(frameGo, out var fb)) centre = fb.center;

            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Panel";
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(root.transform, false);
            panel.transform.position = centre + root.transform.forward * 0.25f;
            // Quads and TMP text face their local -Z; turn them to face the same way as the frame (root.forward).
            panel.transform.rotation = root.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            panel.transform.localScale = new Vector3(6.1f, 4f, 1f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = NameplateMaterial();

            var label = new GameObject("Label");
            label.transform.SetParent(root.transform, false);
            label.transform.position = centre + root.transform.forward * 0.3f;
            label.transform.rotation = root.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            var tmp = label.AddComponent<TextMeshPro>();
            tmp.text = text.Replace(" ", "\n");
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 4f;
            tmp.fontSizeMax = 40f;
            tmp.characterSpacing = 2f;
            tmp.lineSpacing = -22f;
            tmp.color = Color.white;
            // Outline lives in a saved material preset: setting outlineWidth here would instance a material into the scene.
            var textMat = NameplateTextMaterial(tmp.font);
            if (textMat != null) tmp.fontSharedMaterial = textMat;
            tmp.rectTransform.sizeDelta = new Vector2(5.4f, 3.4f);
            GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
        }

        private const string NameplateTextMaterialPath = "Assets/_Game/Environment/Buildings/Materials/BuildingPalette/Signs/M_TMP_GateNameplate.mat";

        private static Material NameplateTextMaterial(TMP_FontAsset font)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(NameplateTextMaterialPath);
            if (mat != null || font == null || font.material == null) return mat;
            WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(NameplateTextMaterialPath).Replace('\\', '/'));
            mat = new Material(font.material) { name = "M_TMP_GateNameplate" };
            mat.EnableKeyword("OUTLINE_ON");
            mat.SetFloat("_OutlineWidth", 0.15f);
            mat.SetColor("_OutlineColor", new Color32(10, 40, 110, 255));
            AssetDatabase.CreateAsset(mat, NameplateTextMaterialPath);
            return mat;
        }

        private static Material NameplateMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(NameplateMaterialPath);
            bool created = mat == null;
            if (created)
            {
                WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(NameplateMaterialPath).Replace('\\', '/'));
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Sign_GateNameplate" };
            }
            // Deep saturated blue like the demo's SYN CORP panel; a modest glow so the white text stays readable.
            mat.SetColor("_BaseColor", new Color(0.03f, 0.16f, 0.62f));
            mat.SetFloat("_Smoothness", 0.3f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.04f, 0.2f, 0.85f) * 0.9f);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (created) AssetDatabase.CreateAsset(mat, NameplateMaterialPath);
            else EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
