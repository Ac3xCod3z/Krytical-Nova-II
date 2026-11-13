namespace KryticalNova.Crossfire
{
    /// <summary>What the server said about Crossfire, as this machine holds it (a copy of the server's clock; never decided here).</summary>
    public struct CrossfireState
    {
        public CrossfireNow Now;
        /// <summary>When the yards start taking players in (the join window opens); they take them until the War.</summary>
        public double JoinOpensAt;
        public float CallRepeatSeconds;
        public int MinLevel, BracketSize, BracketCount;
        /// <summary>False until the server has said anything (no war shown).</summary>
        public bool Known;

        public bool GatesOpen => Known && CrossfireRules.GatesOpen(Now.Phase);

        public bool JoinOpen(double now) =>
            Known && (Now.Phase == CrossfirePhase.Muster || (Now.Phase == CrossfirePhase.Call && now >= JoinOpensAt));

        /// <summary>A player of <paramref name="level"/> is called to this war (the bracket rule).</summary>
        public int BracketOf(int level) => CrossfireRules.BracketOf(level, MinLevel, BracketSize, BracketCount);
    }

    /// <summary>
    /// The Crossfire state for what shows it (HUD, Muster Gates): the server's own copy on the server, the received one on a client.
    /// Written by the Crossfire systems only.
    /// </summary>
    public static class CrossfireView
    {
        /// <summary>The server's clock (server process).</summary>
        public static CrossfireState Server;
        /// <summary>What this player's game last received.</summary>
        public static CrossfireState Client;
        /// <summary>A server world runs in this process (host, Editor, dedicated server): the gates follow the server's clock.</summary>
        public static bool HasServer;

        /// <summary>The state a Muster Gate follows: the server's where there is one, else the received one.</summary>
        public static CrossfireState ForGates => HasServer ? Server : Client;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Server = default;
            Client = default;
            HasServer = false;
        }
    }
}
