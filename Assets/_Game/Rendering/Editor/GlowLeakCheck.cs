using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Player;
using KryticalNova.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Goes through every weapon model and looks for glow that shines where it must not: through the body of the
    /// character that carries it, and through a wall. Run it in Play, on the local player (menu Krytical Nova ▸
    /// Lighting ▸ Check Glow Leaks). For each weapon model, at the two looks of the last Temper band (Rare and
    /// Epic, the strongest), it draws the character from a camera of its own with the glow and without it; what
    /// differs is the light. It reports how much of that light lies on the body where the weapon is not in front
    /// of it (carried, seen from the front; drawn, seen from behind) and how much shows on a wall that hides the
    /// character altogether, and writes the table and a sheet of pictures, with that light marked, to
    /// <see cref="OutputFolder"/>.
    /// <para>The wall figure must be 0. The body figures are not leaks by themselves: a weapon held in front of the
    /// body lays its light over the body behind it, as it should. They are for comparing a change against the last
    /// run, and the sheets show where that light lies (marked magenta): on a body that stands in front of the
    /// weapon it is a leak.</para>
    /// </summary>
    public static class GlowLeakCheck
    {
        public const string OutputFolder = "Temp/GlowLeaks";
        private const int Size = 384;
        private const int Differs = 12;          // a pixel differs when a channel moved by more than this (of 255)
        private const int SheetColumns = 8;
        private const float ViewDistance = 2.7f;
        private const float ChestHeight = 0.3f;  // above the player's root, which is at the hips
        private static readonly Color32 Mark = new Color32(255, 0, 255, 255);

        private struct Look
        {
            public string name;
            public ItemRarity rarity;
            public int temper;
        }

        private static readonly Look[] Looks =
        {
            new Look { name = "Epic120", rarity = ItemRarity.Epic, temper = 120 },
            new Look { name = "Rare120", rarity = ItemRarity.Rare, temper = 120 },
        };

        private sealed class Result
        {
            public string model, look;
            public int carriedBody, carriedLight, drawnBody, drawnLight, wall;
        }

        /// <summary>True while a check is running.</summary>
        public static bool Running { get; private set; }

        [MenuItem("Krytical Nova/Lighting/Check Glow Leaks (In Play)")]
        public static void Run()
        {
            if (!Application.isPlaying || PlayerInventory.Local == null)
            {
                Debug.LogWarning("[GlowLeaks] Enter Play and wait for your character first: the check draws it with every weapon.");
                return;
            }
            if (Running) return;
            // Run by something that is there for the whole of it: the check hides parts of the character as it goes.
            System.Type hud = System.Type.GetType("KryticalNova.UI.ProgressionHUD, KryticalNova.UI");   // by name: no UI under the server target
            MonoBehaviour runner = hud != null ? (MonoBehaviour)Object.FindAnyObjectByType(hud) : null;
            if (runner == null) runner = PlayerInventory.Local;
            runner.StartCoroutine(Check(PlayerInventory.Local.gameObject));
        }

        private static IEnumerator Check(GameObject me)
        {
            Running = true;
            var equipment = me.GetComponent<PlayerEquipment>();
            var mount = me.GetComponentInChildren<WeaponVisualMount>(true);
            var binder = me.GetComponentInChildren<EquipmentVisualBinder>(true);
            var motor = me.GetComponent<PlayerAnimancerMotor>();
            if (equipment == null || mount == null || equipment.Catalog == null || equipment.Progression == null)
            {
                Debug.LogError("[GlowLeaks] The local player has no equipment or weapon mount to check.");
                Running = false;
                yield break;
            }

            // One item for each weapon model.
            var models = new List<ItemDefinition>();
            var seen = new HashSet<GameObject>();
            foreach (ItemDefinition item in equipment.Catalog.Items)
                if (item != null && item.equipSlot == EquipSlot.Weapon && item.weaponPrefab != null && seen.Add(item.weaponPrefab)) models.Add(item);
            models.Sort((a, b) => string.CompareOrdinal(a.weaponPrefab.name, b.weaponPrefab.name));

            // What must hold still, or keep out of the pictures, while they are compared.
            float timeScale = Time.timeScale;
            var suns = new List<(Light light, LightShadows shadows)>();
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (light.type == LightType.Directional && light.shadows != LightShadows.None) suns.Add((light, light.shadows));
            var pets = new List<GameObject>();
            // A pet glows too. The follower sits on its owner: it is the pet's own model that is put away.
            foreach (PetFollower pet in Object.FindObjectsByType<PetFollower>(FindObjectsInactive.Exclude))
                if (pet.PetTransform != null && pet.PetTransform.gameObject.activeSelf) pets.Add(pet.PetTransform.gameObject);

            var body = new List<Renderer>();
            foreach (SkinnedMeshRenderer skin in me.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                if (skin.enabled && skin.GetComponentInParent<WeaponVisualMount>() == null && skin.sharedMesh != null) body.Add(skin);

            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "Glow Leak Check" };
            var read = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            Camera cam = MakeCamera(rt);
            GameObject wall = MakeWall();
            var results = new List<Result>();
            var sheets = new Dictionary<string, List<(string label, Color32[] picture)>>();

            foreach ((Light light, LightShadows _) in suns) light.shadows = LightShadows.None;
            foreach (GameObject pet in pets) pet.SetActive(false);
            GlowHaloFeature.ClockOverride = 5f;

            // Carried first, then drawn: the weapon is drawn once and every model is put in the hand.
            for (int drawn = 0; drawn < 2; drawn++)
            {
                Time.timeScale = 1f;
                if (motor != null) motor.RequestSetWeaponDraw(drawn == 1);
                yield return new WaitForSeconds(2.5f);
                Time.timeScale = 0f;

                int at = 0;
                foreach (ItemDefinition item in models)
                {
                    mount.SetWeapon(item);
                    yield return null;
                    yield return null;
                    var weapon = new List<Renderer>(mount.GetComponentsInChildren<Renderer>(false));
                    foreach (Look look in Looks)
                    {
                        if (!GearProgressionRules.TryGetGlow(equipment.Progression.temper, look.temper, look.rarity, out Color colour, out TemperGlowLook glow))
                            continue;
                        mount.SetGlow(true, colour, glow);
                        Result result = results.Find(r => r.model == item.weaponPrefab.name && r.look == look.name);
                        if (result == null) results.Add(result = new Result { model = item.weaponPrefab.name, look = look.name });

                        // Carried: from the front, where the body stands before the weapon. Drawn: from behind.
                        Place(cam, me.transform, behind: drawn == 1);
                        wall.SetActive(false);
                        GlowHaloFeature.Suspended = true;
                        Color32[] plain = Shoot(cam, rt, read);
                        Hide(weapon, true);
                        Color32[] bare = Shoot(cam, rt, read);
                        Hide(body, true);
                        Color32[] empty = Shoot(cam, rt, read);
                        Hide(body, false);
                        Hide(weapon, false);
                        GlowHaloFeature.Suspended = false;
                        Color32[] lit = Shoot(cam, rt, read);

                        int light = 0, onBody = 0;
                        for (int i = 0; i < lit.Length; i++)
                        {
                            if (!Differ(lit[i], plain[i])) continue;
                            light++;
                            // The body is seen here, and the weapon is not in front of it.
                            if (!Differ(bare[i], empty[i], Differs * 2) || Differ(plain[i], bare[i])) continue;
                            onBody++;
                            lit[i] = Mark;
                        }
                        if (drawn == 1) { result.drawnBody = onBody; result.drawnLight = light; }
                        else { result.carriedBody = onBody; result.carriedLight = light; }
                        Keep(sheets, (drawn == 1 ? "Drawn_" : "Carried_") + look.name, $"{item.weaponPrefab.name} {onBody}", lit);

                        // A wall between the camera and all of it: nothing may show.
                        if (drawn == 1)
                        {
                            wall.SetActive(true);
                            PlaceWall(wall.transform, cam.transform, me.transform);
                            GlowHaloFeature.Suspended = true;
                            Color32[] walled = Shoot(cam, rt, read);
                            GlowHaloFeature.Suspended = false;
                            Color32[] walledLit = Shoot(cam, rt, read);
                            int through = 0;
                            for (int i = 0; i < walledLit.Length; i++)
                            {
                                if (!Differ(walledLit[i], walled[i])) continue;
                                through++;
                                walledLit[i] = Mark;
                            }
                            result.wall = through;
                            if (through > 0) Keep(sheets, "Wall_" + look.name, $"{item.weaponPrefab.name} {through}", walledLit);
                            wall.SetActive(false);
                        }
                    }
                    at++;
                    if (at % 10 == 0) Debug.Log($"[GlowLeaks] {(drawn == 1 ? "drawn" : "carried")}: {at} of {models.Count} weapon models.");
                }
            }

            // Everything back as it was.
            GlowHaloFeature.Suspended = false;
            GlowHaloFeature.ClockOverride = null;
            Time.timeScale = timeScale;
            foreach ((Light light, LightShadows shadows) in suns) if (light != null) light.shadows = shadows;
            foreach (GameObject pet in pets) if (pet != null) pet.SetActive(true);
            Object.Destroy(wall);
            Object.Destroy(cam.gameObject);
            mount.SetGlow(false, default, default);
            if (motor != null) motor.RequestSetWeaponDraw(false);
            if (binder != null)
                typeof(EquipmentVisualBinder).GetMethod("ApplyAll", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(binder, null);

            Write(results, sheets);
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(read);
            Running = false;
        }

        private static Camera MakeCamera(RenderTexture target)
        {
            Camera main = Camera.main;
            var go = new GameObject("Glow Leak Camera") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.enabled = false;
            cam.fieldOfView = 36f;
            cam.nearClipPlane = 0.1f;
            cam.targetTexture = target;
            // The game's own renderer (the glow is one of its features), without what smears a still: no anti-aliasing
            // that looks at earlier frames, no bloom.
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData mainData = main.GetUniversalAdditionalCameraData();
            FieldInfo index = typeof(UniversalAdditionalCameraData).GetField("m_RendererIndex", BindingFlags.Instance | BindingFlags.NonPublic);
            if (index != null && mainData != null) data.SetRenderer((int)index.GetValue(mainData));
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthTexture = true;
            return cam;
        }

        private static GameObject MakeWall()
        {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Glow Leak Wall";
            wall.hideFlags = HideFlags.HideAndDontSave;
            // No collider: the camera's fade must leave it whole.
            Object.Destroy(wall.GetComponent<Collider>());
            wall.transform.localScale = new Vector3(4f, 4f, 0.1f);
            wall.SetActive(false);
            return wall;
        }

        private static void Place(Camera cam, Transform player, bool behind)
        {
            Vector3 chest = player.position + Vector3.up * ChestHeight;
            Vector3 side = behind ? -player.forward : player.forward;
            // A little to one side and from above, as the game's own camera looks.
            Vector3 from = chest + (side + player.right * 0.25f).normalized * ViewDistance + Vector3.up * 0.9f;
            cam.transform.SetPositionAndRotation(from, Quaternion.LookRotation(chest - from, Vector3.up));
        }

        private static void PlaceWall(Transform wall, Transform cam, Transform player)
        {
            Vector3 chest = player.position + Vector3.up * ChestHeight;
            Vector3 toCamera = (cam.position - chest).normalized;
            wall.SetPositionAndRotation(chest + toCamera * 1.3f, Quaternion.LookRotation(toCamera, Vector3.up));
        }

        private static void Hide(List<Renderer> renderers, bool hidden)
        {
            foreach (Renderer renderer in renderers)
                if (renderer != null) renderer.forceRenderingOff = hidden;
        }

        private static Color32[] Shoot(Camera cam, RenderTexture rt, Texture2D read)
        {
            cam.Render();
            RenderTexture before = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            RenderTexture.active = before;
            return read.GetPixels32();
        }

        private static bool Differ(Color32 a, Color32 b, int by = Differs) =>
            Mathf.Abs(a.r - b.r) > by || Mathf.Abs(a.g - b.g) > by || Mathf.Abs(a.b - b.b) > by;

        private static void Keep(Dictionary<string, List<(string, Color32[])>> sheets, string sheet, string label, Color32[] picture)
        {
            if (!sheets.TryGetValue(sheet, out var list)) sheets[sheet] = list = new List<(string, Color32[])>();
            list.Add((label, picture));
        }

        private static void Write(List<Result> results, Dictionary<string, List<(string label, Color32[] picture)>> sheets)
        {
            Directory.CreateDirectory(OutputFolder);
            results.Sort((a, b) => (b.wall * 1000 + b.drawnBody + b.carriedBody).CompareTo(a.wall * 1000 + a.drawnBody + a.carriedBody));
            var table = new StringBuilder("model\tlook\tthrough wall\tcarried: on body\tcarried: all light\tdrawn: on body\tdrawn: all light\n");
            int walls = 0;
            foreach (Result r in results)
            {
                table.Append($"{r.model}\t{r.look}\t{r.wall}\t{r.carriedBody}\t{r.carriedLight}\t{r.drawnBody}\t{r.drawnLight}\n");
                if (r.wall > 0) walls++;
            }
            File.WriteAllText(OutputFolder + "/GlowLeaks.tsv", table.ToString());

            foreach (var sheet in sheets)
            {
                int count = sheet.Value.Count;
                int columns = Mathf.Min(SheetColumns, count);
                int rows = (count + columns - 1) / columns;
                var atlas = new Texture2D(columns * Size, rows * Size, TextureFormat.RGBA32, false);
                var index = new StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    int column = i % columns, row = i / columns;
                    atlas.SetPixels32(column * Size, (rows - 1 - row) * Size, Size, Size, sheet.Value[i].picture);
                    index.Append($"row {row + 1} column {column + 1}\t{sheet.Value[i].label}\n");
                }
                File.WriteAllBytes($"{OutputFolder}/{sheet.Key}.png", atlas.EncodeToPNG());
                File.WriteAllText($"{OutputFolder}/{sheet.Key}.txt", index.ToString());
                Object.Destroy(atlas);
            }
            Debug.Log($"[GlowLeaks] Done: {results.Count} cases, {walls} with light through a wall. Table and sheets are in {Path.GetFullPath(OutputFolder)}.");
        }
    }
}
