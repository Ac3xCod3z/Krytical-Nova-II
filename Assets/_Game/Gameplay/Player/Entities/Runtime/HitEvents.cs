using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// One landed (or evaded) strike, kept on the ATTACKER's ghost for every client that sees it (M4.4h, M4-combat.md section 15): what
    /// NGO's ResolveHit sent as DamageFeedbackRpc to the attacker and SpawnImpactVfxClientRpc to everyone. Written by the server only.
    /// </summary>
    [InternalBufferCapacity(HitEventRing.Capacity)]
    public struct HitEvent : IBufferElementData
    {
        /// <summary>The attacker's running count of hits; a client plays each number once.</summary>
        [GhostField] public uint Seq;
        [GhostField] public int TargetGhostId;
        [GhostField] public int DamageMilli;
        [GhostField] public int ReflectedMilli;
        /// <summary><see cref="HitEventRing"/> flag bits (NGO's CriticalBit .. SkillBit).</summary>
        [GhostField] public byte Flags;
        /// <summary>The combo step (1-3).</summary>
        [GhostField] public byte Step;
        /// <summary>A skill hit's label for the attacker's combat line (NGO PlayerSkillAttacks' label; M4.5e); empty for a basic strike.</summary>
        [GhostField] public Unity.Collections.FixedString64Bytes Label;
    }

    /// <summary>The attacker's last hit number (server count); the ring holds the newest <see cref="HitEventRing.Capacity"/>.</summary>
    public struct HitEventSeq : IComponentData
    {
        public uint Last;
    }

    /// <summary>The ring's rules, pure: append keeping the newest, and what is new to a client that has seen up to a number.</summary>
    public static class HitEventRing
    {
        /// <summary>Enough for a whole combo plus collateral within one snapshot interval.</summary>
        public const int Capacity = 6;

        // NGO PlayerMeleeCombat's bits (ImpactKind / ShowDamageToAttacker), the same values
        public const byte Critical = 1, Evaded = 2, Reflected = 4, Shattered = 8, Elemental = 16, Skill = 32;

        /// <summary>Server: numbers and appends <paramref name="hit"/>, dropping the oldest when full.</summary>
        public static void Append(DynamicBuffer<HitEvent> ring, ref HitEventSeq seq, HitEvent hit)
        {
            hit.Seq = ++seq.Last;
            if (ring.Length >= Capacity) ring.RemoveAt(0);
            ring.Add(hit);
        }

        /// <summary>The newest number in the ring, 0 when empty: a client meeting a ghost for the first time starts there (no replay).</summary>
        public static uint Newest(DynamicBuffer<HitEvent> ring) => ring.Length > 0 ? ring[ring.Length - 1].Seq : 0;
    }

    /// <summary>A cast the server ran for this player (M4.5c): every other client plays it (NGO TriggerSkillCastClientRpc). Server-written.</summary>
    [InternalBufferCapacity(CastEventRing.Capacity)]
    public struct CastEvent : IBufferElementData
    {
        [GhostField] public uint Seq;
        [GhostField] public Unity.Collections.FixedString64Bytes SkillId;
        [GhostField] public short Rank;
        /// <summary>The ghost it was cast at, 0 = none (a buff or an area attack).</summary>
        [GhostField] public int TargetGhostId;
    }

    /// <summary>The player's hold-to-charge as the others see it (M4.5f; NGO ChargeStartedRpc / ChargeEndedRpc). Server-written.</summary>
    public struct ChargeState : IComponentData
    {
        /// <summary>Bumped on every start or end: a client plays each once.</summary>
        [GhostField] public byte Seq;
        [GhostField] public Unity.Collections.FixedString64Bytes SkillId;
        [GhostField] public int AimGhostId;
        /// <summary>1 started, 2 ended (charged), 3 ended (let go early).</summary>
        [GhostField] public byte Phase;
    }

    /// <summary>
    /// A Squad member's Stance that reached this player (M4.5g): every client, the player's own included, plays it once (NGO
    /// SquadBuffClientRpc, Everyone). Server-written.
    /// </summary>
    [InternalBufferCapacity(SquadBuffRing.Capacity)]
    public struct SquadBuffEvent : IBufferElementData
    {
        [GhostField] public uint Seq;
        [GhostField] public Unity.Collections.FixedString64Bytes SkillId;
        /// <summary>The rank it has now (the higher of the two when it was running already).</summary>
        [GhostField] public short Rank;
        /// <summary>The giver's name (16 characters at most).</summary>
        [GhostField] public Unity.Collections.FixedString32Bytes Giver;
    }

    /// <summary>The player's last Squad buff number (server count).</summary>
    public struct SquadBuffSeq : IComponentData
    {
        public uint Last;
    }

    /// <summary>The Squad buff ring's rules, as <see cref="HitEventRing"/>'s.</summary>
    public static class SquadBuffRing
    {
        public const int Capacity = 4;

        public static void Append(DynamicBuffer<SquadBuffEvent> ring, ref SquadBuffSeq seq, SquadBuffEvent given)
        {
            given.Seq = ++seq.Last;
            if (ring.Length >= Capacity) ring.RemoveAt(0);
            ring.Add(given);
        }

        public static uint Newest(DynamicBuffer<SquadBuffEvent> ring) => ring.Length > 0 ? ring[ring.Length - 1].Seq : 0;
    }

    /// <summary>
    /// One buff running on the player as the server has it (M4.5i, owner pick A): the whole list is rewritten when it changes, and every
    /// view keeps its buff copy equal to it (BuffMirror). Server-written.
    /// </summary>
    [InternalBufferCapacity(RunningBuffs.Max)]
    public struct RunningBuff : IBufferElementData
    {
        [GhostField] public Unity.Collections.FixedString64Bytes SkillId;
        [GhostField] public short Rank;
        /// <summary>The server tick it runs out on (<c>NetworkTick.SerializedData</c>).</summary>
        [GhostField] public uint EndTick;
        [GhostField] public byte FromSquad;
        /// <summary>The Squad member who gave it (16 characters at most).</summary>
        [GhostField] public Unity.Collections.FixedString32Bytes Giver;
    }

    /// <summary>The running list's clock: seconds left as an end tick and back.</summary>
    public static class RunningBuffs
    {
        public const int Max = 16;

        /// <summary>The tick <paramref name="seconds"/> after <paramref name="now"/> (rounded up: a buff never ends early).</summary>
        public static uint EndTick(Unity.NetCode.NetworkTick now, float seconds, int tickRate)
        {
            var end = now;
            end.Add((uint)Unity.Mathematics.math.max(0, (int)Unity.Mathematics.math.ceil(seconds * tickRate)));
            return end.SerializedData;
        }

        /// <summary>Seconds from <paramref name="now"/> to <paramref name="endTick"/>; 0 or less = run out.</summary>
        public static float SecondsLeft(Unity.NetCode.NetworkTick now, uint endTick, int tickRate)
        {
            var end = new Unity.NetCode.NetworkTick { SerializedData = endTick };
            if (!end.IsValid || !now.IsValid || tickRate <= 0) return 0f;
            return end.TicksSince(now) / (float)tickRate;
        }
    }

    /// <summary>The caster's last cast number (server count).</summary>
    public struct CastEventSeq : IComponentData
    {
        public uint Last;
    }

    /// <summary>The cast ring's rules, as <see cref="HitEventRing"/>'s.</summary>
    public static class CastEventRing
    {
        public const int Capacity = 4;

        public static void Append(DynamicBuffer<CastEvent> ring, ref CastEventSeq seq, CastEvent cast)
        {
            cast.Seq = ++seq.Last;
            if (ring.Length >= Capacity) ring.RemoveAt(0);
            ring.Add(cast);
        }

        public static uint Newest(DynamicBuffer<CastEvent> ring) => ring.Length > 0 ? ring[ring.Length - 1].Seq : 0;
    }
}
