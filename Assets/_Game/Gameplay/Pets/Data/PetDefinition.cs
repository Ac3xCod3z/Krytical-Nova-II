using System;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Pets
{
    /// <summary>How a pet looks at one of its five growth stages.</summary>
    [Serializable]
    public struct PetStageLook
    {
        [Tooltip("Shown in the pet panel and in the evolution banner.")]
        public string name;
        [Tooltip("The model for this stage. Empty = the pet item's own model (the stand-in until this stage's art exists).")]
        public GameObject model;
        [Tooltip("Size relative to the base stage.")]
        [Min(0.1f)] public float scale;
        [Tooltip("Colour of the glow that rides on the pet.")]
        public Color glow;
        [Tooltip("How strong the glow is, 0 (none) to 1.")]
        [Range(0f, 1f)] public float glowStrength;
    }

    /// <summary>
    /// One pet: the item that is the pet, the stat it grows and how it looks at each of its five stages. Growth and
    /// hunger are not here: they belong to each pet item (<see cref="PetItemState"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Pets/Pet")]
    public sealed class PetDefinition : ScriptableObject
    {
        [Tooltip("The item that is this pet (ItemDefinition.itemId).")]
        public ushort itemId;
        public string displayName = "Pet";
        [TextArea(2, 5)] public string description;

        [Header("Stat")]
        [Tooltip("The stat this pet grows.")]
        public StatType stat = StatType.MaxHP;
        [Tooltip("What the owner gets at 100 % growth. Every 1 % of growth pays out 1/100 of it.")]
        [Min(0f)] public float maxValue = 2200f;

        [Header("Looks (base, 25 %, 50 %, 75 %, 100 %)")]
        public PetStageLook[] stages = new PetStageLook[PetRules.StageCount];

        [Header("Chatter")]
        [Tooltip("What this pet says when its owner dies, lands a critical hit, levels up and so on: words for a pet that talks, emoji for an animal.")]
        public PetChatterSet chatter;

        [Header("Follow")]
        [Tooltip("Added to the pet settings' follow gap for this pet only, in metres. More hovers further from its owner.")]
        public float followGapAdjust;

        public PetStageLook Stage(int stage)
        {
            if (stages == null || stages.Length == 0) return new PetStageLook { scale = 1f };
            PetStageLook look = stages[Mathf.Clamp(stage, 0, stages.Length - 1)];
            if (look.scale <= 0f) look.scale = 1f;
            return look;
        }

        /// <summary>"Stage 2", or the stage's own name when it has one ("Apex").</summary>
        public string StageName(int stage)
        {
            string own = Stage(stage).name;
            return string.IsNullOrEmpty(own) ? $"Stage {Mathf.Clamp(stage, 0, PetRules.LastStage) + 1}" : own;
        }

        /// <summary>Where the pet is on its way: "Stage 2 of 5", or "Apex · 5 of 5" for a stage with a name.</summary>
        public string StageLabel(int stage)
        {
            int number = Mathf.Clamp(stage, 0, PetRules.LastStage) + 1;
            string own = Stage(stage).name;
            return string.IsNullOrEmpty(own) ? $"Stage {number} of {PetRules.StageCount}" : $"{own}  ·  {number} of {PetRules.StageCount}";
        }
    }
}
