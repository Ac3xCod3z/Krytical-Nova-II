using UnityEngine;

namespace KryticalNova.Audio
{
    /// <summary>
    /// A pack of the Deck's music (owner, 22 Oct 2026: every track from day one, built as packs so music packs can come
    /// later): a name and its tracks. Fight music (Cadence) is never put in a pack: the Deck plays out of a fight only.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Audio/Deck Pack", fileName = "DeckPack")]
    public sealed class DeckPack : ScriptableObject
    {
        [Tooltip("What the Deck calls the pack.")]
        public string packName = "Nova Originals";
        [TextArea] public string description;
        public MusicTrack[] tracks = new MusicTrack[0];
    }
}
