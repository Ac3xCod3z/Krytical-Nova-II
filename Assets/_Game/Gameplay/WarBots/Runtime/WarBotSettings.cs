using UnityEngine;

namespace KryticalNova.WarBots
{
    /// <summary>
    /// Every number and every word of the War Bots (owner, 16 Nov 2026; CROSSFIRE-WARBOTS-BRIEF Part B): the server-run players that
    /// fill a short side of a Crossfire war after its vote. The server's War Bots system reads it; nothing about how a bot plays is in
    /// code. Lives in <c>Gameplay/WarBots/Resources/WarBotSettings.asset</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/War Bots/War Bot Settings", fileName = "WarBotSettings")]
    public sealed class WarBotSettings : ScriptableObject
    {
        [Header("Thinking")]
        [Tooltip("Seconds between two looks at the fight by one bot (all bots are spread over this, so a war of 96 costs the same each frame).")]
        [Min(0.05f)] public float thinkSeconds = 0.25f;
        [Tooltip("The quickest a bot reacts to something new (a new enemy, its target dying, an order), in seconds: the best bots.")]
        [Min(0f)] public float reactionMinSeconds = 0.25f;
        [Tooltip("The slowest a bot reacts, in seconds: the weakest bots. Each reaction is rolled between its own fast and slow.")]
        [Min(0f)] public float reactionMaxSeconds = 0.8f;
        [Tooltip("Skill rating range rolled per bot, 0-1. 1 = picks its target well, keeps the Cadence beat, rarely slips.")]
        [Range(0f, 1f)] public float skillMin = 0.35f;
        [Range(0f, 1f)] public float skillMax = 0.9f;
        [Tooltip("At skill 0, the chance a choice is the second-best one instead of the best (no instant perfect choices). Falls to 0 at skill 1.")]
        [Range(0f, 1f)] public float slipChanceAtZeroSkill = 0.45f;

        [Header("Personalities (rolled per bot; weights)")]
        [Min(0f)] public float aggressiveWeight = 3f;
        [Min(0f)] public float carefulWeight = 3f;
        [Min(0f)] public float supportWeight = 2f;
        [Min(0f)] public float loneWolfWeight = 2f;

        [Header("Finding a fight")]
        [Tooltip("A bot looks for enemies within this many metres.")]
        [Min(5f)] public float sightMeters = 60f;
        [Tooltip("A target the bot is already fighting stays its target out to this many times Sight Meters (it does not drop a fleeing enemy at the edge).")]
        [Min(1f)] public float keepTargetSightFactor = 1.5f;
        [Tooltip("An enemy with no enemy of its own side within this many metres counts as isolated.")]
        [Min(1f)] public float isolatedMeters = 12f;
        [Tooltip("Bonus (in metres of distance) for an isolated enemy; a lone wolf counts it twice.")]
        [Min(0f)] public float isolatedBonusMeters = 15f;
        [Tooltip("Bonus (metres) for an enemy at low health, times how much health it is missing (0-1).")]
        [Min(0f)] public float lowHealthBonusMeters = 25f;
        [Tooltip("Bonus (metres) for an enemy that is attacking a teammate (help a teammate under attack); a support or tank bot counts it twice.")]
        [Min(0f)] public float helpTeammateBonusMeters = 20f;
        [Tooltip("Penalty (metres) for each teammate already on that enemy beyond the first two (spreads a side out).")]
        [Min(0f)] public float crowdPenaltyMeters = 6f;
        [Tooltip("Enemies counted round a target and teammates round the bot, within this many metres, to judge a fight it would clearly lose.")]
        [Min(1f)] public float dangerMeters = 15f;
        [Tooltip("A fight is clearly lost when the enemies round the target outnumber the bot's side round it by this many. An aggressive bot ignores it.")]
        [Min(1)] public int loseMargin = 3;

