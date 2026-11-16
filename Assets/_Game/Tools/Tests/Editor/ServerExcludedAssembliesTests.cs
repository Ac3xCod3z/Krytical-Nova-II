using KryticalNova.EditorTools;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Which assemblies the server build's scene copy takes scripts out of (ServerSceneStripper, owner rule 16 Nov 2026):
    /// exactly those whose .asmdef says "!UNITY_SERVER", and never the ones the server runs (the sky's World stays).
    /// </summary>
    public class ServerExcludedAssembliesTests
    {
        [Test]
        public void AnAsmdefWithTheServerConstraint_IsExcluded()
        {
            Assert.IsTrue(ServerExcludedAssemblies.IsServerExcluded("{\"name\":\"X\",\"defineConstraints\":[\"!UNITY_SERVER\"]}"));
            Assert.IsTrue(ServerExcludedAssemblies.IsServerExcluded("{\"name\":\"X\",\"defineConstraints\":[\"KN_A\", \"! UNITY_SERVER\"]}"));
        }

        [Test]
        public void AnAsmdefWithoutIt_IsKept()
        {
            Assert.IsFalse(ServerExcludedAssemblies.IsServerExcluded("{\"name\":\"X\",\"defineConstraints\":[]}"));
            Assert.IsFalse(ServerExcludedAssemblies.IsServerExcluded("{\"name\":\"X\"}"));
            Assert.IsFalse(ServerExcludedAssemblies.IsServerExcluded("{\"name\":\"X\",\"defineConstraints\":[\"UNITY_SERVER\"]}"));
        }

        [Test]
        public void TheProjectsOwnAssemblies_AreSortedRight()
        {
            Assert.IsTrue(ServerExcludedAssemblies.Contains("KryticalNova.World.Presentation"));
            Assert.IsTrue(ServerExcludedAssemblies.Contains("KryticalNova.UI"));
            Assert.IsFalse(ServerExcludedAssemblies.Contains("KryticalNova.World"), "the sky's state and World's rules run on the server");
            Assert.IsFalse(ServerExcludedAssemblies.Contains("KryticalNova.Runtime"));
            Assert.IsFalse(ServerExcludedAssemblies.Contains("Unity.Netcode.Runtime"), "a package without the constraint is kept");
        }
    }
}
