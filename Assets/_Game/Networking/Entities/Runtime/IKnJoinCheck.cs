using Unity.Entities;

namespace KryticalNova.Networking.Entities
{
    public enum KnJoinStatus
    {
        Pending,
        Accepted,
        Refused,
        /// <summary>The sender is proven and only asks about its characters (roster path): it is answered and never let in.</summary>
        Question,
    }

    public readonly struct KnJoinVerdict
    {
        public readonly KnJoinStatus Status;
        public readonly KnRefuseReason Reason;
        public readonly string Detail;
        /// <summary>When accepted: the account the connection plays under (empty from a check that proves nothing).</summary>
        public readonly string Account;
        public readonly string OldAccount;
        public readonly bool Proven;
        /// <summary>For <see cref="KnJoinStatus.Question"/>: the packed roster request (<c>RosterWire.Pack</c>).</summary>
        public readonly string Request;

        public KnJoinVerdict(KnJoinStatus status, KnRefuseReason reason, string detail, string account = null, string oldAccount = null, bool proven = false, string request = null)
        {
            Status = status;
            Reason = reason;
            Detail = detail;
            Account = account;
            OldAccount = oldAccount;
            Proven = proven;
            Request = request;
        }

        public static KnJoinVerdict Pending => new KnJoinVerdict(KnJoinStatus.Pending, KnRefuseReason.None, null);
        public static KnJoinVerdict Accept => new KnJoinVerdict(KnJoinStatus.Accepted, KnRefuseReason.None, null);
        public static KnJoinVerdict AcceptAs(string account, string oldAccount, bool proven) =>
            new KnJoinVerdict(KnJoinStatus.Accepted, KnRefuseReason.None, null, account, oldAccount, proven);
        public static KnJoinVerdict AskRoster(string account, string oldAccount, bool proven, string request) =>
            new KnJoinVerdict(KnJoinStatus.Question, KnRefuseReason.None, null, account, oldAccount, proven, request);
        public static KnJoinVerdict Refuse(KnRefuseReason reason, string detail) => new KnJoinVerdict(KnJoinStatus.Refused, reason, detail);
    }

    /// <summary>
    /// The server's rule for letting a connection in, once its whole join payload has arrived. Called every tick until it
    /// answers something other than Pending, so a check that needs the network (the Unity token) starts its work on the first
    /// call, returns Pending, and is polled: the simulation never waits. Main thread only. Plain C#, no Core or gameplay type:
    /// the real check (bans, full server, token, travel ticket) is plugged in by the game later.
    /// </summary>
    public interface IKnJoinCheck
    {
        KnJoinVerdict Poll(Entity connection, string payload);

        /// <summary>The connection is decided or gone: drop whatever is kept for it.</summary>
        void Forget(Entity connection);
    }

    /// <summary>Lets everyone in, proving nothing. For tests and the Editor; the dedicated server uses <see cref="KnJoinCheck"/>.</summary>
    public sealed class AcceptAllJoinCheck : IKnJoinCheck
    {
        public KnJoinVerdict Poll(Entity connection, string payload) => KnJoinVerdict.Accept;
        public void Forget(Entity connection) { }
    }
}
