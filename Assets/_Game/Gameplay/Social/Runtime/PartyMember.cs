using Unity.Netcode;

namespace KryticalNova.Social
{
    /// <summary>One line of the party roster the server sends each member. The first entry of a roster is the leader.</summary>
    public struct PartyMember : INetworkSerializable
    {
        /// <summary>The member's player object, to read their health while they are in sight.</summary>
        public ulong ObjectId;
        public string Name;
        /// <summary>Their level when the roster was sent.</summary>
        public int Level;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ObjectId);
            if (serializer.IsWriter && Name == null) Name = string.Empty;
            serializer.SerializeValue(ref Name);
            serializer.SerializeValue(ref Level);
        }
    }
}
