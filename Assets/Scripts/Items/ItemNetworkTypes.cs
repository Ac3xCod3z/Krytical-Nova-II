using System;
using Unity.Netcode;

namespace KryticalNova.Items
{
    /// <summary>One inventory cell. itemId 0 = empty. Room to grow (enhance level, refine rolls) without changing callers.</summary>
    public struct ItemStack : INetworkSerializable, IEquatable<ItemStack>
    {
        public ushort itemId;
        public ushort count;

        public static readonly ItemStack Empty = default;

        public ItemStack(ushort itemId, ushort count)
        {
            this.itemId = itemId;
            this.count = itemId == 0 ? (ushort)0 : count;
        }

        public bool IsEmpty => itemId == 0 || count == 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref itemId);
            serializer.SerializeValue(ref count);
        }

        public bool Equals(ItemStack other) => itemId == other.itemId && count == other.count;
        public override bool Equals(object obj) => obj is ItemStack other && Equals(other);
        public override int GetHashCode() => (itemId << 16) | count;
        public override string ToString() => IsEmpty ? "<empty>" : $"{itemId}x{count}";
    }

    /// <summary>Equipped item IDs, one per <see cref="EquipSlot"/> (index 0 unused). Fixed-size so it serializes without allocation.</summary>
    public struct EquipmentLoadout : INetworkSerializable, IEquatable<EquipmentLoadout>
    {
        public ushort weapon, armor, gloves, boots, ring, amulet, cape, pet;

        public ushort this[EquipSlot slot]
        {
            get
            {
                switch (slot)
                {
                    case EquipSlot.Weapon: return weapon;
                    case EquipSlot.Armor: return armor;
                    case EquipSlot.Gloves: return gloves;
                    case EquipSlot.Boots: return boots;
                    case EquipSlot.Ring: return ring;
                    case EquipSlot.Amulet: return amulet;
                    case EquipSlot.Cape: return cape;
                    case EquipSlot.Pet: return pet;
                    default: return 0;
                }
            }
            set
            {
                switch (slot)
                {
                    case EquipSlot.Weapon: weapon = value; break;
                    case EquipSlot.Armor: armor = value; break;
                    case EquipSlot.Gloves: gloves = value; break;
                    case EquipSlot.Boots: boots = value; break;
                    case EquipSlot.Ring: ring = value; break;
                    case EquipSlot.Amulet: amulet = value; break;
                    case EquipSlot.Cape: cape = value; break;
                    case EquipSlot.Pet: pet = value; break;
                }
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref weapon);
            serializer.SerializeValue(ref armor);
            serializer.SerializeValue(ref gloves);
            serializer.SerializeValue(ref boots);
            serializer.SerializeValue(ref ring);
            serializer.SerializeValue(ref amulet);
            serializer.SerializeValue(ref cape);
            serializer.SerializeValue(ref pet);
        }

        public bool Equals(EquipmentLoadout o) =>
            weapon == o.weapon && armor == o.armor && gloves == o.gloves && boots == o.boots &&
            ring == o.ring && amulet == o.amulet && cape == o.cape && pet == o.pet;

        public override bool Equals(object obj) => obj is EquipmentLoadout o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(weapon, armor, gloves, boots, ring, amulet, cape, pet);
    }

    public enum ItemContainer : byte
    {
        Bag = 0,
        Equipment = 1
    }

    /// <summary>A cell in the bag (index = cell) or an equipment slot (index = (byte)EquipSlot).</summary>
    public struct ItemLocation : INetworkSerializable, IEquatable<ItemLocation>
    {
        public ItemContainer container;
        public byte index;

        public static ItemLocation Bag(int cell) => new ItemLocation { container = ItemContainer.Bag, index = (byte)cell };
        public static ItemLocation Equip(EquipSlot slot) => new ItemLocation { container = ItemContainer.Equipment, index = (byte)slot };

        public bool IsBag => container == ItemContainer.Bag;
        public EquipSlot Slot => (EquipSlot)index;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref container);
            serializer.SerializeValue(ref index);
        }

        public bool Equals(ItemLocation other) => container == other.container && index == other.index;
        public override bool Equals(object obj) => obj is ItemLocation other && Equals(other);
        public override int GetHashCode() => ((int)container << 8) | index;
        public override string ToString() => IsBag ? $"Bag[{index}]" : $"Equip[{Slot}]";
    }
}
