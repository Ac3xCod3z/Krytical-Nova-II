using System;

namespace KryticalNova.Core
{
    /// <summary>
    /// Who a connected player is to the server, for as long as that connection lives: a number the server mints once per
    /// connection and never gives out again (DOTS migration seam; a transport's own connection id can be reused by the
    /// next player, this cannot). Anything that holds on to a player across an async gap (a save callback, a key fetch,
    /// a delayed disconnect) captures a PlayerKey and asks <see cref="PlayerKeys"/> whether it is still alive.
    /// Plain C#: no Netcode type. <see cref="None"/> (zero) is never minted.
    /// </summary>
    public readonly struct PlayerKey : IEquatable<PlayerKey>, IComparable<PlayerKey>
    {
        public static readonly PlayerKey None = default;

        public readonly ulong Value;

        public PlayerKey(ulong value) => Value = value;

        public bool IsValid => Value != 0;

        public bool Equals(PlayerKey other) => Value == other.Value;
        public override bool Equals(object obj) => obj is PlayerKey other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(PlayerKey other) => Value.CompareTo(other.Value);

        public static bool operator ==(PlayerKey a, PlayerKey b) => a.Value == b.Value;
        public static bool operator !=(PlayerKey a, PlayerKey b) => a.Value != b.Value;

        public override string ToString() => IsValid ? "P" + Value : "P-none";
    }
}
