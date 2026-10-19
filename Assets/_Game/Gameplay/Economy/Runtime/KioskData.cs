using System;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Items;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>Something for sale in a Kiosk: the item (with its own state), how many are left, and the price of one.</summary>
    [Serializable]
    public struct KioskListing : INetworkSerializable, IEquatable<KioskListing>
    {
        public ItemStack stack;
        public long unitPrice;

        public KioskListing(ItemStack stack, long unitPrice)
        {
            this.stack = stack;
            this.unitPrice = unitPrice;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            stack.NetworkSerialize(serializer);
            serializer.SerializeValue(ref unitPrice);
        }

        public bool Equals(KioskListing other) => stack.Equals(other.stack) && unitPrice == other.unitPrice;
        public override bool Equals(object obj) => obj is KioskListing other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(stack, unitPrice);
    }

    /// <summary>What a player asks to list when opening a Kiosk: so many from a Pack cell, at a price each.</summary>
    public struct KioskOffer : INetworkSerializable
    {
        public short cell;
        public ushort count;
        public long unitPrice;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref cell);
            serializer.SerializeValue(ref count);
            serializer.SerializeValue(ref unitPrice);
        }
    }

    /// <summary>A sale the owner has not been told about yet (it happened while they were away).</summary>
    [Serializable]
    public sealed class KioskSale
    {
        public ushort itemId;
        public int count;
        /// <summary>What reached the till, after tax.</summary>
        public long creds;
        public string buyer;
        public long utc;
    }

    /// <summary>
    /// One character's Kiosk as the server keeps it. While <see cref="open"/> a stand-in sells for the owner in the
    /// world, whether the owner is online or not. Closed or expired, it only holds what is left (unsold items and
    /// the till) until the owner collects it.
    /// </summary>
    [Serializable]
    public sealed class KioskRecord
    {
        public string ownerCharacterId;
        public string ownerAccountId;
        public string ownerName;
        public int faction;
        public string shopName;
        /// <summary>The scene the stand stands in.</summary>
        public string scene;
        public Vector3 position;
        public float yaw;
        public long openedUtc;
        /// <summary>Wall-clock moment (UTC ticks) the Kiosk closes by itself.</summary>
        public long expiresUtc;
        public bool open;
        public List<KioskListing> listings = new List<KioskListing>();
        /// <summary>Creds from sales, after tax, waiting for the owner.</summary>
        public long till;
        public List<KioskSale> unseenSales = new List<KioskSale>();
        /// <summary>What the stand-in looks like: the owner, as they were when they opened it.</summary>
        public CharacterRecipe look;
        public int soldCount;
        public long soldCreds;
    }

    /// <summary>Every Kiosk on the server: the world document "kiosks".</summary>
    [Serializable]
    public sealed class KioskWorld : KryticalNova.Core.IWorldEntries
    {
        public List<KioskRecord> kiosks = new List<KioskRecord>();

        // Online phase 4, milestone 4: kept one entry at a time (ServerSave), keyed by ownerCharacterId.
        public void CollectEntries(List<KeyValuePair<string, string>> into)
        {
            foreach (KioskRecord entry in kiosks)
                if (entry != null) into.Add(new KeyValuePair<string, string>(entry.ownerCharacterId, UnityEngine.JsonUtility.ToJson(entry)));
        }

        public void RestoreEntries(IReadOnlyList<string> texts)
        {
            kiosks = new List<KioskRecord>(texts.Count);
            foreach (string text in texts) kiosks.Add(UnityEngine.JsonUtility.FromJson<KioskRecord>(text));
        }
    }
}
