using System;
using Unity.Netcode;

namespace KryticalNova.Mounts
{
    /// <summary>Where a rider is with their mount.</summary>
    public enum RidePhase : byte
    {
        OnFoot = 0,
        /// <summary>The mount-up clip plays; the rider stays where they are.</summary>
        GettingOn = 1,
        Riding = 2,
        /// <summary>The dismount clip plays; the rider stays where they are.</summary>
        GettingOff = 3,
    }

    /// <summary>What every machine knows of a player's mounts: enough for the stats, the speed and the look.</summary>
    public struct MountNetState : INetworkSerializable, IEquatable<MountNetState>
    {
        /// <summary>The selected mount's box, 1–4; 0 = none.</summary>
        public byte selected;
        public RidePhase phase;
        /// <summary>The selected mount's whole percent of growth, 0–100.</summary>
        public byte growth;
        /// <summary>False when the selected mount is starving.</summary>
        public bool fed;
        /// <summary>The getting on or off in play is the running kind (owner, 6 Nov 2026): the Ride key was pressed while
        /// running, so the rider hops on (or launches off) without stopping.</summary>
        public bool running;
        /// <summary>A running getting-off ends running (path left to the clicked spot) rather than standing.</summary>
        public bool endsRunning;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref selected);
            serializer.SerializeValue(ref phase);
            serializer.SerializeValue(ref growth);
            serializer.SerializeValue(ref fed);
            serializer.SerializeValue(ref running);
            serializer.SerializeValue(ref endsRunning);
        }

        public bool Equals(MountNetState other) => selected == other.selected && phase == other.phase && growth == other.growth && fed == other.fed
            && running == other.running && endsRunning == other.endsRunning;
        public override bool Equals(object obj) => obj is MountNetState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(selected, (byte)phase, growth, fed, running, endsRunning);
    }

    /// <summary>The owner's own copy of all four boxes: which are owned and each one's EXP and hunger, for the Mount window.</summary>
    public struct MountBook : INetworkSerializable, IEquatable<MountBook>
    {
        public byte owned; // bit (box - 1)
        public uint xp1, xp2, xp3, xp4;
        public uint hunger1, hunger2, hunger3, hunger4;

        public bool Owns(int box) => box >= 1 && box <= MountRules.Slots && (owned & (1 << (box - 1))) != 0;

        public MountState Get(int box) => box switch
        {
            1 => new MountState(xp1, hunger1),
            2 => new MountState(xp2, hunger2),
            3 => new MountState(xp3, hunger3),
            4 => new MountState(xp4, hunger4),
            _ => default,
        };

        public void Set(int box, bool owns, in MountState state)
        {
            if (box < 1 || box > MountRules.Slots) return;
            if (owns) owned |= (byte)(1 << (box - 1));
            else owned &= (byte)~(1 << (box - 1));
            switch (box)
            {
                case 1: xp1 = state.XpUnits; hunger1 = state.HungerDeficit; break;
                case 2: xp2 = state.XpUnits; hunger2 = state.HungerDeficit; break;
                case 3: xp3 = state.XpUnits; hunger3 = state.HungerDeficit; break;
                case 4: xp4 = state.XpUnits; hunger4 = state.HungerDeficit; break;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref owned);
            serializer.SerializeValue(ref xp1); serializer.SerializeValue(ref xp2);
            serializer.SerializeValue(ref xp3); serializer.SerializeValue(ref xp4);
            serializer.SerializeValue(ref hunger1); serializer.SerializeValue(ref hunger2);
            serializer.SerializeValue(ref hunger3); serializer.SerializeValue(ref hunger4);
        }

        public bool Equals(MountBook o) => owned == o.owned && xp1 == o.xp1 && xp2 == o.xp2 && xp3 == o.xp3 && xp4 == o.xp4
            && hunger1 == o.hunger1 && hunger2 == o.hunger2 && hunger3 == o.hunger3 && hunger4 == o.hunger4;
        public override bool Equals(object obj) => obj is MountBook other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(owned, xp1, xp2, xp3, xp4, hunger1, hunger2, HashCode.Combine(hunger3, hunger4));
    }

    /// <summary>A character's mounts as they are saved (save key "mounts").</summary>
    [Serializable]
    public sealed class MountSave
    {
        public int version = 1;
        public int selected;
        /// <summary>The Mount Companion's stance (day list item 19): 0 = FREE. An added field, no migration.</summary>
        public int stance;
        public MountSaveEntry[] mounts = new MountSaveEntry[0];
    }

    [Serializable]
    public sealed class MountSaveEntry
    {
        public int id;
        public uint xp;
        public uint hunger;
        // The Mount Companion (day list item 19): added fields whose missing value (0) means full HP and not resting, so no migration.
        /// <summary>Companion HP lost, permille (0 = full), as of <see cref="hpAt"/>.</summary>
        public int hpLost;
        /// <summary>UTC seconds <see cref="hpLost"/> holds from; the regen at home counts after it.</summary>
        public long hpAt;
        /// <summary>UTC seconds a knocked-out mount rests until; 0 = not resting.</summary>
        public long restUntil;
    }
}
