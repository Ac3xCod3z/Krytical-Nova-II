using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Networking.Tests
{
    /// <summary>POST /stop (Linux cloud servers plan, item 16): who may stop the server, and that a second call is harmless.</summary>
    public sealed class StopEndpointRulesTests
    {
        private const string Key = "the-save-service-key";

        private static StopEndpointRules.Decision Call(StopEndpointRules rules, string method = "POST", string path = "/stop",
            bool loopback = true, string given = Key, string expected = Key, bool stopping = false) =>
            rules.Decide(method, path, loopback, given, expected, stopping);

        [Test]
        public void RightKeyFromLoopbackStartsTheStop()
        {
            var rules = new StopEndpointRules();
            StopEndpointRules.Decision d = Call(rules);
            Assert.AreEqual(200, d.Status);
            Assert.IsTrue(d.BeginStop);
            StringAssert.Contains("\"stopping\":true", d.Body);
            Assert.IsTrue(rules.Started);
        }

        [Test]
        public void WrongOrMissingKeyIsForbiddenWithNoDetail()
        {
            var rules = new StopEndpointRules();
            foreach (string given in new[] { "wrong", "", null, Key + "x", Key.Substring(1) })
            {
                StopEndpointRules.Decision d = Call(rules, given: given);
                Assert.AreEqual(403, d.Status, "key: " + given);
                Assert.IsFalse(d.BeginStop);
                Assert.IsEmpty(d.Body);
            }
            Assert.IsFalse(rules.Started);
        }

        [Test]
        public void NoConfiguredKeyAcceptsNothing()
        {
            var rules = new StopEndpointRules();
            Assert.AreEqual(403, Call(rules, given: "", expected: "").Status);
            Assert.AreEqual(403, Call(rules, given: null, expected: null).Status);
            Assert.IsFalse(rules.Started);
        }

        [Test]
        public void ANonLoopbackCallerIsForbiddenEvenWithTheRightKey()
        {
            var rules = new StopEndpointRules();
            StopEndpointRules.Decision d = Call(rules, loopback: false);
            Assert.AreEqual(403, d.Status);
            Assert.IsFalse(d.BeginStop);
            Assert.IsEmpty(d.Body);
            Assert.IsFalse(rules.Started);
        }

        [Test]
        public void OtherMethodsAreNotAllowed()
        {
            var rules = new StopEndpointRules();
            foreach (string method in new[] { "GET", "PUT", "DELETE", "HEAD" })
            {
                Assert.AreEqual(405, Call(rules, method: method).Status, method);
            }
            Assert.IsFalse(rules.Started);
        }

        [Test]
        public void OtherPathsAreNotFound()
        {
            var rules = new StopEndpointRules();
            Assert.AreEqual(404, Call(rules, path: "/").Status);
            Assert.AreEqual(404, Call(rules, path: "/stop/").Status);
            Assert.IsFalse(rules.Started);
        }

        [Test]
        public void TheSecondCallIsAcceptedAsAlreadyStopping()
        {
            var rules = new StopEndpointRules();
            Assert.AreEqual(200, Call(rules).Status);
            StopEndpointRules.Decision again = Call(rules);
            Assert.AreEqual(202, again.Status);
            Assert.IsFalse(again.BeginStop);
            StringAssert.Contains("\"already\":true", again.Body);
            Assert.AreEqual(202, Call(rules).Status);
        }

        [Test]
        public void AServerAlreadyStoppingForAnotherReasonAnswers202AndStartsNothing()
        {
            var rules = new StopEndpointRules();
            StopEndpointRules.Decision d = Call(rules, stopping: true);
            Assert.AreEqual(202, d.Status);
            Assert.IsFalse(d.BeginStop);
            Assert.IsFalse(rules.Started);
        }

        [Test]
        public void ABadKeyCannotUseUpTheStop()
        {
            var rules = new StopEndpointRules();
            Assert.AreEqual(403, Call(rules, given: "nope").Status);
            Assert.AreEqual(200, Call(rules).Status);
        }
    }
}
