using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Starting class/faction baselines and weapon base values for combat stat calculations.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Character Class")]
    public class CharacterClassData : ScriptableObject
    {
        [Header("Faction & Archetype")]
        public string factionName = "Guanyin"; 
        public string weaponType = "Blade";  

        [Header("Base Starting Attributes")]
        public int baseStrength = 10;
        public int baseVitality = 10;
        public int baseAgility = 10;
        public int baseSpirit = 10;

        [Header("Base Health & Chi Pools")]
        public float baseHealthPool = 100f;
        public float baseChiPool = 50f;

        [Header("Weapon Base Modifiers")]
        public float baseWeaponAP = 15f;
        public float baseWeaponDefense = 5f;
    }
}
