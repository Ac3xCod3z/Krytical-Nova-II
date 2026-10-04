using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Factions
{
    /// <summary>Every faction in the game, plus the switch that decides whether factions fight each other.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Factions/Faction Catalog")]
    public sealed class FactionCatalog : ScriptableObject
    {
        [SerializeField] private List<FactionDefinition> factions = new List<FactionDefinition>();

        [Tooltip("The default when a session starts. On: players of different factions are hostile. Off: every player is " +
                 "friendly to every other player. A GM can switch it while playing (gm.pvp).")]
        public bool crossFactionPvp = true;

        public IReadOnlyList<FactionDefinition> Factions => factions;

        public FactionDefinition Get(FactionId id)
        {
            foreach (var faction in factions)
                if (faction != null && faction.id == id) return faction;
            return null;
        }

        public void SetContents(List<FactionDefinition> list) => factions = list;
    }
}
