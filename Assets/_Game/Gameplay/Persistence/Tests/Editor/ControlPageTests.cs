using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The control page's rules (online phase 1, chunk 3): who may reach it, how requests are read, the lock.</summary>
    public sealed class ControlPageTests
    {
        [TestCase("127.0.0.1", true)]
        [TestCase("192.168.1.20", true)]
        [TestCase("10.0.0.5", true)]
        [TestCase("172.16.4.1", true)]
        [TestCase("172.31.255.1", true)]
        [TestCase("172.32.0.1", false)]
        [TestCase("8.8.8.8", false)]
        [TestCase("81.2.69.160", false)]
        [TestCase("::1", true)]
        [TestCase("fe80::1", true)]
        [TestCase("fd12:3456::1", true)]
        [TestCase("2001:4860::8888", false)]
        [TestCase("::ffff:192.168.1.9", true)]
        [TestCase("::ffff:8.8.8.8", false)]
        public void OnlyTheHomeNetworkMayReachThePage(string address, bool allowed)
        {
            Assert.AreEqual(allowed, ControlPageRules.IsHomeNetwork(IPAddress.Parse(address)));
        }

        [TestCase("192.168.1.20:8090", true)]
        [TestCase("localhost:8090", true)]
        [TestCase("GAMING-PC:8090", true)]
        [TestCase("[fe80::1]:8090", true)]
        [TestCase("evil.example.com:8090", false)]
        [TestCase("evil.example.com", false)]
        [TestCase("", false)]
        public void TheRequestMustNameThisMachineNotAWebSite(string host, bool allowed)
        {
            Assert.AreEqual(allowed, ControlPageRules.IsAcceptableHost(host));
        }

        [Test]
        public void ARequestIsReadWithItsHeadersCookieAndBody()
        {
            string raw = "POST /api/kick?x=1 HTTP/1.1\r\nHost: 192.168.1.20:8090\r\nCookie: a=1; kn_page=abc\r\nContent-Length: 13\r\n\r\n{\"id\":\"2\"}xyz";
            var request = ControlPageRules.Read(new MemoryStream(Encoding.UTF8.GetBytes(raw)));
            Assert.IsNotNull(request);
            Assert.AreEqual("POST", request.Method);
            Assert.AreEqual("/api/kick", request.Path);
            Assert.AreEqual("x=1", request.Query);
            Assert.AreEqual("abc", request.Cookie("kn_page"));
            Assert.AreEqual("{\"id\":\"2\"}xyz", request.Body);
        }

        [Test]
        public void ATooLargeOrBrokenRequestIsRefused()
        {
            Assert.IsNull(ControlPageRules.Read(new MemoryStream(Encoding.ASCII.GetBytes("GARBAGE\r\n\r\n"))));
            string big = "POST / HTTP/1.1\r\nContent-Length: " + (ControlPageRules.MaxBodyBytes + 1) + "\r\n\r\n";
            Assert.IsNull(ControlPageRules.Read(new MemoryStream(Encoding.ASCII.GetBytes(big))));
            Assert.IsNull(ControlPageRules.Read(new MemoryStream(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x"))), "no end of headers");
        }

        [Test]
        public void ASpareConnectionClosedUnusedNeedsNoAnswer()
        {
            // Browsers open connections ahead and close them unused; answering those with a 400 failed and filled the log.
            Assert.IsNull(ControlPageRules.Read(new MemoryStream(new byte[0]), out bool empty));
            Assert.IsTrue(empty, "nothing was sent: closed without an answer");
            Assert.IsNull(ControlPageRules.Read(new MemoryStream(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x")), out empty));
            Assert.IsFalse(empty, "a request cut short is still answered with Bad request");
            Assert.IsNotNull(ControlPageRules.Read(new MemoryStream(Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\nHost: x\r\n\r\n")), out empty));
            Assert.IsFalse(empty);
        }

        [Test]
        public void AButtonPressFromAnotherSiteIsRefused()
        {
            var request = new ControlPageRules.Request { Method = "POST", Path = "/api/stop" };
            request.Headers["Host"] = "192.168.1.20:8090";
            request.Headers["Origin"] = "http://192.168.1.20:8090";
            Assert.IsTrue(ControlPageRules.IsSameOrigin(request));
            request.Headers["Origin"] = "http://evil.example.com";
            Assert.IsFalse(ControlPageRules.IsSameOrigin(request));
        }

        [Test]
        public void FiveWrongPasswordsLockForFiveMinutes()
        {
            var lockout = new ControlPageRules.Lockout();
            var now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
            Assert.AreEqual(4, lockout.Wrong("192.168.1.9", now));
            for (int i = 0; i < 3; i++) lockout.Wrong("192.168.1.9", now);
            Assert.IsFalse(lockout.IsLocked("192.168.1.9", now, out _));
            Assert.AreEqual(0, lockout.Wrong("192.168.1.9", now));
            Assert.IsTrue(lockout.IsLocked("192.168.1.9", now.AddMinutes(4), out TimeSpan left));
            Assert.Greater(left.TotalSeconds, 0);
            Assert.IsFalse(lockout.IsLocked("192.168.1.10", now, out _), "another address is not locked");
            Assert.IsFalse(lockout.IsLocked("192.168.1.9", now.AddMinutes(6), out _), "the lock ends");
            Assert.AreEqual(4, lockout.Wrong("192.168.1.9", now.AddMinutes(6)), "and the count starts again");
        }

        [Test]
        public void ABrowserThatHungUpIsNotLoggedButARealFailureIs()
        {
            // The flood of 17 Oct 2026: "Unable to write data to the transport connection", the browser already gone.
            Assert.IsTrue(ControlPageRules.IsClientHangUp(new IOException("Unable to write data to the transport connection.",
                new SocketException((int)SocketError.ConnectionAborted))));
            Assert.IsTrue(ControlPageRules.IsClientHangUp(new IOException("Unable to read data from the transport connection.",
                new SocketException((int)SocketError.ConnectionReset))));
            Assert.IsTrue(ControlPageRules.IsClientHangUp(new SocketException((int)SocketError.ConnectionReset)));
            Assert.IsFalse(ControlPageRules.IsClientHangUp(new IOException("The disk is full.")), "a failure of the page itself");
            Assert.IsFalse(ControlPageRules.IsClientHangUp(new IOException("Unable to read data from the transport connection.",
                new SocketException((int)SocketError.TimedOut))));
            Assert.IsFalse(ControlPageRules.IsClientHangUp(new InvalidOperationException("broken")));
            Assert.IsFalse(ControlPageRules.IsClientHangUp(null));
        }

        [Test]
        public void SecretsAndTokens()
        {
            Assert.IsTrue(ControlPageRules.SecretEquals("abc-def", "abc-def"));
            Assert.IsFalse(ControlPageRules.SecretEquals("abc-def", "abc-deg"));
            Assert.IsFalse(ControlPageRules.SecretEquals("abc", "abcd"));
            Assert.IsFalse(ControlPageRules.SecretEquals(null, "x"));
            Assert.AreEqual(48, ControlPageRules.NewToken().Length);
            Assert.AreNotEqual(ControlPageRules.NewToken(), ControlPageRules.NewToken());
            StringAssert.IsMatch(@"^[a-z]{3}-[a-z]{3}-[a-z]{3}-\d{2}$", ControlPageRules.NewPassword());
            Assert.AreEqual("\"a\\\"b\\u003cc\"", ControlPageRules.Json("a\"b<c"));
        }
    }
}
