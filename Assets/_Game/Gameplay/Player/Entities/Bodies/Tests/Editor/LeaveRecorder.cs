using System;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Player.Entities.Bodies.Tests
{
    /// <summary>
    /// Stand-in for the rules of a real body that must hear the player leave (PlayerSaveAgent, PlayerTrade): records each call and
    /// what the test's probe says about the world at that moment (e.g. the ghost's owner id). Added explicitly by the tests.
    /// </summary>
    public sealed class LeaveRecorder : MonoBehaviour, IServerLeaving
    {
        /// <summary>Shared log of the test (set before the call).</summary>
        public System.Collections.Generic.List<string> Log;
        /// <summary>What to write at the moment of the call (the test reads the world here).</summary>
        public Func<string> Probe;
        public int Calls;

        public void OnServerLeaving()
        {
            Calls++;
            Log?.Add("leaving" + (Probe != null ? "(" + Probe() + ")" : string.Empty));
        }
    }
}
