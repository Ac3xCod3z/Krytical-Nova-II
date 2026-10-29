using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KryticalNova.Core;
using KryticalNova.Networking;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    // ---------------------------------------------------------------------------------------------------------------
    // The dedicated server's real join rule (M2b-1). It composes the same plain C# rules the Netcode for GameObjects server
    // uses (JoinRules, UnityToken, ServerBans through Core's BanLookup) behind small interfaces, so a test world can script
    // each one. It is polled by KnApprovalAssembler once per tick: nothing here waits, awaits or blocks.
    // ---------------------------------------------------------------------------------------------------------------

    public enum KnProofStatus { Pending, Proven, Failed }

    public readonly struct KnProof
    {
        public readonly KnProofStatus Status;
        public readonly string Account;
        public readonly string Why;

        public KnProof(KnProofStatus status, string account, string why)
        {
            Status = status;
            Account = account;
            Why = why;
        }

        public static KnProof Pending => new KnProof(KnProofStatus.Pending, null, null);
        public static KnProof Proven(string account) => new KnProof(KnProofStatus.Proven, account, null);
        public static KnProof Failed(string why) => new KnProof(KnProofStatus.Failed, null, why);
    }

    /// <summary>Proves who a joining game is from its sign-in token. Polled each tick until it is not Pending; main thread.</summary>
    public interface IKnAccountProof
    {
        KnProof Prove(Entity connection, string token);

        /// <summary>The connection is decided or gone.</summary>
        void Forget(Entity connection);
    }

    /// <summary>
    /// The travel ticket a world service hands a player who moves between map servers (Linux cloud servers plan). The world
    /// service does not exist yet, so the only implementation is <see cref="AcceptAllTickets"/>: every join is accepted, with or
    /// without a ticket. When the world service lands, its check replaces this one (it may answer Pending while an HTTP call
    /// runs, exactly like <see cref="IKnAccountProof"/>) and the ticket text the game sent is already in the join payload
    /// (<see cref="JoinRules.PayloadText"/>, the seventh part). Keyed on the account, never on a NetworkId.
    /// </summary>
    public interface IKnTravelTicketCheck
    {
        KnJoinVerdict Check(Entity connection, string account, string ticket);
        void Forget(Entity connection);
    }

    /// <summary>The placeholder ticket rule: accepts everyone. Not for a server that is part of a world with a world service.</summary>
    public sealed class AcceptAllTickets : IKnTravelTicketCheck
    {
        public KnJoinVerdict Check(Entity connection, string account, string ticket) => KnJoinVerdict.Accept;
        public void Forget(Entity connection) { }
    }

    /// <summary>What <see cref="KnJoinCheck"/> needs to know about its server.</summary>
    public sealed class KnJoinCheckOptions
    {
        /// <summary>The build the server runs (GameBuild.Version).</summary>
        public string ServerBuild = string.Empty;
        /// <summary>The server is the Editor (an Editor client or server may join any build).</summary>
        public bool ServerIsEditor;
        /// <summary>Players allowed at once; 0 = no limit.</summary>
        public int MaxPlayers = 100;
        /// <summary>True on a real server: a game that cannot prove itself is turned away. False in an Editor test: it plays as its made-up account.</summary>
        public bool RequireProof = true;
        /// <summary>
        /// Takes development bot tokens (<see cref="KnDevBots"/>). Honoured only in a build that has the bots compiled in
        /// (<c>KN_DEV_BOTS</c> or the Editor); in a release build this field is never read and a bot token fails like any other bad token.
        /// </summary>
        public bool AllowDevBots;

        /// <summary>What a player reads when the sign-in cannot be proven (the same words as the NGO server).</summary>
        public const string NoProofText = "Your game could not prove who you are (Unity sign-in). Check your internet connection and join again.";
    }

    /// <summary>
    /// The join rule of a dedicated server, in order: the join text must read (version, mode), the build must match, the server
    /// must have room, the Unity token must prove an account, that account must not be banned, and the travel ticket must pass.
    /// Every step is repeatable: the assembler polls until a verdict, a step that needs the network says Pending and is asked
    /// again next tick. A question about characters (the roster path) is not answered here yet and is refused with its reason.
    /// </summary>
    public sealed class KnJoinCheck : IKnJoinCheck
    {
        private sealed class State
        {
            public bool Parsed;
            public string Token, OldAccount, Ticket;
            public bool Editor;
            public string Account;
            public bool Proven;
            public bool AccountKnown;
        }

        private readonly KnJoinCheckOptions m_Options;
        private readonly IKnAccountProof m_Proof;
        private readonly IKnTravelTicketCheck m_Tickets;
        private readonly Func<int> m_Online;
        private readonly Func<DateTime> m_UtcNow;
        private readonly Action<string> m_Log;
        private readonly Dictionary<Entity, State> m_States = new Dictionary<Entity, State>();

        /// <param name="online">Players already in (not this one): the assembler's <c>ApprovedCount</c>.</param>
        // Bans are not an argument: they come from Core's BanLookup slot, which Networking fills with the server's ban list.
        public KnJoinCheck(KnJoinCheckOptions options, IKnAccountProof proof, IKnTravelTicketCheck tickets, Func<int> online,
            Func<DateTime> utcNow = null, Action<string> log = null)
        {
            m_Options = options ?? new KnJoinCheckOptions();
            m_Proof = proof;
            m_Tickets = tickets ?? new AcceptAllTickets();
            m_Online = online ?? (() => 0);
            m_UtcNow = utcNow ?? (() => DateTime.UtcNow);
            m_Log = log ?? (_ => { });
        }

        public KnJoinVerdict Poll(Entity connection, string payload)
        {
            if (!m_States.TryGetValue(connection, out State st))
            {
                m_States[connection] = st = new State();
                KnJoinVerdict first = Parse(st, payload);
                if (first.Status == KnJoinStatus.Refused) return first; // refused at once; the assembler forgets the connection
            }
            if (!st.Parsed) return KnJoinVerdict.Refuse(KnRefuseReason.BadRequest, JoinRules.Words(JoinRules.Verdict.Unreadable, m_Options.ServerBuild, null));

            // 1. proof of who this is
            if (!st.AccountKnown)
            {
                KnJoinVerdict proven = ProveAccount(connection, st);
                if (proven.Status != KnJoinStatus.Accepted) return proven;
            }

            // 2. bans, asked every poll: a ban placed while the ticket is pending still counts
            IBanLookup bans = BanLookup.Current;
            if (bans != null && !string.IsNullOrEmpty(st.Account) && bans.TryGetBan(st.Account, m_UtcNow(), out string banned))
            {
                m_Log($"Turned away a banned account {st.Account}.");
                return KnJoinVerdict.Refuse(KnRefuseReason.Banned, banned);
            }

            // 3. the travel ticket
            KnJoinVerdict ticket = m_Tickets.Check(connection, st.Account, st.Ticket);
            if (ticket.Status == KnJoinStatus.Pending) return ticket;
            if (ticket.Status == KnJoinStatus.Refused) return KnJoinVerdict.Refuse(ticket.Reason == KnRefuseReason.None ? KnRefuseReason.BadTicket : ticket.Reason, ticket.Detail);

            return KnJoinVerdict.AcceptAs(st.Account, st.OldAccount, st.Proven);
        }

        public void Forget(Entity connection)
        {
            m_States.Remove(connection);
            m_Proof?.Forget(connection);
            m_Tickets.Forget(connection);
        }

        // Reads the text and applies the build and capacity rules once, when the first poll arrives.
        private KnJoinVerdict Parse(State st, string payload)
        {
            if (!JoinRules.TryReadText(payload, out string build, out bool editor, out string token, out string oldAccount,
                    out string request, out string ticket))
                return KnJoinVerdict.Refuse(KnRefuseReason.BadRequest, JoinRules.Words(JoinRules.Verdict.Unreadable, m_Options.ServerBuild, null));
            JoinRules.Verdict verdict = JoinRules.Judge(m_Options.ServerBuild, m_Options.ServerIsEditor, build, editor,
                m_Online(), string.IsNullOrEmpty(request) ? m_Options.MaxPlayers : 0);
            if (verdict != JoinRules.Verdict.None)
                return KnJoinVerdict.Refuse(verdict == JoinRules.Verdict.Full ? KnRefuseReason.ServerFull : KnRefuseReason.WrongVersion,
                    JoinRules.Words(verdict, m_Options.ServerBuild, build));
            if (!string.IsNullOrEmpty(request))
                return KnJoinVerdict.Refuse(KnRefuseReason.Refused, "This server does not answer questions about characters yet.");
            st.Parsed = true;
            st.Editor = editor;
            st.Token = token;
            st.OldAccount = SaveKeys.IsId(oldAccount) ? oldAccount : string.Empty; // anything else is not an account id
            st.Ticket = ticket;
            return KnJoinVerdict.Accept;
        }

        private KnJoinVerdict ProveAccount(Entity connection, State st)
        {
#if KN_DEV_BOTS || UNITY_EDITOR
            if (m_Options.AllowDevBots && KnDevBots.TryAccount(st.Token, out string botAccount))
            {
                st.Account = botAccount;
                st.Proven = false; // a bot's account is named, never proven
                st.AccountKnown = true;
                return KnJoinVerdict.Accept;
            }
#endif
            if (m_Options.RequireProof)
            {
                KnProof proof = m_Proof != null ? m_Proof.Prove(connection, st.Token) : KnProof.Failed("this server has no way to check sign-ins");
                if (proof.Status == KnProofStatus.Pending) return KnJoinVerdict.Pending;
                if (proof.Status == KnProofStatus.Failed)
                {
                    m_Log($"Turned away connection: no proof of who they are ({proof.Why}).");
                    return KnJoinVerdict.Refuse(KnRefuseReason.NoProof, KnJoinCheckOptions.NoProofText);
                }
                st.Account = proof.Account;
                st.Proven = true;
            }
            else
            {
                // An Editor server taking a test: a game that cannot prove itself plays as its made-up account, if it sent one.
                KnProof proof = m_Proof != null && !string.IsNullOrEmpty(st.Token) ? m_Proof.Prove(connection, st.Token) : KnProof.Failed(null);
                if (proof.Status == KnProofStatus.Pending) return KnJoinVerdict.Pending;
                st.Account = proof.Status == KnProofStatus.Proven ? proof.Account : (string.IsNullOrEmpty(st.OldAccount) ? null : st.OldAccount);
                st.Proven = proof.Status == KnProofStatus.Proven;
            }
            st.AccountKnown = true;
            return KnJoinVerdict.Accept;
        }
    }

    /// <summary>
    /// <see cref="IKnAccountProof"/> for Unity's sign-in tokens: <see cref="UnityToken.Verify"/> against Unity's published keys
    /// (<see cref="UnityKeys"/>). When the keys are not here yet (the server just started, or Unity signed with a new key) the
    /// fetch starts once for the connection and this answers Pending until the task finishes; the task is only looked at
    /// (<c>IsCompleted</c>), never awaited, so the simulation does not wait.
    /// </summary>
    public sealed class UnityAccountProof : IKnAccountProof
    {
        private sealed class Wait { public Task Fetch; }

        private readonly string m_ProjectId;
        private readonly Func<DateTime> m_UtcNow;
        private readonly Func<string, Task<bool>> m_EnsureKeys;
        private readonly Dictionary<Entity, Wait> m_Waits = new Dictionary<Entity, Wait>();

        /// <param name="projectId">The Unity project the tokens must belong to (<c>Application.cloudProjectId</c>, read on the main thread by the caller).</param>
        /// <param name="ensureKeys">Starts fetching Unity's keys; null = <see cref="UnityKeys.EnsureAsync"/>. Tests pass a fake.</param>
        public UnityAccountProof(string projectId, Func<DateTime> utcNow = null, Func<string, Task<bool>> ensureKeys = null)
        {
            m_ProjectId = projectId;
            m_UtcNow = utcNow ?? (() => DateTime.UtcNow);
            m_EnsureKeys = ensureKeys ?? (kid => UnityKeys.EnsureAsync(kid));
        }

        public KnProof Prove(Entity connection, string token)
        {
            if (string.IsNullOrEmpty(token)) return KnProof.Failed("no sign-in token");
            string kid = UnityKeys.KeyIdOf(token);
            if (!KeysHave(kid))
            {
                if (!m_Waits.TryGetValue(connection, out Wait wait))
                {
                    wait = new Wait { Fetch = m_EnsureKeys(kid) };
                    m_Waits[connection] = wait;
                }
                if (wait.Fetch != null && !wait.Fetch.IsCompleted) return KnProof.Pending;
                if (!KeysHave(kid)) return KnProof.Failed("Unity's keys could not be fetched");
            }
            if (!UnityToken.Verify(token, UnityKeys.Current, m_ProjectId, m_UtcNow(), out string playerId, out string why))
                return KnProof.Failed(why);
            return KnProof.Proven(AccountIds.FromUnityPlayer(playerId));
        }

        public void Forget(Entity connection) => m_Waits.Remove(connection);

        private static bool KeysHave(string kid) => UnityKeys.Ready && (kid == null || UnityKeys.Current.ContainsKey(kid));
    }
}
