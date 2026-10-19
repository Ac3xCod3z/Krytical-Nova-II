using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace KryticalNova.Social
{
    public enum CrewRank : byte
    {
        Member = 0,
        /// <summary>Invites, expels Members and writes the announcement.</summary>
        Officer = 1,
        /// <summary>One per Crew. Everything an Officer does, and ranks, titles, the symbol, handing over and disbanding.</summary>
        Leader = 2
    }

    [Serializable]
    public sealed class CrewMember
    {
        public string characterId;
        public string name;
        public int rank;
        /// <summary>The Crew Title the Leader gave this member; empty for none.</summary>
        public string title = string.Empty;
        public int level = 1;
        public int weaponClass;
        public long joinedUtc;
        public long lastSeenUtc;

        public CrewRank Rank => (CrewRank)rank;
    }

    /// <summary>A Crew as the server keeps it, whether its members are online or not.</summary>
    [Serializable]
    public sealed class Crew
    {
        public string id;
        public string name;
        public int faction;
        public int symbol;
        /// <summary>Which of <see cref="CrewSettings.symbolColours"/> the symbol is drawn in. The Leader picks it.</summary>
        public int symbolColour;
        public string announcement = string.Empty;
        public long foundedUtc;
        public int level = 1;
        public List<CrewMember> members = new List<CrewMember>();
    }

    /// <summary>Every Crew on the server: the world document "crews".</summary>
    [Serializable]
    public sealed class CrewsWorld : KryticalNova.Core.IWorldEntries
    {
        public List<Crew> crews = new List<Crew>();

        // Online phase 4, milestone 4: kept one entry at a time (ServerSave), keyed by id.
        public void CollectEntries(List<KeyValuePair<string, string>> into)
        {
            foreach (Crew entry in crews)
                if (entry != null) into.Add(new KeyValuePair<string, string>(entry.id, UnityEngine.JsonUtility.ToJson(entry)));
        }

        public void RestoreEntries(IReadOnlyList<string> texts)
        {
            crews = new List<Crew>(texts.Count);
            foreach (string text in texts) crews.Add(UnityEngine.JsonUtility.FromJson<Crew>(text));
        }
    }

    /// <summary>What everyone can see of a player's Crew: its name and symbol, and the player's rank and title in it.</summary>
    public struct CrewTag : INetworkSerializable, IEquatable<CrewTag>
    {
        public FixedString32Bytes crew;
        public FixedString32Bytes title;
        public CrewRank rank;
        public byte symbol;
        public byte colour;

        public bool InCrew => crew.Length > 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref crew);
            serializer.SerializeValue(ref title);
            serializer.SerializeValue(ref rank);
            serializer.SerializeValue(ref symbol);
            serializer.SerializeValue(ref colour);
        }

        public bool Equals(CrewTag other) =>
            crew.Equals(other.crew) && title.Equals(other.title) && rank == other.rank && symbol == other.symbol && colour == other.colour;
    }

    /// <summary>What a member is told about their Crew, beside the roster.</summary>
    public struct CrewHeader : INetworkSerializable
    {
        public bool exists;
        public FixedString32Bytes name;
        public byte symbol;
        public byte colour;
        public FixedString512Bytes announcement;
        public ushort memberCap;
        public CrewRank myRank;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref exists);
            serializer.SerializeValue(ref name);
            serializer.SerializeValue(ref symbol);
            serializer.SerializeValue(ref colour);
            serializer.SerializeValue(ref announcement);
            serializer.SerializeValue(ref memberCap);
            serializer.SerializeValue(ref myRank);
        }
    }

    /// <summary>A line of the Crew window's roster, as the server sends it.</summary>
    public struct CrewMemberView : INetworkSerializable
    {
        public FixedString64Bytes id;
        public FixedString64Bytes name;
        public CrewRank rank;
        public FixedString32Bytes title;
        public ushort level;
        public byte weaponClass;
        public bool online;
        public long lastSeenUtc;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref id);
            serializer.SerializeValue(ref name);
            serializer.SerializeValue(ref rank);
            serializer.SerializeValue(ref title);
            serializer.SerializeValue(ref level);
            serializer.SerializeValue(ref weaponClass);
            serializer.SerializeValue(ref online);
            serializer.SerializeValue(ref lastSeenUtc);
        }
    }
}
