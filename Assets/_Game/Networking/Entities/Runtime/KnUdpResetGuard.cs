using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Dedicated server on Windows: tells Windows not to report "connection reset" on this process's UDP sockets.
    /// Why (16 Nov 2026, the rejoin lock): when a player's game dies (a crash, a kill, a power cut), the server keeps sending to a
    /// closed port until its timeout; the player's PC answers each packet with ICMP "port unreachable", and Windows turns each
    /// answer into a FAILED receive on the server's socket (WSAECONNRESET). Unity Transport 6.6.0 never gives the buffer of a
    /// failed receive back (<c>UDPNetworkInterface</c>'s receive job skips it), so after 512 of them (the server's receive queue)
    /// the server is deaf for good: still bound, still "listening", ticking, and nobody can join until a restart. A few dropped
    /// players are enough, and so is anyone sending forged ICMP packets. With <c>SIO_UDP_CONNRESET</c> and <c>SIO_UDP_NETRESET</c>
    /// off, Windows drops the ICMP answers silently and no receive fails.
    /// <para>
    /// Finding the socket: it is Unity's own (inside Baselib, no handle is handed out). The guard reads this process's handles from
    /// the kernel's handle table (<c>NtQuerySystemInformation</c>, which touches no handle) and asks only the handles with the same
    /// kind and access rights as a socket it makes itself. It never asks other handles: a question to a pipe that another thread
    /// is reading hangs, and holds that thread up too (the first version did that and froze the server's log). Looked at every
    /// <see cref="CheckSeconds"/> on a timer thread, because Transport makes a new socket when it thinks the old one broke.
    /// The package is not edited.
    /// </para>
    /// </summary>
    public static class KnUdpResetGuard
    {
        /// <summary>Seconds between the guard's looks (the first one a moment after <see cref="Arm"/>).</summary>
        public const int CheckSeconds = 5;

        private static readonly object s_Gate = new object();
        private static readonly HashSet<long> s_Guarded = new HashSet<long>();
        private static Timer s_Timer;
        private static int s_Busy;
        private static int s_Looks;
        private static bool s_ToldNone;

        /// <summary>UDP sockets of this process that have the reports switched off right now (tests, the log).</summary>
        public static int Guarded { get { lock (s_Gate) return s_Guarded.Count; } }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Disarm();

        /// <summary>Starts the guard (server start, after the listen request). Does nothing off Windows.</summary>
        public static void Arm()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            lock (s_Gate)
            {
                if (s_Timer != null) return;
                s_Looks = 0;
                s_ToldNone = false;
                s_Timer = new Timer(_ => Look(), null, 1000, CheckSeconds * 1000);
            }
#endif
        }

        public static void Disarm()
        {
            lock (s_Gate)
            {
                s_Timer?.Dispose();
                s_Timer = null;
                s_Guarded.Clear();
            }
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private const int SocketError = -1;
        private const int SolSocket = 0xFFFF, SoType = 0x1008, SockDgram = 2;
        private const uint SioUdpConnReset = 0x9800000C, SioUdpNetReset = 0x9800000F;
        private const int SystemExtendedHandleInformation = 64;
        private const uint StatusInfoLengthMismatch = 0xC0000004;

        [DllImport("ws2_32.dll")] private static extern int getsockopt(IntPtr s, int level, int optname, out int optval, ref int optlen);
        [DllImport("ws2_32.dll")] private static extern int getsockname(IntPtr s, byte[] name, ref int namelen);
        [DllImport("ws2_32.dll")] private static extern int WSAIoctl(IntPtr s, uint code, ref int inBuffer, int inSize, IntPtr outBuffer, int outSize,
            out int bytesReturned, IntPtr overlapped, IntPtr completionRoutine);
        [DllImport("ntdll.dll")] private static extern uint NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returnLength);

        /// <summary>
        /// Switches the reports off on one UDP socket: true when "port unreachable" no longer fails its receives
        /// (<paramref name="netToo"/>: "network unreachable" neither).
        /// </summary>
        public static bool Guard(IntPtr socket, out bool netToo)
        {
            int off = 0;
            bool conn = WSAIoctl(socket, SioUdpConnReset, ref off, sizeof(int), IntPtr.Zero, 0, out _, IntPtr.Zero, IntPtr.Zero) != SocketError;
            off = 0;
            netToo = WSAIoctl(socket, SioUdpNetReset, ref off, sizeof(int), IntPtr.Zero, 0, out _, IntPtr.Zero, IntPtr.Zero) != SocketError;
            return conn;
        }

        /// <summary>One handle of this process as the kernel's handle table lists it (nothing asked of the handle itself).</summary>
        public struct OwnHandle
        {
            public long Value;
            public uint Access;
            public ushort Type;
        }

        /// <summary>This process's handles, from the kernel's table. Touches no handle, so it can never hang on one.</summary>
        public static List<OwnHandle> OwnHandles()
        {
            long pid = Process.GetCurrentProcess().Id;
            int size = 1 << 22;
            while (true)
            {
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    uint status = NtQuerySystemInformation(SystemExtendedHandleInformation, buffer, size, out int needed);
                    if (status == StatusInfoLengthMismatch) { size = Math.Max(size * 2, needed + (1 << 20)); continue; }
                    if (status != 0) throw new InvalidOperationException("NtQuerySystemInformation failed: 0x" + status.ToString("X8"));
                    // SYSTEM_HANDLE_INFORMATION_EX: count, reserved, then entries of 40 bytes (64-bit): object, pid, handle, access,
                    // back-trace index (2), type index (2), attributes, reserved.
                    long count = Marshal.ReadInt64(buffer);
                    var own = new List<OwnHandle>();
                    for (long i = 0; i < count; i++)
                    {
                        IntPtr e = buffer + 16 + (int)(i * 40);
                        if (Marshal.ReadInt64(e, 8) != pid) continue;
                        own.Add(new OwnHandle
                        {
                            Value = Marshal.ReadInt64(e, 16),
                            Access = (uint)Marshal.ReadInt32(e, 24),
                            Type = (ushort)Marshal.ReadInt16(e, 30),
                        });
                    }
                    return own;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }

        /// <summary>
        /// The handles that look like UDP sockets: the same kind and access rights as one this makes itself (then closes). Only
        /// these are ever asked anything.
        /// </summary>
        public static List<long> SocketLikeHandles()
        {
            uint access; ushort type; long probe;
            using (var reference = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                probe = reference.Handle.ToInt64();
                OwnHandle? mine = null;
                foreach (OwnHandle h in OwnHandles()) if (h.Value == probe) { mine = h; break; }
                if (mine == null) return new List<long>();
                access = mine.Value.Access;
                type = mine.Value.Type;
            }
            var like = new List<long>();
            foreach (OwnHandle h in OwnHandles())
                if (h.Access == access && h.Type == type && h.Value != probe) like.Add(h.Value);
            return like;
        }

        // Timer thread. One look at a time; never throws (a guard that dies silently would bring the lock back).
        private static void Look()
        {
            if (Interlocked.Exchange(ref s_Busy, 1) == 1) return;
            try
            {
                var clock = Stopwatch.StartNew();
                List<long> like = SocketLikeHandles();
                var fresh = new List<string>();
                var name = new byte[28];
                lock (s_Gate)
                {
                    if (s_Timer == null) return;
                    s_Looks++;
                    s_Guarded.RemoveWhere(h => !like.Contains(h));   // closed (Transport made a new socket): forgotten
                    foreach (long h in like)
                    {
                        if (s_Guarded.Contains(h)) continue;
                        var s = new IntPtr(h);
                        int size = sizeof(int);
                        if (getsockopt(s, SolSocket, SoType, out int type, ref size) == SocketError || type != SockDgram) continue;
                        int length = name.Length;
                        if (getsockname(s, name, ref length) == SocketError) continue;   // not bound yet: the next look
                        bool conn = Guard(s, out bool net);
                        if (conn) s_Guarded.Add(h);
                        fresh.Add($"port {(name[2] << 8) | name[3]} ({(conn ? "port-unreachable reports off" : "COULD NOT switch port-unreachable reports off")}" +
                                  $"{(net ? string.Empty : ", net-unreachable reports stay on")})");
                    }
                    if (fresh.Count == 0 && s_Guarded.Count == 0 && s_Looks == 12 && !s_ToldNone)
                    {
                        s_ToldNone = true;
                        Debug.LogWarning($"[KnServer] UDP reset guard: no UDP socket found after a minute ({like.Count} socket-like handles); " +
                                         "a dropped player can still make the server deaf (restart it if nobody can join).");
                    }
                }
                if (fresh.Count > 0)
                    Debug.Log("[KnServer] UDP reset guard: " + string.Join("; ", fresh) +
                              $". A dropped player's closed port can no longer fail the server's receives ({clock.ElapsedMilliseconds} ms).");
            }
            catch (Exception e)
            {
                Debug.LogError("[KnServer] UDP reset guard: the look failed (" + e.GetType().Name + ": " + e.Message + "); it runs again in " + CheckSeconds + " s.");
            }
            finally
            {
                Interlocked.Exchange(ref s_Busy, 0);
            }
        }
#endif
    }
}
