using System.Collections.Generic;
using System.Linq;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Player;
using KryticalNova.UI;
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
        // The faction skin whose version of a scene's UI is being built, and the object all of it hangs under.
        private static UiSkin skin;
        private static Transform skinGroup;

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
            EnsureBook();
            EnsureMenuGlow();

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
            var groups = new List<MenuSkinSwitch.Group>();
            foreach (UiSkin each in Skins())
            {
                BeginSkin(canvas, each);
                var screen = AddScreen(canvas, skin.titleScreen, "Screen_Title");
                ComicBackground(screen);
                TitleLettering(screen);
                // "Press [key] to Continue": the pack ships an Xbox button here.
                StandaloneGlyph(MenuUi.Find(screen, "Input_Button"), Key.Enter);
                // The way out: a quieter line under the prompt, and the popup that asks before the game closes.
                AddExitHint(screen);
                var popup = AddScreen(canvas, skin.confirmPopup, "Popup_ExitGame");

                var controller = SkinObject("Title Controller");
                var hotkeys = AddHotkeys(controller);
                var title = controller.AddComponent<TitleScreenController>();
                SetRef(title, "screen", screen);
                SetRef(title, "exitPopup", popup.gameObject);
                SetRef(title, "hotkeys", hotkeys);
                EndSkin(groups);
            }
            AddSkinSwitch(canvas, groups);
            AddTransition();
            End(build);
        }

        /// <summary>
        /// The game's name in the pack's own title lettering. Every pack sets its title in two halves, each with a
        /// large first letter ("FANTASY" "MENUS", "SCI-FI" "MENUS"): the halves keep their sizes and spacing and take
        /// our letters, a little smaller because our halves are longer.
        /// </summary>
        private static void TitleLettering(Transform screen)
        {
            var title = MenuUi.Find(screen, "Title");
            if (title == null || title.childCount < 2) return;
            // Two parts ("FANTASY" "MENUS") or three ("DARK" "FANTASY" "MENUS"): the numeral is the third, or ends the second.
            bool three = title.childCount >= 3;
            foreach (var label in title.GetChild(0).GetComponentsInChildren<TMP_Text>(true)) SetTitle(label, "K", "RYTICAL", null);
            foreach (var label in title.GetChild(1).GetComponentsInChildren<TMP_Text>(true)) SetTitle(label, "N", three ? "OVA" : "OVA ", three ? null : "II");
            if (three)
                foreach (var label in title.GetChild(2).GetComponentsInChildren<TMP_Text>(true)) SetTitle(label, "II", string.Empty, null);
            CentreTitle((RectTransform)title);
        }

        /// <summary>
        /// The packs set each part of their title in a room of its own, placed for their own words. Ours are other
        /// lengths, which leaves the name to one side of the screen's middle: the whole title is moved so that what
        /// is lettered, first letter to last, is centred where the pack centred its title.
        /// </summary>
        private static void CentreTitle(RectTransform title)
        {
            var parent = title.parent as RectTransform;
            if (parent == null) return;
            Canvas.ForceUpdateCanvases();
            float left = float.MaxValue, right = float.MinValue;
            foreach (TMP_Text label in title.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!label.name.EndsWith("_Main")) continue; // the face itself, not its shadow and underlay
                label.ForceMeshUpdate();
                Bounds lettered = label.textBounds;
                if (lettered.size.x <= 0f) continue;
                foreach (float x in new[] { lettered.min.x, lettered.max.x })
                {
                    float inParent = parent.InverseTransformPoint(label.transform.TransformPoint(new Vector3(x, 0f, 0f))).x;
                    left = Mathf.Min(left, inParent);
                    right = Mathf.Max(right, inParent);
                }
            }
            if (left > right) return;
            float middle = parent.InverseTransformPoint(title.TransformPoint(title.rect.center)).x;
            title.anchoredPosition += new Vector2(middle - (left + right) * 0.5f, 0f);
            Record(title);
        }

        /// <summary>One part of the title, on one line, made smaller until it fits the room the pack gave that part.</summary>
        private static void SetTitle(TMP_Text label, string first, string rest, string tail)
        {
            label.text = InTitleShape(label.text, first, rest, tail, out float size);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.enableAutoSizing = false;
            label.fontSize = size;
            // Our word is longer than the pack's: where it does not fit the room the pack gave that part, it is set
            // smaller by as much as it is too wide (never broken in two, never sized by the room's height).
            Canvas.ForceUpdateCanvases();
            float room = label.rectTransform.rect.width;
            float needed = label.GetPreferredValues(label.text, 100000f, 100000f).x;
            if (room > 1f && needed > room) label.fontSize = size * room / needed;
            Record(label);
        }

        private static readonly System.Text.RegularExpressions.Regex TitleShape = new System.Text.RegularExpressions.Regex(
            @"^(\s*)<size=(\d+)><cspace=(-?\d+)\s*>\s*(\S)<size=(\d+)><cspace=(-?\d+)\s*>(.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);
        private const float TitleShrink = 0.9f;

        /// <summary>
        /// Our letters in the pack's title shape. The sizes are written as shares of the label's own size, which is
        /// handed back: the label can then be sized to fit and keep the large first letter.
        /// </summary>
        private static string InTitleShape(string packText, string first, string rest, string tail, out float size)
        {
            var shape = TitleShape.Match(packText ?? string.Empty);
            size = 270f;
            if (!shape.Success) return first + rest + tail;
            string lead = shape.Groups[1].Value;
            float large = float.Parse(shape.Groups[2].Value), small = float.Parse(shape.Groups[5].Value);
            size = small * TitleShrink;
            int raised = Mathf.RoundToInt(large / small * 100f);
            string wide = shape.Groups[3].Value, narrow = shape.Groups[6].Value;
            if (rest.Length == 0) return $"{lead}<cspace={narrow}><size={raised}%>{first}";
            string text = $"{lead}<size={raised}%><cspace={wide}>{first}<size=100%><cspace={narrow}>{rest}";
            // A numeral at the end stands as tall as the first letter.
            if (!string.IsNullOrEmpty(tail)) text += $"<size={raised}%>{tail}";
            return text;
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
            var groups = new List<MenuSkinSwitch.Group>();
            foreach (UiSkin each in Skins())
            {
                BeginSkin(canvas, each);
                var screen = AddScreen(canvas, skin.loadingScreen, "Screen_Loading");
                ComicBackground(screen);
                EnsureLoadingTip(screen);

                var controller = SkinObject("Loading Controller");
                var loading = controller.AddComponent<LoadingScreenController>();
                SetRef(loading, "screen", screen);
                SetRef(loading, "database", database);
                EndSkin(groups);
            }
            AddSkinSwitch(canvas, groups);
            AddTransition();
            End(build);
        }

        /// <summary>
        /// The tip line of the Loading screen. A pack whose screen has none (Sci-Fi Menus) gets one at the foot of
        /// the screen, lettered like the pack's own line under the bar, so every skin shows the same words.
        /// </summary>
        private static void EnsureLoadingTip(Transform screen)
        {
            if (MenuUi.Find(screen, LoadingScreenController.TipName) != null) return;
            var like = MenuUi.Find<TMP_Text>(screen, "Label_LoadingPercentage");
            Transform content = screen.Find("Content"); // the screen's own, not one of the many deeper in it
            if (like == null || content == null)
            {
                Debug.LogWarning($"[FrontEnd] The {skin.faction} Loading screen has no tip line and no place to add one.");
                return;
            }
            var holder = new GameObject("Tooltip", typeof(RectTransform)) { layer = screen.gameObject.layer };
            var rect = (RectTransform)holder.transform;
            rect.SetParent(content, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(3120f, 100f); // the other packs' tip line, on the 3840 x 2160 canvas
            // This pack's bar sits at the foot of the screen, where the others have their tip: the tip goes over it.
            rect.anchoredPosition = new Vector2(0f, 400f);

            TMP_Text tip = Object.Instantiate(like, rect);
            tip.name = LoadingScreenController.TipName;
            Stretch(tip.rectTransform);
            tip.alignment = TextAlignmentOptions.Center;
            tip.textWrappingMode = TextWrappingModes.Normal;
            tip.overflowMode = TextOverflowModes.Overflow;
            tip.enableAutoSizing = false;
            tip.fontSize = Mathf.Min(like.fontSize, 52f);
            tip.text = string.Empty;
        }

        private static void BuildCharacterSelect(CharacterCreationDatabase database)
        {
            var build = Begin(FrontEnd.CharacterSelectScene);
            var camera = AddCamera(Ink);
            var showroom = AddShowroom(camera, database, 0.42f);
            WireShowroomGear(showroom);
            var canvas = AddCanvas();
            AddDragSurface(canvas, showroom);
            var groups = new List<MenuSkinSwitch.Group>();
            foreach (UiSkin each in Skins())
            {
                BeginSkin(canvas, each);
                var screen = AddScreen(canvas, skin.listScreen, "Screen_CharacterSelect");
                ShowroomBackground(screen);
                InkBands(screen);
                AddSettingsPrompt(screen);
                var popup = AddScreen(canvas, skin.confirmPopup, "Popup_DeleteCharacter");

                var controller = SkinObject("Character Select Controller");
                var hotkeys = AddHotkeys(controller);
                var select = controller.AddComponent<CharacterSelectController>();
                SetRef(select, "screen", screen);
                SetRef(select, "deletePopup", popup.gameObject);
                SetRef(select, "showroom", showroom);
                SetRef(select, "hotkeys", hotkeys);
                EndSkin(groups);
            }
            AddSkinSwitch(canvas, groups);
            AddMenuSettings();
            AddTransition();
            End(build);
        }

        /// <summary>A fourth prompt in the pack's footer row, a copy of its Back prompt: SETTINGS.</summary>
        private static void AddSettingsPrompt(Transform screen)
        {
            var back = MenuUi.Find(screen, "Hotkey_Back");
            if (back == null) return;
            var go = Object.Instantiate(back.gameObject, back.parent);
            go.name = CharacterSelectController.SettingsPromptName;
            go.transform.SetSiblingIndex(back.GetSiblingIndex()); // before Back, which stays last
        }

        /// <summary>
        /// The game's Settings window for a menu scene: a UI Toolkit document of its own over the scene's canvas
        /// (the same window, pages and stores as in the world), opened by the screen's SETTINGS prompt.
        /// </summary>
        private static void AddMenuSettings()
        {
            var go = new GameObject("Menu Settings");
            Parent(go, "--- UI ---");
            var document = go.AddComponent<UnityEngine.UIElements.UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(MenuPanelSettingsPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(MenuSettingsDocumentPath);
            if (document.panelSettings == null || document.visualTreeAsset == null)
                Debug.LogError($"[FrontEnd] Menu Settings needs {MenuPanelSettingsPath} and {MenuSettingsDocumentPath}.");
            var settings = go.AddComponent<MenuSettings>();
            SetRef(settings, "mixer", AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>(AudioSetup.MixerPath));
        }

        // The in-game UI's panel (its theme and scale): the window is the same one, so it is drawn the same way.
        private const string MenuPanelSettingsPath = "Assets/_Game/UI/HUD/ProgressionPanelSettings.asset";
        private const string MenuSettingsDocumentPath = "Assets/_Game/UI/Settings/MenuSettings.uxml";

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

        // ---------------------------------------------------------------- one version of the UI per faction skin

        /// <summary>The three factions' skins: each names the screens of its own Synty menu pack.</summary>
        private static List<UiSkin> Skins()
        {
            FactionSkinBuilder.EnsureSkins();
            var skins = new List<UiSkin>();
            foreach (FactionId faction in FactionSkinBuilder.Factions)
            {
                UiSkin each = FactionSkinBuilder.LoadSkin(faction);
                if (each != null) skins.Add(each);
            }
            return skins;
        }

        /// <summary>
        /// Starts one faction's version of the scene's UI. Its screens, its popups and its controller all hang under
        /// one object on the canvas, which the scene's <see cref="MenuSkinSwitch"/> switches on when the menus wear
        /// that faction's skin.
        /// </summary>
        private static void BeginSkin(Transform canvas, UiSkin of)
        {
            skin = of;
            var go = new GameObject("Skin_" + of.faction, typeof(RectTransform));
            go.layer = canvas.gameObject.layer;
            go.transform.SetParent(canvas, false);
            Stretch((RectTransform)go.transform);
            skinGroup = go.transform;
        }

        /// <summary>A plain object under the skin's root: its controller lives and sleeps with its screens.</summary>
        private static GameObject SkinObject(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(skinGroup, false);
            return go;
        }

        private static void EndSkin(List<MenuSkinSwitch.Group> groups)
        {
            groups.Add(new MenuSkinSwitch.Group { faction = skin.faction, root = skinGroup.gameObject });
            skinGroup.gameObject.SetActive(false); // the switch wakes the one the menus wear
            skin = null;
            skinGroup = null;
        }

        private static void AddSkinSwitch(Transform canvas, List<MenuSkinSwitch.Group> groups)
        {
            var skinSwitch = canvas.gameObject.AddComponent<MenuSkinSwitch>();
            var so = new SerializedObject(skinSwitch);
            SerializedProperty list = so.FindProperty("groups");
            list.arraySize = groups.Count;
            for (int i = 0; i < groups.Count; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("faction").intValue = (int)groups[i].faction;
                element.FindPropertyRelative("root").objectReferenceValue = groups[i].root;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Instances a vendor screen under the skin's root (or the canvas), stretched to fill it. The link to the vendor prefab is kept.</summary>
        private static Transform AddScreen(Transform canvas, GameObject prefab, string name)
        {
            Transform parent = skinGroup != null ? skinGroup : canvas;
            if (prefab == null)
            {
                Debug.LogError($"[FrontEnd] The {(skin != null ? skin.faction.ToString() : "front end")} skin has no screen for '{name}'.");
                var missing = new GameObject(name, typeof(RectTransform));
                missing.transform.SetParent(parent, false);
                return missing.transform;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = name;
            Stretch((RectTransform)go.transform);
            return go.transform;
        }

        private static Transform AddScreen(Transform canvas, string prefabPath, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) Debug.LogError($"[FrontEnd] Vendor prefab missing: {prefabPath}");
            return AddScreen(canvas, prefab, name);
        }

        /// <summary>
        /// The way to the next scene, on its own top-most canvas so it covers every screen, popup and nested vendor
        /// canvas: the page turn of the menus' comic book, and the halftone wipe for when there is no page to turn.
        /// </summary>
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
            AddPageTurn(go, transition);
            AddMenuFeel();
        }

        /// <summary>
        /// The feel of the menus (hover, press, confirm, back, error, the page turn): the game feel's menu prefab,
        /// one in every menu scene. It listens to what the screens say happened; they know nothing of it.
        /// </summary>
        private static void AddMenuFeel()
        {
            GameObject prefab = GameFeelBuilder.EnsureMenuFeel();
            if (prefab == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, buildScene);
            go.name = "Menu Feel";
            Parent(go, "--- Systems ---");
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
            // The glow behind the dots is the one thing of the comic field that is the skin's own colour.
            Tint(MenuUi.Find<Image>(background, "SPR_Glow"), skin != null ? Color.Lerp(ShadowIndigo, skin.menuAccent, 0.6f) : ShadowIndigo, 0.55f);
            Tint(MenuUi.Find<Image>(vignette != null ? vignette : background, "SPR_Vignette"), Ink, 0.9f);
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

        /// <summary>
        /// Character select only: the stage is told where gear is looked up, so its doll wears what the character
        /// was last played in. The creator's stage is left without: a character being made wears no gear.
        /// </summary>
        public static void WireShowroomGear(CharacterShowroom showroom)
        {
            SetRef(showroom, "items", AssetDatabase.LoadAssetAtPath<KryticalNova.Items.ItemCatalog>(SidekickGearGenerator.CatalogPath));
            SetRef(showroom, "progression", AssetDatabase.LoadAssetAtPath<KryticalNova.Items.GearProgressionSettings>(PlayerSystemsSetup.GearProgressionPath));
        }

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
