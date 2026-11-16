using UnityEngine;

namespace KryticalNova.Crossfire
{
    /// <summary>
    /// Every number of Crossfire, the hourly faction war (owner's DAY list item 17, 13 Nov 2026; the Lore Bible's Siege Night). The
    /// server's clock reads it; nothing about the war's times is in code. Plan: <c>_Docs/Plans/crossfire-plan.md</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Crossfire/Crossfire Settings", fileName = "CrossfireSettings")]
    public sealed class CrossfireSettings : ScriptableObject
    {
        [Header("When")]
        [Tooltip("On: the war starts at nightfall on the world clock (one game day is one real hour, so once an hour). Off: every Cycle Minutes from the server's start (test servers).")]
        public bool anchorToNightfall = true;
        [Tooltip("The in-game hour the war starts at (nightfall), 0-24.")]
        [Range(0f, 24f)] public float nightfallHour = 20f;
        [Tooltip("With the nightfall anchor off: minutes from one war's start to the next.")]
        [Min(5f)] public float cycleMinutes = 60f;

        [Header("Phases (Call > Rally > War > End > Gap)")]
        [Tooltip("Call: minutes before the war that eligible players are told it is coming.")]
        [Min(0f)] public float callMinutes = 10f;
        [Tooltip("One call pop-up per this many seconds left before the gates open (60: \"War starts in N minutes\", once a minute). It never names seconds.")]
        [Min(5f)] public float callRepeatSeconds = 60f;
        [Tooltip("The last minutes of the Call in which the Rally Points take players in (the join window).")]
        [Min(0f)] public float joinWindowMinutes = 3f;
        [Tooltip("Rally: seconds everyone waits in their yard with the gates shut.")]
        [Min(0f)] public float musterSeconds = 60f;
        [Tooltip("War: minutes the gates stand open and the war runs.")]
        [Min(1f)] public float warMinutes = 15f;
        [Tooltip("End: seconds of results before everyone is sent back where they entered.")]
        [Min(0f)] public float endSeconds = 20f;

        [Header("Who")]
        [Tooltip("No war below this level.")]
        [Min(1)] public int minLevel = 10;
        [Tooltip("Levels in one bracket (one war is players of one bracket).")]
        [Min(1)] public int bracketSize = 10;
        [Tooltip("Brackets in all; the last one takes every level above it (X: 100-112).")]
        [Min(1)] public int bracketCount = 10;
        [Tooltip("The highest level a player can reach: the top of the last bracket, as its call names it (X: 100-112).")]
        [Min(1)] public int maxLevel = 112;

        [Header("Where each bracket fights (owner, 16 Nov 2026)")]
        [Tooltip("The brackets that have a Crossfire zone now (1 = I ... 10 = X). Players of every other bracket still get their own call, which says plainly that their bracket has no zone yet.")]
        public int[] zoneBrackets = { 10 };
        [Tooltip("The portal (its ZonePortal id) that takes a called player of a bracket with a zone onto their own faction's Rally Point, on this server: the Transit Row port.")]
        public string portPortalId = "zone01-northeast";

        [Header("The sides of a war (owner, 16 Nov 2026)")]
        [Tooltip("Players a faction's side holds in one war, real players and War Bots together (33: 99 in a war). The next real player is refused at the Rally Point; a real player always takes a War Bot's place.")]
        [Min(1)] public int capPerFaction = 33;
        [Tooltip("Told to a real player refused because their side is full. {cap}: the cap.")]
        [TextArea] public string sideFullText = "Crossfire: your side is full ({cap} of {cap}), so you can't join this war.";
        [Tooltip("Told to a player who left the war area during the war: their place and pay are gone.")]
        [TextArea] public string leftWarText = "Crossfire: you left the war, so you lose your place and its pay.";
        [Tooltip("Told to a real player who takes a War Bot's place on their side.")]
        [TextArea] public string tookBotsPlaceText = "Crossfire: you take a War Bot's place on your side.";

