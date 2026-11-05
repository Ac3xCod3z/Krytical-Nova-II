using System;
using System.Collections.Generic;
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

        /// <summary>Where item uses and skill casts go (the local view body); null = decide them on this body.</summary>
        public IBodyRequests Requests { get; set; }

        /// <summary>The requests of the local view body this part belongs to, or null (every other body decides itself).</summary>
        public static IBodyRequests RequestsOf(Component part)
        {
            BodyRoleTag tag = Of(part);
            return tag != null && tag.kind == NetRoleKind.ClientView && tag.isLocal ? tag.Requests : null;
        }

        /// <summary>Raised when <see cref="IsLocal"/> changes (ownership settles after the body exists).</summary>
        public event Action<BodyRoleTag> LocalChanged;

        private static readonly List<BodyRoleTag> ServerBodiesNow = new List<BodyRoleTag>();

        /// <summary>
        /// The server bodies of the entity netcode awake in this process (M7.3/M7.4, 2 Nov 2026): what the world's portals and the GM's
        /// targeting look at. <see cref="PlayerBodies"/> does not list them: their <c>PointAndClickPlayer</c> is switched off (the ghost moves them).
        /// </summary>
        public static IReadOnlyList<BodyRoleTag> ServerBodies => ServerBodiesNow;

        /// <summary>The awake server body of <paramref name="key"/>, or null.</summary>
        public static BodyRoleTag ServerBodyOf(PlayerKey key)
        {
            if (key == PlayerKey.None) return null;
            foreach (BodyRoleTag tag in ServerBodiesNow) if (tag != null && tag.Key == key) return tag;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ServerBodiesNow.Clear();

        private void OnEnable()
        {
            if (kind == NetRoleKind.ServerBody && !ServerBodiesNow.Contains(this)) ServerBodiesNow.Add(this);
        }

        private void OnDisable() => ServerBodiesNow.Remove(this);

        /// <summary>Fills the tag. Call right after AddComponent and before the body is activated.</summary>
        public void Assign(NetRoleKind roleKind, PlayerKey key, ulong connectionSerial, bool local)
        {
            kind = roleKind;
            Key = key;
            ConnectionSerial = connectionSerial;
            isLocal = local;
            if (isActiveAndEnabled) { OnDisable(); OnEnable(); }   // filled on an awake body (tests): the list follows
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

        /// <summary>True for another player's ClientView body on this machine: it only shows, it never decides (M4.5h).</summary>
        public static bool IsRemoteClientView(Component part)
        {
            BodyRoleTag tag = Of(part);
            return tag != null && tag.kind == NetRoleKind.ClientView && !tag.isLocal;
        }

        /// <summary>
        /// The one answer to "is this body this machine's own player?" (its view kicks, messages, death view, camera, panels; plan 52).
        /// A body of the entity netcode answers from its tag: only the local ClientView is mine, a ServerBody is nobody's on this machine.
        /// Every other body (offline, Netcode for GameObjects) keeps NGO's own answer, <paramref name="ngoMine"/>
        /// (<c>!IsSpawned || IsOwner</c>), unchanged. Without it an entity-netcode body, never NGO-spawned, was "mine" on every machine.
        /// </summary>
        public static bool IsMine(Component part, bool ngoMine)
        {
            BodyRoleTag tag = Of(part);
            return tag != null && tag.kind != NetRoleKind.Offline ? tag.kind == NetRoleKind.ClientView && tag.isLocal : ngoMine;
        }
    }
}
