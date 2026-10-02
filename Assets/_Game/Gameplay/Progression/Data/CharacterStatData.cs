using UnityEngine;

namespace KryticalNova.Progression
{
    public enum CharacterAttribute { Strength, Vitality, Agility, Spirit }

    /// <summary>Starting attributes and allocation limits. Combat-derived effects are a later system.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Character Stats")]
    public sealed class CharacterStatData : ScriptableObject
    {
        private const int DefaultBaseValue = 10;
        private const int DefaultCap = 999;
        private const int DefaultPointCost = 1;
        [Min(0)] public int startingStrength = DefaultBaseValue;
        [Min(0)] public int startingVitality = DefaultBaseValue;
        [Min(0)] public int startingAgility = DefaultBaseValue;
        [Min(0)] public int startingSpirit = DefaultBaseValue;
        [Min(1)] public int maximumAttributeValue = DefaultCap;
        [Min(1)] public int allocationCost = DefaultPointCost;

        /// <summary>Gets the configured starting value for an attribute.</summary>
        public int GetStartingValue(CharacterAttribute attribute)
        {
            int value = attribute switch
            {
                CharacterAttribute.Strength => startingStrength,
                CharacterAttribute.Vitality => startingVitality,
                CharacterAttribute.Agility => startingAgility,
                CharacterAttribute.Spirit => startingSpirit,
                _ => 0
            };
            return Mathf.Clamp(value, 0, maximumAttributeValue);
        }

        private void OnValidate()
        {
            maximumAttributeValue = Mathf.Max(1, maximumAttributeValue);
            allocationCost = Mathf.Max(1, allocationCost);
            startingStrength = Mathf.Clamp(startingStrength, 0, maximumAttributeValue);
            startingVitality = Mathf.Clamp(startingVitality, 0, maximumAttributeValue);
            startingAgility = Mathf.Clamp(startingAgility, 0, maximumAttributeValue);
            startingSpirit = Mathf.Clamp(startingSpirit, 0, maximumAttributeValue);
        }
    }
}
