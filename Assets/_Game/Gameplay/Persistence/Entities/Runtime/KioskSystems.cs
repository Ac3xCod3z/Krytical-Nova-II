using System;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>One stack the owner puts up in a Kiosk (sent before its <see cref="KioskOpenRpc"/>, same <see cref="Seq"/>; M7.6).</summary>
    public struct KioskOfferRpc : IRpcCommand
    {
        public uint Seq;
        public short Cell;
        public ushort Count;
        public long Price;
    }

    /// <summary>The owner opens a Kiosk (NGO's <c>PlayerKiosk.OpenRpc</c>): the name, the duration, how many offers came first.</summary>
    public struct KioskOpenRpc : IRpcCommand
    {
        public uint Seq;
        public FixedString64Bytes Name;
        public byte Tier, OfferCount;
    }

    /// <summary>The owner closes (<see cref="Close"/> 1) or collects from (0) their Kiosk (NGO's CloseRpc / CollectRpc).</summary>
    public struct KioskCloseRpc : IRpcCommand
    {
        public byte Close;
    }

    /// <summary>A player buys from a stand by its server id (NGO's <c>PlayerKiosk.BuyRpc</c>): the listing as they saw it, how many.</summary>
    public struct KioskBuyRpc : IRpcCommand
    {
        public uint StandId;
        public byte Index;
        public ushort ItemId, StackCount, Count;
        public ulong Data;
        public long Price;
    }

    /// <summary>One shelf line of the owner's own Kiosk (before its <see cref="KioskViewRpc"/>, same <see cref="Seq"/>).</summary>
    public struct KioskViewLineRpc : IRpcCommand
    {
        public uint Seq;
        public ushort ItemId, Count;
        public ulong Data;
        public long Price;
    }

    /// <summary>The owner's own Kiosk (NGO's <c>KioskOwnerView</c>); its shelf came first under the same <see cref="Seq"/>.</summary>
    public struct KioskViewRpc : IRpcCommand
    {
        public const byte Exists = 1, Open = 2;
        public uint Seq;
        public byte Flags, LineCount;
        public FixedString64Bytes ShopName;
        public long Till, ExpiresUtc, SoldCreds;
        public int SoldCount;
    }

    /// <summary>A stand to a player (NGO's KioskStand network variables for everyone): its sign and place; its shelf came first.</summary>
    public struct StandShownRpc : IRpcCommand
    {
        public uint Id;
        public int Version;
        public int XMm, YMm, ZMm;
        public float Yaw;
        public FixedString64Bytes ShopName, OwnerName;
        public byte Faction, ListingCount;
        public long ExpiresUtc;
    }

    /// <summary>One shelf line of a stand (before its <see cref="StandShownRpc"/>, same id and version).</summary>
    public struct StandListingRpc : IRpcCommand
    {
        public uint Id;
        public int Version;
        public ushort ItemId, Count;
        public ulong Data;
        public long Price;
    }

    /// <summary>The stand's doll look (the owner's look as <see cref="RecipeText"/>), once per stand.</summary>
    public struct StandLookRpc : IRpcCommand
    {
        public uint Id;
        public FixedString512Bytes Text;
    }

    /// <summary>A stand is gone (closed, sold out, out of time).</summary>
    public struct StandGoneRpc : IRpcCommand
    {
        public uint Id;
    }

    /// <summary>
    /// Server: <see cref="IKioskOwnerLink"/> for the bodies of the entity netcode: the owner's own Kiosk goes to the connection of the player whose
    /// server body it is, by its <see cref="PlayerKey"/> (as <see cref="EntityTradeOwnerLink"/>).
    /// </summary>
    public sealed class EntityKioskOwnerLink : IKioskOwnerLink
    {
        static uint s_Seq;

        public void View(Component part, in BodyKioskView view)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return;
            uint seq = ++s_Seq;
            int lines = Math.Min(view.shelf != null ? view.shelf.Length : 0, 64);
            for (int i = 0; i < lines; i++)
            {
                BodyKioskListing l = view.shelf[i];
                Send(em, connection, new KioskViewLineRpc { Seq = seq, ItemId = l.stack.itemId, Count = l.stack.count, Data = l.stack.data, Price = l.unitPrice });
            }
            var header = new KioskViewRpc
            {
                Seq = seq, Flags = (byte)((view.exists ? KioskViewRpc.Exists : 0) | (view.open ? KioskViewRpc.Open : 0)), LineCount = (byte)lines,
                Till = view.till, ExpiresUtc = view.expiresUtc, SoldCreds = view.soldCreds, SoldCount = view.soldCount,
            };
            header.ShopName.CopyFromTruncated(view.shopName ?? string.Empty);
            Send(em, connection, header);
        }

        static void Send<T>(EntityManager em, Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            Entity e = em.CreateEntity();
            em.AddComponentData(e, rpc);
            em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>
    /// Server: the owners' Kiosk requests (NGO's four PlayerKiosk RPCs, all at the Money limit) run on their server bodies. The offers of an
    /// open wait for it per connection (at most 48, the settings' largest Kiosk); the open with a different Seq starts them over.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class KioskRequestServerSystem : SystemBase
    {
        public const int MostOffers = 48;
        EntityQuery m_Offers, m_Opens, m_Closes, m_Buys;
        readonly Dictionary<Entity, (uint seq, List<BodyKioskOffer> offers)> m_Pending = new Dictionary<Entity, (uint, List<BodyKioskOffer>)>();
        readonly List<Entity> m_Left = new List<Entity>();

        /// <summary>Requests run (tests).</summary>
        public int Ran { get; private set; }

        protected override void OnCreate()
        {
            m_Offers = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<KioskOfferRpc>());
            m_Opens = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<KioskOpenRpc>());
            m_Closes = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<KioskCloseRpc>());
            m_Buys = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<KioskBuyRpc>());
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            m_Left.Clear();
            foreach (Entity c in m_Pending.Keys) if (!em.Exists(c)) m_Left.Add(c);
            foreach (Entity c in m_Left) m_Pending.Remove(c);

            if (!m_Offers.IsEmpty)
            {
                using var received = m_Offers.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var offers = m_Offers.ToComponentDataArray<KioskOfferRpc>(Allocator.Temp);
                em.DestroyEntity(m_Offers);
                for (int i = 0; i < offers.Length; i++)
                {
                    Entity c = received[i].SourceConnection;
                    if (!m_Pending.TryGetValue(c, out var pending) || pending.seq != offers[i].Seq)
                        m_Pending[c] = pending = (offers[i].Seq, new List<BodyKioskOffer>());
                    if (pending.offers.Count < MostOffers)
                        pending.offers.Add(new BodyKioskOffer { cell = offers[i].Cell, count = offers[i].Count, unitPrice = offers[i].Price });
                }
            }
            if (!m_Opens.IsEmpty)
            {
                using var received = m_Opens.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var opens = m_Opens.ToComponentDataArray<KioskOpenRpc>(Allocator.Temp);
                em.DestroyEntity(m_Opens);
                for (int i = 0; i < opens.Length; i++)
                {
                    Entity c = received[i].SourceConnection;
                    List<BodyKioskOffer> offers = m_Pending.TryGetValue(c, out var pending) && pending.seq == opens[i].Seq ? pending.offers : new List<BodyKioskOffer>();
                    m_Pending.Remove(c);
                    // an offer line lost on the way would open a different Kiosk than the player asked for: nothing opens
                    if (offers.Count != opens[i].OfferCount) continue;
                    if (PlayerRequestServerSystem.TryBody(World, c, CallKind.Money, out GameObject body) && body.TryGetComponent(out IBodyKiosk kiosk))
                    {
                        kiosk.RunOpen(opens[i].Name.ToString(), offers.ToArray(), opens[i].Tier);
                        Ran++;
                    }
                }
            }
            if (!m_Closes.IsEmpty)
            {
                using var received = m_Closes.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var closes = m_Closes.ToComponentDataArray<KioskCloseRpc>(Allocator.Temp);
                em.DestroyEntity(m_Closes);
                for (int i = 0; i < closes.Length; i++)
                    if (PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out IBodyKiosk kiosk))
                    {
                        if (closes[i].Close != 0) kiosk.RunClose(); else kiosk.RunCollect();
                        Ran++;
                    }
            }
            if (!m_Buys.IsEmpty)
            {
                using var received = m_Buys.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var buys = m_Buys.ToComponentDataArray<KioskBuyRpc>(Allocator.Temp);
                em.DestroyEntity(m_Buys);
                for (int i = 0; i < buys.Length; i++)
                    if (PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out IBodyKiosk kiosk))
                    {
                        KioskBuyRpc b = buys[i];
                        kiosk.RunBuy(b.StandId, b.Index, new BodyKioskListing(new ItemStack(b.ItemId, b.StackCount, b.Data), b.Price), b.Count);
                        Ran++;
                    }
            }
        }
    }

    /// <summary>What one player has been told of the stands (id -> version), and what to tell them now (plain C#, as <see cref="DropListDiff"/>).</summary>
    public sealed class StandListDiff
    {
        readonly Dictionary<uint, int> m_Told = new Dictionary<uint, int>();
        readonly HashSet<uint> m_Seen = new HashSet<uint>();
        readonly List<uint> m_Gone = new List<uint>();

        /// <summary>0: told already; 1: new to the player (the look goes too); 2: changed since.</summary>
        public int Tell(uint id, int version)
        {
            m_Seen.Add(id);
            if (m_Told.TryGetValue(id, out int had))
            {
                if (had == version) return 0;
                m_Told[id] = version;
                return 2;
            }
            m_Told[id] = version;
            return 1;
        }

        public List<uint> EndLook()
        {
            m_Gone.Clear();
            foreach (uint id in m_Told.Keys) if (!m_Seen.Contains(id)) m_Gone.Add(id);
            foreach (uint id in m_Gone) m_Told.Remove(id);
            m_Seen.Clear();
            return m_Gone;
        }
    }

    /// <summary>
    /// Server: tells every player in the game of the Kiosk stands (NGO's KioskStand network variables for everyone): each new or changed stand
    /// (shelf lines, then the sign; the doll's look once), and each that went. Four times a second.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class StandListServerSystem : SystemBase
    {
        public float Interval = 0.25f;
        readonly Dictionary<Entity, StandListDiff> m_Lists = new Dictionary<Entity, StandListDiff>();
        readonly List<Entity> m_Left = new List<Entity>();
        EntityQuery m_InGame;
        double m_Next;

        /// <summary>Stands shown and gone messages sent (tests).</summary>
        public int Shown { get; private set; }
        public int Gone { get; private set; }

        protected override void OnCreate() =>
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());

        protected override void OnUpdate()
        {
            double now = SystemAPI.Time.ElapsedTime;
            if (now < m_Next) return;
            m_Next = now + Interval;
            var em = EntityManager;
            m_Left.Clear();
            foreach (Entity c in m_Lists.Keys) if (!em.Exists(c)) m_Left.Add(c);
            foreach (Entity c in m_Left) m_Lists.Remove(c);
            using var connections = m_InGame.ToEntityArray(Allocator.Temp);
            for (int c = 0; c < connections.Length; c++)
            {
                if (!m_Lists.TryGetValue(connections[c], out StandListDiff list)) m_Lists[connections[c]] = list = new StandListDiff();
                foreach (IKioskStand stand in KioskStands.Server)
                {
                    if (stand == null || stand.ServerId == 0) continue;
                    int told = list.Tell(stand.ServerId, stand.Version);
                    if (told == 0) continue;
                    int lines = Mathf.Min(stand.ListingCount, 64);
                    for (int i = 0; i < lines; i++)
                    {
                        BodyKioskListing l = stand.Listing(i);
                        Send(connections[c], new StandListingRpc { Id = stand.ServerId, Version = stand.Version, ItemId = l.stack.itemId, Count = l.stack.count, Data = l.stack.data, Price = l.unitPrice });
                    }
                    if (told == 1 && stand is ILookCarrier carrier && carrier.HasLook)
                    {
                        string text = RecipeText.Encode(carrier.Look);
                        if (System.Text.Encoding.UTF8.GetByteCount(text) <= FixedString512Bytes.UTF8MaxLengthInBytes)
                            Send(connections[c], new StandLookRpc { Id = stand.ServerId, Text = new FixedString512Bytes(text) });
                    }
                    Vector3 p = stand.Position;
                    var shown = new StandShownRpc
                    {
                        Id = stand.ServerId, Version = stand.Version, XMm = Mathf.RoundToInt(p.x * 1000f), YMm = Mathf.RoundToInt(p.y * 1000f), ZMm = Mathf.RoundToInt(p.z * 1000f),
                        Yaw = stand.Yaw, Faction = stand.FactionByte, ListingCount = (byte)lines, ExpiresUtc = stand.ExpiresUtc,
                    };
                    shown.ShopName.CopyFromTruncated(stand.ShopName ?? string.Empty);
                    shown.OwnerName.CopyFromTruncated(stand.OwnerName ?? string.Empty);
                    Send(connections[c], shown);
                    Shown++;
                }
                foreach (uint id in list.EndLook())
                {
                    Send(connections[c], new StandGoneRpc { Id = id });
                    Gone++;
                }
            }
        }

        void Send<T>(Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            Entity e = EntityManager.CreateEntity();
            EntityManager.AddComponentData(e, rpc);
            EntityManager.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>
    /// Client: the owner's own Kiosk (KioskViewRpc) on the local view body, and the server's stands as views made from the local body's stand
    /// prefab (StandShownRpc / StandGoneRpc), which the Kiosk window browses as before. A host process already has the server's stands: no views.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class KioskClientSystem : SystemBase
    {
        EntityQuery m_ViewLines, m_Views, m_Listings, m_LookRpcs, m_Shown, m_Gone;
        readonly List<BodyKioskListing> m_OwnLines = new List<BodyKioskListing>();
        uint m_OwnSeq;
        readonly Dictionary<(uint id, int version), List<BodyKioskListing>> m_Shelves = new Dictionary<(uint, int), List<BodyKioskListing>>();
        readonly Dictionary<uint, IKioskStand> m_Stands = new Dictionary<uint, IKioskStand>();
        readonly Dictionary<uint, CharacterRecipe> m_Looks = new Dictionary<uint, CharacterRecipe>();
        readonly List<(uint, int)> m_Old = new List<(uint, int)>();

        /// <summary>Stands shown here now (tests).</summary>
        public int Stands => m_Stands.Count;

        protected override void OnCreate()
        {
            m_ViewLines = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<KioskViewLineRpc>());
            m_Views = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<KioskViewRpc>());
            m_Listings = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<StandListingRpc>());
            m_LookRpcs = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<StandLookRpc>());
            m_Shown = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<StandShownRpc>());
            m_Gone = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<StandGoneRpc>());
        }

        protected override void OnDestroy()
        {
            foreach (IKioskStand stand in m_Stands.Values) stand?.RemoveView();
            m_Stands.Clear();
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            IBodyKiosk local = LocalKiosk();
            if (!m_ViewLines.IsEmpty || !m_Views.IsEmpty)
            {
                using var lines = m_ViewLines.ToComponentDataArray<KioskViewLineRpc>(Allocator.Temp);
                using var views = m_Views.ToComponentDataArray<KioskViewRpc>(Allocator.Temp);
                em.DestroyEntity(m_ViewLines);
                em.DestroyEntity(m_Views);
                foreach (KioskViewLineRpc l in lines)
                {
                    if (l.Seq != m_OwnSeq) { m_OwnSeq = l.Seq; m_OwnLines.Clear(); }
                    m_OwnLines.Add(new BodyKioskListing(new ItemStack(l.ItemId, l.Count, l.Data), l.Price));
                }
                foreach (KioskViewRpc v in views)
                {
                    BodyKioskListing[] shelf = v.Seq == m_OwnSeq ? m_OwnLines.ToArray() : Array.Empty<BodyKioskListing>();
                    m_OwnLines.Clear();
                    local?.MirrorView(new BodyKioskView
                    {
                        exists = (v.Flags & KioskViewRpc.Exists) != 0, open = (v.Flags & KioskViewRpc.Open) != 0, shopName = v.ShopName.ToString(),
                        till = v.Till, expiresUtc = v.ExpiresUtc, soldCreds = v.SoldCreds, soldCount = v.SoldCount, shelf = shelf,
                    });
                }
            }
            if (m_Listings.IsEmpty && m_LookRpcs.IsEmpty && m_Shown.IsEmpty && m_Gone.IsEmpty) return;
            using var listings = m_Listings.ToComponentDataArray<StandListingRpc>(Allocator.Temp);
            using var looks = m_LookRpcs.ToComponentDataArray<StandLookRpc>(Allocator.Temp);
            using var shownAll = m_Shown.ToComponentDataArray<StandShownRpc>(Allocator.Temp);
            using var gone = m_Gone.ToComponentDataArray<StandGoneRpc>(Allocator.Temp);
            em.DestroyEntity(m_Listings);
            em.DestroyEntity(m_LookRpcs);
            em.DestroyEntity(m_Shown);
            em.DestroyEntity(m_Gone);
            var server = ClientServerBootstrap.ServerWorld;
            if (server != null && server.IsCreated) return;
            foreach (StandListingRpc l in listings)
            {
                if (!m_Shelves.TryGetValue((l.Id, l.Version), out var shelf)) m_Shelves[(l.Id, l.Version)] = shelf = new List<BodyKioskListing>();
                shelf.Add(new BodyKioskListing(new ItemStack(l.ItemId, l.Count, l.Data), l.Price));
            }
            foreach (StandLookRpc l in looks)
                if (RecipeText.TryDecode(l.Text.ToString(), out CharacterRecipe recipe)) m_Looks[l.Id] = recipe;
            foreach (StandShownRpc s in shownAll)
            {
                m_Shelves.TryGetValue((s.Id, s.Version), out var shelf);
                m_Shelves.Remove((s.Id, s.Version));
                m_Old.Clear();
                foreach (var key in m_Shelves.Keys) if (key.id == s.Id && key.version < s.Version) m_Old.Add(key);
                foreach (var key in m_Old) m_Shelves.Remove(key);
                if (!m_Stands.TryGetValue(s.Id, out IKioskStand stand) || stand == null)
                {
                    stand = local?.ShowStandView(s.Id, new Vector3(s.XMm, s.YMm, s.ZMm) * 0.001f, s.Yaw);
                    if (stand == null) continue;
                    m_Stands[s.Id] = stand;
                }
                stand.ApplyView(s.ShopName.ToString(), s.OwnerName.ToString(), s.Faction, s.ExpiresUtc, shelf != null ? shelf.ToArray() : Array.Empty<BodyKioskListing>());
                if (m_Looks.TryGetValue(s.Id, out CharacterRecipe look) && stand is ILookCarrier carrier)
                {
                    carrier.ShowLook(look);
                    m_Looks.Remove(s.Id);
                }
            }
            foreach (StandGoneRpc g in gone)
            {
                if (!m_Stands.TryGetValue(g.Id, out IKioskStand stand)) continue;
                m_Stands.Remove(g.Id);
                stand?.RemoveView();
            }
        }

        IBodyKiosk LocalKiosk()
        {
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out IBodyKiosk kiosk))
                    return kiosk;
            return null;
        }
    }
}
