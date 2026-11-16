#if KN_DEV_BOTS || UNITY_EDITOR   // test lane only: a release build has no bot brain (KnBotBrainGuardTests)
using System.Collections.Generic;
using Unity.Mathematics;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The decisions of the test-lane bot brain (<see cref="KnBotBrainServerSystem"/>), as plain rules the EditMode tests pin. A bot hunts
    /// inside the Hostile Zone it was sent to (its home): it attacks the nearest living Hostile there, a little spread so a group of bots
    /// does not pile onto one, walks back when it has strayed, and is put back home when it is far away (after a respawn in town).
    /// </summary>
    public static class KnBotBrainRules
    {
        /// <summary>What the brain does for one bot this pass.</summary>
        public enum Act : byte
        {
            /// <summary>Nothing (dead, not hunting, or already busy).</summary>
            Wait,
            /// <summary>Start the basic attack on <see cref="Plan.TargetGhostId"/>, as the second click on a Hostile does.</summary>
            Attack,
            /// <summary>Walk back to the home spot (a server walk order, as a click on the ground).</summary>
            WalkHome,
            /// <summary>Put the bot back at the home spot (it is far away, e.g. respawned in town).</summary>
            TeleportHome,
            /// <summary>Ask the server to switch Autopilot on again (the request a client's Autopilot button sends).</summary>
            AutopilotOn,
        }

        public struct Plan
        {
            public Act Act;
            public int TargetGhostId;
        }

        /// <summary>A Hostile the bot may attack.</summary>
        public struct Candidate
        {
            public int GhostId;
            public int3 FeetMm;
            public bool Alive;
        }

        /// <summary>Everything a pass knows about one bot.</summary>
        public struct View
        {
            public bool Alive;
            /// <summary>The bot is hunting: its Autopilot is on, or it was on before it died (the brain switches it on again).</summary>
            public bool Enrolled;
            public bool AutopilotOn;
            public bool HasHome;
            public int3 MeMm;
            public int3 HomeMm;
            /// <summary>The home Hostile Zone's radius.</summary>
            public int HomeRadiusMm;
            /// <summary>The ghost the basic attack is on now (0 none).</summary>
            public int CurrentTargetGhostId;
            /// <summary>The current target is still alive and in the home zone.</summary>
            public bool CurrentTargetValid;
            /// <summary>A server walk is under way (the attack's walk in or a walk home).</summary>
            public bool Walking;
            /// <summary>The brain may ask for Autopilot again (its ask timer has run out).</summary>
            public bool MayAskAutopilot;
        }

        /// <summary>Beyond this from home the bot is put back instead of walking (m).</summary>
        public const int TeleportBeyondMm = 120_000;
        /// <summary>Within this of home a bot with nothing to attack stays where it is (m).</summary>
        public const int HomeSlackMm = 8_000;
        /// <summary>Each other bot already on a Hostile counts as this much more distance when choosing (spreads a group out).</summary>
        public const int CrowdPenaltyMm = 6_000;

        public static Plan Decide(in View v, IReadOnlyList<Candidate> hostiles, IReadOnlyDictionary<int, int> attackers)
        {
            if (!v.Alive || !v.Enrolled || !v.HasHome) return default;
            long fromHome = FlatDistanceSq(v.MeMm, v.HomeMm);
            if (fromHome > (long)TeleportBeyondMm * TeleportBeyondMm) return new Plan { Act = Act.TeleportHome };
            if (!v.AutopilotOn)
            {
                // Autopilot switches itself off on a death or when the bot strays out of the zone: back in the zone, it is asked again
                bool inZone = fromHome <= (long)v.HomeRadiusMm * v.HomeRadiusMm;
                if (inZone && v.MayAskAutopilot) return new Plan { Act = Act.AutopilotOn };
                if (!inZone && !v.Walking) return new Plan { Act = Act.WalkHome };
                return default;
            }
            if (v.CurrentTargetGhostId != 0 && v.CurrentTargetValid) return default;   // the combo under way goes on
            int pick = PickTarget(v.MeMm, v.HomeMm, v.HomeRadiusMm, hostiles, attackers);
            if (pick >= 0) return new Plan { Act = Act.Attack, TargetGhostId = hostiles[pick].GhostId };
            if (!v.Walking && fromHome > (long)HomeSlackMm * HomeSlackMm) return new Plan { Act = Act.WalkHome };
            return default;
        }

        /// <summary>
        /// The Hostile to attack: alive, inside the home zone, the nearest once each bot already on it counts as
        /// <see cref="CrowdPenaltyMm"/> more; ties go to the lower ghost id. -1 when there is none.
        /// </summary>
        public static int PickTarget(int3 meMm, int3 homeMm, int homeRadiusMm, IReadOnlyList<Candidate> hostiles, IReadOnlyDictionary<int, int> attackers)
        {
            int best = -1;
            double bestScore = double.MaxValue;
            long radiusSq = (long)homeRadiusMm * homeRadiusMm;
            for (int i = 0; i < hostiles.Count; i++)
            {
                Candidate c = hostiles[i];
                if (!c.Alive || FlatDistanceSq(c.FeetMm, homeMm) > radiusSq) continue;
                int crowd = attackers != null && attackers.TryGetValue(c.GhostId, out int n) ? n : 0;
                double score = math.sqrt((double)FlatDistanceSq(meMm, c.FeetMm)) + (double)crowd * CrowdPenaltyMm;
                if (score < bestScore || (score == bestScore && c.GhostId < hostiles[best].GhostId))
                {
                    best = i;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>The ground-plane distance walked between two poses, mm; a jump over <see cref="TeleportBeyondMm"/> is a teleport (0).</summary>
        public static long WalkedMm(int3 fromMm, int3 toMm)
        {
            long sq = FlatDistanceSq(fromMm, toMm);
            if (sq > (long)TeleportBeyondMm * TeleportBeyondMm) return 0;
            return (long)math.round(math.sqrt((double)sq));
        }

        /// <summary>Seconds until a bot's next attack skill, 4-7 s, spread by its number so a group does not cast in step.</summary>
        public static float SkillGapSeconds(int botNumber, int castsSoFar) => 4f + ((botNumber * 7 + castsSoFar * 3) % 31) / 10f;

        public static long FlatDistanceSq(int3 a, int3 b)
        {
            long dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
#endif
