using System;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>A colour the player picked for one channel (hair, eyes, glow...), applied over the base palette.</summary>
    [Serializable]
    public struct ColorOverride : IEquatable<ColorOverride>
    {
        public byte channel;
        public Color32 color;

        public ColorOverride(byte channel, Color32 color)
        {
            this.channel = channel;
            this.color = color;
        }

        public bool Equals(ColorOverride other) =>
            channel == other.channel && color.r == other.color.r && color.g == other.color.g
            && color.b == other.color.b && color.a == other.color.a;

        public override bool Equals(object obj) => obj is ColorOverride other && Equals(other);
        public override int GetHashCode() => (channel << 24) ^ (color.r << 16) ^ (color.g << 8) ^ color.b;
    }
}
