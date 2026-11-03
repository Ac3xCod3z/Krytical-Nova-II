using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>
    /// Which saved parts a player body of the entity netcode (M3a-2b) really loads and writes. Every other part is passed
    /// through: never restored, never captured, so its stored text goes back to the store byte for byte and the part is never
    /// dirty (decision D3). A system leaves the passthrough side in its own port, together with its test, by being added to
    /// <see cref="RealKeys"/>. Bodies of Netcode for GameObjects and the offline avatar do not use this: they load and save
    /// every part as before (<see cref="Never"/>).
    /// </summary>
    public static class SavePassthrough
    {
        /// <summary>
        /// The parts that are real: the look and the progression header (M3a-2b), and the Pack (its counts and the Flask's drinks), the
        /// Serums taken and the wallet (owner, 2 Nov, feel test item 5: items are used on the server's body, so what is used must last).
        /// </summary>
        private static readonly string[] DefaultReal = { "look", "progression", "inventory", "serums", "wallet", "loadouts", "xpboosts", "skills", "movement", "vault", "deepvault", "vendorWeek", "trade", "nova" };

        /// <summary>The parts that were classified as passthrough on purpose; a new participant must be added to one of the two lists.</summary>
        public static readonly string[] PassthroughKeys =
        {
            "autobuff", "autopilot", "crate", "mounts",
            "weaves", "weave", "sponsor",
        };

        private static readonly HashSet<string> Real = new HashSet<string>(DefaultReal);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ResetToDefault();

        /// <summary>Puts the real parts back to the defaults (tests, and a Play session without domain reload).</summary>
        public static void ResetToDefault()
        {
            Real.Clear();
            foreach (string key in DefaultReal) Real.Add(key);
        }

        /// <summary>The parts that are really loaded and saved.</summary>
        public static IReadOnlyCollection<string> RealKeys => Real;

        /// <summary>A system's port: from now on its part is really loaded and saved.</summary>
        public static void MakeReal(string key) => Real.Add(key);

        /// <summary>True for every part that is not real. An unknown key is passed through too: the safe side.</summary>
        public static bool Is(string key) => !Real.Contains(key);

        /// <summary>The policy of a body that does not pass anything through.</summary>
        public static bool Never(string key) => false;

        /// <summary>True when the key was decided on, real or passthrough.</summary>
        public static bool IsClassified(string key)
        {
            if (Real.Contains(key)) return true;
            foreach (string known in PassthroughKeys)
                if (known == key) return true;
            return false;
        }
    }
}
