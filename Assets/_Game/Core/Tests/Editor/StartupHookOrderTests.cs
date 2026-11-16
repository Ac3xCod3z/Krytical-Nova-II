using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The startup race (two live bugs on build 17, 15 Nov 2026: #304 and #313). A built entity server makes its worlds in Entities' own
    /// BeforeSceneLoad bootstrap, in no fixed order with our BeforeSceneLoad hooks, so a reset or a blind install there can wipe or
    /// replace what the server world has just set; the Editor makes its worlds later and never shows it. The rule these pin: statics are
    /// reset at SubsystemRegistration, and a BeforeSceneLoad hook only adds (its own handler, or into an empty slot). kn_check's
    /// startup_race lint flags the same in new code.
    /// </summary>
    public class StartupHookOrderTests
    {
        const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static IEnumerable<Type> OurTypes() => AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => a.GetName().Name.StartsWith("KryticalNova", StringComparison.Ordinal))
            .SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); } });

        static RuntimeInitializeLoadType? When(MethodInfo m) => m.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>()?.loadType;

        static Type TypeNamed(string fullName)
        {
            Type type = OurTypes().FirstOrDefault(t => t.FullName == fullName);
            Assert.IsNotNull(type, fullName);
            return type;
        }

        [Test]
        public void EveryResetStaticsHook_RunsAtSubsystemRegistration()
        {
            var late = new List<string>();
            foreach (Type type in OurTypes())
            {
                foreach (MethodInfo m in type.GetMethods(Statics))
                {
                    RuntimeInitializeLoadType? when = When(m);
                    if (when == null || !m.Name.StartsWith("Reset", StringComparison.Ordinal)) continue;
                    if (when != RuntimeInitializeLoadType.SubsystemRegistration) late.Add($"{type.FullName}.{m.Name} ({when})");
                }
            }
            Assert.IsEmpty(late, "a startup reset after SubsystemRegistration can wipe what a built server's world set at BeforeSceneLoad");
        }

        [TestCase("KryticalNova.Items.WorldDropPool")]
        [TestCase("KryticalNova.UI.AccessibilityPreferences")]
        [TestCase("KryticalNova.DevTools.MeasureBot")]
        [TestCase("KryticalNova.Hostiles.Emerald.HostileBrainLink")]
        public void TheMovedResets_RunAtSubsystemRegistration(string typeName)
        {
            MethodInfo reset = TypeNamed(typeName).GetMethod("ResetStatics", Statics);
            Assert.IsNotNull(reset, typeName + ".ResetStatics");
            Assert.AreEqual(RuntimeInitializeLoadType.SubsystemRegistration, When(reset));
        }

        [Test]
        public void WorldDropPool_ItsBeforeSceneLoadHook_LeavesThePoolAlone()
        {
            Type pool = TypeNamed("KryticalNova.Items.WorldDropPool");
            var free = (IDictionary)pool.GetField("free", Statics).GetValue(null);
            var marker = new GameObject("startup-race marker");
            try
            {
                free[marker] = Activator.CreateInstance(free.GetType().GetGenericArguments()[1]);   // a waiting stack for one prefab
                MethodInfo[] hooks = pool.GetMethods(Statics).Where(m => When(m) == RuntimeInitializeLoadType.BeforeSceneLoad).ToArray();
                Assert.IsNotEmpty(hooks, "the Netcode subscription stays at BeforeSceneLoad (Netcode clears its event at SubsystemRegistration)");
                foreach (MethodInfo m in hooks) m.Invoke(null, null);
                Assert.IsTrue(free.Contains(marker), "a BeforeSceneLoad hook emptied the pool");
            }
            finally
            {
                free.Remove(marker);
                UnityEngine.Object.DestroyImmediate(marker);
            }
        }

        [Test]
        public void AccessibilityPreferences_ItsBeforeSceneLoadHook_KeepsItsListeners()
        {
            Type prefs = TypeNamed("KryticalNova.UI.AccessibilityPreferences");
            FieldInfo changed = prefs.GetField("Changed", Statics);
            Assert.IsNotNull(changed, "the Changed event's field");
            object before = changed.GetValue(null);
            Action listener = () => { };
            try
            {
                changed.SetValue(null, listener);
                foreach (MethodInfo m in prefs.GetMethods(Statics).Where(m => When(m) == RuntimeInitializeLoadType.BeforeSceneLoad))
                    m.Invoke(null, null);
                Assert.AreSame(listener, changed.GetValue(null), "a BeforeSceneLoad hook dropped the listeners");
            }
            finally { changed.SetValue(null, before); }
        }
    }
}
