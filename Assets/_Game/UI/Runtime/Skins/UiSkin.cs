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
