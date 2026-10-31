using KryticalNova.EditorTools;
using NUnit.Framework;
using UnityEditor;

namespace KryticalNova.Tests
{
    /// <summary>When the content cache of a client build is cleared (ClientBuildGuard, the build 13 sky bug).</summary>
    public class ClientBuildGuardTests
    {
        private const string Defines = "A;B";
        private const string Version = "6000.6.3f1";

        [Test]
        public void SameSettingsKeepTheCache()
        {
            string stamp = ClientBuildGuard.Stamp(StandaloneBuildSubtarget.Player, Defines, Version);
            Assert.IsFalse(ClientBuildGuard.NeedsPurge(stamp, stamp, false));
        }

        [Test]
        public void NoRecordClearsTheCache()
        {
            string stamp = ClientBuildGuard.Stamp(StandaloneBuildSubtarget.Player, Defines, Version);
            Assert.IsTrue(ClientBuildGuard.NeedsPurge("", stamp, false));
            Assert.IsTrue(ClientBuildGuard.NeedsPurge(null, stamp, false));
        }

        [Test]
        public void AnotherSubtargetDefinesOrUnityClearsTheCache()
        {
            string last = ClientBuildGuard.Stamp(StandaloneBuildSubtarget.Player, Defines, Version);
            Assert.IsTrue(ClientBuildGuard.NeedsPurge(last, ClientBuildGuard.Stamp(StandaloneBuildSubtarget.Server, Defines, Version), false));
            Assert.IsTrue(ClientBuildGuard.NeedsPurge(last, ClientBuildGuard.Stamp(StandaloneBuildSubtarget.Player, "A;B;C", Version), false));
            Assert.IsTrue(ClientBuildGuard.NeedsPurge(last, ClientBuildGuard.Stamp(StandaloneBuildSubtarget.Player, Defines, "6000.7.0f1"), false));
        }

        [Test]
        public void ASubtargetThatHadToBeCorrectedClearsTheCacheEvenWithTheSameStamp()
        {
            string stamp = ClientBuildGuard.Stamp(StandaloneBuildSubtarget.Player, Defines, Version);
            Assert.IsTrue(ClientBuildGuard.NeedsPurge(stamp, stamp, true));
        }
    }
}
