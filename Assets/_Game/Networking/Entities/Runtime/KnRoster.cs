using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities
{
    // ---------------------------------------------------------------------------------------------------------------
    // The roster path (M2b-2): "which characters does my account have?" asked from the menus, before any map is joined.
    // The Netcode for GameObjects server carried the answer in the refusal text of a connection it never let in. Here the
    // question rides in the join text (JoinRules.PayloadText, the sixth part, packed by RosterWire) and the answer comes back
    // as KnRosterReply pages (approval RPCs, at most KnApprovalChunks.MaxChunkBytes characters each, because one RPC is lost
    // when it is bigger than one packet: spike S9). The connection never gets a NetworkId and takes no player place.
    // ---------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Server to client, in the approval phase: page <see cref="Index"/> of <see cref="Count"/> of the packed roster answer
    /// (<c>RosterWire.Reply</c>: ASCII). Pages may arrive in any order.
    /// </summary>
    public struct KnRosterReply : IApprovalRpcCommand
    {
        public int Index;
        public int Count;
        public FixedString512Bytes Data;
    }

    /// <summary>Plain C# paging of the roster answer, so it can be tested without a world.</summary>
    public static class KnRosterPages
    {
        public const int PageChars = KnApprovalChunks.MaxChunkBytes;
        public const int MaxPages = 16;
        /// <summary>Longest packed answer a server sends (the NGO server's limit was 2600; a page budget of 16 allows about 7600).</summary>
        public const int MaxReplyChars = PageChars * MaxPages;

        public static bool TrySplit(string packedReply, List<string> pages) => KnApprovalChunks.TrySplit(packedReply, MaxPages, pages);

        public enum Add { Ok, Complete, Bad }

        /// <summary>Puts pages together in any order. Duplicates are ignored; a page that contradicts the others is Bad and ends the answer.</summary>
        public sealed class Assembler
        {
            private string[] m_Parts;
            private int m_Have;

            public bool IsComplete => m_Parts != null && m_Have == m_Parts.Length;
            public bool IsBad { get; private set; }
            public int Have => m_Have;
            public int Count => m_Parts?.Length ?? 0;

            public Add Take(int index, int count, string data)
            {
                if (IsBad) return Add.Bad;
                if (count < 1 || count > MaxPages || index < 0 || index >= count || data == null || (m_Parts != null && m_Parts.Length != count))
                {
                    IsBad = true;
                    return Add.Bad;
                }
                m_Parts ??= new string[count];
                if (m_Parts[index] == null)
                {
                    m_Parts[index] = data;
                    m_Have++;
                }
                return IsComplete ? Add.Complete : Add.Ok;
            }

            public string Text => IsComplete ? KnApprovalChunks.Join(m_Parts) : null;
        }
    }

    /// <summary>The server's answer to a roster question, as pages or as a refusal in words, with no ECS in it.</summary>
    public static class KnRosterAnswer
    {
        public const string NoRoster = "This server does not keep characters.";
        public const string Unreadable = "The server could not read that request.";
        public const string Failed = "The server could not answer that. Try again.";
        public const string TooLong = "That answer is too long to send.";

        /// <summary>
        /// Asks <paramref name="roster"/> and fills <paramref name="pages"/> with the packed answer. False with the words a player
        /// reads in <paramref name="refusal"/>. Nothing here is logged with the answer in it (it holds characters of one account);
        /// <paramref name="log"/> gets the account and what went wrong.
        /// </summary>
        public static bool TryMake(IServerRoster roster, string account, string oldAccount, string packedRequest, List<string> pages,
            out string refusal, Action<string> log = null)
        {
            refusal = null;
            pages.Clear();
            string request = RosterWire.Unpack(packedRequest);
            if (roster == null) { refusal = NoRoster; return false; }
            if (request == null || string.IsNullOrEmpty(account)) { refusal = Unreadable; return false; }
            string answer;
            try
            {
                answer = roster.Answer(account, oldAccount ?? string.Empty, request);
            }
            catch (Exception e)
            {
                log?.Invoke($"[Roster] A request from {account} failed: {e.GetType().Name}");
                refusal = Failed;
                return false;
            }
            if (answer == null) { refusal = Failed; return false; }
            string reply = RosterWire.Reply(answer);
            if (!KnRosterPages.TrySplit(reply, pages))
            {
                log?.Invoke($"[Roster] The answer for {account} is too long to send ({reply.Length} characters).");
                pages.Clear();
                refusal = TooLong;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Client: gathers the <see cref="KnRosterReply"/> pages of the server's answer. Present in every client world and idle unless
    /// a roster question was asked (<see cref="KnRosterQuery"/> reads <see cref="Text"/>).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    public partial class KnRosterClientSystem : SystemBase
    {
        private readonly KnRosterPages.Assembler m_Pages = new KnRosterPages.Assembler();
        private EntityQuery m_Replies;

        /// <summary>The packed answer once every page is here, else null.</summary>
        public string Text => m_Pages.Text;
        public bool IsBad => m_Pages.IsBad;
        public int PagesHave => m_Pages.Have;

        protected override void OnCreate()
        {
            m_Replies = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<KnRosterReply>());
        }

        protected override void OnUpdate()
        {
            if (m_Replies.IsEmpty) return;
            var pages = m_Replies.ToComponentDataArray<KnRosterReply>(Allocator.Temp);
            for (int i = 0; i < pages.Length; i++) m_Pages.Take(pages[i].Index, pages[i].Count, pages[i].Data.ToString());
            pages.Dispose();
            EntityManager.DestroyEntity(m_Replies);
        }
    }
}
