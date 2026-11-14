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

        [Header("Announcements (the top-of-screen news every player gets, as GM broadcasts)")]
        [Tooltip("The call, sent when the Call starts and again every Call Repeat Seconds until the gates open. {time}: how long until the gates open; {level}: the lowest level called.")]
        [TextArea] public string callText = "Crossfire! The war starts in {time}. Level {level} and up: get to your Rally Point.";
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
