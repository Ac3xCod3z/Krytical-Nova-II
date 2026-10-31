using System.Reflection;
using KryticalNova.EditorTools;
using KryticalNova.Networking;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The server-off list (ServerOff.md): the build's copy and the running server's copy agree, and the sky presenter is not on it.</summary>
    public class ServerOffListTests
    {
        [Test]
        public void TheServersOwnCopyListsTheSameNamespaces()
        {
            FieldInfo field = typeof(DedicatedServerOptimizer).GetField("RenderOnlyNamespaces", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(field, "DedicatedServerOptimizer.RenderOnlyNamespaces was renamed: update this test and ServerOff.md.");
            Assert.AreEqual(ServerOffList.RenderOnlyNamespaces, (string[])field.GetValue(null),
                "ServerOffList and DedicatedServerOptimizer name different namespaces: change both and ServerOff.md together.");
        }

        [Test]
        public void OnlyRenderOnlyVendorNamespacesAreOnTheList()
        {
            Assert.IsTrue(ServerOffList.IsRenderOnly("VolumetricLights"));
            Assert.IsTrue(ServerOffList.IsRenderOnly("VolumetricFogAndMist2"));
            Assert.IsFalse(ServerOffList.IsRenderOnly("KryticalNova.World"), "the sky presenter must never be switched off by a build");
            Assert.IsFalse(ServerOffList.IsRenderOnly(null));
        }
    }
}
