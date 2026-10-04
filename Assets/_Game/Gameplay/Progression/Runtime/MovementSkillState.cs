using System;
using Unity.Netcode;

namespace KryticalNova.Progression
{
    /// <summary>A player's learned movement ranks and the skill it is moving with, as the server holds them.</summary>
    public struct MovementSkillState : INetworkSerializable, IEquatable<MovementSkillState>
    {
        public byte runRank;
        public byte jumpRank;
        public MovementSkillId selected;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref runRank);
            serializer.SerializeValue(ref jumpRank);
            serializer.SerializeValue(ref selected);
        }

        public bool Equals(MovementSkillState o) => runRank == o.runRank && jumpRank == o.jumpRank && selected == o.selected;
        public override bool Equals(object obj) => obj is MovementSkillState o && Equals(o);
        public override int GetHashCode() => (runRank << 16) | (jumpRank << 8) | (int)selected;
    }
}
