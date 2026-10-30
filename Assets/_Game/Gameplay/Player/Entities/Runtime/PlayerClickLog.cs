using System;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// One line per ground click on each side, for the one-tick click-start check of the 2-client gate (plan sections 33 and 43):
    /// the owner's gather writes <c>[KnClick] client seq=S tick=T</c> (the tick the input is for), the server's plan system writes
    /// <c>[KnClick] server seq=S tick=T</c> (the tick it started the walk). A script pairs them by seq and records the difference
    /// (expected 0 or 1). Off unless a sink is set (the test-lane players set it to their trace file); no cost otherwise.
    /// </summary>
    public static class PlayerClickLog
    {
        public static Action<string> Sink;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Sink = null;

        public static void Client(ushort seq, uint tick) => Sink?.Invoke("[KnClick] client seq=" + seq + " tick=" + tick);

        public static void Server(ushort seq, uint tick) => Sink?.Invoke("[KnClick] server seq=" + seq + " tick=" + tick);
    }
}
