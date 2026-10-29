using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// One chunk of the join request a client sends while its connection is in Handshake/Approval (the Unity-token proof is
    /// longer than one packet: spike S9, one RPC carries at most about 1350 bytes and a bigger one is lost silently).
    /// Chunks may arrive in any order; <see cref="KnApprovalAssembler"/> puts them together by <see cref="Index"/>.
    /// Data is ASCII, at most <see cref="KnApprovalChunks.MaxChunkBytes"/> bytes (a FixedString512Bytes holds 509).
    /// </summary>
    public struct KnApprovalRequest : IApprovalRpcCommand
    {
        public int Index;
        public int Count;
        public FixedString512Bytes Data;
    }

    /// <summary>Why a join was turned away (a byte on the wire).</summary>
    public enum KnRefuseReason : byte
    {
        None = 0,
        BadRequest = 1,
        TooManyChunks = 2,
        TooManyBytes = 3,
        TimedOut = 4,
        Refused = 5,
        ServerFull = 6,
        /// <summary>The game and the server are different builds.</summary>
        WrongVersion = 7,
        /// <summary>The proven account is banned.</summary>
        Banned = 8,
        /// <summary>The Unity sign-in could not be proven (missing, wrong, expired, or Unity's keys could not be fetched).</summary>
        NoProof = 9,
        /// <summary>The travel ticket is missing, wrong or used.</summary>
        BadTicket = 10,
        /// <summary>The server is saving and stopping: it takes nobody new.</summary>
        ServerStopping = 11,
    }

    /// <summary>
    /// Server to client, sent in the approval phase just before the server closes the connection. An approval RPC on
    /// purpose: only those may travel while a connection is not yet approved.
    /// </summary>
    public struct JoinRefusedRpc : IApprovalRpcCommand
    {
        public byte Reason; // KnRefuseReason
        public FixedString512Bytes Detail;
    }

    /// <summary>Server singleton: the pre-approval budget of one connection. Absent = <see cref="Default"/>.</summary>
    public struct KnApprovalLimits : IComponentData
    {
        public int MaxChunks;
        public int MaxTotalBytes;
        public float MaxSeconds;

        public static KnApprovalLimits Default => new KnApprovalLimits { MaxChunks = 8, MaxTotalBytes = 4000, MaxSeconds = 10f };
    }

    /// <summary>Client singleton: the text the client proves itself with (the join token), sent in chunks once connected.</summary>
    public struct KnJoinPayload : IComponentData
    {
        public FixedString4096Bytes Text;
    }

    /// <summary>Client singleton, written when the server refused this client (read by the join UI later).</summary>
    public struct KnJoinRefusal : IComponentData
    {
        public byte Reason; // KnRefuseReason
        public FixedString512Bytes Detail;
    }

    /// <summary>Server singleton: the server is stopping (the stop machine made it). Every join not yet decided is refused as <see cref="KnRefuseReason.ServerStopping"/>.</summary>
    public struct KnServerStopping : IComponentData { }

    /// <summary>
    /// Server, on a connection entity: the account the join check proved (<see cref="Proven"/>), or on a development server the
    /// account the game claimed, plus the made-up id the game played under before sign-in (for the one-time carry-over).
    /// 32-character save ids fit a FixedString64Bytes. <see cref="ConnectionKeySystem"/> hands them to Core's ServerAccounts.
    /// </summary>
    public struct ConnectionAccount : IComponentData
    {
        public FixedString64Bytes Account;
        public FixedString64Bytes OldAccount;
        public bool Proven;
    }

    /// <summary>Client side: this connection already sent its approval chunks.</summary>
    public struct KnApprovalSent : IComponentData { }

    /// <summary>Server side: close this connection after a few more ticks, so the refusal RPC leaves first.</summary>
    public struct KnPendingDisconnect : IComponentData
    {
        public int TicksLeft;
    }
}
