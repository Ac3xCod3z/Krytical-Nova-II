using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// One RPC must fit one packet: spike S9 measured about 1350 bytes, and a bigger RPC is lost without a trace (no retry, no
    /// fragmentation). So every IRpcCommand / IApprovalRpcCommand struct of every Krytical Nova assembly is measured here; anything
    /// that can carry more must be chunked (see KnApprovalRequest). The in-memory size is an upper bound of the serialized size.
    /// </summary>
    public class RpcSizeLintTests
    {
        public const int MaxSerializedBytes = 1300;
        private const int HeaderMargin = 16; // netcode header 5 B + rpc message header 4 B, rounded up

        [Test]
        public void EveryRpcCommand_FitsOnePacket()
        {
            var tooBig = new List<string>();
            int checkedTypes = 0;
            foreach (Type type in RpcTypes())
            {
                checkedTypes++;
                int size = UnsafeUtility.SizeOf(type) + HeaderMargin;
                if (size > MaxSerializedBytes) tooBig.Add($"{type.FullName}: up to {size} B");
            }
            Assert.GreaterOrEqual(checkedTypes, 2, "the lint found no RPC commands: is it looking in the right assemblies?");
            Assert.IsEmpty(tooBig, "RPC commands above " + MaxSerializedBytes + " B are lost on the wire; chunk them: " + string.Join("; ", tooBig));
        }

        [Test]
        public void TheLint_KnowsTheApprovalRpcs()
        {
            var names = RpcTypes().Select(t => t.Name).ToList();
            CollectionAssert.Contains(names, nameof(KnApprovalRequest));
            CollectionAssert.Contains(names, nameof(JoinRefusedRpc));
        }

        [Test]
        public void ApprovalChunk_AtItsLargest_IsWellUnderTheLimit()
        {
            Assert.Less(UnsafeUtility.SizeOf<KnApprovalRequest>() + HeaderMargin, 600);
        }

        private static IEnumerable<Type> RpcTypes()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string name = assembly.GetName().Name;
                if (!name.StartsWith("KryticalNova", StringComparison.Ordinal)) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                foreach (Type type in types)
                {
                    if (!type.IsValueType || type.IsAbstract || type.IsGenericTypeDefinition) continue;
                    if (typeof(IRpcCommand).IsAssignableFrom(type) || typeof(IApprovalRpcCommand).IsAssignableFrom(type)) yield return type;
                }
            }
        }
    }
}
