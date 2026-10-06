using System;
using Unity.Netcode;

namespace KryticalNova.Progression
{
    /// <summary>A player's whole progression as the server holds it. Small enough to resend on every change.</summary>
    [Serializable]
    public struct ProgressionState : INetworkSerializable, IEquatable<ProgressionState>
    {
        public int level;
        public int experience;
        public int statPoints;
        public int skillPoints;
        public int strength, vitality, agility, spirit;
        /// <summary>Renown: what Tuning (and later the Herald) is paid in. Nothing earns it yet.</summary>
        public int renown;

        public int this[CharacterAttribute attribute]
        {
            get
            {
                switch (attribute)
                {
                    case CharacterAttribute.Strength: return strength;
                    case CharacterAttribute.Vitality: return vitality;
                    case CharacterAttribute.Agility: return agility;
                    case CharacterAttribute.Spirit: return spirit;
                    default: return 0;
                }
            }
            set
            {
                switch (attribute)
                {
                    case CharacterAttribute.Strength: strength = value; break;
                    case CharacterAttribute.Vitality: vitality = value; break;
                    case CharacterAttribute.Agility: agility = value; break;
                    case CharacterAttribute.Spirit: spirit = value; break;
                }
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref level);
            serializer.SerializeValue(ref experience);
            serializer.SerializeValue(ref statPoints);
            serializer.SerializeValue(ref skillPoints);
            serializer.SerializeValue(ref strength);
            serializer.SerializeValue(ref vitality);
            serializer.SerializeValue(ref agility);
            serializer.SerializeValue(ref spirit);
            serializer.SerializeValue(ref renown);
        }

        public bool Equals(ProgressionState o) =>
            level == o.level && experience == o.experience && statPoints == o.statPoints && skillPoints == o.skillPoints &&
            strength == o.strength && vitality == o.vitality && agility == o.agility && spirit == o.spirit && renown == o.renown;

        public override bool Equals(object obj) => obj is ProgressionState o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(level, experience, statPoints, skillPoints, HashCode.Combine(strength, vitality, agility, spirit), renown);
    }
}
