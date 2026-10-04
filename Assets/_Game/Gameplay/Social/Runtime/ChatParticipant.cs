using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>What the chat rules need to know about one connected player.</summary>
    public struct ChatParticipant
    {
        public ulong ClientId;
        public string Name;
        public FactionId Faction;
        /// <summary>Empty when the player is in no guild.</summary>
        public string Guild;
        /// <summary>0 when the player is in no party.</summary>
        public ulong Party;
        public Vector3 Position;
        /// <summary>True for the host's own player: the only one allowed on the GM channel.</summary>
        public bool IsGameMaster;
    }
}