        [Header("The War Bot vote of a short side (before the gates open)")]
        [Tooltip("Seconds a short side has to vote, from the Rally's start (it always closes a second before the gates open). Nobody answering is a no.")]
        [Min(1f)] public float voteSeconds = 20f;
        [Tooltip("The vote always closes at least this many seconds before the gates open, so the bots are in before the war.")]
        [Min(0f)] public float voteClosesBeforeGatesSeconds = 1f;
        [Tooltip("The small heading of the vote's accept / decline panel.")]
        public string voteEyebrow = "CROSSFIRE";
        [Tooltip("The question. {real}: the side's real players; {cap}: the cap.")]
        [TextArea] public string voteText = "Your side has {real} of {cap} players. Fill it to {cap} with War Bots?";
        [Tooltip("Told to the side when its vote passed. {cap}, {real} as above.")]
        [TextArea] public string votePassedText = "Crossfire: your side voted yes. War Bots fill it to {cap}.";
        [Tooltip("Told to the side when its vote failed or nobody answered.")]
        [TextArea] public string voteFailedText = "Crossfire: your side voted no. You fight with {real}.";

        [Header("Team deathmatch: the most kills wins, a tie is possible. The pay (owner's numbers, 16 Nov 2026)")]
        [Tooltip("The character's EXP, in % of the EXP of their current level step (win / tie / loss). A character at the level cap gets the Maxed Character share in Creds instead.")]
        public Vector3 characterExpPercent = new Vector3(10f, 6f, 4f);
        [Tooltip("The active pet's and the selected mount's EXP, in % of the EXP that fills their growth (win / tie / loss). A fully grown one gets its Maxed share in Creds instead.")]
        public Vector3 companionExpPercent = new Vector3(0.5f, 0.35f, 0.25f);
        [Tooltip("Creds for a WIN in the top bracket (X). Bracket n pays n / Bracket Count of it.")]
        [Min(0)] public long topWinCreds = 50000;
        [Tooltip("A tie's and a loss's Creds, as a share of the win's (tie / loss).")]
        public Vector2 tieAndLossCredsShare = new Vector2(0.6f, 0.4f);
        [Tooltip("Extra Creds, as a share of that result's Creds, for each share that is already maxed (character at the cap / pet fully grown / mount fully grown).")]
        public Vector3 maxedCredsShare = new Vector3(0.5f, 0.1f, 0.1f);
        [Tooltip("Told to each real player when the war ends. {kills}: their side's kills; {score}: every side's kills; {pay}: what they were paid.")]
        [TextArea] public string wonText = "Crossfire is over: your side WON with {kills} kills ({score}). {pay}";
        [TextArea] public string tiedText = "Crossfire is over: a TIE at {kills} kills ({score}). {pay}";
        [TextArea] public string lostText = "Crossfire is over: your side lost with {kills} kills ({score}). {pay}";
        [Tooltip("Told to everyone when the war ends with no kill on any side: then nobody is paid (owner, 16 Nov 2026).")]
        [TextArea] public string noKillsText = "Crossfire is over: no side scored a kill, so nobody is paid.";
        [Tooltip("The chance a kill of a real player by a real player drops an item (1 in 2,000; War Bots never drop).")]
        [Range(0f, 1f)] public float killDropChance = 0.0005f;
        [Tooltip("The drop is a piece of gear a Hostile of the killer's level can drop (the mobs' own rule): of the highest item level at or under it, within this many levels below.")]
        [Min(0)] public int killDropLevelsBelow = 9;
        [Tooltip("On: the drop is of the best rarity that level range has (as a Bruiser's). Off: any of them.")]
        public bool killDropBestRarity = true;
        [Tooltip("Told to the player whose kill dropped an item. {item}: its name.")]
        [TextArea] public string killDropText = "Crossfire: your kill dropped {item}!";

