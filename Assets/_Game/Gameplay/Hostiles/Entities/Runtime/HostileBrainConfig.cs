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
        public uint AttackCooldownTicks;
        public float ThreatSwitchMelee, ThreatSwitchRanged;
        public uint ThreatDecayAfterTicks;
        public float ThreatDecayPerTick;
        public float HealThreatFactor, TauntBonus;
        public uint GiveUpTicks;
        public float AssistRadius;

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
                AttackCooldownTicks = Ticks(s.attackCooldownSeconds),
                ThreatSwitchMelee = s.threatSwitchMelee,
                ThreatSwitchRanged = s.threatSwitchRanged,
                ThreatDecayAfterTicks = Ticks(s.threatDecayAfterSeconds),
                ThreatDecayPerTick = s.threatDecayPerSecond / math.max(1, tickRate),
                HealThreatFactor = s.healThreatFactor,
                TauntBonus = s.tauntBonus,
                GiveUpTicks = Ticks(s.giveUpSeconds),
                AssistRadius = s.assistRadius,
            };
        }
    }
}
