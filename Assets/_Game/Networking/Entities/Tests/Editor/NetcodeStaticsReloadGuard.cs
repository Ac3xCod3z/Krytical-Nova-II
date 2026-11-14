using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Networking.Entities.Tests
{
    /// <summary>
    /// EditMode netcode tests leaked "Persistent allocates 4" at the next domain reload (14 Nov 2026, item 19's Unity turn): the first
    /// <c>NetcodeWorld</c> a test makes (<see cref="KnTestWorld"/>) creates Unity.NetCode's <c>Netcode</c> singleton, whose
    /// <c>GhostEntityMapping</c> holds a Persistent NativeHashMap and NativeHashSet. The package frees them before an assembly reload only from
    /// <c>Netcode.SetupStaticCallbacks</c>, a <c>[RuntimeInitializeOnLoadMethod]</c> that runs on entering Play, so an Editor that ran these tests
    /// without entering Play leaked them. This registers the same clean-up for the Editor at load: the package's own
    /// <c>Netcode.DisposeAfterEnterEditMode</c> (internal, so by name), which does nothing when there is no singleton and is safe to call twice.
    /// Editor-only, in the test assembly: nothing of it is in a game build.
    /// </summary>
    [InitializeOnLoad]
    static class NetcodeStaticsReloadGuard
    {
        const string NetcodeType = "Unity.NetCode.Netcode, Unity.NetCode";
        const string DisposeMethod = "DisposeAfterEnterEditMode";

        static NetcodeStaticsReloadGuard()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= DisposeNetcodeStatics;
            AssemblyReloadEvents.beforeAssemblyReload += DisposeNetcodeStatics;
        }

        /// <summary>True when the package's clean-up was found and ran (tests).</summary>
        internal static bool DisposeNetcodeStatics(bool log)
        {
            MethodInfo dispose = System.Type.GetType(NetcodeType)?.GetMethod(DisposeMethod, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (dispose == null)
            {
                if (log) Debug.LogWarning($"[NetcodeStaticsReloadGuard] {NetcodeType}.{DisposeMethod} not found: the netcode package changed; EditMode netcode tests may leak at reload again.");
                return false;
            }
            dispose.Invoke(null, null);
            return true;
        }

        static void DisposeNetcodeStatics() => DisposeNetcodeStatics(true);
    }
}
