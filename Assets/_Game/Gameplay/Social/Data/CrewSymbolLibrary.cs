using System;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// The pictures a Crew's symbol is chosen from. A Crew stores only a number (<see cref="CrewSettings.symbolCount"/>
    /// of them); each faction draws that number as a picture from its own interface pack, so a Hegemony Crew's third
    /// symbol and a Covenant Crew's third are different pictures in the look of their own faction. Built by
    /// CrewSymbolBuilder from the table there; the owner picks which pictures.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Social/Crew Symbol Library")]
    public sealed class CrewSymbolLibrary : ScriptableObject
    {
        [Serializable]
        public struct FactionSymbols
        {
            public FactionId faction;
            [Tooltip("In the order of a Crew's symbol number, from 0.")]
            public Sprite[] symbols;
        }

        [Tooltip("One list a faction, each as long as Crew Settings' symbol count.")]
        public FactionSymbols[] sets = Array.Empty<FactionSymbols>();

        /// <summary>The picture of a faction's symbol with this number, or null when it has none.</summary>
        public Sprite Get(FactionId faction, int index)
        {
            if (sets == null) return null;
            foreach (FactionSymbols set in sets)
            {
                if (set.faction != faction || set.symbols == null || set.symbols.Length == 0) continue;
                return set.symbols[Mathf.Abs(index) % set.symbols.Length];
            }
            return null;
        }

        /// <summary>How many pictures a faction has.</summary>
        public int CountFor(FactionId faction)
        {
            if (sets == null) return 0;
            foreach (FactionSymbols set in sets)
                if (set.faction == faction) return set.symbols != null ? set.symbols.Length : 0;
            return 0;
        }
    }
}
