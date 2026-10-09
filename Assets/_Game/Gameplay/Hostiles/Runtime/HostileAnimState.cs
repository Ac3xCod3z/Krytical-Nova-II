using System;
using Unity.Netcode;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// The part of a Hostile's animation that lasts (how fast it moves, whether it is in a fight, stunned or dead),
    /// in one small value the server replicates. Attacks and hit reactions are moments and travel as messages.
    /// </summary>
    public struct HostileAnimState : INetworkSerializable, IEquatable<HostileAnimState>
    {
        private const byte CombatBit = 1, StunnedBit = 2, DeadBit = 4;

        /// <summary>Movement speed in steps of 0.05 m/s, so a jittering value is not sent every tick.</summary>
        public byte SpeedSteps;
        public byte Flags;
        /// <summary>Which death animation plays, counted from 1.</summary>
        public byte DeathIndex;

        public const float SpeedStep = 0.05f;

        public float Speed
        {
            get => SpeedSteps * SpeedStep;
            set => SpeedSteps = (byte)Math.Min(255, Math.Max(0, (int)Math.Round(value / SpeedStep)));
        }

        public bool InCombat { get => (Flags & CombatBit) != 0; set => Set(CombatBit, value); }
        public bool Stunned { get => (Flags & StunnedBit) != 0; set => Set(StunnedBit, value); }
        public bool Dead { get => (Flags & DeadBit) != 0; set => Set(DeadBit, value); }

        private void Set(byte bit, bool on) => Flags = (byte)(on ? Flags | bit : Flags & ~bit);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref SpeedSteps);
            serializer.SerializeValue(ref Flags);
            serializer.SerializeValue(ref DeathIndex);
        }

        public bool Equals(HostileAnimState other) =>
            SpeedSteps == other.SpeedSteps && Flags == other.Flags && DeathIndex == other.DeathIndex;

        public override bool Equals(object obj) => obj is HostileAnimState other && Equals(other);
        public override int GetHashCode() => SpeedSteps | (Flags << 8) | (DeathIndex << 16);
    }
}
