using System.Collections.Generic;
using Unity.Collections;

namespace KryticalNova.Player
{
    /// <summary>
    /// The rules of GM sight (<c>gm.seeall</c>) that do not depend on a network layer: who is sent the list, the switch of each GM
    /// (default on), the name cut. NGO's <c>PlayerGmSight</c> keeps its own network variable; the entity netcode (DOTS cutover wave 1, 12 Nov 2026)
    /// keeps the switch here, per connection, on the server. Numbers are NGO's: a name is cut to 30 characters, a list goes out once a second
    /// and is fresh for 3.5 s on the client.
    /// </summary>
    public static class GmSightRules
    {
        public const float SendEvery = 1f;
        public const float FreshFor = 3.5f;
        public const int NameChars = 30;

        static readonly Dictionary<ulong, bool> Switches = new Dictionary<ulong, bool>();

        /// <summary>True unless this connection's GM turned sight off (on by default).</summary>
        public static bool SeeAll(ulong serial) => !Switches.TryGetValue(serial, out bool on) || on;

        /// <summary><c>gm.seeall</c> on the server for this connection's GM.</summary>
        public static void Set(ulong serial, bool on)
        {
            if (on) Switches.Remove(serial); else Switches[serial] = false;
        }

        /// <summary>A connection left: its switch is forgotten (the next time in, sight is on again, as NGO's network variable).</summary>
        public static void Forget(ulong serial) => Switches.Remove(serial);

        /// <summary>NGO's rule: only a GM whose sight is on is sent the list.</summary>
        public static bool Sees(bool isGm, bool seeAll) => isGm && seeAll;

        /// <summary>A character name as the list carries it: at most <see cref="NameChars"/> characters, never null, and short enough for 64 bytes.</summary>
        public static FixedString64Bytes Fit(string name)
        {
            name ??= string.Empty;
            if (name.Length > NameChars) name = name.Substring(0, NameChars);
            var fitted = new FixedString64Bytes();
            fitted.CopyFromTruncated(name);
            return fitted;
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Switches.Clear();
    }
}
