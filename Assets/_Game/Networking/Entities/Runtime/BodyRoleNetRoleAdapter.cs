using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Answers <see cref="NetRole"/> for the entity netcode (M3a-2b step 1). A body is never network-spawned here: it carries a
    /// <see cref="BodyRoleTag"/> written before it was activated, and only components that implement
    /// <see cref="IRoleAwareBody"/> read it. Every other component answers Offline, so unported systems run their
    /// single-player path as local stubs (decision D2).
    /// While no Krytical Nova server or client world exists, every question goes to the adapter that was installed before this
    /// one (the Netcode for GameObjects adapter), so that path behaves exactly as before.
    /// </summary>
    public sealed class BodyRoleNetRoleAdapter : INetRoleAdapter
    {
        private readonly INetRoleAdapter m_Fallback;
        private readonly Func<NetRoleKind> m_ProcessProbe;
        private readonly Dictionary<GameObject, BodyRoleTag> m_Tags = new Dictionary<GameObject, BodyRoleTag>();

        /// <param name="fallback">The adapter to ask while no entity netcode world exists (may be null).</param>
        /// <param name="processProbe">What this process is; null = look at the netcode worlds (tests pass a stub).</param>
        public BodyRoleNetRoleAdapter(INetRoleAdapter fallback = null, Func<NetRoleKind> processProbe = null)
        {
            m_Fallback = fallback;
            m_ProcessProbe = processProbe ?? ProcessFromWorlds;
        }

        /// <summary>
        /// Puts this adapter in front of the installed one and does the same for <see cref="NetSession"/>. Idempotent. Called when
        /// the first server or client world is made, never at startup, so a Play session without the entity netcode is untouched.
        /// </summary>
        public static void Install()
        {
            if (!(NetRole.Adapter is BodyRoleNetRoleAdapter)) NetRole.Install(new BodyRoleNetRoleAdapter(NetRole.Adapter));
            if (!(NetSession.Source is KnNetSession)) NetSession.Use(new KnNetSession(NetSession.Source));
        }

        internal static NetRoleKind ProcessFromWorlds()
        {
            if (HasLiveWorld(ClientServerBootstrap.ServerWorlds)) return NetRoleKind.ServerBody;
            if (HasLiveWorld(ClientServerBootstrap.ClientWorlds)) return NetRoleKind.ClientView;
            return NetRoleKind.Offline;
        }

        internal static bool HasLiveWorld(IReadOnlyList<World> worlds)
        {
            for (int i = 0; i < worlds.Count; i++)
                if (worlds[i] != null && worlds[i].IsCreated) return true;
            return false;
        }

        public NetRoleKind Process
        {
            get
            {
                NetRoleKind kind = m_ProcessProbe();
                if (kind != NetRoleKind.Offline) return kind;
                return m_Fallback != null ? m_Fallback.Process : NetRoleKind.Offline;
            }
        }

        private bool UseFallback => m_ProcessProbe() == NetRoleKind.Offline;

        public NetRoleKind Of(Component networked)
        {
            if (UseFallback) return m_Fallback != null ? m_Fallback.Of(networked) : NetRoleKind.Offline;
            if (!(networked is IRoleAwareBody)) return NetRoleKind.Offline;
            BodyRoleTag tag = TagOf(networked);
            return tag != null ? tag.Kind : NetRoleKind.Offline;
        }

        public bool IsOwner(Component networked)
        {
            if (UseFallback) return m_Fallback != null && m_Fallback.IsOwner(networked);
            BodyRoleTag tag = TagOf(networked);
            return tag != null && tag.Kind == NetRoleKind.ClientView && tag.IsLocal;
        }

        public bool IsOwnedByServer(Component networked) => UseFallback && m_Fallback != null && m_Fallback.IsOwnedByServer(networked);

        public bool IsRemoteOwned(Component networked)
        {
            if (UseFallback) return m_Fallback != null && m_Fallback.IsRemoteOwned(networked);
            BodyRoleTag tag = TagOf(networked);
            return tag != null && tag.Kind != NetRoleKind.Offline;
        }

        public ulong OwnerId(Component networked)
        {
            if (UseFallback) return m_Fallback != null ? m_Fallback.OwnerId(networked) : 0UL;
            BodyRoleTag tag = TagOf(networked);
            return tag != null ? tag.ConnectionSerial : 0UL;
        }

        public void DisconnectOwner(Component networked, string reason)
        {
            if (UseFallback)
            {
                if (m_Fallback != null) m_Fallback.DisconnectOwner(networked, reason);
                return;
            }
            BodyRoleTag tag = TagOf(networked);
            if (tag != null && tag.Kind == NetRoleKind.ServerBody) KnPlayerRefusals.Enqueue(tag.Key, reason);
        }

        // Of() runs on hot paths: the tag is found once per object and remembered (a destroyed object drops out when met).
        private BodyRoleTag TagOf(Component part)
        {
            if (part == null) return null;
            GameObject id = part.gameObject;
            if (m_Tags.TryGetValue(id, out BodyRoleTag cached))
            {
                if (cached != null) return cached;
                m_Tags.Remove(id);
            }
            BodyRoleTag tag = BodyRoleTag.Of(part);
            if (tag != null)
            {
                if (m_Tags.Count > 4096) m_Tags.Clear();
                m_Tags[id] = tag;
            }
            return tag;
        }
    }
}
