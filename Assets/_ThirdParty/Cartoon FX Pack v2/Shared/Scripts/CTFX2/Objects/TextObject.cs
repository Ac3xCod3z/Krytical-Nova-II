using System;
using System.Collections.Generic;
using System.Linq;
using PSD.Creator.Objects;
using UnityEngine;

namespace CTFX2.Creator.Objects
{
    [CreateAssetMenu(fileName = "Text", menuName = "CTFX2/Components/Text")]
    public class TextObject : BillboardObject
    {
        [Serializable]
        public class CharacterMapping
        {
            [SerializeField] private char character;
            public char Character { get => char.ToLowerInvariant(character); set => character = char.ToLowerInvariant(value); }
            public int index;
        }
        [SerializeField] private List<CharacterMapping> characterMap;

        public bool IsInitialized() => characterMap != null && characterMap.Count > 0;

        public void SetCharacterMap(char character, int index)
        {
            character = char.ToLowerInvariant(character);
            characterMap ??= new List<CharacterMapping>();

            var mapping = characterMap.FirstOrDefault(c => c.Character == character);
            if (mapping != null)
                mapping.index = index;
            else
                characterMap.Add(new CharacterMapping() { Character = character, index = index });
        }

        public void ClearCharacterMap()
        {
            characterMap = new List<CharacterMapping>();
        }

        public int GetIndex(char character)
        {
            character = char.ToLowerInvariant(character);
            if (character == ' ') return characterMap.Count;
            var mapping = characterMap.FirstOrDefault(c => c.Character == character);
            if (mapping != null) return mapping.index;
            return 0;
        }

        public bool HasCharacter(char character)
        {
            character = char.ToLowerInvariant(character);
            if (character == ' ') return true;
            return characterMap.Any(c => c.Character == character);
        }
    }
}
