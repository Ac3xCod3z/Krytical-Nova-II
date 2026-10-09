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
        [Tooltip("The ornament on the middle of a window's top edge, and one drawn behind it (horns behind a skull). Either may be none.")]
        public Sprite crown, crownBack;
        [Tooltip("The ornaments' sizes in panel pixels, and how far their tops stand above the window's top edge.")]
        public Vector2 crownSize = new Vector2(64f, 32f), crownBackSize = new Vector2(96f, 48f);
        public float crownRise = 24f, crownBackRise = 30f;

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
