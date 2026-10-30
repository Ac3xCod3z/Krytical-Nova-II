#if UNITY_SERVER || UNITY_EDITOR
using System.Threading;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The decisions behind <c>POST /stop</c> (Linux cloud servers plan, item 16), without any socket or Unity call so they
    /// can be tested: who may ask, what is answered, and whether this call starts the stop. The first good call starts it
    /// (200); every call after it, and any call while the server is already stopping for another reason, is answered 202.
    /// </summary>
    public sealed class StopEndpointRules
    {
        /// <summary>What to answer, and whether the caller must now start the save-and-quit.</summary>
        public readonly struct Decision
        {
            public readonly int Status;
            public readonly string Body;
            public readonly bool BeginStop;

            public Decision(int status, string body, bool beginStop)
            {
                Status = status;
                Body = body;
                BeginStop = beginStop;
            }
        }

        public const string StopPath = "/stop";
        public const string KeyHeader = "X-KN-Key";

        private const string AlreadyBody = "{\"stopping\":true,\"already\":true}";

        private int started; // 0 = running, 1 = a stop was begun through this endpoint

        /// <summary>True once a good call has begun the stop.</summary>
        public bool Started => Volatile.Read(ref started) == 1;

        /// <summary>
        /// Decides one request. <paramref name="expectedKey"/> is the save service key; empty means no key is configured,
        /// and then nothing is accepted. <paramref name="serverAlreadyStopping"/> is true when the server is stopping
        /// for another reason (control page, gm.shutdown, a signal). Safe to call from any thread.
        /// </summary>
        public Decision Decide(string method, string path, bool fromLoopback, string givenKey, string expectedKey, bool serverAlreadyStopping)
        {
            if (!fromLoopback) return new Decision(403, string.Empty, false);
            if (path != StopPath) return new Decision(404, string.Empty, false);
            if (method != "POST") return new Decision(405, string.Empty, false);
            if (string.IsNullOrEmpty(expectedKey) || !ControlPageRules.SecretEquals(givenKey, expectedKey))
                return new Decision(403, string.Empty, false);
            if (serverAlreadyStopping) return new Decision(202, AlreadyBody, false);
            if (Interlocked.CompareExchange(ref started, 1, 0) != 0) return new Decision(202, AlreadyBody, false);
            return new Decision(200, "{\"stopping\":true}", true);
        }
    }
}
#endif
