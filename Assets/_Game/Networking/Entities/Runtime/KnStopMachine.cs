using System;
using System.Threading;

namespace KryticalNova.Networking.Entities
{
    /// <summary>Who asked the server to stop (for the log line).</summary>
    public enum KnStopSource { None = 0, Signal = 1, Console = 2, Http = 3, Code = 4 }

    /// <summary>
    /// The stop request, as threads other than the game's see it. A SIGTERM handler, the console-close handler and the HTTP
    /// <c>POST /stop</c> thread only call <see cref="Request"/> (one interlocked write, nothing else: a signal handler may
    /// not call anything that allocates or logs); the managed stop system takes it on the main thread
    /// (<see cref="TryTake"/>). Asking twice is harmless.
    /// </summary>
    public static class KnStopRequests
    {
        private static int s_Pending;   // KnStopSource of a request not yet taken, else 0
        private static int s_Stopping;  // 1 from the first request on (page: "already stopping")

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Reset();

        /// <summary>Any thread, signal-handler safe.</summary>
        public static void Request(KnStopSource source)
        {
            Interlocked.CompareExchange(ref s_Pending, (int)source, 0);
            Volatile.Write(ref s_Stopping, 1);
        }

        /// <summary>Main thread: takes the pending request, if there is one.</summary>
        public static bool TryTake(out KnStopSource source)
        {
            source = (KnStopSource)Interlocked.Exchange(ref s_Pending, 0);
            return source != KnStopSource.None;
        }

        /// <summary>True from the first request on: the server is stopping (or about to).</summary>
        public static bool Stopping => Volatile.Read(ref s_Stopping) == 1;

        public static void Reset()
        {
            Volatile.Write(ref s_Pending, 0);
            Volatile.Write(ref s_Stopping, 0);
        }
    }

    /// <summary>What the stop machine does to the world and the process. Faked in tests.</summary>
    public interface IKnStopActions
    {
        /// <summary>Stop taking new players. (Netcode has no public "stop listening": new joins are refused as ServerStopping.)</summary>
        void CloseDoor();
        /// <summary>Save every player and the world through Core's IServerShutdown. May throw; the machine goes on.</summary>
        void SaveAll();
        /// <summary>Ask every open connection to close (the polite way: the transport says goodbye).</summary>
        void DisconnectAll();
        /// <summary>How many connections are still open.</summary>
        int OpenConnections();
        /// <summary>Close, at once and without waiting, every connection that is still open (the grace ran out).</summary>
        void ForceCloseAll();
        void Quit(int exitCode);
        void Log(string line);
    }

    /// <summary>
    /// The server's safe stop as a small state machine, one <see cref="Step"/> per simulation tick on the main thread:
    /// first step after the request: close the door, save everyone, disconnect everyone; then wait for the
    /// connections to close, at most <see cref="GraceTicks"/> ticks (it quits at once when they are all gone, and force-closes
    /// whatever is still open when the grace is over: the transport's own disconnect timeout took 32 s in the M2c run); then quit
    /// once with exit code 0.
    /// <see cref="Request"/> is idempotent. A save that throws is logged and the stop goes on (a hung server is worse than a late
    /// save, and every player was also saved when their connection closed).
    /// </summary>
    public sealed class KnStopMachine
    {
        public enum Phase { Running, Stopping, Done }

        private readonly IKnStopActions m_Actions;
        private bool m_Began;
        private int m_TicksLeft;

        /// <summary>The most ticks to wait for the connections to close after the disconnects (default 3 s at the simulation rate).</summary>
        public int GraceTicks = KnNetDefaults.StopGraceTicks(KnNetDefaults.StopGraceSeconds);

        public Phase Current { get; private set; } = Phase.Running;
        public string Reason { get; private set; }
        public int ExitCode => 0;

        public KnStopMachine(IKnStopActions actions)
        {
            m_Actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        /// <summary>Starts the stop. True for the call that started it, false for every later one.</summary>
        public bool Request(string reason)
        {
            if (Current != Phase.Running) return false;
            Current = Phase.Stopping;
            Reason = string.IsNullOrWhiteSpace(reason) ? "The server was asked to stop." : reason;
            return true;
        }

        public void Step()
        {
            if (Current != Phase.Stopping) return;
            if (!m_Began)
            {
                m_Began = true;
                m_Actions.Log("[KnServer] Stopping: " + Reason + " Saving every player and the world...");
                Safely(m_Actions.CloseDoor, "closing the door");
                Safely(m_Actions.SaveAll, "saving");
                Safely(m_Actions.DisconnectAll, "disconnecting");
                m_TicksLeft = Math.Max(1, GraceTicks);
                m_Actions.Log("[KnServer] Saved. Waiting for the players to be sent away, then quitting.");
                return;
            }
            int open = 0;
            try { open = m_Actions.OpenConnections(); }
            catch (Exception e) { m_Actions.Log("[KnServer] Problem while counting connections (the stop goes on): " + e.GetType().Name + ": " + e.Message); }
            if (open > 0 && --m_TicksLeft > 0) return;
            if (open > 0)
            {
                m_Actions.Log("[KnServer] " + open + " connection(s) did not close in time: closing them now.");
                Safely(m_Actions.ForceCloseAll, "closing the last connections");
            }
            Current = Phase.Done;
            m_Actions.Quit(ExitCode);
        }

        private void Safely(Action action, string what)
        {
            try { action(); }
            catch (Exception e) { m_Actions.Log("[KnServer] Problem while " + what + " (the stop goes on): " + e.GetType().Name + ": " + e.Message); }
        }
    }
}
