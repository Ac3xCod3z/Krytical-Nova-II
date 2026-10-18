using System;
using Unity.Netcode;

namespace KryticalNova.Economy
{
    /// <summary>How many of one item a character has bought from the vendors this week, of an item that has a weekly limit.</summary>
    public struct VendorBought : INetworkSerializable, IEquatable<VendorBought>
    {
        public ushort itemId;
        public ushort count;

        public VendorBought(ushort itemId, ushort count)
        {
            this.itemId = itemId;
            this.count = count;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref itemId);
            serializer.SerializeValue(ref count);
        }

        public bool Equals(VendorBought other) => itemId == other.itemId && count == other.count;
        public override bool Equals(object obj) => obj is VendorBought other && Equals(other);
        public override int GetHashCode() => itemId * 397 ^ count;
    }
}
