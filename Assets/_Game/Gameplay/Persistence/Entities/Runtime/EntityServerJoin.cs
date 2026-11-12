using System;
using System.Collections;
using System.Globalization;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Networking.Entities;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// Fills <see cref="EntityServerLink"/> (DOTS front end, 8 Nov 2026): the shipped Title > sign-in > character select > Enter joins a
    /// server of the entity netcode as NGO's <c>NetworkConnectionManager.JoinChosenServer</c> joins its own, with the same rules: sign in
    /// to Unity first (a server lets in only proven players), send the build, the token and the old made-up account (<see cref="JoinRules"/>),
    /// in once this player's own body stands in the world, a refusal ends the wait at once with the server's words, no answer within
    /// <see cref="JoinTimeoutSeconds"/> (NGO's 20 s) is "did not answer". The chosen character goes with the go-in-game request
    /// (<see cref="CharacterSessionGoInGame"/>). The roster questions of character select go through <see cref="KnRosterClient"/>.
    /// Plain UDP only to this PC's own test server (<see cref="EntityListRules.IsTestLaneListing"/>), DTLS to every other server, the owner's own PC included.
    /// </summary>
    public static class EntityServerJoin
    {
        public const float JoinTimeoutSeconds = 20f;
        const string NoSignIn = "Could not sign in to Unity, which the server needs to know who you are. Check your internet connection and try again.";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Register()
        {
            EntityServerLink.Join = Join;
            EntityServerLink.AskRoster = AskRoster;
        }

        static bool Encrypted(ServerListing server) => EntityListRules.Encrypted(server);
        static string Address(ServerListing server) => EntityListRules.JoinAddress(server);

        static IEnumerator Token(Action<string> got)
        {
            var signIn = UnityAccount.JoinTokenAsync();
            while (!signIn.IsCompleted) yield return null;
            got(signIn.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? signIn.Result : null);
        }

        static IEnumerator Join(ServerListing server, Action<string> done)
        {
            string token = null;
            yield return Token(t => token = t);
            if (string.IsNullOrEmpty(token)) { done(NoSignIn); yield break; }
            // the server keeps this player's saves under the same id: the game's own copy follows it (as NGO's join)
            LocalAccount.VerifiedId = AccountIds.FromUnityPlayer(UnityAccount.PlayerNow);
            string join = JoinRules.PayloadText(GameBuild.Version, Application.isEditor, token, LocalAccount.MadeUpId);
            Debug.Log($"[KnJoin] Joining {server.name} at {Address(server)}:{server.port} ({(Encrypted(server) ? "DTLS" : "plain UDP")}) as player {UnityAccount.PlayerNow}.");
            if (!KnBootstrap.StartClient(Address(server), (ushort)server.port, join, Encrypted(server))) { done($"Could not start joining {server.name}."); yield break; }

            float started = Time.realtimeSinceStartup;
            while (LocalPlayer.Object == null)
            {
                string refused = Refusal();
                if (refused != null)
                {
                    Debug.Log($"[KnJoin] Refused after {Since(started)} s: {refused}");
                    KnBootstrap.Stop();
                    done(refused);
                    yield break;
                }
                if (!AnyClientWorld())
                {
                    done($"{server.name} closed the connection.");
                    yield break;
                }
                if (Time.realtimeSinceStartup - started > JoinTimeoutSeconds)
                {
                    KnBootstrap.Stop();
                    done($"{server.name} did not answer. It may be off, or not reachable from here.");
                    yield break;
                }
                yield return null;
            }
            Debug.Log($"[KnJoin] In the world after {Since(started)} s.");
            // Log Out and Exit end the session the normal way: the worlds stop, Netcode for GameObjects may run again
            EntityNetcodeSession.StopWorlds = Leave;
            done(null);
        }

        static void Leave()
        {
            EntityNetcodeSession.StopWorlds = null;
            KnBootstrap.Stop();
            EntityNetcodeSession.Owns = false;
        }

        static IEnumerator AskRoster(ServerListing server, string request, Action<string, string> done)
        {
            string token = null;
            yield return Token(t => token = t);
            if (string.IsNullOrEmpty(token)) { done(null, NoSignIn); yield break; }
            KnRosterQuery q = KnRosterClient.Ask(new KnRosterAsk
            {
                Address = Address(server),
                Port = (ushort)server.port,
                Encrypted = Encrypted(server),
                Build = GameBuild.Version,
                IsEditor = Application.isEditor,
                Token = token,
                OldAccount = LocalAccount.MadeUpId,
                Request = request,
            });
            while (!q.IsDone) yield return null;
            KnRosterResult r = q.Result;
            if (r != null && r.Ok) done(r.Answer, null);
            else done(null, r != null && !string.IsNullOrEmpty(r.Error) ? r.Error : $"{server.name} did not answer.");
        }

        static string Since(float t) => (Time.realtimeSinceStartup - t).ToString("F1", CultureInfo.InvariantCulture);

        static World ClientWorld()
        {
            foreach (World w in World.All)
                if (w.IsCreated && w.Name == KnBootstrap.ClientWorldName) return w;
            return null;
        }

        static bool AnyClientWorld() => ClientWorld() != null;

        // The server's own words when it turned this client away; null when none arrived.
        static string Refusal()
        {
            World client = ClientWorld();
            if (client == null) return null;
            using var q = client.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KnJoinRefusal>());
            if (q.IsEmpty) return null;
            using var refusals = q.ToComponentDataArray<KnJoinRefusal>(Allocator.Temp);
            return refusals[0].Detail.ToString();
        }
    }
}
