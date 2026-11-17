using KryticalNova.Factions;

namespace KryticalNova.Crossfire
{
    /// <summary>What the results screen shows (owner: 30 s at the war's end): <see cref="CrossfireResultKind"/>.</summary>
    public static class CrossfireResultKind
    {
        public const byte Won = 0, Tied = 1, Lost = 2, NoPay = 3;
    }

    /// <summary>The last war's results as this player's game received them (the HUD shows them until <see cref="ShownUntil"/>).</summary>
    public struct CrossfireResults
    {
        public byte Result;
        public FactionId Side;
        public int KillsHegemony, KillsNull, KillsCovenant;
        public string Title, Pay;
        /// <summary>Unscaled real time (s) the screen goes away; 0 = none received.</summary>
        public double ShownUntil;
    }

    /// <summary>The Marshal's JOIN CROSSFIRE button (UI) to the entity netcode's request (Crossfire.Entities fills it). Null while no game is joined.</summary>
    public static class CrossfireJoinLink
    {
        public static System.Action Requester;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Requester = null;
    }

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
        /// <summary>The team deathmatch's kills per side this war (owner, 16 Nov 2026), for the HUD's counter.</summary>
        public int KillsHegemony, KillsNull, KillsCovenant;

        public bool GatesOpen => Known && CrossfireRules.GatesOpen(Now.Phase);

        public bool JoinOpen(double now) =>
            Known && (Now.Phase == CrossfirePhase.Muster || (Now.Phase == CrossfirePhase.Call && now >= JoinOpensAt));

        /// <summary>The Rally Gates now (<see cref="CrossfireRules.GatePhaseAt"/>).</summary>
        public CrossfireGatePhase GatePhase(double now, double openingSeconds) =>
            Known ? CrossfireRules.GatePhaseAt(Now, openingSeconds, now) : CrossfireGatePhase.Shut;

        /// <summary>How far the Rally Gates have opened now, 0 to 1 (<see cref="CrossfireRules.GateOpenAmount"/>).</summary>
        public float GateOpenAmount(double now, double openingSeconds) =>
            Known ? CrossfireRules.GateOpenAmount(Now, openingSeconds, now) : 0f;

        /// <summary>The war takes players now: the join window, the Rally, or a running War until <paramref name="lateCutoffSeconds"/> before its end.</summary>
        public bool TakesPlayers(double now, double lateCutoffSeconds) =>
            JoinOpen(now) || (Known && CrossfireRules.LateJoinOpen(Now, lateCutoffSeconds, now));

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

        /// <summary>The last war's results screen (client).</summary>
        public static CrossfireResults Results;

        /// <summary>The state a Muster Gate follows: the server's where there is one, else the received one.</summary>
        public static CrossfireState ForGates => HasServer ? Server : Client;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Server = default;
            Client = default;
            Results = default;
            HasServer = false;
        }
    }
}
