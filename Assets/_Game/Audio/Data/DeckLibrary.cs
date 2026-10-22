using UnityEngine;
using UnityEngine.Audio;

namespace KryticalNova.Audio
{
    /// <summary>
    /// The Deck, a player's own music player (owner, 22 Oct 2026): which packs it holds, where it plays, how loud it
    /// starts, and the motion each faction makes when it is opened. Every number of the Deck is here; the player is
    /// <see cref="DeckPlayer"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Audio/Deck Library", fileName = "DeckLibrary")]
    public sealed class DeckLibrary : ScriptableObject
    {
        [Tooltip("Every pack the Deck holds, in the order the list shows them. All unlocked from day one.")]
        public DeckPack[] packs = new DeckPack[0];
        [Tooltip("The mixer group it plays on (Music), so the Music slider sets it.")]
        public AudioMixerGroup output;
        [Range(0f, 1f)] public float defaultVolume = 0.7f;
        [Tooltip("Seconds the Deck takes to step aside when a fight starts, and to come back after it.")]
        [Min(0.05f)] public float duckSeconds = 0.4f;
        [Min(0.05f)] public float returnSeconds = 2f;
        [Tooltip("Bars of the window's visualiser, and how loud a bar must be to reach the top.")]
        [Range(8, 48)] public int bars = 24;
        [Min(0.001f)] public float barGain = 18f;

        [Header("Opening the Deck: the motion each faction makes (emote keys: S/E + group + name)")]
        [Tooltip("Hegemony: a real phone, out of the back pocket.")]
        public string hegemonyEmote = "S/Use Phone/One Hand";
        [Tooltip("Null: a device read with both hands (a dark-magic prop when one exists).")]
        public string nullEmote = "S/Use Phone/Two Hands Squinting";
        [Tooltip("Covenant: still, hands together (a rune stone when one exists).")]
        public string covenantEmote = "S/Pray/Standing";
    }
}
