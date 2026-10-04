using UnityEngine;

namespace KryticalNova.Factions
{
    /// <summary>One faction: its name, lore and the colours that identify it in menus, lighting and UI skins.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Factions/Faction")]
    public sealed class FactionDefinition : ScriptableObject
    {
        public FactionId id;
        public string displayName = "Faction";
        [Tooltip("One line under the name on the faction card.")]
        public string tagline;
        [TextArea(4, 14)] public string lore;
        public Sprite crest;

        [Header("Identity colours (UI accents, showroom lighting)")]
        public Color accentColor = Color.white;
        public Color secondaryColor = Color.gray;
    }
}