        [Header("Announcements (the top-of-screen news every player gets, as GM broadcasts)")]
        [Tooltip("The call, sent when the Call starts and again every Call Repeat Seconds until the gates open, to each player for THEIR OWN bracket. {time}: how long until the gates open; {bracket}: the bracket's numeral (X); {levels}: its levels (100-112); {level}: its lowest level; {where}: Where Text, or No Zone Text for a bracket with no zone yet.")]
        [TextArea] public string callText = "Crossfire! Bracket {bracket} (levels {levels}): the war starts in {time}. {where}";
        [Tooltip("{where} of the call for a bracket with a zone.")]
        [TextArea] public string whereText = "Go through the Transit Row port to reach your Rally Point.";
        [Tooltip("{where} of the call for a bracket with no zone yet.")]
        [TextArea] public string noZoneText = "Your bracket has no Crossfire zone yet, so there is no war for it this time.";
        [Tooltip("Told to a called player whose bracket has no zone yet when they go through the Transit Row port. {bracket}, {levels} as in the call.")]
        [TextArea] public string portNoZoneText = "Crossfire: bracket {bracket} has no zone yet.";
        [Tooltip("Told to a player of a bracket with a zone when their faction has no Rally Point on this map yet.")]
        [TextArea] public string portNoRallyText = "Crossfire: your faction's Rally Point is not built yet.";
        [Tooltip("Told to a player the Transit Row port has just put on their Rally Point (empty: nothing is said).")]
        [TextArea] public string portArrivedText = "Crossfire: you are at your Rally Point. Wait for the Rally Gates to open.";
        [Tooltip("Sent once when the gates close after a war.")]
        [TextArea] public string gatesClosedText = "Crossfire is over: the gates are closed.";
        [Tooltip("The last seconds before the gates open count down, one line a second (10: 10 .. 1). No other line names seconds. 0 = none.")]
        [Min(0f)] public float finalCountdownSeconds = 10f;
        [Tooltip("A line of the final countdown. {n}: the number.")]
        public string countdownText = "Crossfire: the gates open in {n}";

        /// <summary>Where the one asset lives: <c>Gameplay/Crossfire/Resources/CrossfireSettings.asset</c>.</summary>
        /// <summary>The name players see for a phase (owner, 13 Nov 2026: the staging is the Rally, its yards the Rally Points and their gates the Rally Gates;
        /// the code keeps Muster as the identifier).</summary>
        public static string PhaseName(CrossfirePhase phase) => phase == CrossfirePhase.Muster ? "Rally" : phase.ToString();

        public const string ResourcePath = "CrossfireSettings";

        static CrossfireSettings s_Loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Loaded = null;

        /// <summary>The settings asset; null (with one error) when it is missing: then no war runs.</summary>
        public static CrossfireSettings Instance
        {
            get
            {
                if (s_Loaded == null)
                {
                    s_Loaded = Resources.Load<CrossfireSettings>(ResourcePath);
                    if (s_Loaded == null) Debug.LogError("[Kn] No Resources/" + ResourcePath + " asset: Crossfire does not run.");
                }
                return s_Loaded;
            }
        }

        /// <summary>The phase lengths in seconds, for <see cref="CrossfireRules"/>.</summary>
        /// <summary>The bracket of a level (1 = I ...), 0 below <see cref="minLevel"/>: no call.</summary>
        public int BracketOf(int level) => CrossfireRules.BracketOf(level, minLevel, bracketSize, bracketCount);

        /// <summary>This bracket has a Crossfire zone now (<see cref="zoneBrackets"/>).</summary>
        public bool HasZone(int bracket) => bracket > 0 && zoneBrackets != null && System.Array.IndexOf(zoneBrackets, bracket) >= 0;

        /// <summary>The levels of a bracket, as its call names them: "10-19" ... "100-112" (the last one runs to <see cref="maxLevel"/>).</summary>
        public string LevelsOf(int bracket)
        {
            if (bracket <= 0) return string.Empty;
            int low = minLevel + (bracket - 1) * bracketSize;
            int high = bracket >= bracketCount ? System.Math.Max(low, maxLevel) : low + bracketSize - 1;
            return low + "-" + high;
        }

        public CrossfireTimes Times => new CrossfireTimes
        {
            CallSeconds = callMinutes * 60.0,
            JoinWindowSeconds = joinWindowMinutes * 60.0,
            MusterSeconds = musterSeconds,
            WarSeconds = warMinutes * 60.0,
            EndSeconds = endSeconds,
        };
    }
}
