using Unity.Entities;
using Unity.Mathematics;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>The Hostiles mind's numbers in ticks (singleton), made from <see cref="HostileBrainSettings"/>: the asset is the only place they are set.</summary>
    public struct HostileBrainConfig : IComponentData
    {
        public float NoticeSecondsDefault;
        public float ReachSlack;
        public float CommitFraction;
        public float FacingDegrees;
        public uint MinVisibleWarningTicks;
        public uint WarningLatencyTicks;
        public uint AttackCooldownTicks;
        public float ThreatSwitchMelee, ThreatSwitchRanged;
        public uint ThreatDecayAfterTicks;
        public float ThreatDecayPerTick;
        public float HealThreatFactor, TauntBonus;
        public uint GiveUpTicks;
        public uint PatrolPauseMinTicks, PatrolPauseMaxTicks;
        public float AssistRadius;
        // the stage manager (M6.5)
        public int AttackCapacity, AttackCostLight, AttackCostHeavy;
        // crowding by body size (owner pick A, 7 Nov 2026): HostileRingRules
        public float SlotStandGap, SlotRingFill, BruiserArcFactor, PlayerRadius;
        public float WaitRingMin, WaitRingMax;

        public static HostileBrainConfig From(HostileBrainSettings s, int tickRate)
        {
            uint Ticks(float seconds) => (uint)math.max(0, (int)math.ceil(seconds * tickRate - 1e-4f));
            return new HostileBrainConfig
            {
                NoticeSecondsDefault = s.noticeSecondsDefault,
                ReachSlack = s.reachSlack,
                CommitFraction = s.commitFraction,
                FacingDegrees = s.facingDegrees,
                MinVisibleWarningTicks = Ticks(s.minVisibleWarningSeconds),
                WarningLatencyTicks = Ticks(s.warningLatencySeconds),
                AttackCooldownTicks = Ticks(s.attackCooldownSeconds),
                ThreatSwitchMelee = s.threatSwitchMelee,
                ThreatSwitchRanged = s.threatSwitchRanged,
                ThreatDecayAfterTicks = Ticks(s.threatDecayAfterSeconds),
                ThreatDecayPerTick = s.threatDecayPerSecond / math.max(1, tickRate),
                HealThreatFactor = s.healThreatFactor,
                TauntBonus = s.tauntBonus,
                GiveUpTicks = Ticks(s.giveUpSeconds),
                PatrolPauseMinTicks = Ticks(s.patrolPauseSeconds.x),
                PatrolPauseMaxTicks = math.max(Ticks(s.patrolPauseSeconds.x), Ticks(s.patrolPauseSeconds.y)),
                AssistRadius = s.assistRadius,
                AttackCapacity = s.attackCapacity,
                SlotStandGap = math.max(0f, s.slotStandGap),
                SlotRingFill = math.saturate(s.slotRingFill),
                BruiserArcFactor = math.max(1f, s.bruiserArcFactor),
                PlayerRadius = KryticalNova.Player.Entities.HitProxy.Player.RadiusMm * 0.001f,   // the player's hit capsule (0.5 m)
                AttackCostLight = s.attackCostLight,
                AttackCostHeavy = s.attackCostHeavy,
                WaitRingMin = s.waitRingRadius.x,
                WaitRingMax = math.max(s.waitRingRadius.x, s.waitRingRadius.y),
            };
        }
    }
}
