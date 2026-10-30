using System;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// One line per ground click on each side, for the one-tick click-start check of the 2-client gate (plan sections 33 and 43):
    /// the owner's gather writes <c>[KnClick] client owner=N seq=S tick=T</c> (the tick the input is for), the server's plan system
    /// writes <c>[KnClick] server owner=N seq=S tick=T</c> (the tick it started the walk). A script pairs them by owner and seq and records the difference
    /// (expected 0 or 1). Off unless a sink is set (the test-lane players set it to their trace file); no cost otherwise.
    /// </summary>
    public static class PlayerClickLog
    {
        public static Action<string> Sink;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Sink = null;

        // owner = the ghost's NetworkId: every client counts its seq from 1, so the pair is (owner, seq)
        public static void Client(int owner, ushort seq, uint tick) => Sink?.Invoke("[KnClick] client owner=" + owner + " seq=" + seq + " tick=" + tick);

        public static void Server(int owner, ushort seq, uint tick) => Sink?.Invoke("[KnClick] server owner=" + owner + " seq=" + seq + " tick=" + tick);
    }
}
