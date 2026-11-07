using System;
using System.Collections.Generic;
using System.Reflection;
using KryticalNova.Networking.Entities;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities.Tests
{
    /// <summary>
    /// 7 Nov 2026 hand test: KN_DOTS_HOSTILES=1 installed no brains because HostileEntitiesSetup added its world hook at SubsystemRegistration,
    /// the stage where KnWorldHooks clears its hooks; the order between the two is not fixed. This guards every subscriber in the game's
    /// assemblies: whoever touches KnWorldHooks' hooks from a [RuntimeInitializeOnLoadMethod] does it after SubsystemRegistration.
    /// </summary>
    public sealed class WorldHookOrderTests
    {
        [Test]
        public void EveryWorldHookSubscriber_RegistersAfterTheClear()
        {
            var hooks = new HashSet<FieldInfo>
            {
                typeof(KnWorldHooks).GetField(nameof(KnWorldHooks.ServerWorldMade)),
                typeof(KnWorldHooks).GetField(nameof(KnWorldHooks.ClientWorldMade)),
            };
            Assert.IsFalse(hooks.Contains(null), "KnWorldHooks' two hook fields");
            var early = new List<string>();
            int subscribers = 0;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.GetName().Name.StartsWith("KryticalNova", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
                foreach (Type type in types)
                {
                    if (type == typeof(KnWorldHooks)) continue;   // its own clear
                    foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        var init = method.GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>();
                        if (init == null || !TouchesAny(method, hooks)) continue;
                        subscribers++;
                        if (init.loadType == RuntimeInitializeLoadType.SubsystemRegistration) early.Add(type.FullName + "." + method.Name);
                    }
                }
            }
            Assert.GreaterOrEqual(subscribers, 2, "the instrument sees the known subscribers (PlayerWorldsSetup.Register, HostileEntitiesSetup.Hook)");
            CollectionAssert.IsEmpty(early, "these add a world hook at SubsystemRegistration, where KnWorldHooks may clear it afterwards: use AfterAssembliesLoaded");
        }

        /// <summary>Whether the method's IL loads or stores one of the fields (ldsfld 0x7E, stsfld 0x80, ldsflda 0x7F + a field token).</summary>
        static bool TouchesAny(MethodInfo method, HashSet<FieldInfo> fields)
        {
            byte[] il = method.GetMethodBody()?.GetILAsByteArray();
            if (il == null) return false;
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x7E && il[i] != 0x7F && il[i] != 0x80) continue;
                int token = BitConverter.ToInt32(il, i + 1);
                int table = token >> 24;
                if (table != 0x04 && table != 0x0A) continue;   // a field (0x04) or, from another assembly, a member reference (0x0A)
                try
                {
                    if (fields.Contains(method.Module.ResolveField(token))) return true;
                }
                catch (ArgumentException) { }
            }
            return false;
        }
    }
}
