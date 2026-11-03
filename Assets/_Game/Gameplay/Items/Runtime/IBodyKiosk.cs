using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>One listing of a Kiosk for the entity netcode (NGO's <c>KioskListing</c>): the stack on the shelf and the price of one.</summary>
    public struct BodyKioskListing
    {
        public ItemStack stack;
        public long unitPrice;

        public BodyKioskListing(ItemStack stack, long unitPrice)
        {
            this.stack = stack;
            this.unitPrice = unitPrice;
        }
    }

    /// <summary>What the owner is told about their own Kiosk (NGO's <c>KioskOwnerView</c> and its shelf), for the entity netcode.</summary>
    public struct BodyKioskView
    {
        public bool exists, open;
        public string shopName;
        public long till, expiresUtc, soldCreds;
        public int soldCount;
        public BodyKioskListing[] shelf;
    }

    /// <summary>
    /// A player body's side of the Kiosks, for the entity netcode (M7.6 + 8.2.4): the network runs its owner's requests on the server body,
    /// shows the owner's own Kiosk on its view, and makes the client's stand views from the body's stand prefab. Here, below Economy, so the
    /// network need not know Economy (as <see cref="IBodyStorage"/>).
    /// </summary>
    public interface IBodyKiosk
    {
        void RunOpen(string shopName, KryticalNova.Core.BodyKioskOffer[] offers, int tier);
        void RunClose();
        void RunCollect();
        void RunBuy(uint standId, int index, BodyKioskListing expected, int count);
        void MirrorView(in BodyKioskView view);

        /// <summary>Client: a stand view (not network-spawned) of the server's stand with this id, from this body's stand prefab.</summary>
        IKioskStand ShowStandView(uint serverId, Vector3 position, float yaw);
    }

    /// <summary>A Kiosk stand as the entity netcode sees it: on the server the one it keeps; on a client the view it shows.</summary>
    public interface IKioskStand
    {
        uint ServerId { get; }
        /// <summary>Goes up whenever the sign or the shelf changes (the server's stands).</summary>
        int Version { get; }
        Vector3 Position { get; }
        float Yaw { get; }
        string ShopName { get; }
        string OwnerName { get; }
        byte FactionByte { get; }
        long ExpiresUtc { get; }
        int ListingCount { get; }
        BodyKioskListing Listing(int index);

        /// <summary>Client view: shows the server's sign and shelf.</summary>
        void ApplyView(string shopName, string ownerName, byte faction, long expiresUtc, BodyKioskListing[] shelf);

        /// <summary>Client view: the server's stand is gone.</summary>
        void RemoveView();
    }

    /// <summary>The stands the entity netcode's server keeps (filled by the stands themselves).</summary>
    public static class KioskStands
    {
        private static readonly List<IKioskStand> ServerKept = new List<IKioskStand>(16);

        public static IReadOnlyList<IKioskStand> Server => ServerKept;

        public static void Add(IKioskStand stand) { if (stand != null && !ServerKept.Contains(stand)) ServerKept.Add(stand); }
        public static void Remove(IKioskStand stand) => ServerKept.Remove(stand);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ServerKept.Clear();
    }

    /// <summary>Server: how a server body's own Kiosk reaches its player (NGO's owner ViewRpc).</summary>
    public interface IKioskOwnerLink
    {
        void View(Component part, in BodyKioskView view);
    }

    /// <summary>The server's <see cref="IKioskOwnerLink"/>; null where there is no entity netcode server.</summary>
    public static class KioskOwnerLink
    {
        public static IKioskOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
