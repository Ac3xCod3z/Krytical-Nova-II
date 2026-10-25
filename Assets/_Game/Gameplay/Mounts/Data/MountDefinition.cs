using System;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>How a mount looks and moves at one of its three growth stages (0, 50 and 100 %).</summary>
    [Serializable]
    public struct MountStageLook
    {
        [Tooltip("Shown in the Mount window and in the evolution message.")]
        public string name;
        [Tooltip("The model for this stage (Characters/Mounts/<Mount>_<stage>). Empty = not built yet.")]
        public GameObject model;
        [Tooltip("The icon of the mount at this stage, made by the icon library. Shown in the Mount window.")]
        public Sprite icon;
        [Tooltip("Where the rider stands (feet level) on this stage, in the mount model's own space, in metres: the seat socket.")]
        public Vector3 seat;
        [Tooltip("The mount's own clips at this stage.")]
        public AnimationClip idle, walk, run;
        [Tooltip("Other idles played now and then at rest (a sniff, a look round).")]
        public AnimationClip[] idleExtras;
        [Tooltip("Colour of the evolution moment's effect when this stage is reached.")]
        public Color glow;

        [Header("The rider on this stage (owner, 25 Oct 2026: each stage has its own straddle and stirrups)")]
        [Tooltip("Humanoid clips keyed on this stage's seat. Empty = the mount's own rider clips.")]
        public AnimationClip riderIdle, riderWalk, riderRun, mountUp, dismount;
        public AnimationClip[] riderIdleExtras;
        [Tooltip("Where the rider's ankles go on this stage (the stirrup treads), in the mount model's own space. The game puts the feet there with IK, whatever the character's size. Zero = no stirrups.")]
        public Vector3 stirrupLeft, stirrupRight;
    }

    /// <summary>
    /// One mount: the item that registers it, its box in the Mount window, the two main stats it grows (owner, 25 Oct
    /// 2026: Strength, Vitality, Agility or Spirit, every mount a different pair), how it looks at each of its three
    /// stages and how its rider sits.
    /// Growth and hunger are not here: they belong to each character's own mounts (<see cref="MountState"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Mounts/Mount")]
    public sealed class MountDefinition : ScriptableObject
    {
        [Tooltip("Its box in the Mount window, 1 to 4. Never renumber: saves are keyed on it.")]
        [Range(1, MountRules.Slots)] public byte mountId = 1;
        [Tooltip("The item that, used from the Pack, adds this mount to the Mount window (ItemDefinition.itemId). 0 = it cannot be had yet.")]
        public ushort itemId;
        public string displayName = "Mount";
        [TextArea(2, 5)] public string description;

        [Header("Stats (each at 100 % growth; every 1 % pays 1/100 of it)")]
        public StatType firstStat = StatType.Vitality;
        [Min(0f)] public float firstMax = 50f;
        public StatType secondStat = StatType.Strength;
        [Min(0f)] public float secondMax = 50f;

        [Header("Looks (0 %, 50 %, 100 %)")]
        public MountStageLook[] stages = new MountStageLook[MountRules.StageCount];

        [Header("Rider (Humanoid clips, made on this mount's seat)")]
        public AnimationClip riderIdle;
        public AnimationClip riderWalk;
        public AnimationClip riderRun;
        [Tooltip("Other rider idles played now and then at rest (a look round, a pat on the neck).")]
        public AnimationClip[] riderIdleExtras = new AnimationClip[0];
        public AnimationClip mountUp;
        public AnimationClip dismount;

        /// <summary>True when the mount can be ridden: its first stage has a model and its rider clips exist.</summary>
        public bool IsBuilt => stages != null && stages.Length > 0 && stages[0].model != null && riderIdle != null;

        public MountStageLook Stage(int stage)
        {
            if (stages == null || stages.Length == 0) return default;
            return stages[Mathf.Clamp(stage, 0, stages.Length - 1)];
        }

        /// <summary>The stage's own name, or "Stage 2".</summary>
        public string StageName(int stage)
        {
            string own = Stage(stage).name;
            return string.IsNullOrEmpty(own) ? $"Stage {Mathf.Clamp(stage, 0, MountRules.LastStage) + 1}" : own;
        }

        /// <summary>The rider's clip for a stage: the stage's own, or the mount's when the stage has none.</summary>
        public AnimationClip RiderClip(int stage, Func<MountStageLook, AnimationClip> ofStage, AnimationClip fallback)
        {
            AnimationClip own = stages != null && stages.Length > 0 ? ofStage(Stage(stage)) : null;
            return own != null ? own : fallback;
        }

        /// <summary>The icon for a stage, or that of the nearest earlier stage that has one.</summary>
        public Sprite IconFor(int stage)
        {
            if (stages == null) return null;
            for (int s = Mathf.Clamp(stage, 0, stages.Length - 1); s >= 0; s--)
                if (stages[s].icon != null) return stages[s].icon;
            return null;
        }
    }
}
