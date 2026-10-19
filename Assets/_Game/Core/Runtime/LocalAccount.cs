using System;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// The account this running copy of the game plays under: the one Unity's sign-in proves while joining a server
    /// (online phase 2), else the id the game made up once per machine profile. In Core because both the connection
    /// code (which proves it) and the character code (which saves under it) need it, and neither should need the other.
    /// <c>CharacterSession</c> still offers the same three properties to its own callers.
    /// </summary>
    public static class LocalAccount
    {
        private const string AccountKey = "kn.account.v1";
        private static string madeUp;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            madeUp = null;
            VerifiedId = null;
        }

        /// <summary>
        /// The id of this player's account: what its characters, and the things they share (the Deep Vault), are
        /// saved under. On a server it is the account Unity's sign-in proves (<see cref="VerifiedId"/>); playing on this
        /// PC alone, the id the game made up (<see cref="MadeUpId"/>).
        /// </summary>
        public static string Id => !string.IsNullOrEmpty(VerifiedId) ? VerifiedId : MadeUpId;

        /// <summary>
        /// The account Unity's sign-in proves, set while joining a server (the server keeps saves under the same id); empty
        /// when playing on this PC alone.
        /// </summary>
        public static string VerifiedId { get; set; }

        /// <summary>
        /// The id the game made up once per machine profile, kept in PlayerPrefs beside the roster: the account of play on
        /// this PC alone, and, sent once to a server, how its characters from before sign-in are carried over.
        /// </summary>
        public static string MadeUpId
        {
            get
            {
                if (madeUp != null) return madeUp;
                string profile = LocalProfile.Name();
                string key = string.IsNullOrEmpty(profile) ? AccountKey : AccountKey + "." + profile;
                madeUp = PlayerPrefs.GetString(key, string.Empty);
                if (!SaveKeys.IsId(madeUp))
                {
                    madeUp = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(key, madeUp);
                    PlayerPrefs.Save();
                }
                return madeUp;
            }
        }

        /// <summary>Forgets the made-up id read from PlayerPrefs, so the next read takes it again (a profile switch).</summary>
        public static void ForgetMadeUp() => madeUp = null;
    }
}
