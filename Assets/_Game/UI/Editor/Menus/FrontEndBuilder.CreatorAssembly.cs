using System.Collections.Generic;
using KryticalNova.UI;
using KryticalNova.UI.Menus;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.EditorTools
{
    // The character creator of a skin whose packs have no creator screen. It has the sections of Fantasy Screens'
    // creator (tabs, origin tiles with a description and traits, a list of options, swatches, the name), under the
    // names the controller looks for, and every part of it is the skin's own menu pack's: its tab strip, its framed
    // panel, its rows, sliders, text field, button and prompts. A skin names the parts (UiSkin, "the character creator").
    public static partial class FrontEndBuilder
    {
        // On the menus' 3840 x 2160 canvas.
        private const float MenuCanvasHeight = 2160f;
        private const float CreatorPanelLeft = 150f, CreatorPanelWidth = 1950f, CreatorPanelFoot = 330f, CreatorPanelTop = 270f;
        // The sections are laid out at the packs' own sizes and shown this much larger: a pack's rows are made for a
        // list as wide as the screen, and ours is half of one.
        private const float CreatorPageScale = 1.25f;
        private const float CreatorTileSize = 270f, CreatorTileGap = 30f, CreatorPictureInset = 20f;
        private const float CreatorSwatchSize = 96f, CreatorSwatchGap = 12f;
        private const float CreatorHeadline = 72f, CreatorSubhead = 56f, CreatorReading = 40f;
        private const float CreatorRowGap = 22f;

        /// <summary>Fills an empty screen object with one faction's creator and hands the controller its parts.</summary>
        private static void AssembleCreator(Transform screen, CharacterCreatorController creator)
        {
            AssembleCreatorBackground(screen);
            RectTransform content = Box("Content", screen);
            AssembleCreatorTabs(content, creator);

            // The page: the pack's framed panel over the left of the screen, the showroom's character to its right.
            RectTransform panels = Box("Panels", content);
            panels.anchorMin = Vector2.zero;
            panels.anchorMax = new Vector2(0f, 1f);
            panels.pivot = new Vector2(0f, 0.5f);
            panels.offsetMin = new Vector2(CreatorPanelLeft, CreatorPanelFoot);
            panels.offsetMax = new Vector2(CreatorPanelLeft + CreatorPanelWidth, -CreatorPanelTop);
            RectTransform frame = Part(skin.framedPanel, panels, "Panel_Frame");
            Stretch(frame);
            // What the pack writes on its panel is not ours; where it writes it is where our sections go.
            var written = MenuUi.Find(frame, "Info") as RectTransform;
            Vector2 low = written != null ? written.offsetMin : new Vector2(80f, 80f), high = written != null ? written.offsetMax : new Vector2(-80f, -80f);
            Vector2 room = new Vector2(CreatorPanelWidth, MenuCanvasHeight - CreatorPanelFoot - CreatorPanelTop) + high - low;
            RectTransform page = Box("Sections", panels);
            Vector2 middle = new Vector2(0.5f, 0.5f);
            Place(page, middle, middle, middle, (low + high) * 0.5f, room / CreatorPageScale);
            page.localScale = Vector3.one * CreatorPageScale;
            Hide(written);
            Hide(MenuUi.Find(frame, "Input_Menu"));
            // The page is read over a lit scene: its backing is made nearly solid.
            var backing = MenuUi.Find<Image>(frame, "SPR_Background");
            if (backing != null && backing.color.a < 0.92f) Tint(backing, backing.color, 0.92f);

            AssembleOrigin(page, creator);
            AssembleOptions(page, creator);
            AssembleColours(page, creator);
            AssembleFinalise(page, creator);

            // The foot of the screen: a band of ink and the pack's row of prompts.
            RectTransform footer = Box("Footer", content);
            Place(footer, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 140f));
            Box("SPR_Background", footer).gameObject.AddComponent<Image>().raycastTarget = false;
            Stretch(Part(skin.footer, footer, "Input_Footer_01"));
            PrepareCreatorFooter(screen);

            ReadableFaces(screen);
        }

        /// <summary>The showroom is the background; over it only the pack's vignette, in ink.</summary>
        private static void AssembleCreatorBackground(Transform screen)
        {
            var like = skin.listScreen != null ? MenuUi.Find(skin.listScreen.transform, "Background") : null;
            var vignette = like != null ? MenuUi.Find<Image>(like, "SPR_Vignette") : null;
            if (vignette == null || vignette.sprite == null) return;
            RectTransform background = Box("Background", screen);
            var image = Box("SPR_Vignette", background).gameObject.AddComponent<Image>();
            image.sprite = vignette.sprite;
            image.type = vignette.type;
            image.color = new Color(Ink.r, Ink.g, Ink.b, 0.75f);
            image.raycastTarget = false; // empty screen turns the character
        }

        /// <summary>The pack's strip of icon tabs: its first seven buttons are our tabs, its two keys are Q and E.</summary>
        private static void AssembleCreatorTabs(Transform content, CharacterCreatorController creator)
        {
            RectTransform nav = Box("Navigation", content);
            Place(nav, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1500f, 180f));
            RectTransform strip = Part(skin.tabStrip, nav, "Navigation_Tabs");
            Transform row = MenuUi.Find(strip, "H_Group");
            if (row == null) row = strip;

            string[] names = { "Origin", "Discipline", "Body", "Head", "Preview", "Colour", "Finalise" };
            var buttons = new List<Transform>();
            for (int i = 0; i < row.childCount; i++)
                if (row.GetChild(i).name.StartsWith("Button_")) buttons.Add(row.GetChild(i));
            // A pack's strip has fewer buttons than we have tabs: the rest are copies of its first, after its last.
            while (buttons.Count > 0 && buttons.Count < names.Length)
            {
                Transform copy = Object.Instantiate(buttons[0].gameObject, row).transform;
                copy.SetSiblingIndex(buttons[buttons.Count - 1].GetSiblingIndex() + 1);
                buttons.Add(copy);
            }
            var tabs = new List<MenuRow>();
            for (int tab = 0; tab < buttons.Count; tab++)
            {
                Transform button = buttons[tab];
                if (tab >= names.Length)
                {
                    Hide(button);
                    continue;
                }
                button.name = "Tab_" + names[tab];
                var icon = MenuUi.Find<Image>(button, "ICON");
                Sprite picture = tab < skin.creatorTabIcons.Length ? skin.creatorTabIcons[tab] : null;
                if (icon != null && picture != null)
                {
                    icon.sprite = picture;
                    icon.preserveAspect = true;
                }
                tabs.Add(button.gameObject.AddComponent<MenuRow>());
            }
            if (tabs.Count < names.Length) Debug.LogWarning($"[FrontEnd] The {skin.faction} tab strip has {tabs.Count} buttons; the creator has {names.Length} tabs.");
            SetRefs(creator, "tabButtons", tabs);
            TabKey(strip, "Input_Key_Left", "Q");
            TabKey(strip, "Input_Key_Right", "E");
        }

        /// <summary>A key at the end of the tab strip: the pack shows a gamepad button there, we letter the cap.</summary>
        private static void TabKey(Transform strip, string keyName, string letter)
        {
            Transform key = MenuUi.Find(strip, keyName);
            if (key == null) return;
            Hide(key.Find("ICON"));
            // For its gamepad button the pack switches the cap off: it is switched back on, to fit its letter.
            foreach (var behaviour in key.GetComponents<Behaviour>()) behaviour.enabled = true;
            var label = MenuUi.Find<TMP_Text>(key, "Label_Input");
            if (label == null) return;
            label.gameObject.SetActive(true);
            label.text = letter;
        }

        /// <summary>
        /// Origin (and Discipline, which uses the same tiles): a headline, four tiles two by two, the chosen one's
        /// name and description, and its three traits along the foot.
        /// </summary>
        private static void AssembleOrigin(Transform page, CharacterCreatorController creator)
        {
            RectTransform section = Box("Section_Origin", page);
            SetRef(creator, "originSection", section.gameObject);

            TMP_Text headline = Label(skin.headerLabel, section, "Label_Header", CreatorHeadline, TextAlignmentOptions.Center);
            Place(headline.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 110f));

            const float gridTop = 120f;
            float gridHeight = CreatorTileSize * 2f + CreatorTileGap;
            RectTransform grid = Box("Origins", section);
            Place(grid, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -gridTop), new Vector2(0f, gridHeight));
            var cells = grid.gameObject.AddComponent<GridLayoutGroup>();
            cells.cellSize = new Vector2(CreatorTileSize, CreatorTileSize);
            cells.spacing = new Vector2(CreatorTileGap, CreatorTileGap);
            cells.childAlignment = TextAnchor.UpperCenter;
            cells.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            cells.constraintCount = 2; // a lineage per row: masculine, feminine

            var tiles = new List<MenuRow>();
            var portraits = new List<RawImage>();
            for (int i = 0; i < OriginTileCount; i++)
            {
                RectTransform tile = IconTile(grid, $"Origin_{i:00}", out Transform pictures, out int under);
                // The tile's own picture is the class weapon on the Discipline tab; the controller shows it then.
                var icon = MenuUi.Find<Image>(tile, "ICON");
                if (icon != null) icon.gameObject.SetActive(false);
                RectTransform portraitRect = Box("Portrait", pictures);
                portraitRect.SetSiblingIndex(under);
                portraitRect.offsetMin = new Vector2(CreatorPictureInset, CreatorPictureInset); // the chosen one's ring shows round the picture
                portraitRect.offsetMax = new Vector2(-CreatorPictureInset, -CreatorPictureInset);
                var portrait = portraitRect.gameObject.AddComponent<RawImage>();
                portrait.raycastTarget = false;
                portrait.enabled = false; // shown once the portrait studio has rendered it
                portraits.Add(portrait);
                tiles.Add(tile.GetComponent<MenuRow>());
            }
            SetRefs(creator, "originTiles", tiles);
            SetRefs(creator, "originPortraits", portraits);

            RectTransform description = Box("Description", section);
            Place(description, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(gridTop + gridHeight + 20f)), new Vector2(0f, 250f));
            TMP_Text name = Label(skin.headerLabel, description, "Label_Header", CreatorSubhead, TextAlignmentOptions.Center);
            Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 90f));
            TMP_Text text = Label(skin.bodyLabel, description, "Label_Description", CreatorReading, TextAlignmentOptions.Top);
            Place(text.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -48f), new Vector2(-60f, -96f));
            text.textWrappingMode = TextWrappingModes.Normal;

            RectTransform traits = Box("Traits", section);
            Place(traits, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 80f));
            var across = traits.gameObject.AddComponent<HorizontalLayoutGroup>();
            across.spacing = 24f;
            across.childControlWidth = across.childControlHeight = true;
            across.childForceExpandWidth = across.childForceExpandHeight = true;
            for (int i = 0; i < FactionSkinBuilder.TraitCount; i++)
            {
                RectTransform chip = Box($"Trait_0{i + 1}", traits);
                var chipBacking = chip.gameObject.AddComponent<Image>();
                chipBacking.color = new Color(0f, 0f, 0f, 0.45f);
                chipBacking.raycastTarget = false;
                RectTransform iconRect = Box("Icon", chip);
                Place(iconRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(56f, 56f));
                var icon = iconRect.gameObject.AddComponent<Image>();
                icon.sprite = i < skin.traitIcons.Length ? skin.traitIcons[i] : null;
                icon.enabled = icon.sprite != null;
                icon.preserveAspect = true;
                icon.color = skin.menuAccent;
                icon.raycastTarget = false;
                TMP_Text trait = Label(skin.bodyLabel, chip, "Label_Stat", CreatorReading - 6f, TextAlignmentOptions.MidlineLeft);
                Stretch(trait.rectTransform);
                trait.rectTransform.offsetMin = new Vector2(82f, 0f);
                trait.rectTransform.offsetMax = new Vector2(-8f, 0f);
            }
        }

        /// <summary>The list the Body, Head and Preview tabs share: a heading, then a pool of option rows and sliders.</summary>
        private static void AssembleOptions(Transform page, CharacterCreatorController creator)
        {
            RectTransform section = Box("Section_Options", page);
            SetRef(creator, "optionsSection", section.gameObject);
            Down(section, CreatorRowGap);
            Heading(section);

            var options = new List<MenuOptionRow>();
            for (int i = 0; i < OptionRowCount; i++)
                options.Add(Part(skin.optionRow, section, $"Row_Option_{i:00}").gameObject.AddComponent<MenuOptionRow>());
            var sliders = new List<MenuSliderRow>();
            for (int i = 0; i < SliderRowCount; i++)
                sliders.Add(SliderRow(section, $"Row_Slider_{i:00}").gameObject.AddComponent<MenuSliderRow>());
            SetRefs(creator, "optionRows", options);
            SetRefs(creator, "sliderRows", sliders);
            section.gameObject.SetActive(false);
        }

        /// <summary>Three swatch groups (skin and two colour channels), each with room for the largest palette on two lines.</summary>
        private static void AssembleColours(Transform page, CharacterCreatorController creator)
        {
            RectTransform section = Box("Section_Colour", page);
            SetRef(creator, "colourSection", section.gameObject);
            Down(section, 40f);

            float gridHeight = CreatorSwatchSize * 2f + CreatorSwatchGap;
            var groups = new List<MenuSwatchGroup>();
            for (int g = 0; g < 3; g++)
            {
                RectTransform group = Box($"Colours_{g:00}", section);
                group.sizeDelta = new Vector2(0f, 100f + gridHeight);
                RectTransform heading = Heading(group);
                Place(heading, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 80f));
                RectTransform grid = Box("Grid_Colours", group);
                Place(grid, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(0f, gridHeight));
                var cells = grid.gameObject.AddComponent<GridLayoutGroup>();
                cells.cellSize = new Vector2(CreatorSwatchSize, CreatorSwatchSize);
                cells.spacing = new Vector2(CreatorSwatchGap, CreatorSwatchGap);
                cells.childAlignment = TextAnchor.UpperLeft;

                for (int i = 0; i < SwatchesPerGroup; i++)
                {
                    RectTransform swatch = IconTile(grid, $"Swatch_{i:00}", out Transform pictures, out int under);
                    // A swatch is its colour, true: nothing of the button but its frame and its chosen glow stays.
                    for (int c = 0; c < pictures.childCount; c++)
                    {
                        Transform layer = pictures.GetChild(c);
                        if (layer.name != "Selected" && layer.name != "SPR_Frame") layer.gameObject.SetActive(false);
                    }
                    RectTransform colour = Box("COLOUR", pictures);
                    colour.SetSiblingIndex(under);
                    colour.offsetMin = new Vector2(14f, 14f);
                    colour.offsetMax = new Vector2(-14f, -14f);
                    colour.gameObject.AddComponent<Image>().raycastTarget = false;
                }
                groups.Add(group.gameObject.AddComponent<MenuSwatchGroup>());
            }
            SetRefs(creator, "swatchGroups", groups);
            section.gameObject.SetActive(false);
        }

        /// <summary>The name: the pack's text field, a row that suggests names, a line of feedback and the create button.</summary>
        private static void AssembleFinalise(Transform page, CharacterCreatorController creator)
        {
            RectTransform section = Box("Section_Finalise", page);
            SetRef(creator, "finaliseSection", section.gameObject);
            Down(section, 30f);
            Heading(section);

            // The pack's field draws its frame past its own rectangle: it gets a line with room for that.
            RectTransform nameLine = Box("Input_Name", section);
            nameLine.sizeDelta = new Vector2(0f, 220f);
            RectTransform field = Part(skin.textField, nameLine, "Input");
            Place(field, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-80f, 130f));
            if (skin.textFieldFill.a > 0f)
            {
                // The skin's own fill, and the field's picture in the skin's accent to go with it.
                Transform fill = field.Find("Background");
                if (fill != null) Tint(MenuUi.Find<Image>(fill, "SPR_Background"), skin.textFieldFill, skin.textFieldFill.a);
                var picture = field.Find("ICON") != null ? field.Find("ICON").GetComponent<Image>() : null;
                if (picture != null) Tint(picture, skin.menuAccent, picture.color.a);
            }
            SetRef(creator, "nameInput", WireNameField(field.GetComponentInChildren<TMP_InputField>(true)));

            SetRef(creator, "nameSuggestion", Part(skin.optionRow, section, "Array_Suggestion").gameObject.AddComponent<MenuOptionRow>());

            TMP_Text message = Label(skin.bodyLabel, section, "Label_NameMessage", CreatorReading - 2f, TextAlignmentOptions.MidlineLeft);
            message.rectTransform.sizeDelta = new Vector2(0f, 70f);
            message.textWrappingMode = TextWrappingModes.NoWrap;
            SetRef(creator, "nameMessage", message);

            RectTransform buttonLine = Box("Finalise", section);
            buttonLine.sizeDelta = new Vector2(0f, 260f);
            RectTransform button = Part(skin.startButton, buttonLine, "Button_Finalise");
            Place(button, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, button.sizeDelta);
            SetRef(creator, "finaliseButton", button.GetComponent<Button>());
            section.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- parts

        /// <summary>A plain object that fills its parent.</summary>
        private static RectTransform Box(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);
            return rect;
        }

        /// <summary>
        /// A part of the skin's pack, unpacked: an assembled screen puts its own pictures between a part's layers,
        /// which a prefab instance does not allow. The vendor prefab is only read.
        /// </summary>
        private static RectTransform Part(GameObject prefab, Transform parent, string name)
        {
            GameObject go = AddVendorPrefab(prefab, parent, name);
            if (PrefabUtility.IsPartOfPrefabInstance(go))
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            return (RectTransform)go.transform;
        }

        private static void Place(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        /// <summary>One of the pack's labels, empty, at a size of ours: the controller writes on it.</summary>
        private static TMP_Text Label(GameObject prefab, Transform parent, string name, float size, TextAlignmentOptions alignment)
        {
            RectTransform rect = Part(prefab, parent, name);
            var label = rect.GetComponentInChildren<TMP_Text>(true);
            if (label == null) label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            foreach (var fitter in rect.GetComponents<ContentSizeFitter>()) Object.DestroyImmediate(fitter);
            label.enableAutoSizing = false;
            // The packs' reading faces stand differently tall on the line: the skin says by how much.
            label.fontSize = prefab == skin.bodyLabel ? size * skin.fontScale : size;
            label.alignment = alignment;
            label.text = string.Empty;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>
        /// The pack's slider row says a number on its handle. Ours says what each end of the slider means, as Fantasy
        /// Screens' row does: the word for the low end before the slider, the word for the high end after it.
        /// </summary>
        private static RectTransform SliderRow(Transform parent, string name)
        {
            RectTransform row = Part(skin.sliderRow, parent, name);
            Hide(MenuUi.Find(row, "Label_Value"));
            if (!(MenuUi.Find(row, "Value") is RectTransform value)) return row;
            const float word = 190f, gap = 14f;
            value.anchoredPosition += new Vector2(-(word + gap), 0f); // room after the slider for the high end's word
            // Both from the row's right edge, which the slider is laid out from.
            float right = value.anchoredPosition.x + (1f - value.pivot.x) * value.sizeDelta.x, left = right - value.sizeDelta.x;
            TMP_Text low = Label(skin.bodyLabel, row, "Label_Value_Min", CreatorReading - 6f, TextAlignmentOptions.MidlineRight);
            Place(low.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(left - gap, 0f), new Vector2(word, 0f));
            TMP_Text high = Label(skin.bodyLabel, row, "Label_Value_Max", CreatorReading - 6f, TextAlignmentOptions.MidlineLeft);
            Place(high.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0.5f), new Vector2(right + gap, 0f), new Vector2(word, 0f));
            return row;
        }

        /// <summary>Lays a section's lines out from the top, each as wide as the page and as tall as its own part.</summary>
        private static void Down(RectTransform section, float gap)
        {
            var list = section.gameObject.AddComponent<VerticalLayoutGroup>();
            list.padding = new RectOffset(0, 0, 10, 0);
            list.childAlignment = TextAnchor.UpperCenter;
            list.spacing = gap;
            list.childControlWidth = true;
            list.childControlHeight = false;
            list.childForceExpandWidth = true;
            list.childForceExpandHeight = false;
        }

        /// <summary>The pack's heading of a section of a list; the controller writes the section's name on it.</summary>
        private static RectTransform Heading(Transform parent)
        {
            RectTransform heading = Part(skin.sectionHeader, parent, "Section_Header");
            MenuUi.SetText(heading, "Label_Header", string.Empty);
            return heading;
        }

        /// <summary>
        /// The pack's icon button as a tile that shows a picture of ours and can be the chosen one of several.
        /// <paramref name="pictures"/> is where its layers are and <paramref name="under"/> the place among them over
        /// its backing and its glow, under its frame.
        /// </summary>
        private static RectTransform IconTile(Transform parent, string name, out Transform pictures, out int under)
        {
            RectTransform tile = Part(skin.iconButton, parent, name);
            pictures = MenuUi.Find(tile, "Content");
            if (pictures == null) pictures = tile;
            Transform chosen = pictures.Find("Selected");
            under = chosen != null ? chosen.GetSiblingIndex() + 1 : 0;
            var row = tile.gameObject.AddComponent<MenuRow>();
            // The packs mark the chosen button with a glow behind its icon, which a picture covers: the chosen tile
            // also wears its own frame a second time, larger and in the skin's accent, over everything.
            var frame = MenuUi.Find<Image>(pictures, "SPR_Frame");
            if (frame != null)
            {
                RectTransform ring = Box("Chosen_Ring", pictures);
                ring.offsetMin = frame.rectTransform.offsetMin - new Vector2(8f, 8f);
                ring.offsetMax = frame.rectTransform.offsetMax + new Vector2(8f, 8f);
                var image = ring.gameObject.AddComponent<Image>();
                image.sprite = frame.sprite;
                image.type = frame.type;
                image.pixelsPerUnitMultiplier = frame.pixelsPerUnitMultiplier;
                image.color = skin.menuAccent; // the packs' frames are pale metal: the accent stands out against every one
                image.raycastTarget = false;
                ring.gameObject.SetActive(false);
                SetRef(row, "selectedMark", ring.gameObject);
            }
            return tile;
        }

        /// <summary>
        /// A label whose face cannot be drawn (one of the sci-fi pack's ships without its material, and one of the
        /// dark fantasy pack's labels names no face) is set in the pack's reading face.
        /// </summary>
        private static void ReadableFaces(Transform screen)
        {
            var like = skin.bodyLabel != null ? skin.bodyLabel.GetComponentInChildren<TMP_Text>(true) : null;
            TMP_FontAsset reading = like != null ? like.font : null;
            if (reading == null) return;
            // A label with no face is handed TextMesh Pro's default one as it is read.
            foreach (TMP_Text label in screen.GetComponentsInChildren<TMP_Text>(true))
                if (label.font == null || label.font.material == null || label.font == TMP_Settings.defaultFontAsset) label.font = reading;
        }
    }
}
