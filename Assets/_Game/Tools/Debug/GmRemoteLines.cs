using KryticalNova.Networking;
using QFSW.QC;
using Unity.Netcode;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// A joined Game Master's commands run on the server. Quantum Console finds this preprocessor by itself and hands it
    /// every command line before running it, from the GM console (F2) and from the GM Tools window alike; while this
    /// machine is a joined GM (<see cref="PlayerGm"/>) the line is sent to the server, and what runs here is a command
    /// that does nothing. The console stays as it is otherwise: hints while typing, Tab, the history. The server checks
    /// every line again, so a player who is not GM gets nothing from it. "clear" stays here: it empties this console.
    /// </summary>
    public sealed class GmRemoteLines : IQcPreprocessor
    {
        public const string Sent = "gm-sent-to-server";

        /// <summary>The player the GM's commands act on, as last picked here with gm.target (null: their own).</summary>
        public static ulong? RemoteTarget { get; private set; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => RemoteTarget = null;

        // First, before macros: the server expands nothing a GM did not type.
        public int Priority => int.MaxValue;

        /// <summary>True while this machine is a joined player who is GM: its commands belong to the server.</summary>
        public static bool ForwardsToServer
        {
            get
            {
                NetworkManager nm = NetworkManager.Singleton;
                return nm != null && nm.IsListening && !nm.IsServer && PlayerGm.Local != null && PlayerGm.Local.IsGm;
            }
        }

        public string Process(string text)
        {
            if (!ForwardsToServer || text == Sent) return text;
            string line = text.Trim();
            if (line.Equals("clear", System.StringComparison.OrdinalIgnoreCase)) return text;
            NoteTarget(line);
            PlayerGm.Local.Run(line);
            return Sent;
        }

        // The window's "Target player" shows whom the commands act on; the server keeps the real pick.
        private static void NoteTarget(string line)
        {
            if (!line.StartsWith("gm.target ", System.StringComparison.OrdinalIgnoreCase)) return;
            string who = line.Substring("gm.target ".Length).Trim();
            if (who.Equals("self", System.StringComparison.OrdinalIgnoreCase) || who.Equals("me", System.StringComparison.OrdinalIgnoreCase)) RemoteTarget = null;
            else if (ulong.TryParse(who, out ulong id)) RemoteTarget = PlayerGm.Local != null && id == PlayerGm.Local.OwnerClientId ? (ulong?)null : id;
        }

        [Command(Sent, "Sent to the server: the answer follows from there.")]
        private static void SentToServer() { }
    }
}
