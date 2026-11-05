using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>The owner asks the server to use one of an item (by its id: the two Packs are not the same array; feel test item 5).</summary>
    public struct ItemUseRpc : IRpcCommand
    {
        public ushort ItemId;
    }

    /// <summary>The owner asks the server to cast a buff on the player's own body (Heavenly Serenity, a ward; feel test item 5).</summary>
    /// <summary>The owner's hold-to-charge started or ended (M4.5f; NGO ChargeStartedRpc / ChargeEndedRpc).</summary>
    public struct ChargeRpc : IRpcCommand
    {
        public FixedString64Bytes SkillId;
        public int AimGhostId;
        /// <summary>1 started, 2 ended charged, 3 let go early.</summary>
        public byte Phase;
    }

    /// <summary>An Autoward volley (M4 step 7; NGO CastWardsServerRpc): the ward ids joined by '|'.</summary>
    public struct WardsRpc : IRpcCommand
    {
        public FixedString512Bytes SkillIds;
    }

    public struct SkillCastRpc : IRpcCommand
    {
        public FixedString64Bytes SkillId;
        /// <summary>The attacked ghost (M4.5a; 0 = none: a buff or an area attack).</summary>
        public int TargetGhostId;
    }

    /// <summary>The server's answer to <see cref="ItemUseRpc"/> or <see cref="SkillCastRpc"/>, for the owner's own body to show.</summary>
    public struct RequestReplyRpc : IRpcCommand
    {
        public const byte ItemUsed = 1, ItemRefused = 2, SkillCast = 3, SkillRefused = 4, WardCast = 5;
        public byte Kind;
        /// <summary>A ward of a volley: 1 for the one shown as a cast (NGO WardCastClientRpc's shown, the first).</summary>
        public byte Shown;
        /// <summary>Item used: it restored HP / Flux (the feel to play).</summary>
        public byte Health, Flux;
        /// <summary>Skill cast: which, at what rank.</summary>
        public FixedString64Bytes SkillId;
        public short Rank;
        /// <summary>Refused: the reason in the game's words.</summary>
        public FixedString128Bytes Text;
    }

    /// <summary>
    /// One Pack cell of the server's body for its owner's own body (the HUD and the hotbar show the server's counts and Flask drinks).
    /// <see cref="ClearAll"/> empties the owner's Pack first (on arrival: its starter bag is not the player's).
    /// </summary>
    public struct PackCellRpc : IRpcCommand
    {
        public const byte ClearAll = 255;
        public byte Index;
        public ushort ItemId;
        public ushort Count;
        public ulong Data;
    }

    /// <summary>A line the server says to one player (<see cref="OwnerMessages"/>): shown in the player's message area.</summary>
    /// <summary>The owner asks the server's body to move, equip, unequip, destroy or sort (NGO's five Pack RPCs; M8 8.1.2).</summary>
    public struct PackOpRpc : IRpcCommand
    {
        public byte Kind;
        public byte FromContainer, FromIndex, ToContainer, ToIndex;
    }

    /// <summary>The owner asks the server's body to save, rename, delete or wear a loadout slot (NGO's four loadout RPCs; M8 8.1.3).</summary>
    public struct LoadoutOpRpc : IRpcCommand
    {
        public byte Kind, Index;
        public FixedString128Bytes Name;
    }

    /// <summary>One loadout slot to its owner (NGO's owner-only list): the name, and each slot's piece with its own state.</summary>
    public struct LoadoutSlotRpc : IRpcCommand
    {
        public byte Index, Used;
        public FixedString32Bytes Name;
        public ushort Weapon, Armor, Gloves, Boots, Ring, Amulet, Cape, Pet;
        public ulong WeaponData, ArmorData, GlovesData, BootsData, RingData, AmuletData, CapeData, PetData;

        public static LoadoutSlotRpc Of(int index, in LoadoutEntry e) => new LoadoutSlotRpc
        {
            Index = (byte)index, Used = (byte)(e.used ? 1 : 0), Name = e.name,
            Weapon = e.ids.weapon, Armor = e.ids.armor, Gloves = e.ids.gloves, Boots = e.ids.boots, Ring = e.ids.ring, Amulet = e.ids.amulet,
            Cape = e.ids.cape, Pet = e.ids.pet,
            WeaponData = e.data.weapon, ArmorData = e.data.armor, GlovesData = e.data.gloves, BootsData = e.data.boots, RingData = e.data.ring,
            AmuletData = e.data.amulet, CapeData = e.data.cape, PetData = e.data.pet,
        };

        public LoadoutEntry Entry => new LoadoutEntry
        {
            used = Used != 0, name = Name,
            ids = new EquipmentLoadout { weapon = Weapon, armor = Armor, gloves = Gloves, boots = Boots, ring = Ring, amulet = Amulet, cape = Cape, pet = Pet },
            data = new EquipmentData
            {
                weapon = WeaponData, armor = ArmorData, gloves = GlovesData, boots = BootsData, ring = RingData, amulet = AmuletData, cape = CapeData,
                pet = PetData,
            },
        };
    }

    /// <summary>The owner asks the server's body to refill its Flasks at the Apothecary in reach (NGO's <c>PlayerFlask.RefillRpc</c>).</summary>
    public struct FlaskRefillRpc : IRpcCommand
    {
    }

    /// <summary>The owner puts stat points into an attribute (NGO's <c>PlayerCharacterProgression.AllocateServerRpc</c>; M8 8.3.1).</summary>
    public struct AllocateRpc : IRpcCommand
    {
        public byte Attribute;
        public short Count;
    }

    /// <summary>One XP booster's clock to its owner (NGO's owner-only list): exact on a use or run-out, at most 15 s behind otherwise.</summary>
    public struct XpBoostClockRpc : IRpcCommand
    {
        public byte Index;
        public float Seconds;
    }

    /// <summary>The owner buys ranks of a skill (NGO's <c>PlayerCombatSkills.UpgradeServerRpc</c>; M8 8.3.2).</summary>
    public struct SkillUpgradeRpc : IRpcCommand
    {
        public FixedString64Bytes SkillId;
        public byte Count;
    }

    /// <summary>The owner asks the server's body to use the Vault or the Deep Vault (NGO's seven PlayerStorage RPCs; M8 8.2.1).</summary>
    public struct StorageOpRpc : IRpcCommand
    {
        public byte Kind, Op;
        public short A, B;
        public long Amount;
    }

    /// <summary>One storage cell to its owner (NGO's owner-only list); <see cref="ClearAll"/> empties the owner's copy first (on arrival).</summary>
    public struct StorageCellRpc : IRpcCommand
    {
        public const byte ClearAll = 255;
        public byte Kind, Index;
        public ushort ItemId, Count;
        public ulong Data;
    }

    /// <summary>The Creds kept in a storage, to its owner (NGO's owner-only variable).</summary>
    public struct StorageCredsRpc : IRpcCommand
    {
        public byte Kind;
        public long Creds;
    }

    /// <summary>The owner buys from a vendor (NGO's <c>PlayerShopping.BuyRpc</c>; M8 8.2.2): the stock's NPC by name, the item, how many.</summary>
    public struct ShopBuyRpc : IRpcCommand
    {
        public FixedString64Bytes Vendor;
        public ushort ItemId, Count;
    }

    /// <summary>The owner sells from a Pack cell (NGO's <c>PlayerShopping.SellRpc</c>): the stack the player saw and whether they confirmed.</summary>
    public struct ShopSellRpc : IRpcCommand
    {
        public ushort Cell, Count, ItemId, StackCount;
        public ulong Data;
        public byte Confirmed;
    }

    /// <summary>
    /// This week's limited purchases to their owner (NGO's owner-only netBoughtWeek + netBought): a <see cref="Begin"/> line with the week
    /// empties the owner's copy, then one line per item.
    /// </summary>
    public struct VendorWeekRpc : IRpcCommand
    {
        public long Week;
        public ushort ItemId, Count;
        public byte Begin;
    }

    /// <summary>The owner's trade request (NGO's nine <c>PlayerTrade</c> RPCs; M8 8.2.3): the op, the name asked, a cell or line, a count, Creds, a flag.</summary>
    public struct TradeOpRpc : IRpcCommand
    {
        public byte Op, Flag;
        public FixedString64Bytes Name;
        public short A;
        public ushort B;
        public long Creds;
    }

    /// <summary>One line of a trade view to its owner (sent before the <see cref="TradeViewRpc"/> with the same <see cref="Seq"/>).</summary>
    public struct TradeLineRpc : IRpcCommand
    {
        public uint Seq;
        public byte Theirs;
        public short Cell;
        public ushort ItemId, Count;
        public ulong Data;
    }

    /// <summary>The trade's header to its owner (NGO's <c>TradeHeader</c>); its lines came first under the same <see cref="Seq"/>.</summary>
    public struct TradeViewRpc : IRpcCommand
    {
        public const byte Active = 1, MyLocked = 2, TheirLocked = 4, MyConfirmed = 8, TheirConfirmed = 16;
        public uint Seq;
        public byte Flags, MineCount, TheirCount;
        public FixedString64Bytes PartnerName;
        public long MyCreds, TheirCreds, PerTrade, RemainingToday;
    }

    /// <summary>The trade window's notice to its owner (NGO's <c>NoticeRpc</c>).</summary>
    public struct TradeNoticeRpc : IRpcCommand
    {
        public FixedString512Bytes Text;
    }

    /// <summary>The trade request prompt to its owner (NGO's <c>PromptRpc</c> / <c>PromptClosedRpc</c>).</summary>
    public struct TradePromptRpc : IRpcCommand
    {
        public FixedString64Bytes From;
        public float Seconds;
        public byte Close;
    }

    /// <summary>The owner picks up a drop by its server id (NGO's <c>PlayerLoot.PickupRpc</c>; M7.5).</summary>
    public struct PickupRpc : IRpcCommand
    {
        public uint DropId;
    }

    /// <summary>The owner puts an item from the Pack or a slot on the ground (NGO's <c>PlayerLoot.DropRpc</c>).</summary>
    public struct DropItemRpc : IRpcCommand
    {
        public byte Container, Index;
    }

    /// <summary>The owner buys Run or Jump ranks (NGO's <c>PlayerMovementSkills.UpgradeServerRpc</c>).</summary>
    public struct MovementUpgradeRpc : IRpcCommand
    {
        public byte Skill, Count;
    }

    /// <summary>One skill's rank to its owner (NGO's owner-only netRanks, by index of the available skills).</summary>
    public struct SkillRankRpc : IRpcCommand
    {
        public byte Index, Rank;
    }

    /// <summary>The owner's unlocked books (NGO's netUnlockedBooks: a mask of the first 64 skills).</summary>
    public struct SkillBooksRpc : IRpcCommand
    {
        public ulong Books;
    }

    public struct ServerTellRpc : IRpcCommand
    {
        public FixedString512Bytes Text;
    }

    /// <summary>
    /// Server: <see cref="OwnerMessages"/> for the bodies of the entity netcode: the line goes to the connection of the player whose server
    /// body said it (found by its <see cref="PlayerKey"/> in the registry). A body that is not a server body is not told here.
    /// </summary>
    public sealed class EntityOwnerMessages : IOwnerMessages
    {
        public bool Tell(Component part, string text)
        {
            BodyRoleTag tag = BodyRoleTag.Of(part);
            if (tag == null || tag.Kind != NetRoleKind.ServerBody || tag.Key == PlayerKey.None) return false;
            if (!GhostBodyRegistry.TryGetByKey(tag.Key, out GhostBodyEntry entry) || entry.World == null || !entry.World.IsCreated) return false;
            return Send(entry.World, entry.NetworkId, text);
        }

        /// <summary>Sends <paramref name="text"/> to the in-game connection <paramref name="networkId"/> of <paramref name="server"/>.</summary>
        public static bool Send(World server, int networkId, string text)
        {
            if (server == null || !server.IsCreated || networkId == 0 || string.IsNullOrEmpty(text)) return false;
            var em = server.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            using var connections = query.ToEntityArray(Allocator.Temp);
            using var ids = query.ToComponentDataArray<NetworkId>(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
            {
                if (ids[i].Value != networkId) continue;
                if (text.Length > 500) text = text.Substring(0, 500);
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new ServerTellRpc { Text = new FixedString512Bytes(text) });
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connections[i] });
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Server: <see cref="ITradeOwnerLink"/> for the bodies of the entity netcode (M8 8.2.3): the view, notice and prompt go to the
    /// connection of the player whose server body it is, found by its <see cref="PlayerKey"/> in the registry, never by a NetworkId (a
    /// NetworkId can be given again to the next player; a key never is). A body that is not a server body is not told here.
    /// </summary>
    public sealed class EntityTradeOwnerLink : ITradeOwnerLink
    {
        static uint s_Seq;

        /// <summary>Lines sent (tests).</summary>
        public static int Sent { get; private set; }

        public void View(Component part, in BodyTradeView view)
        {
            if (!TryConnection(part, out EntityManager em, out Entity connection)) return;
            uint seq = ++s_Seq;
            int mine = Math.Min(view.mine != null ? view.mine.Length : 0, 64), theirs = Math.Min(view.theirs != null ? view.theirs.Length : 0, 64);
            for (int i = 0; i < mine; i++) Send(em, connection, Line(seq, 0, view.mine[i]));
            for (int i = 0; i < theirs; i++) Send(em, connection, Line(seq, 1, view.theirs[i]));
            byte flags = (byte)((view.active ? TradeViewRpc.Active : 0) | (view.myLocked ? TradeViewRpc.MyLocked : 0) | (view.theirLocked ? TradeViewRpc.TheirLocked : 0)
                | (view.myConfirmed ? TradeViewRpc.MyConfirmed : 0) | (view.theirConfirmed ? TradeViewRpc.TheirConfirmed : 0));
            var header = new TradeViewRpc
            {
                Seq = seq, Flags = flags, MineCount = (byte)mine, TheirCount = (byte)theirs,
                MyCreds = view.myCreds, TheirCreds = view.theirCreds, PerTrade = view.perTrade, RemainingToday = view.remainingToday,
            };
            header.PartnerName.CopyFromTruncated(view.partnerName ?? string.Empty);
            Send(em, connection, header);
        }

        public void Notice(Component part, string text)
        {
            if (!TryConnection(part, out EntityManager em, out Entity connection)) return;
            text ??= string.Empty;
            if (text.Length > 500) text = text.Substring(0, 500);
            Send(em, connection, new TradeNoticeRpc { Text = new FixedString512Bytes(text) });
        }

        public void Prompt(Component part, string fromName, float seconds)
        {
            if (!TryConnection(part, out EntityManager em, out Entity connection)) return;
            var prompt = new TradePromptRpc { Seconds = seconds };
            prompt.From.CopyFromTruncated(fromName ?? string.Empty);
            Send(em, connection, prompt);
        }

        public void PromptClosed(Component part)
        {
            if (TryConnection(part, out EntityManager em, out Entity connection)) Send(em, connection, new TradePromptRpc { Close = 1 });
        }

        static TradeLineRpc Line(uint seq, byte theirs, BodyTradeLine line) =>
            new TradeLineRpc { Seq = seq, Theirs = theirs, Cell = line.cell, ItemId = line.stack.itemId, Count = line.stack.count, Data = line.stack.data };

        static void Send<T>(EntityManager em, Entity connection, T rpc) where T : unmanaged, IRpcCommand
        {
            Entity e = em.CreateEntity();
            em.AddComponentData(e, rpc);
            em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = connection });
            Sent++;
        }

        /// <summary>The in-game connection of the player whose server body <paramref name="part"/> is on, by its key.</summary>
        public static bool TryConnection(Component part, out EntityManager em, out Entity connection)
        {
            em = default;
            connection = Entity.Null;
            BodyRoleTag tag = BodyRoleTag.Of(part);
            if (tag == null || tag.Kind != NetRoleKind.ServerBody || tag.Key == PlayerKey.None) return false;
            if (!GhostBodyRegistry.TryGetByKey(tag.Key, out GhostBodyEntry entry) || entry.World == null || !entry.World.IsCreated) return false;
            em = entry.World.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            using var connections = query.ToEntityArray(Allocator.Temp);
            using var ids = query.ToComponentDataArray<NetworkId>(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
                if (ids[i].Value == entry.NetworkId) { connection = connections[i]; return true; }
            return false;
        }
    }

    /// <summary>
    /// Client: the local view body's requests (<see cref="IBodyRequests"/> on its tag) become RPCs to the server. Nothing is used or cast
    /// here: the server's answer comes back as <see cref="RequestReplyRpc"/>.
    /// </summary>
    public sealed class EntityBodyRequests : IBodyRequests, KryticalNova.CharacterCreation.IRestyleRequests
    {
        readonly World m_World;

        public EntityBodyRequests(World clientWorld) => m_World = clientWorld;

        public void UseItem(ushort itemId)
        {
            if (m_World == null || !m_World.IsCreated || itemId == 0) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new ItemUseRpc { ItemId = itemId });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void PackOp(PackOpKind kind, byte fromContainer, byte fromIndex, byte toContainer, byte toIndex)
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new PackOpRpc { Kind = (byte)kind, FromContainer = fromContainer, FromIndex = fromIndex, ToContainer = toContainer, ToIndex = toIndex });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void LoadoutOp(LoadoutOpKind kind, byte index, string name)
        {
            if (m_World == null || !m_World.IsCreated) return;
            name ??= string.Empty;
            if (System.Text.Encoding.UTF8.GetByteCount(name) > FixedString128Bytes.UTF8MaxLengthInBytes) return;   // NGO's TextFits refuses it too
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new LoadoutOpRpc { Kind = (byte)kind, Index = index, Name = new FixedString128Bytes(name) });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void UpgradeSkill(string skillId, byte count)
        {
            if (m_World == null || !m_World.IsCreated || count == 0 || string.IsNullOrEmpty(skillId)
                || System.Text.Encoding.UTF8.GetByteCount(skillId) > FixedString64Bytes.UTF8MaxLengthInBytes) return;   // NGO's TextFits too
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new SkillUpgradeRpc { SkillId = new FixedString64Bytes(skillId), Count = count });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void StorageOp(byte kind, StorageOpKind op, short a, short b, long amount)
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new StorageOpRpc { Kind = kind, Op = (byte)op, A = a, B = b, Amount = amount });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void ShopBuy(string vendor, ushort itemId, ushort count)
        {
            if (m_World == null || !m_World.IsCreated || itemId == 0 || count == 0 || string.IsNullOrEmpty(vendor)
                || System.Text.Encoding.UTF8.GetByteCount(vendor) > FixedString64Bytes.UTF8MaxLengthInBytes) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new ShopBuyRpc { Vendor = new FixedString64Bytes(vendor), ItemId = itemId, Count = count });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void ShopSell(ushort cell, ushort count, ushort itemId, ushort stackCount, ulong data, bool confirmed)
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new ShopSellRpc
            {
                Cell = cell, Count = count, ItemId = itemId, StackCount = stackCount, Data = data, Confirmed = (byte)(confirmed ? 1 : 0),
            });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        uint m_KioskSeq;

        public void GearWork(GearWorkOpKind op, short cell, short cell2, ushort alloy, ushort protection)
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new GearWorkRpc { Op = (byte)op, Cell = cell, Cell2 = cell2, Alloy = alloy, Protection = protection });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        uint m_RestyleSeq;

        /// <summary>The proposed look in pages of <see cref="RestylePageRpc.PageChars"/> (NGO sent it whole as a CharacterNetState).</summary>
        public void Restyle(byte service, in KryticalNova.CharacterCreation.CharacterRecipe proposed)
        {
            if (m_World == null || !m_World.IsCreated) return;
            string text = RecipeText.Encode(proposed);
            int count = (text.Length + RestylePageRpc.PageChars - 1) / RestylePageRpc.PageChars;
            if (count == 0 || count > RestylePageRpc.MostPages) return;
            var em = m_World.EntityManager;
            uint seq = ++m_RestyleSeq;
            for (int i = 0; i < count; i++)
            {
                string chunk = text.Substring(i * RestylePageRpc.PageChars, System.Math.Min(RestylePageRpc.PageChars, text.Length - i * RestylePageRpc.PageChars));
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new RestylePageRpc { Seq = seq, Service = service, Index = (byte)i, Count = (byte)count, Chunk = new FixedString512Bytes(chunk) });
                em.AddComponent<SendRpcCommandRequest>(rpc);
            }
        }

        public void Chat(ChatChannel channel, string text, string whisperTarget, byte[] linkContainers, byte[] linkCells)
        {
            if (m_World == null || !m_World.IsCreated) return;
            if (!ChatText.TrySend(channel, text, whisperTarget, linkContainers, linkCells, out ChatSendRpc send)) return;   // NGO's RPC refused more links
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, send);
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void MountOp(MountOpKind op, int box)
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new MountOpRpc { Op = (byte)op, Box = (byte)Mathf.Clamp(box, 0, 255) });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void PetTrick(int nameHash)
        {
            if (m_World == null || !m_World.IsCreated || nameHash == 0) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new PetTrickRpc { NameHash = nameHash });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void Emote(bool stop, string key)
        {
            if (m_World == null || !m_World.IsCreated || string.IsNullOrEmpty(key)) return;
            if (System.Text.Encoding.UTF8.GetByteCount(key) > FixedString64Bytes.UTF8MaxLengthInBytes) return;   // NGO's RPC carried a FixedString64Bytes
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new EmoteRpc { Stop = (byte)(stop ? 1 : 0), Key = new FixedString64Bytes(key) });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void CrewOp(CrewOpKind op, string a, string b, byte b0, byte b1, byte b2, byte b3, bool flag)
        {
            if (m_World == null || !m_World.IsCreated) return;
            a ??= string.Empty;
            b ??= string.Empty;
            // NGO's TextFits refuses a longer text too (names at most 24, ids 32, titles 12, the announcement 160 characters)
            if (System.Text.Encoding.UTF8.GetByteCount(a) > FixedString64Bytes.UTF8MaxLengthInBytes || System.Text.Encoding.UTF8.GetByteCount(b) > FixedString512Bytes.UTF8MaxLengthInBytes) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new CrewOpRpc
            {
                Op = (byte)op, Flag = (byte)(flag ? 1 : 0), B0 = b0, B1 = b1, B2 = b2, B3 = b3, A = new FixedString64Bytes(a), B = new FixedString512Bytes(b),
            });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void SponsorOp(SponsorOpKind op, string a, bool flag)
        {
            if (m_World == null || !m_World.IsCreated) return;
            a ??= string.Empty;
            if (System.Text.Encoding.UTF8.GetByteCount(a) > FixedString64Bytes.UTF8MaxLengthInBytes) return;   // NGO's TextFits refuses it too
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new SponsorOpRpc { Op = (byte)op, Flag = (byte)(flag ? 1 : 0), A = new FixedString64Bytes(a) });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void Follow(string memberName)
        {
            if (m_World == null || !m_World.IsCreated) return;
            memberName ??= string.Empty;
            if (System.Text.Encoding.UTF8.GetByteCount(memberName) > FixedString64Bytes.UTF8MaxLengthInBytes) return;   // NGO's TextFits refuses it too
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new FollowRpc { Name = new FixedString64Bytes(memberName) });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void DuelOp(DuelOpKind op, Transform target, bool accept)
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new DuelOpRpc
            {
                Op = (byte)op, Accept = (byte)(accept ? 1 : 0),
                TargetGhostId = target != null ? KryticalNova.Player.Entities.Bodies.BodyClickSource.GhostIdOf(target) : 0,
            });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void PartyOp(PartyOpKind op, string name, bool flag)
        {
            if (m_World == null || !m_World.IsCreated) return;
            name ??= string.Empty;
            if (System.Text.Encoding.UTF8.GetByteCount(name) > FixedString64Bytes.UTF8MaxLengthInBytes) return;   // NGO's TextFits refuses it too
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new PartyOpRpc { Op = (byte)op, Flag = (byte)(flag ? 1 : 0), Name = new FixedString64Bytes(name) });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void ContactOp(ContactOpKind op, string a, string b, bool flag)
        {
            if (m_World == null || !m_World.IsCreated) return;
            a ??= string.Empty;
            b ??= string.Empty;
            // NGO's TextFits refuses a longer text too (ids are 32, names at most 20, notes 60 characters)
            if (System.Text.Encoding.UTF8.GetByteCount(a) > FixedString64Bytes.UTF8MaxLengthInBytes || System.Text.Encoding.UTF8.GetByteCount(b) > FixedString512Bytes.UTF8MaxLengthInBytes) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new ContactOpRpc { Op = (byte)op, Flag = (byte)(flag ? 1 : 0), A = new FixedString64Bytes(a), B = new FixedString512Bytes(b) });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void CrateOp(bool withdraw, ushort a, ushort count)
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new CrateOpRpc { Withdraw = (byte)(withdraw ? 1 : 0), A = a, Count = count });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void AutobuffSlots(string[] skillIds)
        {
            if (m_World == null || !m_World.IsCreated) return;
            string text = AutobuffSlotsRpc.Join(skillIds);
            if (System.Text.Encoding.UTF8.GetByteCount(text) > FixedString512Bytes.UTF8MaxLengthInBytes) return;   // NGO's 1024-char cap is looser; ids are short
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new AutobuffSlotsRpc { Ids = new FixedString512Bytes(text) });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void WeaveOp(WeaveOpKind op, byte slot, ushort id, byte stage)
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new WeaveOpRpc { Op = (byte)op, Slot = slot, Id = id, Stage = stage });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void Nova(NovaOpKind op, byte arg)
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new NovaOpRpc { Op = (byte)op, Arg = arg });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void Craft(ushort recipeId, ushort times)
        {
            if (m_World == null || !m_World.IsCreated || recipeId == 0) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new CraftRpc { Recipe = recipeId, Times = times });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void KioskOpen(string shopName, BodyKioskOffer[] offers, byte tier)
        {
            if (m_World == null || !m_World.IsCreated) return;
            shopName ??= string.Empty;
            offers ??= Array.Empty<BodyKioskOffer>();
            // NGO's TextFits on the name; the Kiosk window never offers more stacks than the largest Kiosk holds
            if (System.Text.Encoding.UTF8.GetByteCount(shopName) > FixedString64Bytes.UTF8MaxLengthInBytes || offers.Length > KioskRequestServerSystem.MostOffers) return;
            var em = m_World.EntityManager;
            uint seq = ++m_KioskSeq;
            foreach (BodyKioskOffer offer in offers)
            {
                Entity line = em.CreateEntity();
                em.AddComponentData(line, new KioskOfferRpc { Seq = seq, Cell = offer.cell, Count = offer.count, Price = offer.unitPrice });
                em.AddComponent<SendRpcCommandRequest>(line);
            }
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new KioskOpenRpc { Seq = seq, Name = new FixedString64Bytes(shopName), Tier = tier, OfferCount = (byte)offers.Length });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void KioskClose() => SendKioskClose(1);
        public void KioskCollect() => SendKioskClose(0);

        void SendKioskClose(byte close)
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new KioskCloseRpc { Close = close });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void KioskBuy(uint standId, byte index, ushort itemId, ushort stackCount, ulong data, long unitPrice, ushort count)
        {
            if (m_World == null || !m_World.IsCreated || standId == 0) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new KioskBuyRpc { StandId = standId, Index = index, ItemId = itemId, StackCount = stackCount, Data = data, Price = unitPrice, Count = count });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void TradeOp(TradeOpKind op, string name, short a, ushort b, long creds, bool flag)
        {
            if (m_World == null || !m_World.IsCreated) return;
            name ??= string.Empty;
            if (System.Text.Encoding.UTF8.GetByteCount(name) > FixedString64Bytes.UTF8MaxLengthInBytes) return;   // NGO's TextFits refuses it too
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new TradeOpRpc { Op = (byte)op, Name = new FixedString64Bytes(name), A = a, B = b, Creds = creds, Flag = (byte)(flag ? 1 : 0) });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void Pickup(uint dropId)
        {
            if (m_World == null || !m_World.IsCreated || dropId == 0) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new PickupRpc { DropId = dropId });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void DropItem(byte container, byte index)
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new DropItemRpc { Container = container, Index = index });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void UpgradeMovement(byte skill, byte count)
        {
            if (m_World == null || !m_World.IsCreated || count == 0) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new MovementUpgradeRpc { Skill = skill, Count = count });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void Allocate(byte attribute, short count)
        {
            if (m_World == null || !m_World.IsCreated || count <= 0) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new AllocateRpc { Attribute = attribute, Count = count });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void RefillFlask()
        {
            if (m_World == null || !m_World.IsCreated) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponent<FlaskRefillRpc>(rpc);
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void Charge(string skillId, Transform aimedAt, bool started, bool completed)
        {
            if (m_World == null || !m_World.IsCreated || string.IsNullOrEmpty(skillId) || skillId.Length > FixedString64Bytes.UTF8MaxLengthInBytes) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new ChargeRpc
            {
                SkillId = new FixedString64Bytes(skillId),
                AimGhostId = aimedAt != null ? KryticalNova.Player.Entities.Bodies.BodyClickSource.GhostIdOf(aimedAt) : 0,
                Phase = (byte)(started ? 1 : completed ? 2 : 3),
            });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void CastWards(string skillIds)
        {
            if (m_World == null || !m_World.IsCreated || string.IsNullOrEmpty(skillIds)) return;
            var ids = new FixedString512Bytes();
            if (ids.Append(skillIds) != FormatError.None) return;   // more than a volley holds: NGO's TextFits refused it too
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, new WardsRpc { SkillIds = ids });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }

        public void CastSkill(string skillId, Transform target)
        {
            if (m_World == null || !m_World.IsCreated || string.IsNullOrEmpty(skillId) || skillId.Length > FixedString64Bytes.UTF8MaxLengthInBytes) return;
            var em = m_World.EntityManager;
            Entity rpc = em.CreateEntity();
            int targetGhost = target != null ? KryticalNova.Player.Entities.Bodies.BodyClickSource.GhostIdOf(target) : 0;
            em.AddComponentData(rpc, new SkillCastRpc { SkillId = new FixedString64Bytes(skillId), TargetGhostId = targetGhost });
            em.AddComponent<SendRpcCommandRequest>(rpc);
        }
    }

    /// <summary>
    /// Server: runs the owners' item uses and buff casts on their server bodies with the game's own rules (NGO's <c>ServerUse</c> and
    /// <c>ServerTryCast</c>: alive, the shared use cooldown, the Flask's drinks, a full pool, the cast checks), counted by the request
    /// limits (items, combat). A use changes one player's documents and writes no ledger line (NGO writes none for a use); the Pack is
    /// saved as any change is (the save agent). Answers each request so the owner's own body can show it.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerRequestServerSystem : SystemBase
    {
        EntityQuery m_Charges, m_Wards, m_Uses, m_Casts, m_PackOps, m_LoadoutOps, m_Refills, m_Allocations, m_Upgrades, m_MoveUpgrades, m_Pickups, m_Drops, m_Storage, m_Buys, m_Sells, m_Trades;

        /// <summary>Requests run, refused, and dropped by the request limits (tests).</summary>
        public int Used { get; private set; }
        public int Refused { get; private set; }
        public int OverBudget { get; private set; }
        /// <summary>Pack and loadout requests run (tests).</summary>
        public int PackOps { get; private set; }
        public int LoadoutOps { get; private set; }
        public int Refills { get; private set; }
        public int Allocations { get; private set; }
        public int Upgrades { get; private set; }
        public int Pickups { get; private set; }
        public int StorageOps { get; private set; }
        public int ShopOps { get; private set; }
        public int TradeOps { get; private set; }
        public int Drops { get; private set; }

        protected override void OnCreate()
        {
            m_Uses = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<ItemUseRpc>());
            m_Casts = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<SkillCastRpc>());
            m_Charges = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<ChargeRpc>());
            m_Wards = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<WardsRpc>());
            m_PackOps = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<PackOpRpc>());
            m_LoadoutOps = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<LoadoutOpRpc>());
            m_Refills = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<FlaskRefillRpc>());
            m_Allocations = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<AllocateRpc>());
            m_Upgrades = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<SkillUpgradeRpc>());
            m_MoveUpgrades = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<MovementUpgradeRpc>());
            m_Pickups = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<PickupRpc>());
            m_Drops = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<DropItemRpc>());
            m_Storage = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<StorageOpRpc>());
            m_Buys = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<ShopBuyRpc>());
            m_Sells = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<ShopSellRpc>());
            m_Trades = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<TradeOpRpc>());
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            if (!m_Uses.IsEmpty)
            {
                var received = m_Uses.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var uses = m_Uses.ToComponentDataArray<ItemUseRpc>(Allocator.Temp);
                em.DestroyEntity(m_Uses);
                for (int i = 0; i < uses.Length; i++) UseItem(received[i].SourceConnection, uses[i].ItemId);
                received.Dispose();
                uses.Dispose();
            }
            if (!m_Wards.IsEmpty)
            {
                var received = m_Wards.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var volleys = m_Wards.ToComponentDataArray<WardsRpc>(Allocator.Temp);
                em.DestroyEntity(m_Wards);
                for (int i = 0; i < volleys.Length; i++) Wards(received[i].SourceConnection, volleys[i].SkillIds.ToString());
                received.Dispose();
                volleys.Dispose();
            }
            if (!m_Charges.IsEmpty)
            {
                var received = m_Charges.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var charges = m_Charges.ToComponentDataArray<ChargeRpc>(Allocator.Temp);
                em.DestroyEntity(m_Charges);
                for (int i = 0; i < charges.Length; i++) Charge(received[i].SourceConnection, charges[i]);
                received.Dispose();
                charges.Dispose();
            }
            if (!m_Casts.IsEmpty)
            {
                var received = m_Casts.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var casts = m_Casts.ToComponentDataArray<SkillCastRpc>(Allocator.Temp);
                em.DestroyEntity(m_Casts);
                for (int i = 0; i < casts.Length; i++) Cast(received[i].SourceConnection, casts[i].SkillId.ToString(), casts[i].TargetGhostId);
                received.Dispose();
                casts.Dispose();
            }
            if (!m_PackOps.IsEmpty)
            {
                var received = m_PackOps.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var ops = m_PackOps.ToComponentDataArray<PackOpRpc>(Allocator.Temp);
                em.DestroyEntity(m_PackOps);
                for (int i = 0; i < ops.Length; i++) RunPackOp(received[i].SourceConnection, ops[i]);
                received.Dispose();
                ops.Dispose();
            }
            if (!m_Storage.IsEmpty)
            {
                var received = m_Storage.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var asks = m_Storage.ToComponentDataArray<StorageOpRpc>(Allocator.Temp);
                em.DestroyEntity(m_Storage);
                // NGO: items by the Items limit, Creds by the Money limit, then PlayerStorage's own rules on the server body
                for (int i = 0; i < asks.Length; i++)
                {
                    var op = (StorageOpKind)asks[i].Op;
                    CallKind kind = op == StorageOpKind.DepositCreds || op == StorageOpKind.WithdrawCreds ? CallKind.Money : CallKind.Items;
                    if (!BodyOf(received[i].SourceConnection, kind, out GameObject body)) continue;
                    foreach (var storage in body.GetComponents<IBodyStorage>())
                        if (storage.StorageKind == asks[i].Kind) { storage.RunStorageOp(op, asks[i].A, asks[i].B, asks[i].Amount); StorageOps++; break; }
                }
                received.Dispose();
                asks.Dispose();
            }
            if (!m_Buys.IsEmpty)
            {
                var received = m_Buys.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var asks = m_Buys.ToComponentDataArray<ShopBuyRpc>(Allocator.Temp);
                em.DestroyEntity(m_Buys);
                // NGO: the Money limit, then PlayerShopping's own rules on the server body (reach, price, week limit, room, wallet, ledger, save)
                for (int i = 0; i < asks.Length; i++)
                    if (BodyOf(received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out IBodyShopping shop))
                    {
                        shop.RunBuy(asks[i].Vendor.ToString(), asks[i].ItemId, asks[i].Count);
                        ShopOps++;
                    }
                received.Dispose();
                asks.Dispose();
            }
            if (!m_Sells.IsEmpty)
            {
                var received = m_Sells.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var asks = m_Sells.ToComponentDataArray<ShopSellRpc>(Allocator.Temp);
                em.DestroyEntity(m_Sells);
                for (int i = 0; i < asks.Length; i++)
                    if (BodyOf(received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out IBodyShopping shop))
                    {
                        shop.RunSell(asks[i].Cell, asks[i].Count, new ItemStack(asks[i].ItemId, asks[i].StackCount, asks[i].Data), asks[i].Confirmed != 0);
                        ShopOps++;
                    }
                received.Dispose();
                asks.Dispose();
            }
            if (!m_Trades.IsEmpty)
            {
                var received = m_Trades.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var asks = m_Trades.ToComponentDataArray<TradeOpRpc>(Allocator.Temp);
                em.DestroyEntity(m_Trades);
                // NGO: every trade RPC at the Money limit, then PlayerTrade's own rules on the server body
                for (int i = 0; i < asks.Length; i++)
                    if (BodyOf(received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out IBodyTrade trade))
                    {
                        trade.RunTradeOp((TradeOpKind)asks[i].Op, asks[i].Name.ToString(), asks[i].A, asks[i].B, asks[i].Creds, asks[i].Flag != 0);
                        TradeOps++;
                    }
                received.Dispose();
                asks.Dispose();
            }
            if (!m_Pickups.IsEmpty)
            {
                var received = m_Pickups.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var asks = m_Pickups.ToComponentDataArray<PickupRpc>(Allocator.Temp);
                em.DestroyEntity(m_Pickups);
                // NGO: the Items limit, then PlayerLoot's own rules on the server body (alive, reach, hold, faction, Creds to the wallet, ledger)
                for (int i = 0; i < asks.Length; i++)
                    if (BodyOf(received[i].SourceConnection, CallKind.Items, out GameObject body) && body.TryGetComponent(out PlayerLoot loot))
                    {
                        loot.RunPickup(asks[i].DropId);
                        Pickups++;
                    }
                received.Dispose();
                asks.Dispose();
            }
            if (!m_Drops.IsEmpty)
            {
                var received = m_Drops.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var asks = m_Drops.ToComponentDataArray<DropItemRpc>(Allocator.Temp);
                em.DestroyEntity(m_Drops);
                for (int i = 0; i < asks.Length; i++)
                    if (BodyOf(received[i].SourceConnection, CallKind.Items, out GameObject body) && body.TryGetComponent(out PlayerLoot loot))
                    {
                        loot.RunDrop(new ItemLocation { container = (ItemContainer)asks[i].Container, index = asks[i].Index });
                        Drops++;
                    }
                received.Dispose();
                asks.Dispose();
            }
            if (!m_MoveUpgrades.IsEmpty)
            {
                var received = m_MoveUpgrades.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var asks = m_MoveUpgrades.ToComponentDataArray<MovementUpgradeRpc>(Allocator.Temp);
                em.DestroyEntity(m_MoveUpgrades);
                for (int i = 0; i < asks.Length; i++)
                    if (BodyOf(received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out PlayerMovementSkills moves))
                    {
                        moves.RunUpgrade((MovementSkillId)asks[i].Skill, asks[i].Count);
                        Upgrades++;
                    }
                received.Dispose();
                asks.Dispose();
            }
            if (!m_Upgrades.IsEmpty)
            {
                var received = m_Upgrades.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var asks = m_Upgrades.ToComponentDataArray<SkillUpgradeRpc>(Allocator.Temp);
                em.DestroyEntity(m_Upgrades);
                // NGO: the Money limit, then the server body's own rules (unlocked, level, max rank, skill points)
                for (int i = 0; i < asks.Length; i++)
                    if (BodyOf(received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out PlayerCombatSkills skills))
                    {
                        skills.RunUpgrade(asks[i].SkillId.ToString(), asks[i].Count);
                        Upgrades++;
                    }
                received.Dispose();
                asks.Dispose();
            }
            if (!m_Allocations.IsEmpty)
            {
                var received = m_Allocations.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var asks = m_Allocations.ToComponentDataArray<AllocateRpc>(Allocator.Temp);
                em.DestroyEntity(m_Allocations);
                // NGO: the Money limit, then the server body's own rules (points held, the attribute's maximum)
                for (int i = 0; i < asks.Length; i++)
                    if (BodyOf(received[i].SourceConnection, CallKind.Money, out GameObject body) && body.TryGetComponent(out PlayerCharacterProgression progression))
                    {
                        progression.RunAllocate((CharacterAttribute)asks[i].Attribute, asks[i].Count);
                        Allocations++;
                    }
                received.Dispose();
                asks.Dispose();
            }
            if (!m_Refills.IsEmpty)
            {
                var received = m_Refills.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                em.DestroyEntity(m_Refills);
                // NGO: the Items limit, then the server body's own rules (alive, an Apothecary in reach, the cost through the wallet, save)
                for (int i = 0; i < received.Length; i++)
                    if (BodyOf(received[i].SourceConnection, CallKind.Items, out GameObject body) && body.TryGetComponent(out PlayerFlask flask))
                    {
                        flask.RunRefill();
                        Refills++;
                    }
                received.Dispose();
            }
            if (!m_LoadoutOps.IsEmpty)
            {
                var received = m_LoadoutOps.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var ops = m_LoadoutOps.ToComponentDataArray<LoadoutOpRpc>(Allocator.Temp);
                em.DestroyEntity(m_LoadoutOps);
                for (int i = 0; i < ops.Length; i++)
                    if (BodyOf(received[i].SourceConnection, CallKind.Items, out GameObject body) && body.TryGetComponent(out PlayerLoadouts loadouts))
                    {
                        loadouts.RunLoadoutOp((LoadoutOpKind)ops[i].Kind, ops[i].Index, ops[i].Name.ToString());
                        LoadoutOps++;
                    }
                received.Dispose();
                ops.Dispose();
            }
        }

        /// <summary>The server body of the connection's player; false (dropped) when there is none or the request limit says no.</summary>
        bool BodyOf(Entity connection, CallKind kind, out GameObject body)
        {
            if (TryBody(World, connection, kind, out body, out bool overBudget)) return true;
            if (overBudget) OverBudget++;
            return false;
        }

        /// <summary>
        /// The server body of the player on <paramref name="connection"/> when its request limit <paramref name="kind"/> allows one more (NGO's
        /// RpcBudget.Allow on the caller), for every request system of the entity netcode.
        /// </summary>
        public static bool TryBody(World server, Entity connection, CallKind kind, out GameObject body) => TryBody(server, connection, kind, out body, out _);

        static bool TryBody(World server, Entity connection, CallKind kind, out GameObject body, out bool overBudget)
        {
            body = null;
            overBudget = false;
            var em = server.EntityManager;
            if (connection == Entity.Null || !em.Exists(connection) || !em.HasComponent<NetworkId>(connection)) return false;
            if (!GhostBodyRegistry.TryGetByNetworkId(server, em.GetComponentData<NetworkId>(connection).Value, out GhostBodyEntry entry) || entry.Body == null)
                return false;
            if (entry.Key != PlayerKey.None && RpcBudget.Spend(entry.Key, kind, UnityEngine.Time.realtimeSinceStartup) != RpcBudget.SpendResult.Allowed)
            {
                overBudget = true;
                return false;
            }
            body = entry.Body;
            return true;
        }

        // NGO's Move/QuickEquip/QuickUnequip/Destroy/Sort RPCs: the Items request limit, then the server body's own rules (refusals go to the
        // owner's message line from the body); the Pack mirror sends what changed.
        void RunPackOp(Entity connection, in PackOpRpc op)
        {
            if (!BodyOf(connection, CallKind.Items, out GameObject body)) return;
            var inventory = body.GetComponent<PlayerInventory>();
            if (inventory == null) return;
            var from = new ItemLocation { container = (ItemContainer)op.FromContainer, index = op.FromIndex };
            var to = new ItemLocation { container = (ItemContainer)op.ToContainer, index = op.ToIndex };
            inventory.RunPackOp((PackOpKind)op.Kind, from, to);
            PackOps++;
        }

        void UseItem(Entity connection, ushort itemId)
        {
            if (!BodyOf(connection, CallKind.Items, out GameObject body)) return;
            if (UseOn(body, itemId, out RequestReplyRpc reply)) Used++; else Refused++;
            if (reply.Kind != 0) Reply(connection, reply);
        }

        /// <summary>
        /// One use of <paramref name="itemId"/> on a server body, by the inventory's own rules (NGO's <c>UseItem</c> -> <c>ServerUse</c>):
        /// true when it was used. <paramref name="reply"/> is the answer for its player (Kind 0 when there is nothing to say).
        /// </summary>
        public static bool UseOn(GameObject body, ushort itemId, out RequestReplyRpc reply)
        {
            reply = default;
            var inventory = body != null ? body.GetComponent<PlayerInventory>() : null;
            if (inventory == null) return false;
            // an item whose effect lands in a part this server still passes through would be spent from the (real) Pack and its
            // effect lost at the next save: refused, nothing taken, until that system's port (M8 research, 2 Nov: mounts, boosts, Dossiers)
            if (inventory.Catalog != null && inventory.Catalog.Get(itemId) is ItemDefinition def && !EffectPorted(def.useEffect))
            {
                reply = new RequestReplyRpc { Kind = RequestReplyRpc.ItemRefused, Text = Fit(NotPortedText) };
                return false;
            }
            string refused = null;
            bool used = false, health = false, flux = false;
            Action<string> onRefused = reason => refused ??= reason;
            Action<ushort, bool, bool> onUsed = (id, h, f) => { used = true; health = h; flux = f; };
            inventory.RequestFailed += onRefused;
            inventory.ItemUsed += onUsed;
            try { inventory.UseItem(itemId); }
            finally
            {
                inventory.RequestFailed -= onRefused;
                inventory.ItemUsed -= onUsed;
            }
            if (used) reply = new RequestReplyRpc { Kind = RequestReplyRpc.ItemUsed, Health = (byte)(health ? 1 : 0), Flux = (byte)(flux ? 1 : 0) };
            else if (refused != null) reply = new RequestReplyRpc { Kind = RequestReplyRpc.ItemRefused, Text = Fit(refused) };
            return used;
        }

        /// <summary>What a refused item use of an effect not ported yet tells the player.</summary>
        public const string NotPortedText = "This item can't be used on this server yet.";

        /// <summary>
        /// The item effects whose result this server saves (or that save nothing): HP/Flux stims, the Flask (its drinks are on the bottle),
        /// a pet's ration (its growth is on the pet item), a Serum (its part is real), Recall (a move). Every other effect writes a part that
        /// is still passed through (Autopilot, Dossiers) or has its own flow (Name Change), so it waits for its port.
        /// </summary>
        public static bool EffectPorted(ItemUseEffect effect) => effect == ItemUseEffect.None || effect == ItemUseEffect.FluxFlask
            || effect == ItemUseEffect.FeedPet || effect == ItemUseEffect.Serum || effect == ItemUseEffect.Recall
            || effect == ItemUseEffect.ExperienceBoost    // M8 8.3.3: the "xpboosts" part is real (B13)
            || effect == ItemUseEffect.LearnSkill         // M8 8.3.2: the "skills" part is real (B14)
            || effect == ItemUseEffect.AutobuffTime       // M8 8.3.11: the "autobuff" part is real (B26)
            || effect == ItemUseEffect.RegisterMount || effect == ItemUseEffect.FeedMount;   // M8 8.4.8: the "mounts" part is real (B38)

        void Cast(Entity connection, string skillId, int targetGhostId)
        {
            if (!BodyOf(connection, CallKind.Combat, out GameObject body)) return;
            var skills = body.GetComponent<PlayerCombatSkills>();
            SkillDefinitionSO skill = skills != null ? skills.GetSkill(skillId) : null;
            if (skill == null) return;
            // M4.5a: an attack is cast by NGO's own server cast on this body at the target's server body (range, Flux, cooldown and the
            // hits through ResolveHit decided here); an attack whose target is gone casts at nothing and is refused by CanCast
            Transform target = skill.IsAttack && !skill.IsAreaAttack ? ServerBodyOf(targetGhostId) : null;
            if (skills.ServerTryCast(skill, target, out string reason))
            {
                Used++;
                RecordCast(body, skillId, (short)skills.GetRank(skill), skill.IsAttack ? targetGhostId : 0);
                Reply(connection, new RequestReplyRpc { Kind = RequestReplyRpc.SkillCast, SkillId = new FixedString64Bytes(skillId), Rank = (short)skills.GetRank(skill) });
                return;
            }
            Refused++;
            Reply(connection, new RequestReplyRpc { Kind = RequestReplyRpc.SkillRefused, SkillId = new FixedString64Bytes(skillId), Text = Fit(reason) });
        }

        // M4 step 7: an Autoward volley (NGO CastWardsServerRpc -> ServerCastWards -> WardCastClientRpc, NotifyCastFailedClientRpc)
        readonly List<SkillDefinitionSO> m_VolleyCast = new List<SkillDefinitionSO>();
        void Wards(Entity connection, string skillIds)
        {
            if (!BodyOf(connection, CallKind.Combat, out GameObject body)) return;
            var skills = body.GetComponent<PlayerCombatSkills>();
            if (skills == null) return;
            if (skills.RunWards(skillIds, m_VolleyCast) == 0)
            {
                Refused++;
                Reply(connection, new RequestReplyRpc { Kind = RequestReplyRpc.SkillRefused, Text = Fit("No ward could be cast now.") });
                return;
            }
            Used++;
            for (int i = 0; i < m_VolleyCast.Count; i++)
            {
                SkillDefinitionSO ward = m_VolleyCast[i];
                short rank = (short)skills.GetRank(ward);
                // the others see the volley as one cast, the first (NGO shows it once); every ward's buff reaches them on the running list
                if (i == 0) RecordCast(body, ward.skillId, rank, 0);
                Reply(connection, new RequestReplyRpc
                {
                    Kind = RequestReplyRpc.WardCast, SkillId = new FixedString64Bytes(ward.skillId), Rank = rank, Shown = (byte)(i == 0 ? 1 : 0),
                });
            }
            m_VolleyCast.Clear();
        }

        // M4.5f: the owner's charge on its ghost for every other client (NGO ChargeStartedRpc / ChargeEndedRpc, owner-invoked)
        void Charge(Entity connection, ChargeRpc charge)
        {
            if (charge.Phase < 1 || charge.Phase > 3 || !BodyOf(connection, CallKind.Combat, out GameObject body)) return;
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(e.World, World) || e.Body != body || !em.Exists(e.Ghost) || !em.HasComponent<KryticalNova.Player.Entities.ChargeState>(e.Ghost)) continue;
                var state = em.GetComponentData<KryticalNova.Player.Entities.ChargeState>(e.Ghost);
                em.SetComponentData(e.Ghost, new KryticalNova.Player.Entities.ChargeState
                {
                    Seq = (byte)(state.Seq + 1), SkillId = charge.SkillId, AimGhostId = charge.AimGhostId, Phase = charge.Phase,
                });
                return;
            }
        }

        // M4.5c: the cast on the caster's ghost for every other client (NGO TriggerSkillCastClientRpc)
        void RecordCast(GameObject body, string skillId, short rank, int targetGhostId)
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(e.World, World) || e.Body != body || !em.Exists(e.Ghost) || !em.HasBuffer<KryticalNova.Player.Entities.CastEvent>(e.Ghost)) continue;
                var seq = em.GetComponentData<KryticalNova.Player.Entities.CastEventSeq>(e.Ghost);
                KryticalNova.Player.Entities.CastEventRing.Append(em.GetBuffer<KryticalNova.Player.Entities.CastEvent>(e.Ghost), ref seq,
                    new KryticalNova.Player.Entities.CastEvent { SkillId = new FixedString64Bytes(skillId), Rank = rank, TargetGhostId = targetGhostId });
                em.SetComponentData(e.Ghost, seq);
                return;
            }
        }

        /// <summary>The server body of the player ghost <paramref name="ghostId"/> in this world, or null.</summary>
        Transform ServerBodyOf(int ghostId)
        {
            if (ghostId == 0) return null;
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
                    && em.GetComponentData<GhostInstance>(e.Ghost).ghostId == ghostId)
                    return e.Body.transform;
            return CombatTargetRegistry.BodyOf(World, ghostId);   // an attackable ghost that is not a player (M4.2b: the Strike Dummy)
        }

        void Reply(Entity connection, RequestReplyRpc reply)
        {
            Entity rpc = EntityManager.CreateEntity();
            EntityManager.AddComponentData(rpc, reply);
            EntityManager.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }

        static FixedString128Bytes Fit(string text)
        {
            text ??= string.Empty;
            if (text.Length > 120) text = text.Substring(0, 120);
            return new FixedString128Bytes(text);
        }
    }

    /// <summary>
    /// Server: sends each player's Pack cells to their own client when they change (the server's body is where items are used; the
    /// owner's HUD and hotbar show its counts). On arrival the owner's Pack is cleared and every held cell sent.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerPackSyncServerSystem : SystemBase
    {
        /// <summary>Seconds between looks at the Packs.</summary>
        public float Interval = 0.1f;

        readonly Dictionary<Entity, ItemStack[]> m_Sent = new Dictionary<Entity, ItemStack[]>();
        readonly Dictionary<Entity, LoadoutEntry[]> m_SentLoadouts = new Dictionary<Entity, LoadoutEntry[]>();
        readonly Dictionary<Entity, (float[] seconds, float nextSend)> m_SentClocks = new Dictionary<Entity, (float[], float)>();
        readonly Dictionary<Entity, (List<byte> ranks, ulong books)> m_SentSkills = new Dictionary<Entity, (List<byte>, ulong)>();
        readonly Dictionary<(Entity, byte), (ItemStack[] cells, long creds)> m_SentStorage = new Dictionary<(Entity, byte), (ItemStack[], long)>();
        readonly List<(Entity, byte)> m_StorageGone = new List<(Entity, byte)>();
        readonly Dictionary<Entity, (long week, Dictionary<ushort, int> bought)> m_SentWeeks = new Dictionary<Entity, (long, Dictionary<ushort, int>)>();
        readonly List<byte> m_Ranks = new List<byte>();

        /// <summary>NGO's PlayerExperienceBoosts.SendSeconds: a running clock is sent at most this often between uses and run-outs.</summary>
        public const float ClockSendSeconds = 15f;
        readonly Dictionary<int, Entity> m_Connections = new Dictionary<int, Entity>();
        readonly List<Entity> m_Gone = new List<Entity>();
        EntityQuery m_InGame;
        float m_Next;

        /// <summary>Cells and loadout slots sent (tests).</summary>
        public int CellsSent { get; private set; }
        public int LoadoutSlotsSent { get; private set; }
        public int ClocksSent { get; private set; }
        public int SkillCellsSent { get; private set; }
        public int StorageCellsSent { get; private set; }
        public int WeeksSent { get; private set; }

        protected override void OnCreate() =>
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());

        protected override void OnUpdate()
        {
            if (UnityEngine.Time.realtimeSinceStartup < m_Next) return;
            m_Next = UnityEngine.Time.realtimeSinceStartup + Interval;
            var em = EntityManager;
            m_Connections.Clear();
            var connections = m_InGame.ToEntityArray(Allocator.Temp);
            var ids = m_InGame.ToComponentDataArray<NetworkId>(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++) m_Connections[ids[i].Value] = connections[i];
            connections.Dispose();
            ids.Dispose();

            m_Gone.Clear();
            foreach (Entity ghost in m_Sent.Keys) if (!em.Exists(ghost)) m_Gone.Add(ghost);
            foreach (Entity ghost in m_Gone) { m_Sent.Remove(ghost); m_SentLoadouts.Remove(ghost); m_SentClocks.Remove(ghost); m_SentSkills.Remove(ghost); m_SentWeeks.Remove(ghost); }
            m_StorageGone.Clear();
            foreach (var k in m_SentStorage.Keys) if (!em.Exists(k.Item1)) m_StorageGone.Add(k);
            foreach (var k in m_StorageGone) m_SentStorage.Remove(k);

            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost)) continue;
                if (!m_Connections.TryGetValue(entry.NetworkId, out Entity connection)) continue;
                var inventory = entry.Body.GetComponent<PlayerInventory>();
                if (inventory == null) continue;
                if (!m_Sent.TryGetValue(entry.Ghost, out ItemStack[] sent))
                {
                    sent = new ItemStack[PlayerInventory.Capacity + WornCells];
                    // every worn slot once on arrival, empty ones too (the view's prefab may wear its own starter pieces)
                    for (int slot = 1; slot < WornCells; slot++) sent[PlayerInventory.Capacity + slot] = new ItemStack(ushort.MaxValue, 0, 0);
                    m_Sent[entry.Ghost] = sent;
                    Send(connection, new PackCellRpc { Index = PackCellRpc.ClearAll });
                }
                for (int i = 0; i < PlayerInventory.Capacity; i++)
                {
                    ItemStack now = inventory.Get(i);
                    if (now.Equals(sent[i])) continue;
                    sent[i] = now;
                    Send(connection, new PackCellRpc { Index = (byte)i, ItemId = now.itemId, Count = now.count, Data = now.data });
                    CellsSent++;
                }
                // the loadout slots (NGO: the owner-only list), all ten on arrival, then each one that changed
                if (entry.Body.TryGetComponent(out PlayerLoadouts loadouts))
                {
                    bool arriving = !m_SentLoadouts.TryGetValue(entry.Ghost, out LoadoutEntry[] had);
                    if (arriving) m_SentLoadouts[entry.Ghost] = had = new LoadoutEntry[LoadoutRules.Slots];
                    for (int i = 0; i < LoadoutRules.Slots; i++)
                    {
                        LoadoutEntry now = loadouts.Get(i);
                        if (!arriving && now.Equals(had[i])) continue;
                        had[i] = now;
                        Entity slotRpc = em.CreateEntity();
                        em.AddComponentData(slotRpc, LoadoutSlotRpc.Of(i, now));
                        em.AddComponentData(slotRpc, new SendRpcCommandRequest { TargetConnection = connection });
                        LoadoutSlotsSent++;
                    }
                }
                // the Vault and Deep Vault cells and Creds (NGO: owner-only list + variable each), emptied then filled on arrival, then changes
                foreach (var storage in entry.Body.GetComponents<IBodyStorage>())
                {
                    var key = (entry.Ghost, storage.StorageKind);
                    bool arriving = !m_SentStorage.TryGetValue(key, out var told);
                    if (arriving)
                    {
                        told = (new ItemStack[storage.CellCount], long.MinValue);
                        Entity clear = em.CreateEntity();
                        em.AddComponentData(clear, new StorageCellRpc { Kind = storage.StorageKind, Index = StorageCellRpc.ClearAll });
                        em.AddComponentData(clear, new SendRpcCommandRequest { TargetConnection = connection });
                    }
                    for (int i = 0; i < told.cells.Length; i++)
                    {
                        ItemStack now = storage.Get(i);
                        if (now.Equals(told.cells[i])) continue;
                        told.cells[i] = now;
                        Entity cellRpc = em.CreateEntity();
                        em.AddComponentData(cellRpc, new StorageCellRpc { Kind = storage.StorageKind, Index = (byte)i, ItemId = now.itemId, Count = now.count, Data = now.data });
                        em.AddComponentData(cellRpc, new SendRpcCommandRequest { TargetConnection = connection });
                        StorageCellsSent++;
                    }
                    if (storage.Creds != told.creds)
                    {
                        told.creds = storage.Creds;
                        Entity credsRpc = em.CreateEntity();
                        em.AddComponentData(credsRpc, new StorageCredsRpc { Kind = storage.StorageKind, Creds = storage.Creds });
                        em.AddComponentData(credsRpc, new SendRpcCommandRequest { TargetConnection = connection });
                    }
                    m_SentStorage[key] = told;
                }
                // this week's limited purchases (NGO: the owner-only netBoughtWeek + netBought), on arrival, then whenever the week or a count changes
                if (entry.Body.TryGetComponent(out IBodyShopping shopping))
                {
                    bool arriving = !m_SentWeeks.TryGetValue(entry.Ghost, out var told);
                    if (arriving || !SameWeek(told.week, told.bought, shopping))
                    {
                        told = (shopping.BoughtWeek, new Dictionary<ushort, int>(shopping.Bought));
                        m_SentWeeks[entry.Ghost] = told;
                        SendWeek(em, connection, new VendorWeekRpc { Week = told.week, Begin = 1 });
                        foreach (KeyValuePair<ushort, int> bought in told.bought)
                            SendWeek(em, connection, new VendorWeekRpc { Week = told.week, ItemId = bought.Key, Count = (ushort)Mathf.Clamp(bought.Value, 0, ushort.MaxValue) });
                        WeeksSent++;
                    }
                }
                // the skills' ranks and unlocked books (NGO: the owner-only netRanks and netUnlockedBooks), all on arrival, then changes
                if (entry.Body.TryGetComponent(out PlayerCombatSkills combatSkills))
                {
                    ulong books = combatSkills.ReadRanks(m_Ranks);
                    bool arriving = !m_SentSkills.TryGetValue(entry.Ghost, out var had);
                    if (arriving) had = (new List<byte>(), ~books);
                    for (int i = 0; i < m_Ranks.Count && i < 256; i++)
                    {
                        if (!arriving && i < had.ranks.Count && had.ranks[i] == m_Ranks[i]) continue;
                        while (had.ranks.Count <= i) had.ranks.Add(0);
                        had.ranks[i] = m_Ranks[i];
                        Entity rankRpc = em.CreateEntity();
                        em.AddComponentData(rankRpc, new SkillRankRpc { Index = (byte)i, Rank = m_Ranks[i] });
                        em.AddComponentData(rankRpc, new SendRpcCommandRequest { TargetConnection = connection });
                        SkillCellsSent++;
                    }
                    if (books != had.books)
                    {
                        had.books = books;
                        Entity booksRpc = em.CreateEntity();
                        em.AddComponentData(booksRpc, new SkillBooksRpc { Books = books });
                        em.AddComponentData(booksRpc, new SendRpcCommandRequest { TargetConnection = connection });
                        SkillCellsSent++;
                    }
                    m_SentSkills[entry.Ghost] = had;
                }
                // the XP boosters' clocks (NGO: the owner-only list; exact on a use or run-out, else every 15 s)
                if (entry.Body.TryGetComponent(out PlayerExperienceBoosts boosts) && boosts.Count > 0)
                {
                    float now = UnityEngine.Time.time;
                    bool arriving = !m_SentClocks.TryGetValue(entry.Ghost, out var clocks) || clocks.seconds.Length != boosts.Count;
                    if (arriving) clocks = (new float[boosts.Count], 0f);
                    bool due = now >= clocks.nextSend;
                    bool sentAny = false;
                    for (int i = 0; i < boosts.Count; i++)
                    {
                        float left = boosts.SecondsLeft(i);
                        float had = clocks.seconds[i];
                        bool jump = (left > 0f) != (had > 0f) || left > had + 1f;   // a use (more time) or a run-out
                        if (!arriving && !jump && !(due && !Mathf.Approximately(left, had))) continue;
                        clocks.seconds[i] = left;
                        Entity clockRpc = em.CreateEntity();
                        em.AddComponentData(clockRpc, new XpBoostClockRpc { Index = (byte)i, Seconds = left });
                        em.AddComponentData(clockRpc, new SendRpcCommandRequest { TargetConnection = connection });
                        sentAny = true;
                        ClocksSent++;
                    }
                    if (arriving || sentAny && due) clocks.nextSend = now + ClockSendSeconds;
                    m_SentClocks[entry.Ghost] = clocks;
                }
                // the worn pieces with their own state (NGO: the loadout and netEquippedData to the owner), after the Pack's cells
                for (int slot = 1; slot < WornCells; slot++)
                {
                    ItemStack now = inventory.GetEquipped((EquipSlot)slot);
                    int at = PlayerInventory.Capacity + slot;
                    if (now.Equals(sent[at])) continue;
                    sent[at] = now;
                    Send(connection, new PackCellRpc { Index = (byte)at, ItemId = now.itemId, Count = now.count, Data = now.data });
                    CellsSent++;
                }
            }
        }

        /// <summary>Cells after the Pack's: one per equipment slot (index Capacity + slot; slot 0 is None).</summary>
        public const int WornCells = (int)EquipSlot.Pet + 1;

        static bool SameWeek(long week, Dictionary<ushort, int> told, IBodyShopping shopping)
        {
            if (week != shopping.BoughtWeek || told.Count != shopping.Bought.Count) return false;
            foreach (KeyValuePair<ushort, int> bought in shopping.Bought)
                if (!told.TryGetValue(bought.Key, out int count) || count != bought.Value) return false;
            return true;
        }

        static void SendWeek(EntityManager em, Entity connection, VendorWeekRpc line)
        {
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, line);
            em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }

        void Send(Entity connection, PackCellRpc cell)
        {
            Entity rpc = EntityManager.CreateEntity();
            EntityManager.AddComponentData(rpc, cell);
            EntityManager.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
        }
    }

    /// <summary>
    /// Client: the server's answers and Pack cells reach the local view body: a use's feel, a refusal's words, a cast shown by the
    /// body's copy of its skills, and the Pack cells mirrored.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerRequestClientSystem : SystemBase
    {
        EntityQuery m_Replies, m_Cells, m_Tells, m_Slots, m_Clocks, m_SkillRanks, m_SkillBooks, m_StorageCells, m_StorageCreds, m_Weeks, m_TradeLines, m_TradeViews, m_TradeNotices, m_TradePrompts;

        /// <summary>Answers, cells and the server's lines applied (tests).</summary>
        public int Replies { get; private set; }
        public int Cells { get; private set; }
        public int Tells { get; private set; }

        protected override void OnCreate()
        {
            m_Replies = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<RequestReplyRpc>());
            m_Cells = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<PackCellRpc>());
            m_Tells = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<ServerTellRpc>());
            m_Slots = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<LoadoutSlotRpc>());
            m_Clocks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<XpBoostClockRpc>());
            m_SkillRanks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<SkillRankRpc>());
            m_SkillBooks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<SkillBooksRpc>());
            m_StorageCells = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<StorageCellRpc>());
            m_StorageCreds = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<StorageCredsRpc>());
            m_Weeks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<VendorWeekRpc>());
            m_TradeLines = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<TradeLineRpc>());
            m_TradeViews = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<TradeViewRpc>());
            m_TradeNotices = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<TradeNoticeRpc>());
            m_TradePrompts = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<TradePromptRpc>());
        }

        // a trade view's lines wait here for their header (same Seq), which the server sends after them
        readonly Dictionary<uint, (List<BodyTradeLine> mine, List<BodyTradeLine> theirs)> m_TradePending =
            new Dictionary<uint, (List<BodyTradeLine>, List<BodyTradeLine>)>();
        readonly List<uint> m_TradeOld = new List<uint>();

        protected override void OnUpdate()
        {
            var em = EntityManager;
            if (!m_Tells.IsEmpty)
            {
                var tells = m_Tells.ToComponentDataArray<ServerTellRpc>(Allocator.Temp);
                em.DestroyEntity(m_Tells);
                foreach (ServerTellRpc tell in tells)
                {
                    ClientPresentationRegistry.Messages?.ShowMessage(tell.Text.ToString());
                    Tells++;
                }
                tells.Dispose();
            }
            GameObject body = LocalBody();
            if (!m_Cells.IsEmpty)
            {
                var cells = m_Cells.ToComponentDataArray<PackCellRpc>(Allocator.Temp);
                em.DestroyEntity(m_Cells);
                var inventory = body != null ? body.GetComponent<PlayerInventory>() : null;
                if (inventory != null)
                    foreach (PackCellRpc cell in cells)
                    {
                        if (cell.Index == PackCellRpc.ClearAll)
                            for (int i = 0; i < PlayerInventory.Capacity; i++) inventory.MirrorCell(i, ItemStack.Empty);
                        else if (cell.Index >= PlayerInventory.Capacity)
                            inventory.MirrorWorn((EquipSlot)(cell.Index - PlayerInventory.Capacity), new ItemStack(cell.ItemId, cell.Count, cell.Data));
                        else inventory.MirrorCell(cell.Index, new ItemStack(cell.ItemId, cell.Count, cell.Data));
                        Cells++;
                    }
                cells.Dispose();
            }
            if (!m_StorageCells.IsEmpty || !m_StorageCreds.IsEmpty)
            {
                var cellRpcs = m_StorageCells.ToComponentDataArray<StorageCellRpc>(Allocator.Temp);
                var credRpcs = m_StorageCreds.ToComponentDataArray<StorageCredsRpc>(Allocator.Temp);
                em.DestroyEntity(m_StorageCells);
                em.DestroyEntity(m_StorageCreds);
                var storages = body != null ? body.GetComponents<IBodyStorage>() : null;
                if (storages != null)
                    foreach (var storage in storages)
                    {
                        foreach (StorageCellRpc c in cellRpcs)
                        {
                            if (c.Kind != storage.StorageKind) continue;
                            if (c.Index == StorageCellRpc.ClearAll)
                                for (int i = 0; i < storage.CellCount; i++) storage.MirrorCell(i, ItemStack.Empty);
                            else storage.MirrorCell(c.Index, new ItemStack(c.ItemId, c.Count, c.Data));
                        }
                        foreach (StorageCredsRpc c in credRpcs) if (c.Kind == storage.StorageKind) storage.MirrorCreds(c.Creds);
                    }
                cellRpcs.Dispose();
                credRpcs.Dispose();
            }
            if (!m_TradeLines.IsEmpty || !m_TradeViews.IsEmpty || !m_TradeNotices.IsEmpty || !m_TradePrompts.IsEmpty)
            {
                var lines = m_TradeLines.ToComponentDataArray<TradeLineRpc>(Allocator.Temp);
                var views = m_TradeViews.ToComponentDataArray<TradeViewRpc>(Allocator.Temp);
                var notices = m_TradeNotices.ToComponentDataArray<TradeNoticeRpc>(Allocator.Temp);
                var prompts = m_TradePrompts.ToComponentDataArray<TradePromptRpc>(Allocator.Temp);
                em.DestroyEntity(m_TradeLines);
                em.DestroyEntity(m_TradeViews);
                em.DestroyEntity(m_TradeNotices);
                em.DestroyEntity(m_TradePrompts);
                foreach (TradeLineRpc line in lines)
                {
                    if (!m_TradePending.TryGetValue(line.Seq, out var pending))
                        m_TradePending[line.Seq] = pending = (new List<BodyTradeLine>(), new List<BodyTradeLine>());
                    (line.Theirs != 0 ? pending.theirs : pending.mine).Add(new BodyTradeLine(line.Cell, new ItemStack(line.ItemId, line.Count, line.Data)));
                }
                var trade = body != null ? body.GetComponent<IBodyTrade>() : null;
                foreach (TradeViewRpc header in views)
                {
                    m_TradePending.TryGetValue(header.Seq, out var pending);
                    m_TradePending.Remove(header.Seq);
                    if (trade == null) continue;
                    trade.MirrorView(new BodyTradeView
                    {
                        active = (header.Flags & TradeViewRpc.Active) != 0, partnerName = header.PartnerName.ToString(),
                        myCreds = header.MyCreds, theirCreds = header.TheirCreds,
                        myLocked = (header.Flags & TradeViewRpc.MyLocked) != 0, theirLocked = (header.Flags & TradeViewRpc.TheirLocked) != 0,
                        myConfirmed = (header.Flags & TradeViewRpc.MyConfirmed) != 0, theirConfirmed = (header.Flags & TradeViewRpc.TheirConfirmed) != 0,
                        perTrade = header.PerTrade, remainingToday = header.RemainingToday,
                        mine = pending.mine != null ? pending.mine.ToArray() : Array.Empty<BodyTradeLine>(),
                        theirs = pending.theirs != null ? pending.theirs.ToArray() : Array.Empty<BodyTradeLine>(),
                    });
                    // a header answers every earlier view: lines left from those are dropped
                    m_TradeOld.Clear();
                    foreach (uint seq in m_TradePending.Keys) if (seq < header.Seq) m_TradeOld.Add(seq);
                    foreach (uint seq in m_TradeOld) m_TradePending.Remove(seq);
                }
                if (trade != null)
                {
                    foreach (TradeNoticeRpc notice in notices) trade.MirrorNotice(notice.Text.ToString());
                    foreach (TradePromptRpc prompt in prompts)
                    {
                        if (prompt.Close != 0) trade.MirrorPromptClosed();
                        else trade.MirrorPrompt(prompt.From.ToString(), prompt.Seconds);
                    }
                }
                lines.Dispose();
                views.Dispose();
                notices.Dispose();
                prompts.Dispose();
            }
            if (!m_Weeks.IsEmpty)
            {
                var lines = m_Weeks.ToComponentDataArray<VendorWeekRpc>(Allocator.Temp);
                em.DestroyEntity(m_Weeks);
                var shopping = body != null ? body.GetComponent<IBodyShopping>() : null;
                if (shopping != null)
                    foreach (VendorWeekRpc line in lines)
                    {
                        if (line.Begin != 0) shopping.MirrorWeek(line.Week);
                        else shopping.MirrorBought(line.ItemId, line.Count);
                    }
                lines.Dispose();
            }
            if (!m_SkillRanks.IsEmpty || !m_SkillBooks.IsEmpty)
            {
                var skills = body != null ? body.GetComponent<PlayerCombatSkills>() : null;
                var ranks = m_SkillRanks.ToComponentDataArray<SkillRankRpc>(Allocator.Temp);
                var bookSets = m_SkillBooks.ToComponentDataArray<SkillBooksRpc>(Allocator.Temp);
                em.DestroyEntity(m_SkillRanks);
                em.DestroyEntity(m_SkillBooks);
                if (skills != null)
                {
                    foreach (SkillRankRpc rank in ranks) skills.MirrorRank(rank.Index, rank.Rank);
                    foreach (SkillBooksRpc set in bookSets) skills.MirrorBooks(set.Books);
                }
                ranks.Dispose();
                bookSets.Dispose();
            }
            if (!m_Clocks.IsEmpty)
            {
                var clocks = m_Clocks.ToComponentDataArray<XpBoostClockRpc>(Allocator.Temp);
                em.DestroyEntity(m_Clocks);
                var boosts = body != null ? body.GetComponent<PlayerExperienceBoosts>() : null;
                if (boosts != null) foreach (XpBoostClockRpc clock in clocks) boosts.MirrorSeconds(clock.Index, clock.Seconds);
                clocks.Dispose();
            }
            if (!m_Slots.IsEmpty)
            {
                var slots = m_Slots.ToComponentDataArray<LoadoutSlotRpc>(Allocator.Temp);
                em.DestroyEntity(m_Slots);
                var loadouts = body != null ? body.GetComponent<PlayerLoadouts>() : null;
                if (loadouts != null) foreach (LoadoutSlotRpc slot in slots) loadouts.MirrorEntry(slot.Index, slot.Entry);
                slots.Dispose();
            }
            if (!m_Replies.IsEmpty)
            {
                var replies = m_Replies.ToComponentDataArray<RequestReplyRpc>(Allocator.Temp);
                em.DestroyEntity(m_Replies);
                if (body != null) foreach (RequestReplyRpc reply in replies) Show(body, reply);
                replies.Dispose();
            }
        }

        void Show(GameObject body, in RequestReplyRpc reply)
        {
            Replies++;
            switch (reply.Kind)
            {
                case RequestReplyRpc.ItemUsed:
                    ClientPresentationRegistry.Feel?.Consumed(body.transform, reply.Health != 0, reply.Flux != 0);
                    break;
                case RequestReplyRpc.ItemRefused:
                    body.GetComponent<PlayerInventory>()?.ShowRefused(reply.Text.ToString());
                    break;
                case RequestReplyRpc.SkillCast:
                    body.GetComponent<PlayerCombatSkills>()?.ShowServerCast(reply.SkillId.ToString(), reply.Rank);
                    break;
                case RequestReplyRpc.SkillRefused:
                    body.GetComponent<PlayerCombatSkills>()?.ShowCastRefused(reply.Text.ToString());
                    break;
                case RequestReplyRpc.WardCast:
                    body.GetComponent<PlayerCombatSkills>()?.ShowWardCast(reply.SkillId.ToString(), reply.Rank, reply.Shown != 0);
                    break;
            }
        }

        GameObject LocalBody()
        {
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null) return entry.Body;
            return null;
        }
    }
}
