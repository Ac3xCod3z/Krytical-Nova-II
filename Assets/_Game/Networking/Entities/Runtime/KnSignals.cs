using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Operating-system stop signals for the dedicated server, turned into <see cref="KnStopRequests"/>. Linux: a SIGTERM
    /// handler through libc <c>signal</c> (a stock Unity Linux player ends on SIGTERM in about 0.2 s without
    /// <c>Application.wantsToQuit</c>: spike M1). Windows: the console's Ctrl+C, close button, log-off and shutdown, which
    /// give the process only a few seconds, so the console thread waits until the stop has finished. A handler may run on any
    /// thread with almost nothing safe to call: it sets the request and returns. Not installed in the Editor.
    /// </summary>
    public static class KnSignals
    {
        private static bool s_Installed;
        private static readonly ManualResetEventSlim s_Stopped = new ManualResetEventSlim(false);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Installed = false;
            s_Stopped.Reset();
        }

        /// <summary>Called when the process is about to quit, so a waiting console handler returns.</summary>
        public static void NotifyStopped() => s_Stopped.Set();

        /// <summary>True once <see cref="NotifyStopped"/> let the console handler go (tests: the stop itself must never do it).</summary>
        public static bool ConsoleReleased => s_Stopped.IsSet;

        /// <summary>Installs the handlers once. Returns a line to log.</summary>
        public static string Install()
        {
            if (s_Installed) return "stop signals were already installed";
            s_Installed = true;
#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
            try
            {
                s_SignalHandler = OnSigTerm;
                SetSignal(SigTerm, Marshal.GetFunctionPointerForDelegate(s_SignalHandler));
                return "SIGTERM handler installed (saves everyone, then quits)";
            }
            catch (Exception e) { return "could not watch SIGTERM: stopping by signal will not save first (" + e.GetType().Name + ")"; }
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                s_ConsoleHandler = OnConsoleEvent;
                return SetConsoleCtrlHandler(s_ConsoleHandler, true)
                    ? "console handler installed (Ctrl+C or closing the window saves everyone, then quits)"
                    : "could not watch the console window: closing it will not save first";
            }
            catch (Exception e) { return "could not watch the console window (" + e.GetType().Name + ")"; }
#else
            return "no stop signal handler on this platform (the Editor uses POST /stop or a test request)";
#endif
        }

#if UNITY_STANDALONE_LINUX && !UNITY_EDITOR
        private delegate void SignalHandler(int signal);
        private const int SigTerm = 15;
        private static SignalHandler s_SignalHandler; // held, or the garbage collector frees the callback the system calls

        [DllImport("libc", EntryPoint = "signal")]
        private static extern IntPtr SetSignal(int signal, IntPtr handler);

        [AOT.MonoPInvokeCallback(typeof(SignalHandler))]
        private static void OnSigTerm(int signal) => KnStopRequests.Request(KnStopSource.Signal);
#endif

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        private delegate bool ConsoleHandler(int eventType);
        private const int ConsoleWaitMilliseconds = 4500; // Windows ends a closing console about five seconds after the event
        private static ConsoleHandler s_ConsoleHandler;

        [DllImport("kernel32.dll")]
        private static extern bool SetConsoleCtrlHandler(ConsoleHandler routine, bool add);

        [AOT.MonoPInvokeCallback(typeof(ConsoleHandler))]
        private static bool OnConsoleEvent(int eventType)
        {
            KnStopRequests.Request(KnStopSource.Console);
            s_Stopped.Wait(ConsoleWaitMilliseconds);
            return true;
        }
#endif
    }
}
