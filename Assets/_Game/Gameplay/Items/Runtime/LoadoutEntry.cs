using System;
using Unity.Collections;
using Unity.Netcode;

namespace KryticalNova.Items
{
    /// <summary>One saved loadout: its name and the piece it names in each equipment slot (0 = leave that slot alone).</summary>
    public struct LoadoutEntry : INetworkSerializable, IEquatable<LoadoutEntry>
    {
        public bool used;
        public FixedString32Bytes name;
        public EquipmentLoadout ids;
        public EquipmentData data;

        /// <summary>The piece saved for a slot, with the state it had then, or empty.</summary>
        public ItemStack Piece(EquipSlot slot) => ids[slot] != 0 ? new ItemStack(ids[slot], 1, data[slot]) : ItemStack.Empty;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref used);
            serializer.SerializeValue(ref name);
            serializer.SerializeValue(ref ids);
            serializer.SerializeValue(ref data);
        }

        public bool Equals(LoadoutEntry other) => used == other.used && name.Equals(other.name) && ids.Equals(other.ids) && data.Equals(other.data);
        public override bool Equals(object obj) => obj is LoadoutEntry other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(used, name, ids, data);
    }
}
