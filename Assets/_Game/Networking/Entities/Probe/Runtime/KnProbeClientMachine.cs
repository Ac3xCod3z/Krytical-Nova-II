#if KN_PROBE || UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

namespace KryticalNova.Networking.Entities.Probe
{
    public enum KnProbePhase { NotStarted, Roster, Connecting, Connected, Done }

    /// <summary>What a client concluded about a connection that ended after it had connected.</summary>
    public enum KnDropCause { Kicked, ServerClosed }

    /// <summary>Plain C# rules of the probe client, so EditMode tests can run the whole script without a socket.</summary>
    public static class KnProbeLogic
    {
        /// <summary>
        /// A connection that was up has ended. The client asks the server's page <c>GET /health</c>: a server that is stopping answers
        /// 503, a server that is gone does not answer, and only a healthy server (200) means this client was sent away on purpose.
        /// </summary>
        public static KnDropCause ClassifyDrop(bool pageAnswered, int healthStatus)
        {
            if (!pageAnswered) return KnDropCause.ServerClosed;
            return healthStatus == 200 ? KnDropCause.Kicked : KnDropCause.ServerClosed;
        }
    }

    /// <summary>
    /// The scripted behaviour of one probe client as a small state machine fed with what the world shows (<c>OnRosterDone</c>,
    /// <c>OnConnected</c>, <c>OnRefused</c>, <c>OnDropped</c>) and with the clock (<c>Tick</c>). It fills the <see cref="KnProbeResult"/> and
    /// ends in exactly one <see cref="KnProbeExitReason"/>. No Unity call: the MonoBehaviour that watches the real world is
    /// <c>KnProbeClient</c>.
    /// </summary>
    public sealed class KnProbeClientMachine
    {
        private readonly KnProbeArgs m_Args;
        private readonly KnProbeResult m_Result;
        private readonly Func<long> m_NowMs;
        private double m_ConnectDeadline;
        private double m_QuitAt;

        public KnProbePhase Phase { get; private set; } = KnProbePhase.NotStarted;
        public KnProbeExitReason Reason { get; private set; } = KnProbeExitReason.None;
        public bool IsDone => Phase == KnProbePhase.Done;
        public int ExitCode => KnProbeExit.Code(Reason);

        public KnProbeClientMachine(KnProbeArgs args, KnProbeResult result, Func<long> nowMs = null)
        {
            m_Args = args ?? throw new ArgumentNullException(nameof(args));
            m_Result = result ?? throw new ArgumentNullException(nameof(result));
            m_NowMs = nowMs ?? KnProbeResult.NowMs;
        }

        /// <summary>Starts the script. <paramref name="now"/> is any steadily rising clock in seconds.</summary>
        public void Begin(double now)
        {
            if (Phase != KnProbePhase.NotStarted) return;
            m_Result.startedUtcMs = m_NowMs();
            if (m_Args.Action == KnProbeAction.Roster)
            {
                m_Result.rosterAsked = true;
                Phase = KnProbePhase.Roster;
                m_ConnectDeadline = now + m_Args.TimeoutSeconds; // the whole roster step shares the connect budget
            }
            else
            {
                Phase = KnProbePhase.Connecting;
                m_ConnectDeadline = now + m_Args.TimeoutSeconds;
            }
        }

        /// <summary>True when the machine now wants the connection started (first the roster has been answered or was not asked).</summary>
        public bool WantsConnect => Phase == KnProbePhase.Connecting;

        public void OnRosterDone(bool ok, string answer, string error, string refusalReasonName, double now)
        {
            if (Phase != KnProbePhase.Roster) return;
            m_Result.rosterOk = ok;
            m_Result.rosterAnswer = answer ?? string.Empty;
            m_Result.rosterError = error ?? string.Empty;
            if (!ok)
            {
                bool refused = !string.IsNullOrEmpty(refusalReasonName) && refusalReasonName != "None";
                if (refused)
                {
                    m_Result.refusalReason = refusalReasonName;
                    m_Result.refusalDetail = error ?? string.Empty;
                    Finish(KnProbeExitReason.Refused);
                }
                else Finish(KnProbeExitReason.Timeout);
                return;
            }
            Phase = KnProbePhase.Connecting;
            m_ConnectDeadline = now + m_Args.TimeoutSeconds;
        }

        public void OnConnected(int networkId, double now)
        {
            if (Phase != KnProbePhase.Connecting) return;
            m_Result.connected = true;
            m_Result.networkId = networkId;
            m_Result.connectedUtcMs = m_NowMs();
            Phase = KnProbePhase.Connected;
            m_QuitAt = m_Args.QuitAfterSeconds > 0 ? now + m_Args.QuitAfterSeconds : 0;
        }

        public void OnRefused(string reasonName, string detail)
        {
            if (Phase == KnProbePhase.Done) return;
            m_Result.refusalReason = reasonName ?? string.Empty;
            m_Result.refusalDetail = detail ?? string.Empty;
            Finish(KnProbeExitReason.Refused);
        }

        /// <summary>The connection that was up has ended.</summary>
        public void OnDropped(KnDropCause cause)
        {
            if (Phase != KnProbePhase.Connected) return;
            m_Result.droppedUtcMs = m_NowMs();
            if (cause == KnDropCause.Kicked)
            {
                m_Result.kicked = true;
                Finish(KnProbeExitReason.Kicked);
            }
            else Finish(KnProbeExitReason.ServerClosed);
        }

        public void OnError(string error)
        {
            if (Phase == KnProbePhase.Done) return;
            m_Result.error = error ?? string.Empty;
            Finish(KnProbeExitReason.Error);
        }

        /// <summary>Clock step: the connect budget and the quit-after time.</summary>
        public void Tick(double now)
        {
            switch (Phase)
            {
                case KnProbePhase.Roster:
                case KnProbePhase.Connecting:
                    if (now > m_ConnectDeadline) Finish(KnProbeExitReason.Timeout);
                    break;
                case KnProbePhase.Connected:
                    if (m_QuitAt > 0 && now >= m_QuitAt) Finish(KnProbeExitReason.QuitAfter);
                    break;
            }
        }

        private void Finish(KnProbeExitReason reason)
        {
            if (Phase == KnProbePhase.Done) return;
            Phase = KnProbePhase.Done;
            Reason = reason;
            m_Result.exitReason = KnProbeExit.Name(reason);
            m_Result.exitCode = KnProbeExit.Code(reason);
            m_Result.endedUtcMs = m_NowMs();
        }
    }
}
#endif
