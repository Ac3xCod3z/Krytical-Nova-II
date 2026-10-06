using System;
using Unity.Netcode;

namespace KryticalNova.Items
{
    /// <summary>
    /// One inventory cell. itemId 0 = empty. <see cref="data"/> is what belongs to this one item and travels with
    /// it wherever it goes (Pack, equipment, Vault, a trade, a Kiosk, the ground): a pet's growth and hunger today,
    /// Temper / Fusion / Tuning later. 0 means a plain item, so everything that has no state of its own is unchanged.
    /// What the bits mean is up to the kind of item (see <c>PetItemState</c>). Stacks merge only when it matches.
    /// </summary>
    [Serializable]
    public struct ItemStack : INetworkSerializable, IEquatable<ItemStack>
    {
        public ushort itemId;
        public ushort count;
        public ulong data;

        public static readonly ItemStack Empty = default;

        public ItemStack(ushort itemId, ushort count, ulong data = 0)
        {
            this.itemId = itemId;
            this.count = itemId == 0 ? (ushort)0 : count;
            this.data = itemId == 0 ? 0 : data;
        }

        public bool IsEmpty => itemId == 0 || count == 0;

        /// <summary>True when the two are the same item in the same state, so they may share a stack.</summary>
        public bool StacksWith(in ItemStack other) => itemId == other.itemId && data == other.data;

        /// <summary>The same item and state with another count; empty when the count is 0.</summary>
        public ItemStack WithCount(int newCount) => newCount > 0 ? new ItemStack(itemId, (ushort)newCount, data) : Empty;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref itemId);
            serializer.SerializeValue(ref count);
            serializer.SerializeValue(ref data);
        }

        public bool Equals(ItemStack other) => itemId == other.itemId && count == other.count && data == other.data;
        public override bool Equals(object obj) => obj is ItemStack other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(itemId, count, data);
        public override string ToString() => IsEmpty ? "<empty>" : data == 0 ? $"{itemId}x{count}" : $"{itemId}x{count}#{data:X}";
    }

    /// <summary>Equipped item IDs, one per <see cref="EquipSlot"/> (index 0 unused). Fixed-size so it serializes without allocation.</summary>
    [Serializable]
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

    /// <summary>The <see cref="ItemStack.data"/> of each equipped item, one per <see cref="EquipSlot"/>. Owner and server only.</summary>
    [Serializable]
    public struct EquipmentData : INetworkSerializable, IEquatable<EquipmentData>
    {
        public ulong weapon, armor, gloves, boots, ring, amulet, cape, pet;

        public ulong this[EquipSlot slot]
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

        public bool Equals(EquipmentData o) =>
            weapon == o.weapon && armor == o.armor && gloves == o.gloves && boots == o.boots &&
            ring == o.ring && amulet == o.amulet && cape == o.cape && pet == o.pet;

        public override bool Equals(object obj) => obj is EquipmentData o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(weapon, armor, gloves, boots, ring, amulet, cape, pet);
    }

    /// <summary>
    /// What a character wears, as whole items: the ids everyone sees (<see cref="ids"/>) and each item's own
    /// <see cref="data"/>. The inventory rules work on this, so an item keeps its state on its way in and out of a slot.
    /// </summary>
    public struct EquippedItems
    {
        public EquipmentLoadout ids;
        public EquipmentData data;

        public EquippedItems(EquipmentLoadout ids, EquipmentData data)
        {
            this.ids = ids;
            this.data = data;
        }

        /// <summary>The item in a slot (count 1), or empty. Setting an empty stack clears the slot.</summary>
        public ItemStack this[EquipSlot slot]
        {
            get => ids[slot] != 0 ? new ItemStack(ids[slot], 1, data[slot]) : ItemStack.Empty;
            set
            {
                ids[slot] = value.IsEmpty ? (ushort)0 : value.itemId;
                data[slot] = value.IsEmpty ? 0 : value.data;
            }
        }

        public bool IsEmpty(EquipSlot slot) => ids[slot] == 0;
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
