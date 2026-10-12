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
        [Tooltip("Bones of the pet's own model it is worn on. None = one, at the place below.")]
        public string[] bones = new string[0];
        [Tooltip("With no bone named: where on the pet it goes. These places do not move with the animation, so dust stays on the floor the pet stands on while the pet hops.")]
        public PetEffectAnchor anchor = PetEffectAnchor.Body;
        [Tooltip("Moved this far from that place, in the pet's own space (x to its right, y up, z ahead), in footprints: 1 = as far as the pet is wide.")]
        public Vector3 offset;
        [Tooltip("On: its particles are tinted with the glow colour of the pet's growth stage, so the effect changes as the pet grows.")]
        public bool stageColour = true;
        [Tooltip("Its size against the size it was made for, which is the pet's footprint. An effect made for a small pet is brought down for a wide one.")]
        [Min(0.05f)] public float size = 1f;
        [Tooltip("The growth stages it is worn at, first (x) to last (y): 0 is the base form, 4 fully grown. A pet whose stages are different creatures (the Tidecaller's egg, then its swimmers) has effects that belong to some of them only.")]
        public Vector2Int stages = new Vector2Int(0, PetRules.LastStage);

        [Header("Only with an animation")]
        [Tooltip("The animation it belongs to: the name of a state of the pet's Animator (HopSpin, Move). Empty = it is worn all the time.")]
        public string animation = "";
        [Tooltip("The moments of that animation at which it plays, once each, as shares of the animation (0 = its start, 1 = its end). None = it plays for the stretch below.")]
        public float[] at = new float[0];
        [Tooltip("With no moments above: the stretch of the animation during which it plays, as shares of it. 0 to 0 = the whole animation.")]
        public Vector2 during;
        [Tooltip("On: when the animation is cut short (its owner walks off, a fight starts) what is out of this effect is taken away at once. For an effect that makes no sense without the pose: a beam, the blur round a spinning barrel.")]
        public bool endsWithAnimation;

        [Header("Only on a bone")]
        [Tooltip("On: it turns with the bone and looks down it, the way the bone runs (a muzzle flash at the end of a gun barrel). Its offset is then measured in the bone's own frame, in metres of the model: z = down the bone, so the bone's length puts it at the tip.")]
        public bool alongBone;
    }

    /// <summary>Something a pet says at a moment of one of its animations, in its speech bubble.</summary>
    [Serializable]
    public sealed class PetLine
    {
        [Tooltip("The animation it belongs to: the name of a state of the pet's Animator.")]
        public string animation = "";
        [Tooltip("The moment of that animation at which it is said, as a share of the animation (0 = its start, 1 = its end).")]
        [Range(0f, 1f)] public float at;
        public string text = "";
        [Tooltip("The growth stages it is said at, first (x) to last (y).")]
        public Vector2Int stages = new Vector2Int(0, PetRules.LastStage);
    }

    /// <summary>
    /// An animation that is followed by another instead of going back to rest (the Drone Core at 100 %: it comes
    /// apart, hovers as a swarm, then comes together again). The one that follows is never played by turn on its
    /// own: it is only reached through the one before it.
    /// </summary>
    [Serializable]
    public sealed class PetFollowOn
    {
        [Tooltip("The animation: the name of a state of the pet's Animator.")]
        public string animation = "";
        [Tooltip("How many times it is played through before the next one starts.")]
        [Min(1)] public int times = 1;
        [Tooltip("The animation that follows it.")]
        public string then = "";
    }

    /// <summary>Where on a pet an effect goes when it names no bone.</summary>
    public enum PetEffectAnchor
    {
        /// <summary>The middle of the body.</summary>
        Body,
        /// <summary>Under it: the middle of the floor it stands or hovers on.</summary>
        Feet,
        /// <summary>The top of it.</summary>
        Top,
    }

    [Serializable]
    public struct PetStageLook
    {
        [Tooltip("Shown in the pet panel and in the evolution banner.")]
        public string name;
        [Tooltip("The model for this stage. Empty = the pet item's own model (the stand-in until this stage's art exists).")]
        public GameObject model;
        [Tooltip("The icon of the pet at this stage, baked from the model above (Items > Bake Pet Stage Icons). Empty = the pet item's own icon.")]
        public Sprite icon;
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

        [Header("Charge (an animation in which it turns on its owner and drives at them)")]
        [Tooltip("The animation: the name of a state of the model's Animator (the Tidecaller's Ram). While it plays the pet faces its owner, and its lunge is stretched or shortened so that its nose just reaches them. Empty = none.")]
        public string chargeAnimation = "";
        [Tooltip("The bone of the model that the animation carries forward in its lunge: how far it has gone tells how far through the lunge the pet is.")]
        public string chargeBone = "";

        [Header("Rest pose (a model whose file was saved in some other pose than the one it rests in)")]
        [Tooltip("The animation whose first frame is the pose the pet rests in. Its resting state has no animation of its own, so the model rests as its file has it; this puts it in that first frame before it is ever shown, and it is measured in that pose. Empty = it rests as its file has it.")]
        public string restPose = "";

        [Header("Animations that follow one another")]
        public PetFollowOn[] followOns = new PetFollowOn[0];

        [Header("What it says in its animations")]
        public PetLine[] lines = new PetLine[0];

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
