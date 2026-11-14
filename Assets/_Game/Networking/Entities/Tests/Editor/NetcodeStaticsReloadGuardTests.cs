using NUnit.Framework;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// <see cref="NetcodeStaticsReloadGuard"/>: the netcode package's clean-up is still where the guard looks for it (a package update that
    /// renames it fails here, not as a silent leak), and calling it again with nothing left to free is harmless.
    /// </summary>
    public sealed class NetcodeStaticsReloadGuardTests
    {
        [Test]
        public void ThePackagesCleanUpIsFound_AndIsSafeToCallTwice()
        {
            Assert.IsTrue(NetcodeStaticsReloadGuard.DisposeNetcodeStatics(false), "Unity.NetCode.Netcode.DisposeAfterEnterEditMode");
            Assert.IsTrue(NetcodeStaticsReloadGuard.DisposeNetcodeStatics(false), "a second call with no singleton left");
        }
    }
}
