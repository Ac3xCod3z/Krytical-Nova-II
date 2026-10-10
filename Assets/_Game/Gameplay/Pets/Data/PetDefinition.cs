using System;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Pets
{
    /// <summary>How a pet looks at one of its five growth stages.</summary>
    /// <summary>
    /// An effect a pet wears as its own mark (the Emberwing's cinders, the Drone Core's data chips): one copy on each
    /// named bone of its model, or one on the middle of its body when none is named. The effect follows the bone's
    /// place, never its turning, and decides for itself whether its particles stay behind in the world.
    /// </summary>
    [Serializable]
    public sealed class PetEffect
    {
        public GameObject prefab;
        [Tooltip("Bones of the pet's own model it is worn on. None = one, on the middle of the body.")]
        public string[] bones = new string[0];
        [Tooltip("On: its particles are tinted with the glow colour of the pet's growth stage, so the effect changes as the pet grows.")]
        public bool stageColour = true;
    }

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

        [Header("Stance")]
        [Tooltip("On: it bobs up and down beside its owner, like a drone. Off: it keeps its height (a creature that stands).")]
        public bool bobs = true;
        [Tooltip("An effect worn under its feet, sized to the pet (the Kitsune Spirit's clouds, which hold it up). Empty = none.")]
        public GameObject footEffect;
        [Tooltip("The bones of the model that are its feet: each gets the effect above. Empty = one under the middle of the body.")]
        public string[] footBones = new string[0];

        [Header("Effects of its own (besides the one under its feet)")]
        [Tooltip("What marks this pet out: worn on the bones named, whether it stands or flies.")]
        public PetEffect[] effects = new PetEffect[0];

        [Header("Movement loop (a model with one movement animation instead of a walk and a run)")]
        [Tooltip("The true/false parameter of the model's Animator that holds its movement loop (the Drone Core's FrontRoll). Empty = none: a model with Walk and Run parameters uses those.")]
        public string moveLoop = "";
        [Tooltip("How fast the loop is played while the owner walks. 1 = as it was made.")]
        [Min(0.01f)] public float moveLoopWalkSpeed = 0.6f;
        [Tooltip("How fast the loop is played while the owner runs. 1 = as it was made.")]
        [Min(0.01f)] public float moveLoopRunSpeed = 1.5f;

        [Tooltip("The true/false parameter that holds the loop the model waits in instead of its default state (Emberwing's Fly: it hovers on its wings rather than sitting perched in mid-air). It lets go for the movement loop and for the animations that play once. Empty = it waits in its default state.")]
        public string restLoop = "";

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
