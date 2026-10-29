using Unity.Entities;

namespace KryticalNova.Networking.Entities
{
    public enum KnJoinStatus { Pending, Accepted, Refused }

    public readonly struct KnJoinVerdict
    {
        public readonly KnJoinStatus Status;
        public readonly KnRefuseReason Reason;
        public readonly string Detail;

        public KnJoinVerdict(KnJoinStatus status, KnRefuseReason reason, string detail)
        {
            Status = status;
            Reason = reason;
            Detail = detail;
        }

        public static KnJoinVerdict Pending => new KnJoinVerdict(KnJoinStatus.Pending, KnRefuseReason.None, null);
        public static KnJoinVerdict Accept => new KnJoinVerdict(KnJoinStatus.Accepted, KnRefuseReason.None, null);
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

    /// <summary>The default until the real check lands (M2b): lets everyone in. A test implementation, not for a public server.</summary>
    public sealed class AcceptAllJoinCheck : IKnJoinCheck
    {
        public KnJoinVerdict Poll(Entity connection, string payload) => KnJoinVerdict.Accept;
        public void Forget(Entity connection) { }
    }
}
