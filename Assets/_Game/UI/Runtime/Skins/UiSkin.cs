using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>
    /// What one faction's interface is made of: the frames, panels, buttons, bars and icons of its Synty INTERFACE
    /// packs, its fonts and its accent colours. One asset a faction (UI/Skins). Nothing in a screen names a faction's
    /// art: the in-game UI's skin sheets (KryticalSkin_*.uss) are generated from these assets, and the menus of the
    /// title flow are built from them, so a value changed here changes every screen that uses it. Layout, text and
    /// behaviour are not in a skin and cannot be changed by one.
    /// Gameplay colours are not in a skin either: rarity colours, the colours of a name by level gap and the
    /// factions' namecard colours mean the same in every skin.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/UI/UI Skin", fileName = "UiSkin")]
    public sealed class UiSkin : ScriptableObject
    {
        [Tooltip("The faction whose interface this is.")]
        public FactionId faction;
        [Tooltip("The Synty INTERFACE packs its parts are taken from, for the record.")]
        public string hudPack, menuPack;

        [Header("Fonts (in-game UI)")]
        [Tooltip("Everything that is read: labels, values, body text.")]
        public Font bodyFont;
        [Tooltip("Window titles and headings.")]
        public Font titleFont;
        [Tooltip("Text is drawn this much larger than the layout's own sizes: the packs' fonts stand differently tall on the line.")]
        [Range(0.8f, 1.5f)] public float fontScale = 1f;

        [Header("Accent colours (in-game UI)")]
        [Tooltip("Off: the colours of the layout's own sheets are this skin's colours. On: they are moved into the three colours below, family by family.")]
        public bool recolour;
        [Tooltip("The accent: selections, highlights, the edges of frames. Its hue is what counts.")]
        public Color accent = Color.white;
        [Tooltip("The dark tone of panels and cards. Its hue is what counts.")]
        public Color panelTone = Color.white;
        [Tooltip("Small labels and eyebrows. Its hue is what counts.")]
        public Color label = Color.white;
        [Tooltip("How much stronger or weaker the accent is than the layout's own.")]
        [Range(0.5f, 2f)] public float accentSaturation = 1f;
        [Tooltip("How much stronger or weaker the panel tone is than the layout's own.")]
        [Range(0.5f, 2f)] public float panelSaturation = 1f;

        [Header("In-game UI: frames, panels, buttons, bars, icons")]
        [Tooltip("The fill of a window or panel.")]
        public Sprite panel;
        [Tooltip("An inset card, a row, a button.")]
        public Sprite card;
        [Tooltip("A small token: a tab, a tag, a key cap.")]
        public Sprite token;
        [Tooltip("An item or skill slot.")]
        public Sprite itemSlot;
        [Tooltip("The outline laid over a window or panel.")]
        public Sprite frame;
        [Tooltip("The outline laid over a slot or a small card.")]
        public Sprite frameSmall;
        [Tooltip("A bar: health, Flux, experience, a cast.")]
        public Sprite bar;
        [Tooltip("A pip that is lit, and one that is not.")]
        public Sprite pipOn, pipOff;
        [Tooltip("The close glyph of a window.")]
        public Sprite closeIcon;
        [Tooltip("The small arrow of a grab handle or a stepper.")]
        public Sprite arrowIcon;
        [Tooltip("The soft backing behind text that floats over the world.")]
        public Sprite softBacking;
        [Tooltip("The hexagon, cross and scan-line patterns behind the world map and the larger panels. A look of the sci-fi packs only.")]
        public bool techPatterns;

        [Header("Window chrome: the pack's own frames and ornaments, in their own colours (never tinted)")]
        [Tooltip("The colour behind a window's fill, and behind the tooltip, the menus and the dialogs: nearly solid, so the HUD and the world do not read through what is being read. Clear: the layout's own see-through fill stays.")]
        public Color windowBacking = Color.clear;
        [Tooltip("The frame of a window (Pack, Equipment, Skills, Settings and the like): drawn over its edge.")]
        public Sprite windowFrame;
        [Tooltip("How thick the window frame's widest side is drawn, in panel pixels.")]
        [Min(0f)] public float windowFrameBorder = 20f;
        [Tooltip("How far the window frame reaches out past the window's edge, in panel pixels: spikes and corner pieces stand outside.")]
        public float windowFrameOutset = 6f;
        [Tooltip("The frame of a small panel (chat, a prompt, a tooltip, a dialog). None: the tinted outline of the layout stays.")]
        public Sprite panelFrame;
        [Min(0f)] public float panelFrameBorder = 10f;
        public float panelFrameOutset = 2f;
        [Tooltip("A button: the pack's frame over its fill, flattened into one picture by the skin builder (UI/HUD/Faction/<Faction>/Art). In its own colours; a button's state only lightens or darkens it.")]
        public Sprite button;
        [Tooltip("How thick the button art's widest side is drawn, in panel pixels.")]
        [Min(0f)] public float buttonBorder = 8f;
        [Tooltip("A button of the menu row along the top of the screen: the pack's long bar button, flattened like the button.")]
        public Sprite menuButton;
        [Min(0f)] public float menuButtonBorder = 30f;
        [Tooltip("The band a window's title stands on: the pack's bar, flattened over its fill.")]
        public Sprite headerBand;
        [Min(0f)] public float headerBandBorder = 60f;
        [Tooltip("An item cell or an equipment slot: the pack's cell frame over its fill. The rarity outline is drawn over it and keeps its colours.")]
        public Sprite cell;
        [Min(0f)] public float cellBorder = 8f;
        [Tooltip("The round close button on a window's corner. The X is drawn over it.")]
        public Sprite closeButton;
        [Tooltip("A key cap: the pack's own cap shape. Tinted, unlike the rest of the chrome: the packs colour one cap art three ways.")]
        public Sprite keycap;
        [Min(0f)] public float keycapBorder = 8f;
        [Tooltip("A cap at rest, its lettering, and a cap under the pointer or waiting for a key (the packs' filled cap).")]
        public Color keycapTint = Color.clear, keycapLetter = Color.white, keycapHot = Color.white;
        [Tooltip("What a key does, and the device on top of the Controls page.")]
        public Color controlsLabel = Color.white;
        [Tooltip("The light on the Equipment page's model: the pack's stepped glow behind it, and its beams from above. Tinted with the accent by the sheets.")]
        public Sprite previewGlow, previewBeams;
        [Tooltip("The ornament on the middle of a window's top edge, and one drawn behind it (horns behind a skull). Either may be none.")]
        public Sprite crown, crownBack;
        [Tooltip("The ornaments' sizes in panel pixels, and how far their tops stand above the window's top edge.")]
        public Vector2 crownSize = new Vector2(64f, 32f), crownBackSize = new Vector2(96f, 48f);
        public float crownRise = 24f, crownBackRise = 30f;

        [Header("Inventory pages: the pack's own paper doll, grid and item card")]
        [Tooltip("The pictures an empty equipment slot shows, in the order Weapon, Armor, Gloves, Boots, Ring, Amulet, Cape, Pet.")]
        public Sprite[] slotGlyphs = new Sprite[0];
        [Tooltip("The colour the slot pictures are drawn in (the packs draw them faint, in their own tone).")]
        public Color slotGlyphTint = new Color(0.47f, 0.67f, 0.75f, 0.4f);
        [Tooltip("The bars over the top and under the foot of the Equipment page's model, each in a left and a right half.")]
        public Sprite dollBarTopLeft, dollBarTopRight, dollBarFootLeft, dollBarFootRight;
        [Tooltip("How tall the bars are drawn, in panel pixels, and their colour.")]
        [Min(1f)] public float dollBarHeight = 14f;
        public Color dollBarTint = Color.white;
        [Tooltip("The ornate frame of an equipment slot: drawn over the slot and reaching out past it, in the rarity of what is worn (grey while empty).")]
        public Sprite slotFrame;
        [Min(0f)] public float slotFrameBorder = 12f, slotFrameOutset = 4f;
        [Tooltip("What an equipment slot is filled with, under its picture.")]
        public Color slotFill = new Color(0.1f, 0.14f, 0.18f, 0.9f);
        [Tooltip("The frame of the item card (the tooltip), reaching out past it, and what the card is filled with.")]
        public Sprite cardFrame;
        [Min(0f)] public float cardFrameBorder = 30f, cardFrameOutset = 12f;
        public Color cardBacking = new Color(0.08f, 0.1f, 0.1f, 0.96f);
        [Tooltip("The frame round the item's picture on the card.")]
        public Sprite cardPictureFrame;
        [Min(0f)] public float cardPictureBorder = 16f, cardPictureOutset = 6f;
        [Tooltip("A cell of the Pack's grid: the pack's plain cell, without a frame. None: the equipment slot's art is used.")]
        public Sprite gridCell;
        public Color gridCellTint = Color.white;
        [Min(0f)] public float gridCellBorder = 8f;

        [Header("Settings rows: the pack's own arrows, value box, slider and switch")]
        [Tooltip("The arrows either side of a value or a slider. No right arrow: the left one is shown in a mirror.")]
        public Sprite stepArrowLeft, stepArrowRight;
        [Tooltip("The box a stepped value is written in, and the slider's handle: the pack's frame over its fill, flattened.")]
        public Sprite stepBox;
        [Min(0f)] public float stepBoxBorder = 10f;
        [Tooltip("The slider's track: the pack's frame over its fill, flattened.")]
        public Sprite sliderTrack;
        [Min(0f)] public float sliderTrackBorder = 8f;
        [Tooltip("The two halves of the ON / OFF switch, dim and lit: the pack's frames over its fill, flattened.")]
        public Sprite toggleLeft, toggleLeftLit, toggleRight, toggleRightLit;
        [Min(0f)] public float toggleBorder = 12f;
        [Tooltip("What lies across a row while the pointer is on it, and its colour.")]
        public Sprite rowHighlight;
        public Color rowHighlightTint = new Color(0f, 0.6f, 0.7f, 0.3f);

        [Header("Menus (the title flow): the pack's own screens")]
        [Tooltip("The pack's title screen.")]
        public GameObject titleScreen;
        [Tooltip("The pack's screen with a row of choices and a description: faction select is made from it.")]
        public GameObject choiceScreen;
        [Tooltip("The pack's list of saved games: character select is made from it.")]
        public GameObject listScreen;
        [Tooltip("The pack's loading screen.")]
        public GameObject loadingScreen;
        [Tooltip("The pack's confirmation popup (leaving the game, deleting a character).")]
        public GameObject confirmPopup;
        [Tooltip("The pack's row with a value stepped left and right: the character creator's option rows.")]
        public GameObject optionRow;
        [Tooltip("The pack's body text label.")]
        public GameObject bodyLabel;
        [Tooltip("The colour of the comic dots and bands laid over the menus, and the second colour of the showroom's light.")]
        public Color menuAccent = Color.white;

        [Header("Menus: the character creator")]
        [Tooltip("The pack's own character creator screen. None: the creator is assembled from the parts below, to the same sections.")]
        public GameObject creatorScreen;
        [Tooltip("The pack's strip of icon tabs, with a key at either end.")]
        public GameObject tabStrip;
        [Tooltip("The pack's framed panel: the page the creator's sections stand on.")]
        public GameObject framedPanel;
        [Tooltip("The pack's square icon button: an origin tile, a colour swatch.")]
        public GameObject iconButton;
        [Tooltip("The pack's row with a slider.")]
        public GameObject sliderRow;
        [Tooltip("The pack's text field.")]
        public GameObject textField;
        [Tooltip("The colour of the text field's fill. Clear: the pack's own (the Dark Fantasy pack's is teal, which the Null's is not).")]
        public Color textFieldFill = Color.clear;
        [Tooltip("The pack's heading over a section of a list, and its headline label.")]
        public GameObject sectionHeader, headerLabel;
        [Tooltip("The pack's large button (create the character).")]
        public GameObject startButton;
        [Tooltip("The pack's row of key prompts along the foot of a screen.")]
        public GameObject footer;
        [Tooltip("The pictures on the creator's seven tabs, in tab order: Origin, Discipline, Body, Head, Preview, Colour, Finalise.")]
        public Sprite[] creatorTabIcons = new Sprite[0];
        [Tooltip("The pictures beside an origin's three traits.")]
        public Sprite[] traitIcons = new Sprite[0];

#if UNITY_EDITOR
        /// <summary>Editor: a value of a skin was changed in the Inspector (the skin sheets are rebuilt from it).</summary>
        public static event System.Action Edited;

        private void OnValidate() => Edited?.Invoke();
#endif

        /// <summary>The class the in-game UI wears for a faction's skin; the generated sheet's rules are all under it.</summary>
        public static string ClassOf(FactionId faction)
        {
            switch (faction)
            {
                case FactionId.Hegemony: return "kn-faction-hegemony";
                case FactionId.Null: return "kn-faction-null";
                case FactionId.Covenant: return "kn-faction-covenant";
                default: return string.Empty;
            }
        }
    }
}