        [Header("Staying alive")]
        [Tooltip("Health share (0-1) under which a bot uses an HP Stim when it has one ready.")]
        [Range(0f, 1f)] public float stimHealth = 0.45f;
        [Tooltip("The Stims a bot carries, most wanted first (item ids of the % HP Stims; the war gives them, they are never saved).")]
        public ushort[] stimItems = { 9042, 9041 };
        [Tooltip("How many of each Stim a bot carries into the war.")]
        [Min(0)] public int stimsEach = 6;
        [Tooltip("Seconds a bot waits before it tries another Stim (the Stim's own cooldown still decides).")]
        [Min(0.5f)] public float stimRetrySeconds = 5f;
        [Tooltip("Health share under which a bot runs away when swarmed: careful bots use the high number, aggressive the low.")]
        [Range(0f, 1f)] public float retreatHealthCareful = 0.4f;
        [Range(0f, 1f)] public float retreatHealthAggressive = 0.15f;
        [Tooltip("Swarmed: at least this many enemies within the danger range are attacking the bot, or outnumber its side there by Lose Margin.")]
        [Min(1)] public int swarmedBy = 3;
        [Tooltip("A bot that ran comes back to the fight once its health is above this share.")]
        [Range(0f, 1f)] public float returnHealth = 0.7f;

        [Header("Playing the weapon class")]
        [Tooltip("A ranged bot backs off when an enemy is closer than this many metres...")]
        [Min(0f)] public float kiteWithinMeters = 5f;
        [Tooltip("...to about this far from it.")]
        [Min(1f)] public float kiteToMeters = 11f;
        [Tooltip("A tank stays within this many metres of its side's nearest group and takes the enemies that attack it first.")]
        [Min(1f)] public float tankLeashMeters = 14f;
        [Tooltip("Seconds between two attack skills of one bot, at the shortest and the longest (rolled each time).")]
        [Min(0.5f)] public float skillGapMinSeconds = 3f;
        [Min(0.5f)] public float skillGapMaxSeconds = 6f;
        [Tooltip("Seconds after coming in before a bot casts its first skill.")]
        [Min(0f)] public float firstSkillAfterSeconds = 2f;
        [Tooltip("With no enemy in sight, a bot puts on a buff it does not have when one is ready. A support bot also does it in a fight, first.")]
        public bool buffBetweenFights = true;
        [Tooltip("Seconds a bot leaves a buff alone after the game refused to cast it (no Flux, a cooldown it cannot see...), so it never stands buffing forever instead of fighting.")]
        [Min(0.5f)] public float buffRetrySeconds = 8f;
        [Tooltip("Seconds between two lines in the server log that count what the War Bots chose and why casts were refused (0: never).")]
        [Min(0f)] public float logEverySeconds = 10f;

        [Header("Cadence (the combo system: 1-hit, 2-hit, 3-hit skill, each on the beat)")]
        [Tooltip("How far from the beat (seconds, either way) the best bot presses; the window is the game's own (CadenceSettings).")]
        [Min(0f)] public float beatErrorBestSeconds = 0.03f;
        [Tooltip("How far from the beat the weakest bot presses: most of its presses miss the window.")]
        [Min(0f)] public float beatErrorWorstSeconds = 0.25f;
        [Tooltip("At skill 1, the chance a bot follows the 1-2-3 order at all on its next skill; scaled down with skill.")]
        [Range(0f, 1f)] public float comboTryChance = 0.85f;

        [Header("Parties")]
        [Tooltip("A bot answers a party invite after this many seconds (and at most this many more), as a person reading it.")]
        [Min(0f)] public float inviteAnswerMinSeconds = 1f;
        [Min(0f)] public float inviteAnswerMaxSeconds = 3.5f;
        [Tooltip("A squad leader bot tries to invite free bots of its side again after this many seconds (shortest and longest, picked per try).")]
        [Min(1f)] public float partyTryMinSeconds = 8f;
        [Min(1f)] public float partyTryMaxSeconds = 14f;
        [Tooltip("A bot accepts an invite from a REAL player of its side unless its own party is already that player's or full.")]
        public bool acceptPlayerInvites = true;
        [Tooltip("Bots form parties of this size among themselves (0 = never); a real player's invite still wins.")]
        [Range(0, 5)] public int botPartySize = 3;
        [Tooltip("In a party, a bot keeps within this many metres of its leader when the order is Follow.")]
        [Min(1f)] public float followMeters = 6f;
        [Tooltip("When the order is Hold, a bot only fights within this many metres of where it was told to hold.")]
        [Min(1f)] public float holdMeters = 15f;
        [Tooltip("Party chat words of the LEADER that a bot obeys (whole word, any case).")]
        public string[] followWords = { "follow", "on me", "regroup" };
        public string[] holdWords = { "hold", "stay", "wait" };
        public string[] attackWords = { "attack", "go", "charge", "push" };
        [Tooltip("A party that is outnumbered round its leader (by Lose Margin) regroups on the leader, whatever the order.")]
        public bool regroupWhenOutnumbered = true;

