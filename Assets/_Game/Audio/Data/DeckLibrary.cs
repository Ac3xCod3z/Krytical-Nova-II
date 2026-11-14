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

        /// <summary>How the window looks (build 17, 5.5c): the plain look of 22 Oct, or one of two livelier ones.</summary>
        public enum Look : byte { Classic, BeatColours, FactionGlow }

        [Header("The window's look (build 17, 5.5c)")]
        [Tooltip("Classic: the first look (flat bars). Beat Colours: bars go cyan > amber > red with their level, like the " +
                 "pet's Beat Ring, with falling peak caps and a beat pulse. Faction Glow: bars keep the faction's colour and " +
                 "light up with their level, with the same caps and pulse.")]
        public Look look = Look.BeatColours;
        [Tooltip("Beat Colours: a quiet, a middling and a loud bar (the Beat Ring's low / mid / high).")]
        public Color lowColour = new Color(0.25f, 0.85f, 1f);
        public Color midColour = new Color(1f, 0.72f, 0.18f);
        public Color highColour = new Color(1f, 0.22f, 0.16f);
        [Tooltip("Faction Glow: how dim a silent bar is (1 = as bright as a loud one).")]
        [Range(0f, 1f)] public float glowFloor = 0.4f;
        [Tooltip("How fast a peak cap falls back, in bar heights per second.")]
        [Min(0.05f)] public float peakFallPerSecond = 0.5f;
        [Tooltip("The beat pulse on the cover square and the play button: how much bigger on the beat, and how fast it fades " +
                 "(from the track's own BPM and first beat).")]
        [Range(0f, 0.3f)] public float beatPulseScale = 0.08f;
        [Min(0.5f)] public float beatPulseSharpness = 7f;

        [Header("Opening the Deck: the motion each faction makes (emote keys: S/E + group + name)")]
        [Tooltip("Hegemony: a real phone, out of the back pocket.")]
        public string hegemonyEmote = "S/Use Phone/One Hand";
        [Tooltip("Null: a device read with both hands (a dark-magic prop when one exists).")]
        public string nullEmote = "S/Use Phone/Two Hands Squinting";
        [Tooltip("Covenant: still, hands together (a rune stone when one exists).")]
        public string covenantEmote = "S/Pray/Standing";
    }
}
