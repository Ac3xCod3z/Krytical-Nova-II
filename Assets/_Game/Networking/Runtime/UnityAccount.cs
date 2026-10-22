using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;

namespace KryticalNova.Networking
{
    /// <summary>
    /// Real accounts (online phase 4, milestone 7; owner, 18 and 20 Oct 2026): a player signs in once with a Unity Player
    /// Account (Unity's own page in the browser: email and password, Google or Apple), and the game links it to the player
    /// it already is, so the player's id, and with it every character the servers keep, stays the same. After that the game
    /// signs in by itself as before (Unity keeps the session on this PC); on another PC one sign-in brings the same player.
    /// When the Unity account was already linked to another player (signed in on another PC first), the game becomes that
    /// player. A release game asks before it shows the characters; the Editor and development builds (tests, bots) may go
    /// on without.
    /// </summary>
    public static class UnityAccount
    {
        /// <summary>The identity Unity Player Accounts adds to a player.</summary>
        private const string UnityIdentity = "unity";
        /// <summary>How long the browser page may take (the player types an email and a password, or picks Google).</summary>
        private static readonly TimeSpan BrowserWait = TimeSpan.FromMinutes(10);

        private static bool? linked;

        /// <summary>
        /// The sign-in of the player this PC played until its Unity account opened another player (owner, 22 Oct 2026: a
        /// sign-in left the characters behind). Sent with the next character list, once, so the server can bring them
        /// over; kept in memory only.
        /// </summary>
        public static string LeftBehind { get; private set; }

        /// <summary>The server has had <see cref="LeftBehind"/>.</summary>
        public static void ForgetLeftBehind() => LeftBehind = null;

        /// <summary>
        /// True while the signed-in player may change (a sign-in through the browser, a sign-out): nothing may ask a server
        /// for characters meanwhile, or the brief anonymous player of the switch would be answered (owner, 20 Oct 2026:
        /// "after signing out and signing back in Tolan isn't there").
        /// </summary>
        public static bool Changing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            linked = null;
            Changing = false;
            LeftBehind = null;
        }

        /// <summary>The Unity player signed in now, for the logs.</summary>
        public static string PlayerNow
        {
            get
            {
                try { return AuthenticationService.Instance.IsSignedIn ? AuthenticationService.Instance.PlayerId : "(none)"; }
                catch (Exception) { return "(none)"; }
            }
        }

        /// <summary>A release game must be signed in to an account; the Editor and development builds may skip it.</summary>
        public static bool Required => !Application.isEditor && !Debug.isDebugBuild;

        /// <summary>True when the player signed in on this PC is linked to a Unity account (known after <see cref="IsLinkedAsync"/>).</summary>
        public static bool KnownLinked => linked == true;

        /// <summary>Whether this player is linked to a Unity account. Signs in the usual way first. False when it cannot tell.</summary>
        public static async Task<bool> IsLinkedAsync()
        {
            if (linked.HasValue) return linked.Value;
            if (!await NetworkConnectionManager.SignInAsync()) return false;
            try
            {
                PlayerInfo info = await AuthenticationService.Instance.GetPlayerInfoAsync();
                bool found = false;
                if (info?.Identities != null)
                    foreach (Identity identity in info.Identities)
                        if (string.Equals(identity.TypeId, UnityIdentity, StringComparison.OrdinalIgnoreCase)) found = true;
                linked = found;
                return found;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Account] Could not read the account's sign-in methods: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Opens Unity's sign-in page in the browser and links what the player signs in with to this player. Null when the
        /// player is signed in and linked; otherwise what went wrong, for the menu to say.
        /// </summary>
        public static async Task<string> SignInWithBrowserAsync()
        {
            Changing = true;
            try { return await SignInWithBrowserInnerAsync(); }
            finally { Changing = false; }
        }

        private static async Task<string> SignInWithBrowserInnerAsync()
        {
            if (!await NetworkConnectionManager.SignInAsync()) return "Unity's services could not be reached. Check the internet and try again.";
            IPlayerAccountService accounts = PlayerAccountService.Instance;
            if (!accounts.IsSignedIn)
            {
                var done = new TaskCompletionSource<string>();
                void SignedIn() => done.TrySetResult(null);
                void Failed(RequestFailedException e) => done.TrySetResult("The sign-in page said: " + e.Message);
                accounts.SignedIn += SignedIn;
                accounts.SignInFailed += Failed;
                try
                {
                    await accounts.StartSignInAsync();
                    Task finished = await Task.WhenAny(done.Task, Task.Delay(BrowserWait));
                    if (finished != done.Task) return "The sign-in page was not finished. Try again when you are ready.";
                    if (done.Task.Result != null) return done.Task.Result;
                }
                catch (Exception e)
                {
                    return "The sign-in page could not be opened: " + e.Message;
                }
                finally
                {
                    accounts.SignedIn -= SignedIn;
                    accounts.SignInFailed -= Failed;
                }
            }

            string token = accounts.AccessToken;
            try
            {
                // The usual case: this PC's player gains the account and keeps its id and characters.
                await AuthenticationService.Instance.LinkWithUnityAsync(token);
                Debug.Log($"[Account] This player ({AuthenticationService.Instance.PlayerId}) is now linked to a Unity account.");
            }
            catch (AuthenticationException e) when (e.ErrorCode == AuthenticationErrorCodes.AccountAlreadyLinked)
            {
                // The account belongs to a player already (signed in on another PC first): this game becomes that player.
                // The player it was is proven to the server once more, so its characters can come along.
                string before = AuthenticationService.Instance.PlayerId;
                string proof = AuthenticationService.Instance.AccessToken;
                try
                {
                    AuthenticationService.Instance.SignOut(true);
                    await AuthenticationService.Instance.SignInWithUnityAsync(token);
                    Debug.Log($"[Account] Signed in as the account's own player ({AuthenticationService.Instance.PlayerId}); this PC's player ({before}) is asked to bring its characters along.");
                    if (!string.IsNullOrEmpty(proof) && before != AuthenticationService.Instance.PlayerId) LeftBehind = proof;
                }
                catch (Exception inner)
                {
                    return "Your account could not be opened: " + inner.Message;
                }
            }
            catch (Exception e)
            {
                return "Your account could not be linked: " + e.Message;
            }
            linked = true;
            return null;
        }

        /// <summary>
        /// Signs out of the account on this PC (owner, 20 Oct 2026: brothers share a PC, each with an account of his own).
        /// The next sign-in may be any account; its own characters come with it. Nothing is lost: the characters stay with
        /// the account that was signed out.
        /// </summary>
        public static void SignOut()
        {
            try { if (PlayerAccountService.Instance.IsSignedIn) PlayerAccountService.Instance.SignOut(); }
            catch (Exception e) { Debug.LogWarning($"[Account] Player Accounts sign-out: {e.Message}"); }
            try
            {
                if (AuthenticationService.Instance.IsSignedIn) AuthenticationService.Instance.SignOut(true);
                else AuthenticationService.Instance.ClearSessionToken();
            }
            catch (Exception e) { Debug.LogWarning($"[Account] Sign-out: {e.Message}"); }
            linked = false;
            Debug.Log("[Account] Signed out on this PC.");
        }
    }
}
