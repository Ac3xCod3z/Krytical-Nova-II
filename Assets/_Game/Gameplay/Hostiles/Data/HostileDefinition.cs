using System;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// One species of Hostile: what it is called, how strong, what it looks like and how it moves. Its numbers
    /// come from <see cref="HostileStatCurve"/> for its level and tier. The prefab the server spawns is built
    /// from this asset by the Hostile prefab builder and never edited by hand.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Hostiles/Hostile Definition")]
    public sealed class HostileDefinition : ScriptableObject
    {
        /// <summary>One attack of a species.</summary>
        [Serializable]
        public struct Attack
        {
            public AnimationClip clip;
            [Tooltip("Seconds from the start of the swing to the hit: where the attack event goes on the clip, and how long clients show the warning.")]
            [Min(0.05f)] public float windupSeconds;
            [Tooltip("Multiplies the Hostile's Attack for this attack (the skill multiplier of the damage formula).")]
            [Min(0.1f)] public float damageMultiplier;
            [Tooltip("A heavy attack is always announced to clients before it lands.")]
            public bool heavy;
            [Tooltip("How often this attack is picked against the others.")]
            [Range(1, 100)] public int weight;
            [Tooltip("The warning players see before this swing lands. Default: the Hostile Brain Settings' minimum warning (empty = this). " +
                     "Own: this attack's own seconds below. Off: no warning before this swing at all.")]
            public SwingWarning warning;
            [Tooltip("With Own: the least time players see this swing coming, in seconds (the settings' latency allowance is added).")]
            [Min(0f)] public float warningSeconds;
        }

        /// <summary>
        /// The warning before one attack (owner, 7 Nov 2026): the settings' minimum (the default, also what an attack saved before this had),
        /// the attack's own length, or none. Boss telegraphs come later as their own value here.
        /// </summary>
        public enum SwingWarning : byte
        {
            Default = 0,
            Own = 1,
            Off = 2,
        }

        [Header("Identity")]
        [Tooltip("Stable id, sent over the network and kept in kill counts. Never reuse one.")]
        public ushort id;
        public string displayName = "Hostile";
        public HostileTier tier = HostileTier.Normal;
        [Min(1)] public int level = 1;
        public HostileBehaviour behaviour = HostileBehaviour.FightsBack;
        [Tooltip("What leaves its body when it dies: several green spirits from a creature, one small one from the mouth of a person (light blue), a robot or a skeleton (reddish).")]
        public SpiritKind spirit = SpiritKind.Creature;
        [Tooltip("Ticked by the model inventory for anything boss-sized. Such a species is refused by every Hostile Zone.")]
        public bool isBossCandidate;

        [Header("Numbers on top of the stat curve (1 = as the curve says)")]
        [Min(0.1f)] public float healthMultiplier = 1f;
        [Tooltip("Metres from the target at which it attacks.")]
        [Min(0.5f)] public float attackRange = 2f;
        [Tooltip("How far above its own feet it can strike (metres): a player up a wall or in a tree higher than this is out of " +
                 "its reach (Wallstride). Ranged and flying species reach higher.")]
        [Min(0.5f)] public float reachAbove = 3f;
        [Tooltip("The loot table this species rolls on. Empty until drops are built.")]
        public string lootTableId;

        [Header("Brain (entity netcode server)")]
        [Tooltip("Seconds between noticing a player and turning on them. Below 0 = HostileBrainSettings' default.")]
        public float noticeSeconds = -1f;
        [Tooltip("How hard it is to stagger (0-100): a hit staggers it only when the hit's poise damage is above this. 100 = never.")]
        [Range(0, 100)] public int poise = 30;
        [Tooltip("Joins a fight of its own kind within the assist radius (HostileBrainSettings.assistRadius).")]
        public bool assist;
        [Tooltip("Runs away when nearly dead.")]
        public bool flees;
        [Tooltip("How it picks its attacks (proposal for the owner's hand test).")]
        public HostileScoreProfile scoreProfile = HostileScoreProfile.Brute;

        [Header("Looks")]
        [Tooltip("The character model (a Synty prefab or FBX).")]
        public GameObject model;
        [Min(0.1f)] public float scale = 1f;
        [Tooltip("Capsule the species is hit and targeted by: radius (x) and height (y), metres, before scale.")]
        public Vector2 bodySize = new Vector2(0.45f, 1.8f);

        [Header("Animation (clips of the model's own rig)")]
        public AnimationClip idle;
        public AnimationClip combatIdle;
        public AnimationClip walk;
        public AnimationClip run;
        public AnimationClip turnLeft;
        public AnimationClip turnRight;
        public Attack[] attacks = Array.Empty<Attack>();
        public AnimationClip[] hitReactions = Array.Empty<AnimationClip>();
        public AnimationClip[] deaths = Array.Empty<AnimationClip>();
        public AnimationClip stunned;

        [Header("Built (written by the prefab builder)")]
        public GameObject prefab;

        /// <summary>This species' numbers at its own level, or at another (a GM spawn at a chosen level).</summary>
        public HostileStats StatsFrom(HostileStatCurve curve, int atLevel = 0)
        {
            HostileStats stats = curve.Evaluate(atLevel > 0 ? atLevel : level, tier);
            stats.MaxHealth = Mathf.Round(stats.MaxHealth * healthMultiplier);
            return stats;
        }
    }
}
