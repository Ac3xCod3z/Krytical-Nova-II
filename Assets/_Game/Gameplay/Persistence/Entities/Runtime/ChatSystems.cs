using System;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The owner's chat line (NGO's <c>PlayerChat</c> SendRpc / SendLinkedRpc; M8 8.4.1): link <c>i</c> is the item in container
    /// <c>Ci</c>, cell <c>Ii</c> of the speaker's own Pack or worn gear (<see cref="LinkCount"/> of them, at most <see cref="ChatLinks.Max"/>).
    /// </summary>
    public struct ChatSendRpc : IRpcCommand
    {
        public byte Channel, LinkCount;
        public byte C0, C1, C2, I0, I1, I2;
        public FixedString512Bytes Text;
        public FixedString64Bytes Target;
    }

    /// <summary>One chat message for its player (NGO's owner RPCs ReceiveRpc / ReceiveLinkedRpc / AnnounceRpc / CombatLineRpc), sent to the connection.</summary>
    public struct ChatLineRpc : IRpcCommand
    {
        public byte Kind, Channel, Outgoing, Announcement, Rarity, Flags, LinkCount;
        public float Damage, Reflected;
        public ushort L0, L1, L2;
        public ulong D0, D1, D2;
        public FixedString64Bytes Sender;
        public FixedString128Bytes Highlight;
        public FixedString512Bytes Text;
    }

    /// <summary>Chat text into the fixed strings of the wire (plain C#): cut on a character boundary, never inside one.</summary>
    public static class ChatText
    {
        public static FixedString512Bytes Text(string text)
        {
            var fixedText = new FixedString512Bytes();
            fixedText.CopyFromTruncated(text ?? string.Empty);
            return fixedText;
        }

        public static FixedString64Bytes Name(string text)
        {
            var fixedText = new FixedString64Bytes();
            fixedText.CopyFromTruncated(text ?? string.Empty);
            return fixedText;
        }

        public static FixedString128Bytes Highlight(string text)
        {
            var fixedText = new FixedString128Bytes();
            fixedText.CopyFromTruncated(text ?? string.Empty);
            return fixedText;
        }

        /// <summary>The send of a line: NGO's arguments, links at most <see cref="ChatLinks.Max"/> (more and it is not sent, as NGO's RPC refused it).</summary>
        public static bool TrySend(ChatChannel channel, string text, string whisperTarget, byte[] containers, byte[] cells, out ChatSendRpc rpc)
        {
            rpc = default;
            int count = containers != null ? containers.Length : 0;
            if (count > ChatLinks.Max || (cells != null ? cells.Length : 0) != count) return false;
            rpc = new ChatSendRpc { Channel = (byte)channel, LinkCount = (byte)count, Text = Text(text), Target = Name(whisperTarget) };
            if (count > 0) { rpc.C0 = containers[0]; rpc.I0 = cells[0]; }
            if (count > 1) { rpc.C1 = containers[1]; rpc.I1 = cells[1]; }
            if (count > 2) { rpc.C2 = containers[2]; rpc.I2 = cells[2]; }
            return true;
        }

        /// <summary>The links of a received send, or null for none; false when the send is not NGO's shape (dropped).</summary>
        public static bool TryLinks(in ChatSendRpc rpc, out byte[] containers, out byte[] cells)
        {
            containers = null;
            cells = null;
            if (rpc.LinkCount > ChatLinks.Max) return false;
            if (rpc.LinkCount == 0) return true;
            containers = new byte[rpc.LinkCount];
            cells = new byte[rpc.LinkCount];
            for (int i = 0; i < rpc.LinkCount; i++)
            {
                containers[i] = i == 0 ? rpc.C0 : i == 1 ? rpc.C1 : rpc.C2;
                cells[i] = i == 0 ? rpc.I0 : i == 1 ? rpc.I1 : rpc.I2;
            }
            return true;
        }

        public static ChatLineRpc Line(in BodyChatLine line)
        {
            var rpc = new ChatLineRpc
            {
                Kind = (byte)line.kind, Channel = (byte)line.channel, Outgoing = (byte)(line.outgoing ? 1 : 0), Announcement = (byte)line.announcement,
                Rarity = line.rarity, Flags = line.flags, Damage = line.damage, Reflected = line.reflected,
                Sender = Name(line.sender), Highlight = Highlight(line.highlight), Text = Text(line.text),
            };
            int links = line.linkItems != null && line.linkData != null && line.linkData.Length == line.linkItems.Length ? Math.Min(line.linkItems.Length, ChatLinks.Max) : 0;
            rpc.LinkCount = (byte)links;
            if (links > 0) { rpc.L0 = line.linkItems[0]; rpc.D0 = line.linkData[0]; }
            if (links > 1) { rpc.L1 = line.linkItems[1]; rpc.D1 = line.linkData[1]; }
            if (links > 2) { rpc.L2 = line.linkItems[2]; rpc.D2 = line.linkData[2]; }
            return rpc;
        }

        public static BodyChatLine Line(in ChatLineRpc rpc)
        {
            var line = new BodyChatLine
            {
                kind = (BodyChatKind)rpc.Kind, channel = (ChatChannel)rpc.Channel, outgoing = rpc.Outgoing != 0, announcement = (AnnouncementKind)rpc.Announcement,
                rarity = rpc.Rarity, flags = rpc.Flags, damage = rpc.Damage, reflected = rpc.Reflected,
                sender = rpc.Sender.ToString(), highlight = rpc.Highlight.ToString(), text = rpc.Text.ToString(),
            };
            int links = Math.Min((int)rpc.LinkCount, ChatLinks.Max);
            if (links > 0)
            {
                line.linkItems = new ushort[links];
                line.linkData = new ulong[links];
                for (int i = 0; i < links; i++)
                {
                    line.linkItems[i] = i == 0 ? rpc.L0 : i == 1 ? rpc.L1 : rpc.L2;
                    line.linkData[i] = i == 0 ? rpc.D0 : i == 1 ? rpc.D1 : rpc.D2;
                }
            }
            return line;
        }
    }

    /// <summary>
    /// Server: <see cref="IChatOwnerLink"/> for the bodies of the entity netcode (M8 8.4.1): the message goes to the connection of the player whose
    /// server body it is, found by its <see cref="PlayerKey"/> (as the trade's link), never by a NetworkId, and never through a ghost (chat must
    /// not depend on who is relevant to whom).
    /// </summary>
    public sealed class EntityChatOwnerLink : IChatOwnerLink
    {
        /// <summary>Messages sent (tests).</summary>
        public static int Sent { get; private set; }

        public bool Line(Component part, in BodyChatLine line)
        {
            if (!EntityTradeOwnerLink.TryConnection(part, out EntityManager em, out Entity connection)) return false;
            Entity rpc = em.CreateEntity();
            em.AddComponentData(rpc, ChatText.Line(line));
            em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            Sent++;
            return true;
        }
    }

    /// <summary>Server: the owners' chat lines at the Chat limit (as NGO) on their server bodies, which apply NGO's channel rules.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ChatServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        /// <summary>Lines run (tests).</summary>
        public int Ran { get; private set; }

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<ChatSendRpc>());

        protected override void OnUpdate()
        {
            if (m_Asks.IsEmpty) return;
            using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
            using var asks = m_Asks.ToComponentDataArray<ChatSendRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Asks);
            for (int i = 0; i < asks.Length; i++)
            {
                if (!ChatText.TryLinks(asks[i], out byte[] containers, out byte[] cells)) continue;
                if (!PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Chat, out GameObject body) || !body.TryGetComponent(out IBodyChat chat)) continue;
                chat.RunSend((ChatChannel)asks[i].Channel, asks[i].Text.ToString(), asks[i].Target.ToString(), containers, cells);
                Ran++;
            }
        }
    }

    /// <summary>Client: the server's chat messages shown by the own view (NGO's Show / ShowAnnounced / ShowCombatLine).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ChatClientSystem : SystemBase
    {
        EntityQuery m_Lines;

        /// <summary>Messages shown (tests).</summary>
        public int Shown { get; private set; }

        protected override void OnCreate() =>
            m_Lines = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<ChatLineRpc>());

        protected override void OnUpdate()
        {
            if (m_Lines.IsEmpty) return;
            using var lines = m_Lines.ToComponentDataArray<ChatLineRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Lines);
            IBodyChat own = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null && entry.Body.TryGetComponent(out own)) break;
            if (own == null) return;
            foreach (ChatLineRpc rpc in lines)
            {
                own.MirrorLine(ChatText.Line(rpc));
                Shown++;
            }
        }
    }
}
