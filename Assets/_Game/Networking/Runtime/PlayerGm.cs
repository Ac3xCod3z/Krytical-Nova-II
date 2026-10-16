using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// A player's Game Master rights (<see cref="GmRoster"/>). The host's own player is always GM. A joined player
    /// becomes GM by typing a GM password in the GM console (F2): the password goes to the server, which checks it and
    /// marks the player. From then on each line they type there is sent to the server, run there as if typed on it,
    /// aimed at their own player unless they pick another with gm.target, and only they see the answer. Everyone can
    /// see who is GM (the [GM] tag on the name and in chat). Offline, the player is GM as before.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGm : NetworkBehaviour
    {
        public const string Tag = "[GM] ";
        private const int MaxLineLength = 500;
        private const int ReplyChunk = 3000;

        private readonly NetworkVariable<FixedString32Bytes> gmName = new NetworkVariable<FixedString32Bytes>(
            writePerm: NetworkVariableWritePermission.Server);

        /// <summary>This machine's own player's GM part, while it is in a session.</summary>
        public static PlayerGm Local { get; private set; }

        /// <summary>Raised on this machine with each answer the server sends back to its own player.</summary>
        public static event Action<string> Reply;

        /// <summary>Server: runs a GM's line for that GM's connection and returns the answer (set by the dev tools).</summary>
        public static Func<ulong, string, string> CommandRunner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Local = null;
            Reply = null;
        }

        /// <summary>The host's own player, a player who unlocked GM this session, or the offline player.</summary>
        public bool IsGm => !IsSpawned || OwnerClientId == NetworkManager.ServerClientId || !gmName.Value.IsEmpty;

        /// <summary>Whether others see the [GM] tag on this player: a GM in a session.</summary>
        public bool ShowsTag => IsSpawned && IsGm;

        /// <summary>The GM name they signed in with ("Host" for the host's own player), or empty.</summary>
        public string GmName => !IsSpawned ? string.Empty
            : OwnerClientId == NetworkManager.ServerClientId ? "Host" : gmName.Value.ToString();

        /// <summary>"[GM] name" for a GM in a session, otherwise the name as it is.</summary>
        public static string Label(Component player, string name)
        {
            return player != null && player.TryGetComponent(out PlayerGm gm) && gm.ShowsTag ? Tag + name : name;
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner) Local = this;
            if (IsServer) GmRoster.Revoked += OnRevoked;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            if (IsServer)
            {
                GmRoster.Revoked -= OnRevoked;
                GmRoster.Forget(OwnerClientId);
            }
        }

        /// <summary>Owner: sends a GM password to the server; the answer comes back through <see cref="Reply"/>.</summary>
        public void Login(string password)
        {
            if (!IsSpawned || !IsOwner) return;
            LoginServerRpc(password ?? string.Empty);
        }

        /// <summary>Owner: runs a GM command line on the server; the answer comes back through <see cref="Reply"/>.</summary>
        public void Run(string line)
        {
            if (!IsSpawned || !IsOwner || string.IsNullOrWhiteSpace(line)) return;
            if (line.Length > MaxLineLength) line = line.Substring(0, MaxLineLength);
            CommandServerRpc(line.Trim());
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void LoginServerRpc(string password)
        {
            if (GmRoster.IsGm(OwnerClientId))
            {
                Send("You are already GM.");
                return;
            }
            if (GmRoster.TryLogin(OwnerClientId, password, out string name, out string message, out bool kick))
            {
                gmName.Value = new FixedString32Bytes(name);
                Debug.Log($"[GM] {name} signed in (client {OwnerClientId}).");
                Send(message);
                return;
            }
            Debug.LogWarning($"[GM] A wrong GM password from client {OwnerClientId}.");
            if (kick)
            {
                NetworkManager.DisconnectClient(OwnerClientId, message);
                return;
            }
            Send(message);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CommandServerRpc(string line)
        {
            if (!GmRoster.IsGm(OwnerClientId))
            {
                Send("You are not a GM. Type your GM password first.");
                return;
            }
            if (line.Length > MaxLineLength) line = line.Substring(0, MaxLineLength);
            string answer = CommandRunner != null ? CommandRunner(OwnerClientId, line) : "GM commands are not available on this server.";
            Send(string.IsNullOrEmpty(answer) ? "Done." : answer);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ReplyRpc(string text) => Reply?.Invoke(text);

        // Long answers (gm.help) go in parts: one message stays well under the transport's payload size.
        private void Send(string text)
        {
            if (IsOwner && IsServer)
            {
                Reply?.Invoke(text);
                return;
            }
            for (int start = 0; start < text.Length; start += ReplyChunk)
                ReplyRpc(text.Substring(start, Math.Min(ReplyChunk, text.Length - start)));
        }

        private void OnRevoked(ulong clientId)
        {
            if (clientId != OwnerClientId) return;
            gmName.Value = default;
            Send("Your GM rights were removed on this server.");
        }
    }
}
