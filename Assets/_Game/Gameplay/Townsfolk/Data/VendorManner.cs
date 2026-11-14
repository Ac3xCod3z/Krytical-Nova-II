using System;
using KryticalNova.NPC;
using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// How one vendor notices the player (plan 3.6, step 7; owner 18.1 A): which gesture it greets with, what it calls out as the player
    /// passes and what it says when the player opens its shop. Keyed by the vendor's <see cref="NpcDefinition"/> name. Drafts for the owner
    /// to edit; the timings are <see cref="TownsfolkSettings"/>' vendor rows. Client only: nothing is sent.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Townsfolk/Vendor Manner", fileName = "VendorManner")]
    public sealed class VendorManner : ScriptableObject
    {
        [Tooltip("The NpcDefinition asset's name this manner belongs to (NPC_Blacksmith, ...).")]
        public string npc;
        [Tooltip("What it does when the player passes and looks its way.")]
        public TownGesture greet = TownGesture.Wave;
        [Tooltip("Lines it calls out as the player passes (one at a time, never the same twice running).")]
        public NpcLine[] callouts = Array.Empty<NpcLine>();
        [Tooltip("Lines it says when the player opens its shop.")]
        public NpcLine[] welcome = Array.Empty<NpcLine>();
    }
}
