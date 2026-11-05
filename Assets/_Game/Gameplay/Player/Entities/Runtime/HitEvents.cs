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
