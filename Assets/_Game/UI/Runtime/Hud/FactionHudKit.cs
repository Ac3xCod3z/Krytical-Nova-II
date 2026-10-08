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

        [Tooltip("How large the pieces are drawn: panel pixels (of the 1280 x 720 interface) per canvas unit of the packs (drawn for 3840 x 2160). 0.333 is the size Synty shows them at. The builder sets it so that every faction's Hotbar is as wide as the others.")]
        [Range(0.15f, 0.6f)] public float unitsToPanel = 0.24f;

        [Tooltip("The Hotbar: HP container, slots 1 to 0, Flux container, XP bar, level badge, and the Q W E R bar.")]
        public HotbarChromeView hotbar;

        public IEnumerable<HudChromePiece> Pieces()
        {
            yield return hotbar;
        }
    }
}
