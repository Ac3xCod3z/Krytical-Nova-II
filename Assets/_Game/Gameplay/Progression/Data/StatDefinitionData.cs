using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Designer metadata, limits, and UI display configurations for individual stats.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Stat Definition")]
    public sealed class StatDefinitionData : ScriptableObject
    {
        public StatType statType;
        public string statName;
        public string abbreviation;
        [TextArea(2, 4)] public string description;
        public float minValue = 0f;
        public float maxValue = 999999f;
        public bool isPercentage = false;

        public string FormatValue(float value)
        {
            float clamped = Mathf.Clamp(value, minValue, maxValue);
            return isPercentage ? $"{clamped:0.#}%" : $"{Mathf.RoundToInt(clamped)}";
        }
    }
}
