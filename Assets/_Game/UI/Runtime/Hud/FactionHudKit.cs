using System.Collections.Generic;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.UI.Hud
{
    /// <summary>
    /// The uGUI pieces of one faction's HUD: prefabs composed from that faction's Synty HUD pack by
    /// FactionHudBuilder (menu Krytical Nova ▸ UI ▸ Build Faction HUD). Every faction's kit has the same pieces.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/UI/Faction HUD Kit", fileName = "FactionHudKit")]
    public sealed class FactionHudKit : ScriptableObject
    {
        public FactionId faction;

        [Tooltip("The Hotbar: slots 1 to 0 with the XP bar, and the Q W E R bar.")]
        public HotbarChromeView hotbar;

        [Tooltip("The player's plate: live portrait, level, HP and Flux.")]
        public VitalsChromeView vitals;

        [Tooltip("The minimap's dial: the frame round the map.")]
        public MinimapChromeView minimap;

        [Tooltip("The plate over a Hostile: level badge, health bar and name (HostilePlateBuilder). Not a piece of the HUD: one is made per Hostile on screen.")]
        public HostilePlateView hostilePlate;

        [Tooltip("The namecard over a player: level badge, crest, name, Standing and Crew, and the health bar where it is shown (PlayerCardBuilder). One is made per player on screen.")]
        public PlayerCardView playerCard;

        [Tooltip("The mouse pointer while this faction's HUD shows, and where it points (pixels from its top-left corner). Copied from the faction's UiSkin by Build Faction Skins.")]
        public Texture2D cursor;
        public Vector2 cursorHotspot;

        public IEnumerable<HudChromePiece> Pieces()
        {
            yield return hotbar;
            yield return vitals;
            yield return minimap;
        }
    }
}