        [Header("Moving")]
        [Tooltip("A walk goal this close counts as reached (metres).")]
        [Min(0.5f)] public float goalReachedMeters = 2f;
        [Tooltip("A bot that walks but moves less than this many metres...")]
        [Min(0.1f)] public float stuckMeters = 1f;
        [Tooltip("...for this many seconds, not fighting, is stuck and is put back on its Rally Point.")]
        [Min(1f)] public float stuckSeconds = 12f;
        [Tooltip("A dead bot gets up where the server revives it: in a war, at its own side's Rally Point shrine (Crossfire's yard respawn). " +
                 "Only if it got up further than this from its Rally Point (metres; outside the war, say in town) is it put back there.")]
        [Min(1f)] public float backToRallyAfterRespawnMeters = 60f;
        [Tooltip("Coming in: the first bot stands this far from the Rally Point's landing spot (metres)...")]
        [Min(0f)] public float rallySpreadMinMeters = 1.5f;
        [Tooltip("...each next ring of bots this much further out...")]
        [Min(0f)] public float rallySpreadStepMeters = 0.9f;
        [Tooltip("...in this many rings, then the rings start again (bots go round the spot at the golden angle, so none stand on another).")]
        [Min(1)] public int rallySpreadRings = 6;

        [Header("When the real players are gone (Master-24, 16 Nov 2026)")]
        [Tooltip("Bots fight for real players only: once no real player has been in the war for this many seconds, every bot leaves (a short grace, so one who relogs or steps out a moment does not empty the war).")]
        [Min(1f)] public float noRealPlayerSeconds = 15f;

        [Header("Mounts")]
        [Tooltip("The share of bots that bring a mount (0-1). It rides the way to the field and gets off to fight.")]
        [Range(0f, 1f)] public float mountShare = 0.3f;
        [Tooltip("A bot with a mount rides when its goal is farther than this many metres and no enemy is in sight.")]
        [Min(5f)] public float rideBeyondMeters = 40f;

        [Header("Manners (never spam)")]
        [Tooltip("Seconds between two chat lines of one bot, at the least.")]
        [Min(5f)] public float chatGapSeconds = 25f;
        [Tooltip("Seconds between two emotes of one bot, at the least.")]
        [Min(3f)] public float emoteGapSeconds = 20f;
        [Tooltip("The chance a bot says something when it has a reason (0-1).")]
        [Range(0f, 1f)] public float chatChance = 0.35f;
        [Tooltip("The chance a bot emotes after its kill, or salutes a new party leader (0-1).")]
        [Range(0f, 1f)] public float emoteChance = 0.5f;
        [Tooltip("Emotes after a kill (keys of the emote wheel: E/<group>/<name>).")]
        public string[] killEmotes = { "E/Celebrate/Beat Chest", "E/Celebrate/Dust Shoulder", "E/Taunt/Laugh And Point", "E/Celebrate/Finger Guns Single" };
        [Tooltip("Emotes at the war's win (the side with the most kills).")]
        public string[] winEmotes = { "E/Celebrate/Air Guitar", "E/Celebrate/Clapping Polite", "E/Celebrate/Amped Skitter" };
        [Tooltip("The emote to a new party leader.")]
        public string[] leaderEmotes = { "E/Greet/Salute", "E/Greet/Salute Stomp" };
        [Tooltip("Party chat when a bot is low and running (\"Need heal\" is never said: there are no healers, a bot heals itself).")]
        public string[] lowLines = { "Low, backing off!", "Falling back!", "Need help here!", "Pulling out, cover me!" };
        [Tooltip("Party chat when the party should regroup.")]
        public string[] regroupLines = { "Regroup on me!", "Too many, regroup!", "On the leader!" };
        [Tooltip("Party chat when a bot takes a real player's invite.")]
        public string[] joinLines = { "With you.", "On you, lead.", "Ready.", "Let's go." };
        [Tooltip("Party chat after a kill.")]
        public string[] killLines = { "Got one.", "Down!", "Target down.", "One less." };

