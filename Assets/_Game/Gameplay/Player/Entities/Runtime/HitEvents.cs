using System.Collections.Generic;
using Unity.Collections;
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
        /// <summary>
        /// A skill hit's label for the attacker's combat line (NGO PlayerSkillAttacks' label; M4.5e); empty for a basic strike. 32 bytes: the
        /// longest label today is 25 ("19-character name (3/3)"); a longer one is cut on a whole character (<see cref="HitEventRing.FitLabel"/>).
        /// </summary>
        [GhostField] public Unity.Collections.FixedString32Bytes Label;
    }

    /// <summary>The attacker's last hit number (server count); the ring holds the newest <see cref="HitEventRing.Capacity"/>.</summary>
    public struct HitEventSeq : IComponentData
    {
        public uint Last;
    }

    /// <summary>The ring's rules, pure: append keeping the newest, and what is new to a client that has seen up to a number.</summary>
    public static class HitEventRing
    {
        /// <summary>
        /// The newest hits on the ghost (owner, 5 Nov 2026: an area skill hits every body round the player, no cap): the server releases at
        /// most <see cref="PerTick"/> a tick from its queue, so each stays <see cref="Capacity"/> / <see cref="PerTick"/> ticks and a crowd's
        /// hits come over a few ticks, none rolled out unseen. The attacker's numbers go by a reliable message (HitNumberRpc), not this ring.
        /// </summary>
        public const int Capacity = 16;
        /// <summary>Hits released into the ring per server tick.</summary>
        public const int PerTick = 4;

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

        /// <summary>Server, once a tick: moves at most <paramref name="max"/> waiting hits, oldest first, into the ring. Returns how many.</summary>
        public static int Drain(Queue<HitEvent> waiting, DynamicBuffer<HitEvent> ring, ref HitEventSeq seq, int max = PerTick)
        {
            int moved = 0;
            while (moved < max && waiting != null && waiting.Count > 0)
            {
                Append(ring, ref seq, waiting.Dequeue());
                moved++;
            }
            return moved;
        }

        /// <summary>A label in 32 bytes: a longer one keeps its start, cut on a whole character (never half of one), so it is always valid text.</summary>
        public static Unity.Collections.FixedString32Bytes FitLabel(string text)
        {
            var fitted = new Unity.Collections.FixedString32Bytes();
            if (string.IsNullOrEmpty(text)) return fitted;
            for (int i = 0; i < text.Length; i++)
            {
                int code;
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { code = char.ConvertToUtf32(text[i], text[i + 1]); i++; }
                else if (char.IsSurrogate(text[i])) continue;   // a lone half: no character to keep
                else code = text[i];
                if (fitted.Append(new Unity.Collections.Unicode.Rune(code)) != Unity.Collections.FormatError.None) break;
            }
            return fitted;
        }
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
        /// <summary>Build 17 item 9.2: an absorption shield's HP left (whole), -1 for a buff that is no shield.</summary>
        [GhostField] public int Shield;
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

    /// <summary>A skill hit the server made (M4 step 7): every view plays its VFX once (NGO SkillHitVfxClientRpc, Everyone). Server-written.</summary>
    [InternalBufferCapacity(SkillHitRing.Capacity)]
    public struct SkillHitEvent : IBufferElementData
    {
        [GhostField] public uint Seq;
        [GhostField] public Unity.Collections.FixedString64Bytes SkillId;
        [GhostField] public int XMm, YMm, ZMm;
    }

    /// <summary>The attacker's last skill hit number (server count).</summary>
    public struct SkillHitSeq : IComponentData
    {
        public uint Last;
    }

    /// <summary>The skill hit ring's rules, as <see cref="HitEventRing"/>'s.</summary>
    public static class SkillHitRing
    {
        public const int Capacity = 6;

        public static void Append(DynamicBuffer<SkillHitEvent> ring, ref SkillHitSeq seq, SkillHitEvent hit)
        {
            hit.Seq = ++seq.Last;
            if (ring.Length >= Capacity) ring.RemoveAt(0);
            ring.Add(hit);
        }

        public static uint Newest(DynamicBuffer<SkillHitEvent> ring) => ring.Length > 0 ? ring[ring.Length - 1].Seq : 0;
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

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// A dodge the server decided for this player (build 17 item 11.2): an evaded Hostile attack read high or low, and after the set number in
    /// a row the counter-attack. A one-off moment, so a ring entry with its tick: every view, the player's own included, plays each once (the
    /// clip is presentation; the counter's damage is a server hit on the attacker's HitEvent ring like any other). Server-written.
    /// </summary>
    [InternalBufferCapacity(DodgeRing.Capacity)]
    public struct DodgeEvent : IBufferElementData
    {
        [GhostField] public uint Seq;
        /// <summary>The server tick of the dodge (<c>NetworkTick.SerializedData</c>).</summary>
        [GhostField] public uint Tick;
        /// <summary><c>KryticalNova.Combat.DodgeMove</c>: 1 jump over, 2 sidestep right, 3 sidestep left.</summary>
        [GhostField] public byte Move;
        /// <summary>1 = this dodge ends in the counter-attack.</summary>
        [GhostField] public byte Counter;
    }

    /// <summary>The player's last dodge number (server count).</summary>
    public struct DodgeSeq : IComponentData
    {
        public uint Last;
    }

    /// <summary>The dodge ring's rules, as <see cref="HitEventRing"/>'s.</summary>
    public static class DodgeRing
    {
        public const int Capacity = 4;

        public static void Append(DynamicBuffer<DodgeEvent> ring, ref DodgeSeq seq, DodgeEvent dodge)
        {
            dodge.Seq = ++seq.Last;
            if (ring.Length >= Capacity) ring.RemoveAt(0);
            ring.Add(dodge);
        }

        public static uint Newest(DynamicBuffer<DodgeEvent> ring) => ring.Length > 0 ? ring[ring.Length - 1].Seq : 0;
    }
}
