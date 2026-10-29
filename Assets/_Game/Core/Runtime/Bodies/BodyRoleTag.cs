using System;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// What a player body is in the network, written on the body BEFORE it is activated (so every Awake and OnEnable already
    /// gets the answer from <see cref="NetRole"/>). The body is a normal prefab instance that is never network-spawned; the
    /// entity netcode builds it as a <see cref="NetRoleKind.ServerBody"/> (rules run, the ghost moves it) or a
    /// <see cref="NetRoleKind.ClientView"/> (presentation of the ghost). A body without this tag is the offline avatar or a
    /// Netcode for GameObjects body and answers through its own network object as before.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BodyRoleTag : MonoBehaviour
    {
        [SerializeField] private NetRoleKind kind;
        [SerializeField] private bool isLocal;

        /// <summary>ServerBody or ClientView (Offline is only a tag that was never filled in).</summary>
        public NetRoleKind Kind => kind;

        /// <summary>The server's key of the player this body belongs to (server side; none on a client).</summary>
        public PlayerKey Key { get; private set; }

        /// <summary>The id the connection key was minted for (<see cref="PlayerKeys"/>), what <see cref="NetRole.OwnerId"/> answers.</summary>
        public ulong ConnectionSerial { get; private set; }

        /// <summary>A ClientView this machine controls.</summary>
        public bool IsLocal => isLocal;

        /// <summary>Where movement requests go; null = move the body yourself (agent).</summary>
        public IBodyMover Mover { get; set; }

        /// <summary>Raised when <see cref="IsLocal"/> changes (ownership settles after the body exists).</summary>
        public event Action<BodyRoleTag> LocalChanged;

        /// <summary>Fills the tag. Call right after AddComponent and before the body is activated.</summary>
        public void Assign(NetRoleKind roleKind, PlayerKey key, ulong connectionSerial, bool local)
        {
            kind = roleKind;
            Key = key;
            ConnectionSerial = connectionSerial;
            isLocal = local;
        }

        /// <summary>Changes the local flag and tells listeners (no event when nothing changed).</summary>
        public void SetLocal(bool local)
        {
            if (isLocal == local) return;
            isLocal = local;
            LocalChanged?.Invoke(this);
        }

        /// <summary>The tag of the body this part belongs to, or null.</summary>
        public static BodyRoleTag Of(Component part) => part != null ? part.GetComponentInParent<BodyRoleTag>(true) : null;

        /// <summary>True for the ClientView body this machine controls.</summary>
        public static bool IsLocalClientView(Component part)
        {
            BodyRoleTag tag = Of(part);
            return tag != null && tag.kind == NetRoleKind.ClientView && tag.isLocal;
        }
    }
}
