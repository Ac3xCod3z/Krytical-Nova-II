using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// What may be switched off on a dedicated server, and nothing else (written up in ServerOff.md beside it). The server
    /// build's <see cref="ServerSceneStripper"/> reads it, and the publish gate (a game's map bundles against their source
    /// scenes) reads it too: in a game's bundle nothing from this list may differ, so a server-only switch-off that leaks
    /// into client content is caught. <c>DedicatedServerOptimizer</c> (Networking, a lower layer) keeps its own copy of the
    /// namespaces; <c>ServerOffListTests</c> fails when the two drift apart.
    /// </summary>
    public static class ServerOffList
    {
        /// <summary>Vendor namespaces of components that only draw: a server has nothing to draw them on.</summary>
        public static readonly string[] RenderOnlyNamespaces = { "VolumetricLights", "VolumetricFogAndMist2" };

        /// <summary>True for a component the server may switch off: a render-only vendor namespace.</summary>
        public static bool IsRenderOnly(string typeNamespace) =>
            typeNamespace != null && RenderOnlyNamespaces.Contains(typeNamespace, StringComparer.Ordinal);

        /// <summary>True for a component the server's copy of a scene may switch off: render-only vendor components and UI Toolkit documents.</summary>
        public static bool MayBeOffOnServer(Behaviour behaviour) =>
            behaviour != null && (IsRenderOnly(behaviour.GetType().Namespace) || behaviour is UIDocument);
    }
}