        [Header("Looks and names")]
        [Tooltip("Names come from the creator's own generator (lineage flavour, the name rules). With this on, a bot's name never repeats an online player's.")]
        public bool uniqueNames = true;

        [Header("Attributes (a bot spends its level's points as a player of its role would: shares of Strength, Vitality, Agility, Spirit)")]
        public Vector4 damageAttributes = new Vector4(0.6f, 0.3f, 0.1f, 0f);
        public Vector4 tankAttributes = new Vector4(0.25f, 0.65f, 0.1f, 0f);
        public Vector4 rangedAttributes = new Vector4(0.2f, 0.25f, 0.55f, 0f);

        [Header("Gear quality (between the worst- and best-geared real player of the war)")]
        [Tooltip("Points one rarity step is worth in a gear score (above the Temper cap, so a better rarity always scores higher).")]
        [Min(1)] public int scorePerRarity = 200;

        [Header("No economy (owner's rule)")]
        [Tooltip("Always on: bots carry nothing that can be dropped, sold, traded or saved. Shown so the rule is visible; the code never saves a bot.")]
        public bool noEconomy = true;

        /// <summary>Where the one asset lives: <c>Gameplay/WarBots/Resources/WarBotSettings.asset</c>.</summary>
        public const string ResourcePath = "WarBotSettings";

        static WarBotSettings s_Loaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Loaded = null;

        /// <summary>The settings asset; the code defaults (with one warning) when it is missing, so a war can still be filled.</summary>
        public static WarBotSettings Instance
        {
            get
            {
                if (s_Loaded == null)
                {
                    s_Loaded = Resources.Load<WarBotSettings>(ResourcePath);
                    if (s_Loaded == null)
                    {
                        Debug.LogWarning("[WarBots] No Resources/" + ResourcePath + " asset: the bots play on the code's defaults.");
                        s_Loaded = CreateInstance<WarBotSettings>();
                    }
                }
                return s_Loaded;
            }
        }

        /// <summary>The attribute shares of a role (Strength, Vitality, Agility, Spirit).</summary>
        public Vector4 AttributesOf(WarBotRole role) => role switch
        {
            WarBotRole.Tank => tankAttributes,
            WarBotRole.Ranged => rangedAttributes,
            _ => damageAttributes,
        };

        /// <summary>The weight of a personality.</summary>
        public float WeightOf(WarBotStyle style) => style switch
        {
            WarBotStyle.Aggressive => aggressiveWeight,
            WarBotStyle.Careful => carefulWeight,
            WarBotStyle.Support => supportWeight,
            WarBotStyle.LoneWolf => loneWolfWeight,
            _ => 0f,
        };

        void OnValidate()
        {
            if (reactionMaxSeconds < reactionMinSeconds) reactionMaxSeconds = reactionMinSeconds;
            if (skillMax < skillMin) skillMax = skillMin;
            if (skillGapMaxSeconds < skillGapMinSeconds) skillGapMaxSeconds = skillGapMinSeconds;
            if (inviteAnswerMaxSeconds < inviteAnswerMinSeconds) inviteAnswerMaxSeconds = inviteAnswerMinSeconds;
            if (beatErrorWorstSeconds < beatErrorBestSeconds) beatErrorWorstSeconds = beatErrorBestSeconds;
            if (kiteToMeters < kiteWithinMeters) kiteToMeters = kiteWithinMeters + 1f;
            noEconomy = true;
        }
    }
}
