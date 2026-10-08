using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.UI;
using KryticalNova.UI.Menus;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace KryticalNova.EditorTools
{
    // The two scenes of the "new character" path: choosing a faction, then the creator.
    public static partial class FrontEndBuilder
    {
        private const string Screens = "Assets/Synty/InterfaceFantasyScreens/";
        private const string CharacterIcons = Screens + "Sprites/Icons_Character/ICON_FantasyScreens_Character_";
        private const string OriginTabIcon = "Assets/Synty/InterfaceFantasyMenus/Sprites/Icons_FantasyMenus/SPR_FantasyMenus_Icon_Flag_01.png";
        private const string FinaliseTabIcon = Screens + "Sprites/Icons_Menu/ICON_FantasyScreens_Menu_Done_01_Underlay.png";
        private const string DisciplineTabIcon = Screens + "Sprites/Icons_Inventory/ICON_FantasyScreens_Inventory_Swords_01_Underlay.png";
        private const string PreviewTabIcon = Screens + "Sprites/Icons_Menu/ICON_FantasyScreens_Menu_Message_01_Underlay.png";
        private const string OptionRowPrefab = Menus + "Settings/Settings_FantasyMenus_Item_Array_01.prefab";
        private const string SliderRowPrefab = Screens + "Prefabs/Components/Settings_FantasyScreens_Item_Slider_01.prefab";
        private const string SwatchPrefab = Screens + "Prefabs/Buttons/Button_FantasyScreens_Swatch_01.prefab";
        private const string BodyLabelPrefab = Menus + "_CommonComponents/Label_FantasyMenus_Body.prefab";

        private const int OriginTileCount = 4;  // two lineages, each masculine and feminine
        private const int OptionRowCount = 9;   // the Head tab of a human: look, head, hair, brows, ears, facial hair, nose, teeth + one spare
        private const int SliderRowCount = 2;   // body size and musculature
        private const int SwatchesPerGroup = 16; // the largest palette on offer (hair, elf skin)

        // ---------------------------------------------------------------- faction select

        private static void BuildFactionSelect(CharacterCreationDatabase database)
        {
            var build = Begin(FrontEnd.FactionSelectScene);
            AddCamera(Ink);
            var canvas = AddCanvas();
            var groups = new List<MenuSkinSwitch.Group>();
            foreach (UiSkin each in Skins())
            {
                BeginSkin(canvas, each);
                BuildFactionSelectScreen(canvas, database);
                EndSkin(groups);
            }
            AddSkinSwitch(canvas, groups);
            AddTransition();
            End(build);
        }

        private static void BuildFactionSelectScreen(Transform canvas, CharacterCreationDatabase database)
        {
            var screen = AddScreen(canvas, skin.choiceScreen, "Screen_FactionSelect");
            ComicBackground(screen);

            // One difficulty card per faction. The controller fills them from the faction catalog when the scene opens.
            var cards = new List<Transform>();
            foreach (string cardName in new[] { "Panel_Difficulty_Easy", "Panel_Difficulty_Normal", "Panel_Difficulty_Hard" })
            {
                var card = MenuUi.Find(screen, cardName);
                if (card == null)
                {
                    Debug.LogWarning($"[FrontEnd] Faction card '{cardName}' not found in the Select Difficulty screen.");
                    continue;
                }
                PrepareFactionCard(card);
                cards.Add(card);
            }
            // The pack fixes the row of cards at 16:9 width; let it follow the screen so narrower displays keep all three.
            if (MenuUi.Find(screen, "Panels") is RectTransform row)
            {
                row.anchorMin = new Vector2(0f, row.anchorMin.y);
                row.anchorMax = new Vector2(1f, row.anchorMax.y);
                row.sizeDelta = new Vector2(-40f, row.sizeDelta.y);
                Record(row);
            }
            // The pack hangs a bigger crest beside its middle and right cards (under one name or the other).
            foreach (string crest in new[] { "FantasyWarrior_Crest_Difficulty_02", "FantasyWarrior_Crest_Difficulty_03", "Crest_Difficulty_02", "Crest_Difficulty_03" })
                Hide(MenuUi.Find(screen, crest));

            var controller = SkinObject("Faction Select Controller");
            var hotkeys = AddHotkeys(controller);
            var select = controller.AddComponent<FactionSelectController>();
            SetRef(select, "screen", screen);
            SetRef(select, "database", database);
            SetRef(select, "hotkeys", hotkeys);
            SetRefs(select, "cards", cards);
            var background = MenuUi.Find(screen, "Background");
            if (background != null) SetRef(select, "glow", MenuUi.Find<Image>(background, "SPR_Glow"));
        }

        /// <summary>
        /// Re-plans a difficulty card for a faction: a smaller crest at the top (an icon over a glow, filled in by the
        /// controller) and a tall text area under it, because a faction has a paragraph of lore where a difficulty
        /// has one sentence.
        /// </summary>
        private static void PrepareFactionCard(Transform card)
        {
            var area = MenuUi.Find(card, "Crest");
            if (area == null) return;
            var stock = MenuUi.Find(area, "Crest_Difficulty");
            Sprite glowSprite = null;
            if (stock != null)
            {
                var stockGlow = MenuUi.Find<Image>(stock, "SPR_Glow");
                if (stockGlow != null) glowSprite = stockGlow.sprite;
                Hide(stock);
            }

            var glow = AddImage("Faction_Glow", area, FactionCrestPosition, new Vector2(1000f, 1000f));
            glow.sprite = glowSprite;
            glow.enabled = glowSprite != null;
            var crest = AddImage("Faction_Crest", area, FactionCrestPosition, new Vector2(FactionCrestSize, FactionCrestSize));
            crest.preserveAspect = true;

            // The name and the text stack up from the bottom of the card (the pack's layout); only the text grows.
            var description = MenuUi.Find(card, "Label_Description") as RectTransform;
            if (description != null)
            {
                description.sizeDelta = new Vector2(description.sizeDelta.x, FactionTextHeight);
                Record(description);
            }
        }

        // Card content is 1440 units tall, centred on the card: crest in the top third, text in the lower two.
        private static readonly Vector2 FactionCrestPosition = new Vector2(0f, 470f);
        private const float FactionCrestSize = 400f;
        private const float FactionTextHeight = 800f;

        /// <summary>A new image centred at <paramref name="position"/> from the middle of <paramref name="parent"/>.</summary>
        private static Image AddImage(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        // ---------------------------------------------------------------- character creator

        private static void BuildCharacterCreator(CharacterCreationDatabase database)
        {
            var build = Begin(FrontEnd.CharacterCreatorScene);
            var camera = AddCamera(Ink);
            var showroom = AddShowroom(camera, database, 0.56f); // the options fill the left 56% of the screen
            var studio = AddPortraitStudio(database);
            var canvas = AddCanvas();
            AddDragSurface(canvas, showroom);
            var groups = new List<MenuSkinSwitch.Group>();
            foreach (UiSkin each in Skins())
            {
                BeginSkin(canvas, each);
                BuildCharacterCreatorScreen(canvas, database, showroom, studio);
                EndSkin(groups);
            }
            AddSkinSwitch(canvas, groups);
            AddTransition();
            End(build);
        }

        /// <summary>
        /// One faction's creator. None of the menu packs has a character creator: the layout is Fantasy Screens' in
        /// every skin, with the skin's own option rows and body text, and lettered in the skin's fonts.
        /// </summary>
        private static void BuildCharacterCreatorScreen(Transform canvas, CharacterCreationDatabase database, CharacterShowroom showroom, PortraitStudio studio)
        {
            var screen = AddScreen(canvas, Screens + "Prefabs/Screens/Screen_FantasyScreens_CharacterCreator_01.prefab", "Screen_CharacterCreator");
            ShowroomBackground(screen);

            var controller = SkinObject("Character Creator Controller");
            var hotkeys = AddHotkeys(controller);
            var creator = controller.AddComponent<CharacterCreatorController>();
            SetRef(creator, "screen", screen);
            SetRef(creator, "database", database);
            SetRef(creator, "classes", AssetDatabase.LoadAssetAtPath<KryticalNova.Items.WeaponClassCatalog>(ClassSkillBuilder.CatalogPath));
            SetRef(creator, "gear", AssetDatabase.LoadAssetAtPath<KryticalNova.Items.GearDictionary>(SidekickGearGenerator.DictionaryPath));
            SetRef(creator, "items", AssetDatabase.LoadAssetAtPath<KryticalNova.Items.ItemCatalog>(SidekickGearGenerator.CatalogPath));
            SetRef(creator, "showroom", showroom);
            SetRef(creator, "portraits", studio);
            SetRef(creator, "hotkeys", hotkeys);

            var panels = MenuUi.Find(screen, "Panels");
            if (panels == null)
            {
                Debug.LogError("[FrontEnd] The character creator screen has no 'Panels'; has the pack changed?");
                return;
            }
            // The pack paints an example character on every section; ours is live, in the showroom.
            for (int i = 0; i < panels.childCount; i++) Hide(MenuUi.Find(panels.GetChild(i), "Hero"));
            // Hair and facial hair are rows on the Head tab, not thumbnail pages; there are no classes yet.
            foreach (string unused in new[] { "Section_Class", "Section_Hair", "Section_Facial_Hair" }) Hide(MenuUi.Find(panels, unused));

            PrepareTabs(screen, creator);
            PrepareOrigin(MenuUi.Find(panels, "Section_Race"), creator);
            PrepareOptions(MenuUi.Find(panels, "Section_Body"), creator);
            PrepareColours(MenuUi.Find(panels, "Section_Colour"), creator);
            PrepareFinalise(MenuUi.Find(panels, "Section_Finalise"), creator);
            PrepareCreatorFooter(screen);
            LetterInSkin(screen);
        }

        /// <summary>
        /// Sets a screen that is not of the skin's own pack in the skin's lettering: the pack's display face where the
        /// screen used one, its reading face everywhere else (<see cref="MenuLettering"/> does it as the screen wakes).
        /// The faces are read off the skin's own screens.
        /// </summary>
        private static void LetterInSkin(Transform screen)
        {
            if (skin == null) return;
            TMP_FontAsset display = FontOf(skin.listScreen, "Screen_Header"), reading = FontOf(skin.titleScreen, "Label_Press");
            // One of the sci-fi pack's faces ships without its material and draws nothing where it is set: the display face reads well too.
            if (reading == null) reading = display;
            if (display == null && reading == null) return;
            screen.gameObject.AddComponent<MenuLettering>().Set(display, reading);
        }

        private static TMP_FontAsset FontOf(GameObject prefab, string labelName)
        {
            if (prefab == null) return null;
            var label = MenuUi.Find<TMP_Text>(prefab.transform, labelName);
            return label != null && label.font != null && label.font.material != null ? label.font : null;
        }

        /// <summary>The pack's seven tabs become ours: Origin, Discipline, Body, Head, Preview, Colour, Finalise.</summary>
        private static void PrepareTabs(Transform screen, CharacterCreatorController creator)
        {
            var nav = MenuUi.Find(screen, "Navigation_Tabs");
            if (nav == null) return;
            var tabs = new List<MenuRow>
            {
                PrepareTab(nav, "Button_Randomize", OriginTabIcon),
                PrepareTab(nav, "Button_Facial_Hair", DisciplineTabIcon), // the pack's spare tab: the weapon class
                PrepareTab(nav, "Button_Body", CharacterIcons + "Body_01_Underlay.png"),
                PrepareTab(nav, "Button_Race", CharacterIcons + "Head_01_Underlay.png"), // the screen renames the pack's Head tab
                PrepareTab(nav, "Button_Hair", PreviewTabIcon), // the pack's Hair tab: hair is a row on the Head tab
                PrepareTab(nav, "Button_Colour", CharacterIcons + "Markings_01_Underlay.png"),
                PrepareTab(nav, "Button_Finalise", FinaliseTabIcon)
            };
            // The pack hides nothing here any more: its spare tab is ours. The controller moves it next to Origin
            // when the screen opens (children of the screen prefab cannot be reordered in the saved scene).
            if (tabs[1] != null)
            {
                tabs[1].gameObject.SetActive(true);
                Record(tabs[1].gameObject);
            }
            SetRefs(creator, "tabButtons", tabs);
        }

        private static MenuRow PrepareTab(Transform nav, string tabName, string iconPath)
        {
            var tab = MenuUi.Find(nav, tabName);
            if (tab == null)
            {
                Debug.LogWarning($"[FrontEnd] Creator tab '{tabName}' not found.");
                return null;
            }
            var icon = MenuUi.Find<Image>(tab, "ICON");
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(iconPath);
            if (icon != null && sprite != null)
            {
                icon.sprite = sprite;
                icon.preserveAspect = true;
                Record(icon);
            }
            return tab.gameObject.AddComponent<MenuRow>();
        }

        /// <summary>The pack's race panel: four tiles, each a lineage and body type, with a live-rendered portrait.</summary>
        private static void PrepareOrigin(Transform section, CharacterCreatorController creator)
        {
            if (section == null) return;
            SetRef(creator, "originSection", section.gameObject);
            var grid = MenuUi.Find(section, "Races");
            if (grid == null) return;
            if (grid.TryGetComponent<GridLayoutGroup>(out var layout))
            {
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = 2; // a lineage per row: masculine, feminine
                Record(layout);
            }

            var tiles = new List<MenuRow>();
            var portraits = new List<RawImage>();
            for (int i = 0; i < grid.childCount; i++)
            {
                var tile = grid.GetChild(i);
                if (tiles.Count >= OriginTileCount)
                {
                    Hide(tile);
                    continue;
                }
                tiles.Add(tile.gameObject.AddComponent<MenuRow>());

                var item = MenuUi.Find(tile, "Item");
                if (item == null) item = tile;
                Hide(MenuUi.Find(item, "ICON")); // the pack's sample item picture
                var portraitGo = new GameObject("Portrait", typeof(RectTransform));
                portraitGo.layer = tile.gameObject.layer;
                portraitGo.transform.SetParent(item, false);
                Stretch((RectTransform)portraitGo.transform);
                var portrait = portraitGo.AddComponent<RawImage>();
                portrait.raycastTarget = false;
                portrait.enabled = false; // shown once the portrait studio has rendered it
                portraits.Add(portrait);
            }
            SetRefs(creator, "originTiles", tiles);
            SetRefs(creator, "originPortraits", portraits);
        }

        /// <summary>
        /// The pack's body settings list becomes the shared list of the Body and Head tabs: its own rows are hidden
        /// and a pool of option rows and sliders is added, which the controller fills for whichever tab is open.
        /// </summary>
        private static void PrepareOptions(Transform section, CharacterCreatorController creator)
        {
            if (section == null) return;
            SetRef(creator, "optionsSection", section.gameObject);
            var list = MenuUi.Find(section, "Section_Body_Type");
            if (list == null) return;
            for (int i = 0; i < list.childCount; i++)
            {
                var child = list.GetChild(i);
                if (child.name != "Section_Header" && child.name != "Section_Break") Hide(child);
            }

            var options = new List<MenuOptionRow>();
            for (int i = 0; i < OptionRowCount; i++)
                options.Add(AddVendorPrefab(skin != null && skin.optionRow != null ? skin.optionRow : Load(OptionRowPrefab), list, $"Row_Option_{i:00}").AddComponent<MenuOptionRow>());
            var sliders = new List<MenuSliderRow>();
            for (int i = 0; i < SliderRowCount; i++)
                sliders.Add(AddVendorPrefab(SliderRowPrefab, list, $"Row_Slider_{i:00}").AddComponent<MenuSliderRow>());
            SetRefs(creator, "optionRows", options);
            SetRefs(creator, "sliderRows", sliders);
        }

        /// <summary>Three swatch groups (skin and two colour channels), each with room for the largest palette.</summary>
        private static void PrepareColours(Transform section, CharacterCreatorController creator)
        {
            if (section == null) return;
            SetRef(creator, "colourSection", section.gameObject);
            Hide(MenuUi.Find(section, "Section_Colour_Facial_Hair"));
            Hide(MenuUi.Find(section, "Section_Colour_Tattoo_Colour"));

            var groups = new List<MenuSwatchGroup>();
            foreach (string groupName in new[] { "Section_Colour_Skin", "Section_Colour_Eyes", "Section_Colour_Hair" })
            {
                var group = MenuUi.Find(section, groupName);
                var grid = group != null ? MenuUi.Find(group, "Grid_Colours") : null;
                if (grid == null) continue;
                for (int i = grid.childCount; i < SwatchesPerGroup; i++) AddVendorPrefab(SwatchPrefab, grid, $"Swatch_{i:00}");
                // Three groups have to fit the page without scrolling: no divider bars, tighter gaps.
                Hide(MenuUi.Find(group, "Section_Break"));
                if (group.TryGetComponent<VerticalLayoutGroup>(out var stack))
                {
                    stack.spacing = 30f;
                    stack.padding.bottom = 30;
                    Record(stack);
                }
                // The pack fixes the grid at one row; let it grow when a palette wraps onto a second.
                var fitter = grid.gameObject.AddComponent<ContentSizeFitter>();
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                groups.Add(group.gameObject.AddComponent<MenuSwatchGroup>());
            }
            SetRefs(creator, "swatchGroups", groups);
        }

        /// <summary>Name field, a row that suggests names, a line of feedback and the create button.</summary>
        private static void PrepareFinalise(Transform section, CharacterCreatorController creator)
        {
            if (section == null) return;
            SetRef(creator, "finaliseSection", section.gameObject);
            var settings = MenuUi.Find(section, "Settings");
            var list = settings != null ? MenuUi.Find(settings, "Section_Finalise") : null;
            if (list == null) return;
            foreach (string unused in new[] { "Input_Class", "Array_Voice", "Array_Pronouns", "Toggle_Genitals" }) Hide(MenuUi.Find(list, unused));

            var nameRow = MenuUi.Find(list, "Input_Name");
            if (nameRow != null)
            {
                foreach (var label in Labels(nameRow, "Button_Menu_Item")) SetText(label, "Name");
                SetRef(creator, "nameInput", WireNameField(nameRow.GetComponentInChildren<TMP_InputField>(true)));
            }
            var suggestion = MenuUi.Find(list, "Array_Difficulty");
            if (suggestion != null) SetRef(creator, "nameSuggestion", suggestion.gameObject.AddComponent<MenuOptionRow>());

            var message = AddVendorPrefab(skin != null && skin.bodyLabel != null ? skin.bodyLabel : Load(BodyLabelPrefab), list, "Label_NameMessage");
            if (nameRow != null) message.transform.SetSiblingIndex(nameRow.GetSiblingIndex() + 1);
            var messageRect = (RectTransform)message.transform;
            messageRect.sizeDelta = new Vector2(messageRect.sizeDelta.x, 70f); // the list leaves row heights alone
            var messageText = message.GetComponentInChildren<TMP_Text>(true);
            if (messageText != null)
            {
                messageText.text = string.Empty;
                messageText.alignment = TextAlignmentOptions.MidlineLeft;
                messageText.textWrappingMode = TextWrappingModes.NoWrap;
                Record(messageText);
                SetRef(creator, "nameMessage", messageText);
            }

            var finalise = MenuUi.Find(list, "Button_Finalise");
            if (finalise != null) SetRef(creator, "finaliseButton", finalise.GetComponent<Button>());
        }

        /// <summary>
        /// The pack's name field is a mock-up: an input field with no text hooked up, over a label that just says a
        /// sample name. Hook that label up as the field's text and give it a placeholder.
        /// </summary>
        private static TMP_InputField WireNameField(TMP_InputField field)
        {
            if (field == null) return null;
            var area = field.textViewport != null ? field.textViewport : (RectTransform)field.transform;
            if (field.textComponent == null)
            {
                var text = area.GetComponentInChildren<TMP_Text>(true);
                if (text == null)
                {
                    Debug.LogWarning("[FrontEnd] The name field has no label to type into.");
                    return field;
                }
                text.text = string.Empty;
                Record(text);
                field.textComponent = text;
            }
            if (field.placeholder == null)
            {
                var hint = AddVendorPrefab(BodyLabelPrefab, area, "Label_Placeholder").GetComponentInChildren<TMP_Text>(true);
                if (hint != null)
                {
                    Stretch((RectTransform)hint.transform);
                    hint.text = "Type a name";
                    hint.alignment = field.textComponent.alignment;
                    hint.fontStyle = FontStyles.Italic;
                    hint.color = new Color(hint.color.r, hint.color.g, hint.color.b, 0.45f);
                    hint.raycastTarget = false;
                    Record(hint);
                    field.placeholder = hint;
                }
            }
            Record(field);
            return field;
        }

        /// <summary>Five prompts with names the controller looks up: turn, randomise, undo, back, next.</summary>
        private static void PrepareCreatorFooter(Transform screen)
        {
            var footer = MenuUi.Find(screen, "Footer");
            var group = footer != null ? MenuUi.Find(footer, "H_Group") : null;
            if (group == null) return;
            InkFooter(footer);

            string[] names = { "Prompt_Turn", "Prompt_Randomise", "Prompt_Undo", "Prompt_Back", "Prompt_Next" };
            var prompts = new List<Transform>();
            for (int i = 0; i < group.childCount; i++)
                if (group.GetChild(i).name.StartsWith("Hotkey_Prompt")) prompts.Add(group.GetChild(i));
            // The pack ships four prompts; the fifth is another instance of the same prefab.
            var source = prompts.Count > 0 ? PrefabUtility.GetCorrespondingObjectFromOriginalSource(prompts[0].gameObject) : null;
            while (prompts.Count < names.Length && source != null)
                prompts.Add(((GameObject)PrefabUtility.InstantiatePrefab(source, group)).transform);
            for (int i = 0; i < prompts.Count; i++)
            {
                if (i >= names.Length)
                {
                    Hide(prompts[i]);
                    continue;
                }
                prompts[i].name = names[i];
                Record(prompts[i].gameObject);
            }
        }

        private static void InkFooter(Transform footer)
        {
            Tint(MenuUi.Find<Image>(footer, "SPR_Background"), Ink, 1f);
        }

        /// <summary>The camera, stage and settings that photograph the Origin tiles' portraits, far below the showroom.</summary>
        private static PortraitStudio AddPortraitStudio(CharacterCreationDatabase database)
        {
            var root = new GameObject("Portrait Studio");
            Parent(root, "--- Environment ---");
            root.transform.position = new Vector3(0f, -40f, 0f);
            var stage = new GameObject("Stage").transform;
            stage.SetParent(root.transform, false);

            var cameraGo = new GameObject("Portrait Camera");
            cameraGo.transform.SetParent(root.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 1.58f, -1.55f); // head and shoulders
            var camera = cameraGo.AddComponent<Camera>();
            camera.enabled = false; // renders only when the studio asks
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Ink;
            camera.fieldOfView = 24f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 8f;
            var data = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            int renderer = RendererIndex(MenuRendererName);
            if (renderer >= 0) data.SetRenderer(renderer);

            var studio = root.AddComponent<PortraitStudio>();
            SetRef(studio, "database", database);
            SetRef(studio, "modelPrefab", AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath));
            SetRef(studio, "idleClip", PlayerIdleClip());
            SetRef(studio, "portraitCamera", camera);
            SetRef(studio, "stage", stage);
            return studio;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>Adds another instance of a vendor prefab (a row, a swatch, a label) under a vendor container.</summary>
        private static GameObject AddVendorPrefab(string prefabPath, Transform parent, string name) => AddVendorPrefab(Load(prefabPath), parent, name);

        private static GameObject Load(string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) Debug.LogError($"[FrontEnd] Vendor prefab missing: {prefabPath}");
            return prefab;
        }

        private static GameObject AddVendorPrefab(GameObject prefab, Transform parent, string name)
        {
            GameObject go;
            if (prefab != null) go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            else
            {
                go = new GameObject(name, typeof(RectTransform));
                go.transform.SetParent(parent, false);
            }
            go.name = name;
            return go;
        }

        private static void SetRefs<T>(Object target, string field, IList<T> values) where T : Object
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(field);
            if (property == null || !property.isArray)
            {
                Debug.LogError($"[FrontEnd] {target.GetType().Name} has no serialized array '{field}'.");
                return;
            }
            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
