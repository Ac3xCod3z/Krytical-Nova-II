using System;
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// The rejoin lock's cause in small (16 Nov 2026): on Windows a UDP socket that sent to a closed port gets its next receive
    /// FAILED ("connection reset"), which is what leaks the server's receive buffers; a guarded socket does not.
    /// </summary>
    public class KnUdpResetGuardTests
    {
        private static int ClosedPort()
        {
            using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)probe.Client.LocalEndPoint).Port;   // free again when the probe closes
        }

        private static SocketError ReceiveAfterSendingToAClosedPort(UdpClient socket)
        {
            socket.Client.ReceiveTimeout = 500;
            socket.Send(new byte[] { 1 }, 1, new IPEndPoint(IPAddress.Loopback, ClosedPort()));
            try
            {
                IPEndPoint from = null;
                socket.Receive(ref from);
                return SocketError.Success;
            }
            catch (SocketException e) { return e.SocketErrorCode; }
        }

#if UNITY_EDITOR_WIN
        [Test]
        public void A_plain_socket_fails_its_receive_and_a_guarded_one_only_waits()
        {
            using var plain = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            Assert.AreEqual(SocketError.ConnectionReset, ReceiveAfterSendingToAClosedPort(plain),
                "Windows reports the closed port as a failed receive: the failure Unity Transport leaks a buffer on");

            using var guarded = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            Assert.IsTrue(KnUdpResetGuard.Guard(guarded.Client.Handle, out bool netToo), "the reports are switched off");
            Assert.IsTrue(netToo);
            Assert.AreEqual(SocketError.TimedOut, ReceiveAfterSendingToAClosedPort(guarded), "nothing fails: the receive just waits");
        }

        [Test]
        public void The_handle_table_finds_a_socket_and_never_lists_a_pipe()
        {
            using var socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            using var pipe = new System.IO.Pipes.AnonymousPipeServerStream(System.IO.Pipes.PipeDirection.In);
            var like = KnUdpResetGuard.SocketLikeHandles();
            CollectionAssert.Contains(like, socket.Client.Handle.ToInt64(), "the bound UDP socket is found from the kernel's table");
            CollectionAssert.DoesNotContain(like, pipe.SafePipeHandle.DangerousGetHandle().ToInt64(),
                "a pipe is never asked anything (a question to a busy pipe hangs)");
        }
#endif

        [Test]
        public void Arming_twice_and_disarming_leaves_nothing_behind()
        {
            KnUdpResetGuard.Arm();
            KnUdpResetGuard.Arm();
            KnUdpResetGuard.Disarm();
            Assert.AreEqual(0, KnUdpResetGuard.Guarded);
        }
    }
}
