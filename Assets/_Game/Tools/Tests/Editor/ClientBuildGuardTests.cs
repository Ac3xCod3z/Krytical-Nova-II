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

        [Test]
        public void ARestartIsNeededWhenTheSubtargetWasCorrectedOrTheEditorWasCompiledAsAServer()
        {
            Assert.IsFalse(ClientBuildGuard.NeedsRestart(false, false));
            Assert.IsTrue(ClientBuildGuard.NeedsRestart(true, false), "subtarget corrected in this process: its scripts are still the server's");
            Assert.IsTrue(ClientBuildGuard.NeedsRestart(false, true), "scripts compiled as a server's even with the Player subtarget set");
            Assert.IsTrue(ClientBuildGuard.NeedsRestart(true, true));
        }

        [Test]
        public void TheEditorRunningTheTestsIsNotCompiledAsAServer()
        {
            Assert.IsFalse(ClientBuildGuard.EditorCompiledAsServer, "the game's tests must run in an Editor compiled as the game's");
        }

        [Test]
        public void OnlyTheBuildItselfSaysItIsAServerBuild()
        {
            Assert.IsTrue(ServerSceneStripper.IsServerBuild(true, StandaloneBuildSubtarget.Player));
            Assert.IsTrue(ServerSceneStripper.IsServerBuild(false, StandaloneBuildSubtarget.Server));
            Assert.IsFalse(ServerSceneStripper.IsServerBuild(false, StandaloneBuildSubtarget.Player), "a Server subtarget left in the Editor never makes a game build a server's");
        }
    }
}
