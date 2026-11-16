using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// Live bug, 15 Nov 2026 (build 17: no monsters seen in the zones, GM-spawned ones invisible): a built entity server makes its worlds at
    /// BeforeSceneLoad and sets <see cref="HostilePool.BodyOf"/> there; HostilePool's own reset ran at BeforeSceneLoad too and wiped it, so
    /// every zone spawned plain bodies with no entity brain and no Hostile ghost. These pin the order so it never comes back.
    /// </summary>
    public class HostilePoolStartupTests
    {
        static MethodInfo[] StartupHooks(Type type) => type
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>() != null)
            .ToArray();

        static RuntimeInitializeLoadType When(MethodInfo m) => m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>().loadType;

        [Test]
        public void ThePoolsReset_RunsBeforeAnyWorldIsMade()
        {
            MethodInfo reset = typeof(HostilePool).GetMethod("ResetStatics", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(reset, "HostilePool.ResetStatics");
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, When(reset),
                "the reset that clears BodyOf must run at SubsystemRegistration, before the entity worlds are made (BeforeSceneLoad)");
        }

        [Test]
        public void TheStartupHooksThatRunWithTheWorlds_LeaveTheServerBodiesAlone()
        {
            Func<HostileNet, HostileNet> set = species => species;
            Func<HostileNet, HostileNet> before = HostilePool.BodyOf;
            try
            {
                HostilePool.BodyOf = set;
                MethodInfo[] late = StartupHooks(typeof(HostilePool))
                    .Where(m => When(m) != RuntimeInitializeLoadType.SubsystemRegistration).ToArray();
                Assert.IsNotEmpty(late, "the Netcode hook (BeforeSceneLoad)");
                foreach (MethodInfo m in late) m.Invoke(null, null);
                Assert.AreSame(set, HostilePool.BodyOf, "a hook that may run after the server world is made wiped the server bodies");
            }
            finally { HostilePool.BodyOf = before; }
        }

        [Test]
        public void AfterStartup_ALostHookIsSetAgainAndSaidLoudly_AHeldOneIsLeft()
        {
            Func<HostileNet, HostileNet> before = HostilePool.BodyOf;
            var catalog = ScriptableObject.CreateInstance<HostileBodyCatalog>();
            try
            {
                HostilePool.BodyOf = null;
                StringAssert.Contains("LOST", HostileEntitiesSetup.CheckBodies(catalog));
                Assert.IsNotNull(HostilePool.BodyOf);

                Func<HostileNet, HostileNet> held = HostilePool.BodyOf;
                StringAssert.Contains("held", HostileEntitiesSetup.CheckBodies(catalog));
                Assert.AreSame(held, HostilePool.BodyOf);
            }
            finally
            {
                HostilePool.BodyOf = before;
                UnityEngine.Object.DestroyImmediate(catalog);
            }
        }
    }
}
