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

        public IEnumerable<HudChromePiece> Pieces()
        {
            yield return hotbar;
            yield return vitals;
        }
    }
}
