using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace KryticalNova.Social
{
    /// <summary>One Sponsor and one Recruit, as the server keeps them. Names and levels are as last seen.</summary>
    [Serializable]
    public sealed class SponsorLink
    {
        public string sponsorId;
        public string sponsorName;
        public int sponsorLevel;
        public string recruitId;
        public string recruitName;
        public int recruitLevel;
        public long sinceUtc;
    }

    /// <summary>Every Sponsor link on the server: a world document, so a link outlives both of them logging out.</summary>
    [Serializable]
    public sealed class SponsorsWorld : KryticalNova.Core.IWorldEntries
    {
        public List<SponsorLink> links = new List<SponsorLink>();

        // Online phase 4, milestone 4: kept one entry at a time (ServerSave), keyed by recruit and sponsor.
        public void CollectEntries(List<KeyValuePair<string, string>> into)
        {
            foreach (SponsorLink entry in links)
                if (entry != null) into.Add(new KeyValuePair<string, string>(entry.recruitId + "-" + entry.sponsorId, UnityEngine.JsonUtility.ToJson(entry)));
        }

        public void RestoreEntries(IReadOnlyList<string> texts)
        {
            links = new List<SponsorLink>(texts.Count);
            foreach (string text in texts) links.Add(UnityEngine.JsonUtility.FromJson<SponsorLink>(text));
        }
    }

    /// <summary>One line of a player's own Sponsor window: the character at the other end of a link.</summary>
    public struct SponsorLinkView : INetworkSerializable
    {
        public FixedString64Bytes characterId;
        public FixedString32Bytes name;
        public int level;
        /// <summary>True when the character named here sponsors the owner; false when it is the owner's Recruit.</summary>
        public bool isSponsor;
        public bool online;
        /// <summary>Close enough, right now, for the bonus and the points.</summary>
        public bool near;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref characterId);
            serializer.SerializeValue(ref name);
            serializer.SerializeValue(ref level);
            serializer.SerializeValue(ref isSponsor);
            serializer.SerializeValue(ref online);
            serializer.SerializeValue(ref near);
        }
    }
}
