using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// The game's side of the roster on the server (online phase 4, milestone 5): asks a server about this account's
    /// characters from the menus. It opens a short connection of its own, from the same NetworkManager prefab the
    /// worlds use, without the session manager and its debug panel, signs in to Unity first like a join, and closes once
    /// the answer is in. One question at a time. Netcode drops a connection whose setup differs from the server's before
    /// anything is asked (tick rate, approval, scene management, the prefabs, variable length safety), so a world scene
    /// must not override any of those on its NetworkManager: they are set on the prefab.
    /// </summary>
    public static class RosterLink
    {
        private const float AnswerSeconds = 15f;

        /// <summary>The worlds' NetworkManager prefab; the front end sets it.</summary>
        public static GameObject NetworkPrefab;

        /// <summary>True while a question is out.</summary>
        public static bool Busy { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Busy = false;

        /// <summary>
        /// Asks <paramref name="server"/> and calls <paramref name="done"/> on the game's thread with the answer's text, or
        /// with null and what went wrong, in the words the player reads. Run it as a coroutine.
        /// </summary>
        /// <summary>The answer when the signed-in player changed while asking: the caller asks again.</summary>
        public const string AccountChanged = "Your account changed while the server was asked: asking again.";

        public static IEnumerator Ask(ServerListing server, string request, Action<string, string> done)
        {
            if (Busy) { done(null, "Still waiting for the server's last answer."); yield break; }
            if (UnityAccount.Changing) { done(null, AccountChanged); yield break; }
            if (server == null) { done(null, "Choose a server first."); yield break; }
            if (NetworkPrefab == null) { done(null, "This screen cannot reach servers (its network setup is missing)."); yield break; }
            if (NetworkManager.Singleton != null) { done(null, "A game session is still closing. Try again in a moment."); yield break; }
            string packed = RosterWire.Pack(request);
            if (packed.Length > RosterWire.MaxRequestChars) { done(null, "That request is too long to send."); yield break; }

            Busy = true;
            string answer = null, problem = null;
            Task<bool> signIn = NetworkConnectionManager.SignInAsync();
            while (!signIn.IsCompleted) yield return null;
            if (signIn.IsFaulted || !signIn.Result)
            {
                Busy = false;
                done(null, "Could not sign in to Unity, which the server needs to know who you are. Check your internet connection and try again.");
                yield break;
            }

            // Who asks: an answer for another player than the one signed in when it comes is not used.
            string asker = UnityAccount.PlayerNow;
            GameObject link = Make();
            NetworkManager nm = link.GetComponent<NetworkManager>();
            UnityTransport transport = link.GetComponent<UnityTransport>();
            nm.NetworkConfig.ConnectionApproval = true;
            nm.NetworkConfig.ConnectionData = JoinRules.Payload(GameBuild.Version, Application.isEditor,
                AuthenticationService.Instance.AccessToken, KryticalNova.Core.LocalAccount.MadeUpId, packed);

            bool ready = true;
            if (server.ViaRelay)
            {
                Task<string> relay = PrepareRelay(transport, server);
                while (!relay.IsCompleted) yield return null;
                problem = relay.IsFaulted ? $"Could not reach {server.name} through Unity Relay." : relay.Result;
                ready = problem == null;
            }
            else
            {
                // The PC that runs the server asks it on itself: a router may not loop its own public address back.
                transport.SetConnectionData(true, server.OnThisMachine ? "127.0.0.1" : server.address, (ushort)server.port, "0.0.0.0");
                ServerEncryption.UseOnClient(transport);
            }

            if (ready && !nm.StartClient()) { problem = $"Could not start asking {server.name}."; ready = false; }
            if (ready)
            {
                float until = Time.realtimeSinceStartup + AnswerSeconds;
                while (nm.IsListening && !nm.IsConnectedClient && Time.realtimeSinceStartup < until) yield return null;
                if (nm.IsConnectedClient) problem = $"{server.name} let this question in as a player instead of answering it: it runs an older build.";
                else if (RosterWire.TryReadReply(nm.DisconnectReason, out string text)) answer = text;
                else problem = Plain(nm.DisconnectReason, server);
            }

            if (nm.IsListening) nm.Shutdown();
            float wait = Time.realtimeSinceStartup + 3f;
            while (nm != null && nm.ShutdownInProgress && Time.realtimeSinceStartup < wait) yield return null;
            UnityEngine.Object.Destroy(link);
            yield return null; // gone before the next question makes another
            Busy = false;
            if (answer != null && UnityAccount.PlayerNow != asker)
            {
                Debug.Log($"[Roster] {server.name} answered for player {asker}, but {UnityAccount.PlayerNow} is signed in now: asking again.");
                answer = null;
                problem = AccountChanged;
            }
            if (answer != null) done(answer, null);
            else
            {
                Debug.Log($"[Roster] Asking {server.name} failed: {problem}");
                done(null, problem);
            }
        }

        // A refusal the server wrote is shown as it is; Netcode's own words for a connection closed with no reason (a game
        // and server whose network setup differs, an older server) are said plainly.
        private static string Plain(string reason, ServerListing server)
        {
            if (string.IsNullOrWhiteSpace(reason)) return $"{server.name} did not answer. It may be off, or not reachable from here.";
            if (reason.IndexOf("disconnected by server", StringComparison.OrdinalIgnoreCase) >= 0 || reason.StartsWith("Client-", StringComparison.Ordinal))
                return $"{server.name} closed the connection without answering. It may run another build of the game: open the launcher to update.";
            return reason;
        }

        // The prefab's NetworkManager and transport only: made inside a switched-off holder so nothing of it wakes up
        // before the session manager and the debug panel are taken off.
        private static GameObject Make()
        {
            var holder = new GameObject("Roster Link (holder)");
            holder.SetActive(false);
            GameObject link = UnityEngine.Object.Instantiate(NetworkPrefab, holder.transform);
            link.name = "Roster Link";
            foreach (MonoBehaviour part in link.GetComponentsInChildren<MonoBehaviour>(true))
                if (part is NetworkConnectionManager || part is NetworkDebugHUD) UnityEngine.Object.DestroyImmediate(part);
            link.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(holder);
            return link;
        }

        private static async Task<string> PrepareRelay(UnityTransport transport, ServerListing server)
        {
            try
            {
                Unity.Services.Relay.Models.JoinAllocation place = await Unity.Services.Relay.RelayService.Instance.JoinAllocationAsync(server.relay);
                ServerEncryption.Off(transport); // Relay encrypts by itself
                transport.SetRelayServerData(Unity.Services.Relay.Models.AllocationUtils.ToRelayServerData(place, "dtls"));
                return null;
            }
            catch (Unity.Services.Relay.RelayServiceException)
            {
                return $"{server.name} is not on Unity Relay with that code (it may be off, or it just restarted). Press REFRESH and try again in a few minutes.";
            }
            catch (Exception e)
            {
                return $"Could not reach {server.name} through Unity Relay: {e.Message}";
            }
        }
    }
}
