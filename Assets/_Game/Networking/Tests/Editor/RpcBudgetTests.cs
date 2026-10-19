using System;
using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Networking.Tests
{
    /// <summary>
    /// The speed limit every server request handler starts with (39 runtime files call it): only a server counts, so
    /// a game playing on its own is never limited; every kind of request has a limit of its own; and a server started
    /// without settings falls back to the defaults.
    /// </summary>
    public sealed class RpcBudgetTests
    {
        [TearDown]
        public void Restore() => RpcBudget.Configure(null);

        [Test]
        public void WithoutAServerEveryRequestIsAllowed()
        {
            for (int i = 0; i < 1000; i++)
                Assert.IsTrue(RpcBudget.Allow(42UL, CallKind.Chat, "test"), "nothing counts unless this game is the server");
        }

        [Test]
        public void EveryKindOfRequestHasALimitOfItsOwn()
        {
            var limits = new CallLimits();
            foreach (CallKind kind in Enum.GetValues(typeof(CallKind)))
            {
                CallLimit limit = limits.For(kind);
                Assert.Greater(limit.perSecond, 0f, kind + " refills");
                Assert.GreaterOrEqual(limit.burst, 1f, kind + " allows at least one at once");
            }
            Assert.Less(limits.For(CallKind.Chat).perSecond, limits.For(CallKind.Move).perSecond, "chat is held tighter than movement");
        }

        [Test]
        public void AServerWithoutSettingsUsesTheDefaults()
        {
            RpcBudget.Configure(new CallLimits { floodDrops = 3 });
            Assert.AreEqual(3, RpcBudget.Limits.floodDrops);
            RpcBudget.Configure(null);
            Assert.AreEqual(new CallLimits().floodDrops, RpcBudget.Limits.floodDrops);
        }
    }
}
