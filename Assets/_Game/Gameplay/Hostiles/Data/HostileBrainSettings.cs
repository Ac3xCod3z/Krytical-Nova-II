using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// The numbers of the Hostiles mind on the entity netcode (implementation plan s1.4; owner answers Q1-Q8 of 6 Nov 2026 and the M5.0
    /// parity marks). Read once at server start; the species' own numbers stay on <see cref="HostileDefinition"/> and the stat curve.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Hostiles/Hostile Brain Settings", fileName = "HostileBrainSettings")]
    public sealed class HostileBrainSettings : ScriptableObject
    {
        [Header("Noticing and reach")]
        [Tooltip("Seconds between seeing a player and going for it, for species with no own value (Q8).")]
        public float noticeSecondsDefault = 0.1f;
        [Tooltip("Metres past the attack range a swing still starts and lands.")]
        public float reachSlack = 0.3f;
        [Tooltip("Share of a windup after which a dodge no longer cancels the swing (it can only miss).")]
        [Range(0f, 1f)] public float commitFraction = 0.6f;
        [Tooltip("Degrees off its facing a target may stand for a swing to start or land.")]
        public float facingDegrees = 60f;
        [Tooltip("Shortest windup a player sees before a hit, seconds (Q3).")]
        public float minVisibleWarningSeconds = 0.35f;

        [Header("Threat (Q5)")]
        [Tooltip("A challenger in reach takes the Hostile above this share of the current target's threat.")]
        public float threatSwitchMelee = 1.10f;
        [Tooltip("A challenger out of reach takes the Hostile above this share.")]
        public float threatSwitchRanged = 1.30f;
        [Tooltip("Seconds without new threat before a player's threat decays.")]
        public float threatDecayAfterSeconds = 5f;
        [Tooltip("Share of threat lost per second once it decays.")]
        public float threatDecayPerSecond = 0.05f;
        [Tooltip("Threat a heal makes, as a share of the healing.")]
        public float healThreatFactor = 0.5f;
        [Tooltip("Extra share a taunt adds over the top threat.")]
        public float tauntBonus = 0.10f;

        [Header("Giving up (H14 parity)")]
        [Tooltip("Seconds the target may stand beyond the detection radius before the Hostile gives up (Emerald's chase limit; no heal).")]
        public float giveUpSeconds = 5f;

        [Header("Attack slots (M6.5)")]
        public float slotRingRadius = 2f;
        public Vector2 waitRingRadius = new Vector2(3.5f, 5f);
        public int gridCapacity = 12;
        public int attackCapacity = 10;
        public int rangedAttackCapacity = 6;
        public int gridWeightNormal = 4;
        public int gridWeightBruiser = 8;
        public int attackCostLight = 3;
        public int attackCostHeavy = 5;
        public float maxSlotWaitSeconds = 2f;
        [Tooltip("Metres within which a pack mate joins a fight (AttacksOnSight species).")]
        public float assistRadius = 6f;

        [Header("Sending to clients")]
        public float relevancyRadius = 150f;
        public float viewRadius = 45f;
        public int viewBudget = 32;
    }
}
