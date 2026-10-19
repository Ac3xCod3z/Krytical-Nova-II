using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace KryticalNova.Social
{
    /// <summary>Somebody in a character's contact book: a contact, a pending request, or a blocked character.</summary>
    [Serializable]
    public sealed class ContactEntry
    {
        public string characterId;
        /// <summary>The name they had when last seen by this book.</summary>
        public string name;
        public bool favourite;
        /// <summary>The owner's private note about this contact.</summary>
        public string note = string.Empty;
        public long sinceUtc;
    }

    /// <summary>
    /// One character's social record, kept by the server whether the character is online or not: who they are (so
    /// others can find and list them), their contacts, the requests waiting either way, and who they block.
    /// </summary>
    [Serializable]
    public sealed class ContactBook
    {
        public string characterId;
        public string name;
        public int faction;
        public int level = 1;
        public int weaponClass;
        public long lastSeenUtc;
        public List<ContactEntry> contacts = new List<ContactEntry>();
        /// <summary>Requests from others, waiting for this character's answer.</summary>
        public List<ContactEntry> incoming = new List<ContactEntry>();
        /// <summary>Requests this character sent and that have not been answered.</summary>
        public List<ContactEntry> outgoing = new List<ContactEntry>();
        public List<ContactEntry> blocked = new List<ContactEntry>();
    }

    /// <summary>Every character's book: the world document "contacts".</summary>
    [Serializable]
    public sealed class ContactsWorld : KryticalNova.Core.IWorldEntries
    {
        public List<ContactBook> books = new List<ContactBook>();

        // Online phase 4, milestone 4: kept one entry at a time (ServerSave), keyed by characterId.
        public void CollectEntries(List<KeyValuePair<string, string>> into)
        {
            foreach (ContactBook entry in books)
                if (entry != null) into.Add(new KeyValuePair<string, string>(entry.characterId, UnityEngine.JsonUtility.ToJson(entry)));
        }

        public void RestoreEntries(IReadOnlyList<string> texts)
        {
            books = new List<ContactBook>(texts.Count);
            foreach (string text in texts) books.Add(UnityEngine.JsonUtility.FromJson<ContactBook>(text));
        }
    }

    public enum ContactKind : byte
    {
        Contact = 0,
        /// <summary>They asked; the owner has not answered.</summary>
        Incoming = 1,
        /// <summary>The owner asked; they have not answered.</summary>
        Outgoing = 2,
        Blocked = 3
    }

    /// <summary>A line of the owner's Contacts window, as the server sends it.</summary>
    public struct ContactView : INetworkSerializable
    {
        public ContactKind kind;
        public FixedString64Bytes id;
        public FixedString64Bytes name;
        public bool online;
        public ushort level;
        public byte faction;
        public byte weaponClass;
        public bool favourite;
        public FixedString128Bytes note;
        public long lastSeenUtc;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref kind);
            serializer.SerializeValue(ref id);
            serializer.SerializeValue(ref name);
            serializer.SerializeValue(ref online);
            serializer.SerializeValue(ref level);
            serializer.SerializeValue(ref faction);
            serializer.SerializeValue(ref weaponClass);
            serializer.SerializeValue(ref favourite);
            serializer.SerializeValue(ref note);
            serializer.SerializeValue(ref lastSeenUtc);
        }
    }
}
