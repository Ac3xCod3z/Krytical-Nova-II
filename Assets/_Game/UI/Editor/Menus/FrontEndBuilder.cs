using System.Collections.Generic;
using System.Linq;
using KryticalNova.CharacterCreation;
using KryticalNova.Player;
using KryticalNova.UI.Menus;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the front-end scenes (Title, FactionSelect, CharacterCreator, CharacterSelect, Loading) from Synty's
    /// Fantasy Menus and Fantasy Screens prefabs, with our comic layer on top: Ben-Day dot shading, a halftone
    /// wipe between scenes, and the live character showroom. The vendor prefabs are instanced, never edited.
    /// Re-running rebuilds each scene from scratch, so tune looks here (or in the runtime controllers), not by hand.
    /// </summary>
    public static partial class FrontEndBuilder
    {
        public const string SceneFolder = BuildSceneOrder.MenuSceneFolder;
        public const string MaterialFolder = "Assets/_Game/UI/Menus/Materials";
        public const string GlyphSetPath = "Assets/_Game/UI/Menus/KeyGlyphs.asset";
        private const string KeyboardGlyphs = "Assets/Synty/InterfaceCore/Sprites/Icons_Input/MouseKeyboard/ICON_Input_PC_";
        private const string Menus = "Assets/Synty/InterfaceFantasyMenus/Prefabs/";
        private const string ModelPrefabPath = "Assets/_Game/Characters/Player/Player_01/Player_01.prefab";
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string MenuRendererName = "UIPreview_Renderer"; // comic outlines and dots, without the world's sky-driven passes

        // Colour bible (Rendering-Bible.md, section 6b).
        private static readonly Color Ink = new Color32(0x14, 0x12, 0x2B, 0xFF);
        private static readonly Color ShadowIndigo = new Color32(0x4C, 0x52, 0xBF, 0xFF);
        private static readonly Color Paper = new Color32(0xF4, 0xEE, 0xDF, 0xFF);
        private static readonly Color DeepIndigo = new Color32(0x2B, 0x2C, 0x6E, 0xFF);

        private static Material wipeMaterial, dotsMaterial, backdropMaterial, pedestalMaterial;
        private static KeyGlyphSet glyphs;
        private static Scene buildScene;

        [MenuItem("Krytical Nova/Front End/1. Build Front-End Scenes")]
        public static void BuildAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[FrontEnd] Leave Play mode before building the front-end scenes.");
                return;
            }
            var database = AssetDatabase.LoadAssetAtPath<CharacterCreationDatabase>(CharacterDataBaker.DatabasePath);
            if (database == null)
            {
                Debug.LogError("[FrontEnd] No character database. Run Krytical Nova/Characters/Bake Everything first.");
                return;
            }
            WeatherVfxBuilder.EnsureFolder(SceneFolder);
            EnsureMaterials();
            EnsureGlyphs();

            BuildTitle();
            BuildFactionSelect(database);
            BuildCharacterCreator(database);
            BuildCharacterSelect(database);
            BuildLoading(database);
            BuildSceneOrder.Apply();
            AssetDatabase.SaveAssets();
            Debug.Log($"[FrontEnd] Front-end scenes built in {SceneFolder}. Build order: Title first, the zone after the menus.");
        }

        public static string ScenePath(string sceneName) => $"{SceneFolder}/{sceneName}.unity";

        // ---------------------------------------------------------------- scenes

        private static void BuildTitle()
        {
            var build = Begin(FrontEnd.TitleScene);
            AddCamera(Ink);
            var canvas = AddCanvas();
            var screen = AddScreen(canvas, Menus + "Screens/Screen_FantasyMenus_Title_01.prefab", "Screen_Title");
            ComicBackground(screen);
            // The pack sets its two title halves with a large first letter; keep that shape for our name.
            foreach (var label in Labels(screen, "Title_Fantasy")) SetText(label, "<size=330><cspace=-16>K<size=270><cspace=-4>RYTICAL");
            foreach (var label in Labels(screen, "Title_Menus")) SetText(label, " <size=330><cspace=-10>N<size=270><cspace=-8>OVA <size=330><cspace=4>II");
            // "Press [key] to Continue": the pack ships an Xbox button here.
            StandaloneGlyph(MenuUi.Find(screen, "Input_Button"), Key.Enter);
            // The way out: a quieter line under the prompt, and the popup that asks before the game closes.
            AddExitHint(screen);
            var popup = AddScreen(canvas, Menus + "Popups/Popup_FantasyMenus_Save_Confirmation_01.prefab", "Popup_ExitGame");

            var controller = new GameObject("Title Controller");
            Parent(controller, "--- Systems ---");
            var hotkeys = AddHotkeys(controller);
            var title = controller.AddComponent<TitleScreenController>();
            SetRef(title, "screen", screen);
            SetRef(title, "exitPopup", popup.gameObject);
            SetRef(title, "hotkeys", hotkeys);
            AddTransition();
            End(build);
        }

        /// <summary>"Esc  Exit Game" under the title's prompt, in the prompt's own lettering.</summary>
        private static void AddExitHint(Transform screen)
        {
            var source = MenuUi.Find<TMP_Text>(screen, "Label_Continue");
            if (source == null) return;
            var go = Object.Instantiate(source.gameObject, screen);
            go.name = TitleScreenController.ExitHintName;
            // Only the lettering is wanted: the copy sat in a layout row and must not bring its sizing along.
            foreach (var extra in go.GetComponents<Component>())
                if (!(extra is RectTransform) && !(extra is TMP_Text) && !(extra is CanvasRenderer)) Object.DestroyImmediate(extra);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, 230f);
            rect.sizeDelta = new Vector2(1400f, 100f);
            var label = go.GetComponent<TMP_Text>();
            label.enableAutoSizing = false;
            label.fontSize = source.fontSize * 0.62f;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.text = "Esc   Exit Game";
            label.alpha = 0.8f;
        }

        private static void BuildLoading(CharacterCreationDatabase database)
        {
            var build = Begin(FrontEnd.LoadingScene);
            AddCamera(Ink);
            var canvas = AddCanvas();
            var screen = AddScreen(canvas, Menus + "Screens/Screen_FantasyMenus_Loading_01.prefab", "Screen_Loading");
            ComicBackground(screen);

            var controller = new GameObject("Loading Controller");
            Parent(controller, "--- Systems ---");
            var loading = controller.AddComponent<LoadingScreenController>();
            SetRef(loading, "screen", screen);
            SetRef(loading, "database", database);
            AddTransition();
            End(build);
        }

        private static void BuildCharacterSelect(CharacterCreationDatabase database)
        {
            var build = Begin(FrontEnd.CharacterSelectScene);
            var camera = AddCamera(Ink);
            var showroom = AddShowroom(camera, database, 0.42f);
            var canvas = AddCanvas();
            AddDragSurface(canvas, showroom);
            var screen = AddScreen(canvas, Menus + "Screens/Screen_FantasyMenus_Load_Game_01.prefab", "Screen_CharacterSelect");
            ShowroomBackground(screen);
            InkBands(screen);
            var popup = AddScreen(canvas, Menus + "Popups/Popup_FantasyMenus_Save_Confirmation_01.prefab", "Popup_DeleteCharacter");

            var controller = new GameObject("Character Select Controller");
            Parent(controller, "--- Systems ---");
            var hotkeys = AddHotkeys(controller);
            var select = controller.AddComponent<CharacterSelectController>();
            SetRef(select, "screen", screen);
            SetRef(select, "deletePopup", popup.gameObject);
            SetRef(select, "showroom", showroom);
            SetRef(select, "hotkeys", hotkeys);
            AddTransition();
            End(build);
        }

        // ---------------------------------------------------------------- scene scaffolding

        private struct SceneBuild
        {
            public Scene scene, previous;
            public string path;
            public bool keepOpen;
        }

        /// <summary>Opens a fresh scene next to whatever the user has open and makes it the target for new objects.</summary>
        private static SceneBuild Begin(string sceneName)
        {
            var build = new SceneBuild { path = ScenePath(sceneName) };

            // Rebuilding a scene that is open: it is regenerated from scratch, so the open copy is dropped.
            var open = SceneManager.GetSceneByPath(build.path);
            if (open.IsValid() && open.isLoaded)
            {
                if (SceneManager.sceneCount > 1) EditorSceneManager.CloseScene(open, true);
                else build.keepOpen = true; // the only open scene cannot be closed: replace it, and leave the new one open
            }

            if (build.keepOpen)
            {
                build.scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            else
            {
                build.previous = SceneManager.GetActiveScene();
                build.scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(build.scene);
            }
            buildScene = build.scene;

            // Menus have no sky or day cycle: a fixed studio ambient.
            RenderSettings.skybox = null;
            RenderSettings.sun = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.36f, 0.40f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.24f, 0.25f, 0.33f);
            RenderSettings.ambientGroundColor = new Color(0.11f, 0.10f, 0.14f);
            RenderSettings.subtractiveShadowColor = Ink;

            foreach (var header in new[] { "--- Systems ---", "--- Cameras ---", "--- Lighting & FX ---", "--- UI ---", "--- Environment ---" })
                new GameObject(header);
            AddEventSystem();
            return build;
        }

        private static void End(SceneBuild build)
        {
            EditorSceneManager.SaveScene(build.scene, build.path);
            buildScene = default;
            if (build.keepOpen) return;
            if (build.previous.IsValid()) SceneManager.SetActiveScene(build.previous);
            EditorSceneManager.CloseScene(build.scene, true);
        }

        // Header objects are looked up in the scene being built only: other open scenes use the same header names.
        private static void Parent(GameObject go, string header)
        {
            foreach (var root in buildScene.GetRootGameObjects())
            {
                if (root.name != header) continue;
                go.transform.SetParent(root.transform, false);
                return;
            }
        }

        private static void AddEventSystem()
        {
            var go = new GameObject("EventSystem");
            Parent(go, "--- Systems ---");
            go.AddComponent<EventSystem>();
            var module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions(); // the project-wide actions' UI map, like the zone scenes
        }

        private static Camera AddCamera(Color background)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            Parent(go, "--- Cameras ---");
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.fieldOfView = 30f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 60f;
            go.transform.SetPositionAndRotation(new Vector3(0f, 1.2f, -4.5f), Quaternion.identity);
            go.AddComponent<AudioListener>();
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; // FXAA smears ink lines, TAA ghosts dots
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderShadows = true;
            int renderer = RendererIndex(MenuRendererName);
            if (renderer >= 0) data.SetRenderer(renderer);
            return camera;
        }

        private static int RendererIndex(string rendererName)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                var list = urp.rendererDataList;
                for (int i = 0; i < list.Length; i++)
                    if (list[i] != null && list[i].name == rendererName) return i;
            }
            Debug.LogWarning($"[FrontEnd] URP renderer '{rendererName}' not found; menu cameras use the default renderer.");
            return -1;
        }

        private static Transform AddCanvas()
        {
            var go = new GameObject("UI_Canvas", typeof(RectTransform));
            Parent(go, "--- UI ---");
            go.layer = LayerMask.NameToLayer("UI");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.AddComponent<CanvasScaler>();
            // Same as the Synty demo scenes, so their layouts hold: authored at 4K, scaled by height.
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return go.transform;
        }

        /// <summary>Instances a vendor screen under the canvas, stretched to fill it. The link to the vendor prefab is kept.</summary>
        private static Transform AddScreen(Transform canvas, string prefabPath, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[FrontEnd] Vendor prefab missing: {prefabPath}");
                var missing = new GameObject(name, typeof(RectTransform));
                missing.transform.SetParent(canvas, false);
                return missing.transform;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas);
            go.name = name;
            Stretch((RectTransform)go.transform);
            return go.transform;
        }

        /// <summary>The halftone wipe, on its own top-most canvas so it covers every screen, popup and nested vendor canvas.</summary>
        private static void AddTransition()
        {
            // A root canvas (its header parent is a plain object): the Loading scene carries it into the world.
            var go = new GameObject("Transition_Canvas", typeof(RectTransform));
            Parent(go, "--- UI ---");
            go.layer = LayerMask.NameToLayer("UI");
            var overlay = go.AddComponent<Canvas>();
            overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            overlay.sortingOrder = 1000;
            go.AddComponent<GraphicRaycaster>();

            var coverGo = new GameObject("Halftone Wipe", typeof(RectTransform));
            coverGo.layer = go.layer;
            coverGo.transform.SetParent(go.transform, false);
            Stretch((RectTransform)coverGo.transform);
            var cover = coverGo.AddComponent<Image>();
            cover.material = wipeMaterial;
            cover.color = Color.white;
            cover.raycastTarget = true;

            var transition = go.AddComponent<MenuTransition>();
            SetRef(transition, "cover", cover);
        }

        // ---------------------------------------------------------------- comic layer

        /// <summary>
        /// For screens without a 3D scene behind them: the pack's sample painting is replaced by an indigo field
        /// printed with Ben-Day dots, the same dots the world's comic pass draws.
        /// </summary>
        private static void ComicBackground(Transform screen)
        {
            var background = MenuUi.Find(screen, "Background");
            if (background == null) return;
            Hide(MenuUi.Find(background, "SPR_Screenshot"));
            var vignette = MenuUi.Find(background, "Vignette");
            if (vignette != null) Hide(MenuUi.Find(vignette, "SPR_Fill")); // the pack's dark wash would bury the dots
            var fill = MenuUi.Find<Image>(background, "SPR_Background");
            if (fill != null)
            {
                fill.sprite = null;
                fill.color = DeepIndigo;
                fill.material = dotsMaterial;
                Record(fill);
            }
            Tint(MenuUi.Find<Image>(background, "SPR_Glow"), ShadowIndigo, 0.55f);
            if (vignette != null) Tint(MenuUi.Find<Image>(vignette, "SPR_Vignette"), Ink, 0.9f);
        }

        /// <summary>
        /// For screens over the live showroom: remove the opaque layers so the 3D scene shows through, and recolour
        /// what stays (vignette, edge gradients) to Ink. Handles the background of both packs.
        /// </summary>
        private static void ShowroomBackground(Transform screen)
        {
            var background = MenuUi.Find(screen, "Background");
            if (background == null) return;
            foreach (string opaque in new[] { "SPR_Screenshot", "SPR_Background", "SPR_Glow", "SPR_Fill" })
                Hide(MenuUi.Find(background, opaque));
            Tint(MenuUi.Find<Image>(background, "SPR_Vignette"), Ink, 0.75f);
            // Fantasy Screens darkens the top and bottom with two tall gradients.
            Retint(MenuUi.Find<Image>(background, "SPR_Gradient_Top"), Ink);
            Retint(MenuUi.Find<Image>(background, "SPR_Gradient_Bottom"), Ink);
        }

        /// <summary>The pack's dark blue header and footer bands, recoloured to Ink so they sit with any faction colour.</summary>
        private static void InkBands(Transform screen)
        {
            Tint(MenuUi.Find<Image>(screen, "SPR_Fill_Top"), Ink, 1f);
            Tint(MenuUi.Find<Image>(screen, "SPR_Gradient_Top"), Ink, 1f);
            Tint(MenuUi.Find<Image>(screen, "SPR_Gradient_Bottom"), Ink, 0.5f);
            var footer = MenuUi.Find(screen, "Input_Menu");
            if (footer != null) Tint(MenuUi.Find<Image>(footer, "SPR_Background"), Ink, 1f);
        }

        // ---------------------------------------------------------------- keyboard glyphs

        /// <summary>
        /// The front end shows keyboard glyphs only. This fills the glyph set from the pack's keyboard sprites; keys
        /// without a picture are printed on the keycap as text.
        /// </summary>
        private static void EnsureGlyphs()
        {
            glyphs = AssetDatabase.LoadAssetAtPath<KeyGlyphSet>(GlyphSetPath);
            if (glyphs == null)
            {
                glyphs = ScriptableObject.CreateInstance<KeyGlyphSet>();
                AssetDatabase.CreateAsset(glyphs, GlyphSetPath);
            }
            var pictured = new (Key key, string sprite)[]
            {
                (Key.Enter, "Enter"), (Key.NumpadEnter, "Enter"),
                (Key.Space, "Space"), (Key.Tab, "Tab"),
                (Key.Backspace, "Backspace"),
                (Key.UpArrow, "Arrow_Up"), (Key.DownArrow, "Arrow_Down"),
                (Key.LeftArrow, "Arrow_Left"), (Key.RightArrow, "Arrow_Right")
            };
            var list = new List<KeyGlyphSet.Glyph>();
            foreach (var (key, sprite) in pictured)
            {
                var onKeycap = AssetDatabase.LoadAssetAtPath<Sprite>($"{KeyboardGlyphs}{sprite}_Clean.png");
                var standalone = AssetDatabase.LoadAssetAtPath<Sprite>($"{KeyboardGlyphs}{sprite}_Underlay.png");
                if (onKeycap == null || standalone == null)
                {
                    Debug.LogWarning($"[FrontEnd] Keyboard glyph sprites for {key} not found; it will show as text.");
                    continue;
                }
                list.Add(new KeyGlyphSet.Glyph { key = key, onKeycap = onKeycap, standalone = standalone });
            }
            const string mouse = KeyboardGlyphs + "MouseColor_";
            glyphs.SetContents(list.ToArray(),
                AssetDatabase.LoadAssetAtPath<Sprite>(mouse + "Left_Underlay.png"),
                AssetDatabase.LoadAssetAtPath<Sprite>(mouse + "Right_Underlay.png"),
                AssetDatabase.LoadAssetAtPath<Sprite>(mouse + "Middle_Underlay.png"));
            EditorUtility.SetDirty(glyphs);
        }

        private static MenuHotkeys AddHotkeys(GameObject controller)
        {
            var hotkeys = controller.AddComponent<MenuHotkeys>();
            SetRef(hotkeys, "glyphs", glyphs);
            return hotkeys;
        }

        /// <summary>Replaces the gamepad button on a bare "press this" prompt (Input_Button) with a keyboard glyph.</summary>
        private static void StandaloneGlyph(Transform button, Key key)
        {
            var icon = button != null ? MenuUi.Find<Image>(button, "ICON") : null;
            if (icon == null || glyphs == null || !glyphs.TryGet(key, out var glyph)) return;
            icon.sprite = glyph.standalone;
            icon.preserveAspect = true;
            Record(icon);
        }

        private static void EnsureMaterials()
        {
            WeatherVfxBuilder.EnsureFolder(MaterialFolder);
            wipeMaterial = EnsureMaterial("M_UI_BenDayWipe", "KryticalNova/UI/BenDayWipe", m =>
            {
                m.SetColor("_Color", Ink);
                m.SetFloat("_DotScale", 5f);
                m.SetFloat("_Softness", 0.55f);
            });
            dotsMaterial = EnsureMaterial("M_UI_BenDay_Field", "KryticalNova/UI/BenDay", m =>
            {
                m.SetColor("_InkColor", Ink);
                m.SetFloat("_DotOpacity", 1f);
                m.SetFloat("_CoverageFrom", 0.92f);
                m.SetFloat("_CoverageTo", 0.2f);
                m.SetVector("_Direction", new Vector4(0.25f, -1f, 0f, 0f));
                m.SetFloat("_DotScale", 2.5f);
            });
            backdropMaterial = EnsureMaterial("M_Showroom_Backdrop", "KryticalNova/UI/Showroom Backdrop", m =>
            {
                m.SetColor("_BaseColor", Ink);
                m.SetColor("_GlowColor", ShadowIndigo);
                m.SetVector("_Centre", new Vector4(0f, -1f, 0f, 0f));
                m.SetFloat("_Radius", 5.5f);
                m.SetFloat("_Falloff", 1.4f);
                m.SetFloat("_DotScale", 2f);
            });
            pedestalMaterial = EnsureMaterial("M_Showroom_Pedestal", "KryticalNova/Comic Toon Lit",
                m => m.SetColor("_BaseColor", new Color(0.05f, 0.05f, 0.08f))); // dark: three lights meet on it
        }

        /// <summary>Creates the material once. Existing materials are left alone, so values tuned in the Inspector survive.</summary>
        private static Material EnsureMaterial(string name, string shaderName, System.Action<Material> setup)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[FrontEnd] Shader '{shaderName}' not found.");
                return null;
            }
            material = new Material(shader) { name = name };
            setup(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ---------------------------------------------------------------- showroom

        /// <summary>The lit stage the live character stands on, shared by the creator and character select.</summary>
        private static CharacterShowroom AddShowroom(Camera camera, CharacterCreationDatabase database, float screenShiftX)
        {
            var root = new GameObject("Showroom");
            Parent(root, "--- Environment ---");
            var stage = new GameObject("Stage").transform;
            stage.SetParent(root.transform, false);

            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "Pedestal";
            Object.DestroyImmediate(pedestal.GetComponent<Collider>());
            pedestal.transform.SetParent(root.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, -0.06f, 0f); // top face at the character's feet
            pedestal.transform.localScale = new Vector3(1.9f, 0.06f, 1.9f);
            pedestal.GetComponent<Renderer>().sharedMaterial = pedestalMaterial;

            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = "Backdrop";
            Object.DestroyImmediate(backdrop.GetComponent<Collider>());
            backdrop.transform.SetParent(root.transform, false);
            backdrop.transform.localPosition = new Vector3(0f, 2f, 6f);
            backdrop.transform.localScale = new Vector3(40f, 20f, 1f);
            var backdropRenderer = backdrop.GetComponent<Renderer>();
            backdropRenderer.sharedMaterial = backdropMaterial;
            backdropRenderer.shadowCastingMode = ShadowCastingMode.Off;
            backdropRenderer.receiveShadows = false;

            // The character faces -Z (towards the camera): key and fill come from the front, rim and kicker from behind.
            var lights = new GameObject("Showroom Lights");
            Parent(lights, "--- Lighting & FX ---");
            var key = AddLight(lights.transform, "Key", LightType.Directional, new Color(1f, 0.95f, 0.88f), 2.4f);
            key.transform.rotation = Quaternion.Euler(38f, -32f, 0f);
            key.shadows = LightShadows.Soft; // the one shadowed light (Rendering Bible, section 3)
            var fill = AddLight(lights.transform, "Fill", LightType.Spot, new Color(0.55f, 0.6f, 0.75f), 22f);
            fill.transform.SetPositionAndRotation(new Vector3(2.6f, 1.6f, -3.2f), Quaternion.LookRotation(new Vector3(-2.6f, -0.5f, 3.2f)));
            var rim = AddLight(lights.transform, "Rim", LightType.Spot, new Color(0.6f, 0.85f, 1f), 60f);
            rim.transform.SetPositionAndRotation(new Vector3(-1.8f, 2.6f, 2.8f), Quaternion.LookRotation(new Vector3(1.8f, -1.5f, -2.8f)));
            var kicker = AddLight(lights.transform, "Kicker", LightType.Spot, new Color(1f, 0.82f, 0.6f), 26f);
            kicker.transform.SetPositionAndRotation(new Vector3(2.2f, 0.5f, 2.2f), Quaternion.LookRotation(new Vector3(-2.2f, 0.6f, -2.2f)));

            var showroom = root.AddComponent<CharacterShowroom>();
            SetRef(showroom, "database", database);
            SetRef(showroom, "modelPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath));
            SetRef(showroom, "idleClip", PlayerIdleClip());
            var expressions = AssetDatabase.LoadAssetAtPath<CharacterExpressionLibrary>(CharacterExpressionBaker.LibraryPath);
            if (expressions == null) expressions = CharacterExpressionBaker.Bake();
            SetRef(showroom, "expressions", expressions);
            SetRef(showroom, "viewCamera", camera);
            SetRef(showroom, "stage", stage);
            SetRef(showroom, "rimLight", rim);
            SetRef(showroom, "fillLight", fill);
            var so = new SerializedObject(showroom);
            so.FindProperty("screenShiftX").floatValue = screenShiftX;
            var backdrops = so.FindProperty("backdrop");
            backdrops.arraySize = 1;
            backdrops.GetArrayElementAtIndex(0).objectReferenceValue = backdropRenderer;
            so.ApplyModifiedPropertiesWithoutUndo();
            return showroom;
        }

        private static Light AddLight(Transform parent, string name, LightType type, Color color, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var light = go.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            if (type == LightType.Spot)
            {
                light.range = 12f;
                light.spotAngle = 55f;
                light.innerSpotAngle = 30f;
            }
            return light;
        }

        /// <summary>The idle the player uses in the world, so the showroom character stands the same way.</summary>
        private static AnimationClip PlayerIdleClip()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var motor = player != null ? player.GetComponentInChildren<PlayerAnimancerMotor>(true) : null;
            if (motor == null || motor.IdleClip == null) Debug.LogWarning("[FrontEnd] No idle clip found on the player prefab; the showroom character will hold its bind pose.");
            return motor != null ? motor.IdleClip : null;
        }

        private static void AddDragSurface(Transform canvas, CharacterShowroom showroom)
        {
            var go = new GameObject("Showroom Drag Surface", typeof(RectTransform));
            go.layer = canvas.gameObject.layer;
            go.transform.SetParent(canvas, false);
            go.transform.SetAsFirstSibling(); // behind every panel: only empty screen turns the character
            Stretch((RectTransform)go.transform);
            var surface = go.AddComponent<ShowroomDragSurface>();
            SetRef(surface, "showroom", showroom);
        }

        // ---------------------------------------------------------------- helpers

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static IEnumerable<TMP_Text> Labels(Transform screen, string groupName)
        {
            var group = MenuUi.Find(screen, groupName);
            return group != null ? group.GetComponentsInChildren<TMP_Text>(true) : Enumerable.Empty<TMP_Text>();
        }

        // Changes to an instanced vendor prefab must be recorded, or they are lost when the scene is saved.
        private static void Record(Object target)
        {
            EditorUtility.SetDirty(target);
            PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        }

        private static void SetText(TMP_Text label, string text)
        {
            if (label == null) return;
            label.text = text;
            Record(label);
        }

        /// <summary>Recolours a vendor image, keeping its sprite. Alpha is the layer's strength.</summary>
        private static void Tint(Graphic graphic, Color color, float alpha)
        {
            if (graphic == null) return;
            graphic.color = new Color(color.r, color.g, color.b, alpha);
            Record(graphic);
        }

        /// <summary>Recolours a vendor image, keeping its own strength.</summary>
        private static void Retint(Graphic graphic, Color color)
        {
            if (graphic != null) Tint(graphic, color, graphic.color.a);
        }

        private static void Hide(Transform target)
        {
            if (target == null) return;
            target.gameObject.SetActive(false);
            Record(target.gameObject);
        }

        private static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[FrontEnd] {target.GetType().Name} has no serialized field '{field}'.");
                return;
            }
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
