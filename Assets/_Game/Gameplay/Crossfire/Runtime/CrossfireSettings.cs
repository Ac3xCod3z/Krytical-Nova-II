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

        [Header("Phases (Call > Muster > War > End > Gap)")]
        [Tooltip("Call: minutes before the war that eligible players are told it is coming.")]
        [Min(0f)] public float callMinutes = 10f;
        [Tooltip("The call is repeated this often, in seconds.")]
        [Min(5f)] public float callRepeatSeconds = 60f;
        [Tooltip("The last minutes of the Call in which the Muster Yards take players in (the join window).")]
        [Min(0f)] public float joinWindowMinutes = 3f;
        [Tooltip("Muster: seconds everyone waits in their yard with the gates shut.")]
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

        [Header("HUD (this player's screen only)")]
        [Tooltip("Seconds the big Crossfire banner stays up each time: every call (repeated each Call Repeat Seconds) and the first seconds of the Muster, the War and the End. Between them only the small timer shows.")]
        [Min(1f)] public float bannerSeconds = 6f;

        /// <summary>Where the one asset lives: <c>Gameplay/Crossfire/Resources/CrossfireSettings.asset</c>.</summary>
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
