using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.WarBots
{
    /// <summary>
    /// The decisions of the War Bots as plain rules the EditMode tests pin (owner, 16 Nov 2026; CROSSFIRE-WARBOTS-BRIEF Part B and the
    /// owner's corrections). Crossfire is a team deathmatch: a bot chases kills for its side, picks low-health, isolated or
    /// teammate-attacking enemies, keeps out of fights it would clearly lose, heals ITSELF with Stims (no healers), runs when swarmed,
    /// plays its weapon class (ranged kite, tanks stay with the side), follows its party leader and the leader's follow / hold / attack
    /// order. "Human" is a reaction delay, a sometimes second-best choice and a beat it does not always keep; there is no aim to miss.
    /// Started from the test-lane bot brain's target rule (nearest target with a crowd penalty); it is not named here, so release builds compile without it.
    /// </summary>
    public static class WarBotRules
    {
        // ------------------------------------------------------------------ rolled when a bot comes in

        /// <summary>The lowest and highest of <paramref name="values"/>; the fallback range when there are none.</summary>
        public static void RangeOf(IReadOnlyList<int> values, int fallbackMin, int fallbackMax, out int min, out int max)
        {
            if (values == null || values.Count == 0)
            {
                min = Math.Min(fallbackMin, fallbackMax);
                max = Math.Max(fallbackMin, fallbackMax);
                return;
            }
            min = int.MaxValue;
            max = int.MinValue;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] < min) min = values[i];
                if (values[i] > max) max = values[i];
            }
        }

        /// <summary>One piece's score: its rarity step times <paramref name="perRarity"/>, plus its Temper.</summary>
        public static int GearScore(int rarity, int temper, int perRarity) => Math.Max(0, rarity) * Math.Max(1, perRarity) + Math.Max(0, temper);

        /// <summary>A player's gear score: the rounded average of its worn pieces (0 with nothing worn).</summary>
        public static int GearScoreOf(IReadOnlyList<(int rarity, int temper)> worn, int perRarity)
        {
            if (worn == null || worn.Count == 0) return 0;
            long sum = 0;
            for (int i = 0; i < worn.Count; i++) sum += GearScore(worn[i].rarity, worn[i].temper, perRarity);
            return (int)Math.Round((double)sum / worn.Count, MidpointRounding.AwayFromZero);
        }

        /// <summary>A score back to the rarity step and Temper a bot is dressed in; the Temper is held to <paramref name="temperCap"/>.</summary>
        public static void SplitGearScore(int score, int perRarity, int maxRarity, int temperCap, out int rarity, out int temper)
        {
            perRarity = Math.Max(1, perRarity);
            score = Math.Max(0, score);
            rarity = Math.Min(maxRarity, score / perRarity);
            temper = Math.Min(Math.Max(0, temperCap), score - rarity * perRarity);
            if (temper < 0) temper = 0;
        }

        /// <summary>A personality by the settings' weights; <paramref name="roll"/> is 0-1.</summary>
        public static WarBotStyle PickStyle(WarBotSettings s, double roll)
        {
            float total = 0f;
            for (int i = 0; i <= (int)WarBotStyle.LoneWolf; i++) total += s.WeightOf((WarBotStyle)i);
            if (total <= 0f) return WarBotStyle.Careful;
            double at = Clamp01(roll) * total;
            for (int i = 0; i <= (int)WarBotStyle.LoneWolf; i++)
            {
                at -= s.WeightOf((WarBotStyle)i);
                if (at < 0d) return (WarBotStyle)i;
            }
            return WarBotStyle.LoneWolf;
        }

        /// <summary>
        /// Rolls a bot: its personality, skill rating and reactions, a level between the lowest and highest REAL player of the war and a
        /// gear score between the worst- and best-geared one (so not every bot is maxed), and whether it brings a mount.
        /// </summary>
        public static WarBotProfile RollProfile(WarBotSettings s, WarBotRole role, int levelMin, int levelMax, int gearMin, int gearMax, System.Random rng)
        {
            float skill = Mathf.Lerp(s.skillMin, s.skillMax, (float)rng.NextDouble());
            // a better bot reacts faster: its own window sits nearer the quick end of the settings' range
            float fast = Mathf.Lerp(s.reactionMaxSeconds, s.reactionMinSeconds, skill);
            float slow = Mathf.Lerp(fast, s.reactionMaxSeconds, 0.5f);
            return new WarBotProfile
            {
                Style = PickStyle(s, rng.NextDouble()),
                Role = role,
                Skill = skill,
                ReactFast = fast,
                ReactSlow = Math.Max(fast, slow),
                Level = Between(levelMin, levelMax, rng),
                GearScore = Between(gearMin, gearMax, rng),
                HasMount = rng.NextDouble() < s.mountShare,
                Seed = rng.Next(),
            };
        }

        /// <summary>
        /// <paramref name="points"/> split by <paramref name="shares"/> (x..w: Strength, Vitality, Agility, Spirit): each its rounded-down
        /// share, the rest to the largest share, so the four always add up to the points.
        /// </summary>
        public static int[] SplitPoints(int points, Vector4 shares)
        {
            var split = new int[4];
            if (points <= 0) return split;
            float total = Math.Max(0f, shares.x) + Math.Max(0f, shares.y) + Math.Max(0f, shares.z) + Math.Max(0f, shares.w);
            if (total <= 0f) { split[0] = points; return split; }
            int given = 0, largest = 0;
            for (int i = 0; i < 4; i++)
            {
                float share = Math.Max(0f, shares[i]);
                split[i] = (int)Math.Floor(points * share / total);
                given += split[i];
                if (share > Math.Max(0f, shares[largest])) largest = i;
            }
            split[largest] += points - given;
            return split;
        }

        /// <summary>A whole number from <paramref name="a"/> to <paramref name="b"/>, both included, in either order.</summary>
        public static int Between(int a, int b, System.Random rng)
        {
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            return lo == hi ? lo : rng.Next(lo, hi + 1);
        }

        // ------------------------------------------------------------------ human reactions

        /// <summary>How long the bot takes to act on something new, seconds: between its own fast and slow.</summary>
        public static float ReactionSeconds(in WarBotProfile p, double roll) => Mathf.Lerp(p.ReactFast, p.ReactSlow, (float)Clamp01(roll));

        /// <summary>The chance a choice is the second-best one: high for a weak bot, none for a perfect one.</summary>
        public static float SlipChance(WarBotSettings s, float skill) => s.slipChanceAtZeroSkill * (1f - Mathf.Clamp01(skill));

        /// <summary>How far from the beat the bot presses its next skill (seconds; negative early). <paramref name="roll"/> is 0-1.</summary>
        public static double BeatError(WarBotSettings s, float skill, double roll)
        {
            double spread = Mathf.Lerp(s.beatErrorWorstSeconds, s.beatErrorBestSeconds, Mathf.Clamp01(skill));
            return (Clamp01(roll) * 2d - 1d) * spread;
        }

        /// <summary>
        /// The next attack skill of the Cadence combo: when the bot tries the combo (<paramref name="tryCombo"/>), the ready skill with one
        /// hit more than the last (1 after 3 or after nothing); otherwise, or when that one is not ready, the first ready one. -1 none.
        /// </summary>
        public static int NextComboSkill(IReadOnlyList<int> readyHitCounts, int lastHits, bool tryCombo)
        {
            if (readyHitCounts == null || readyHitCounts.Count == 0) return -1;
            if (tryCombo)
            {
                int want = lastHits >= 1 && lastHits < 3 ? lastHits + 1 : 1;
                for (int i = 0; i < readyHitCounts.Count; i++)
                    if (readyHitCounts[i] == want) return i;
            }
            return 0;
        }

        /// <summary>The bot tries the 1-2-3 order on this skill: the settings' chance, scaled by its skill rating.</summary>
        public static bool TriesCombo(WarBotSettings s, float skill, double roll) => roll < s.comboTryChance * Mathf.Clamp01(skill);

        // ------------------------------------------------------------------ the fight

        /// <summary>
        /// What the bot does this pass, in this order: Stim when low; run when swarmed (or keep running until healed); regroup on an
        /// outnumbered leader; follow the leader; buff; kite as ranged; the attack under way goes on (with a skill when one is ready);
        /// otherwise a new target (the leader's first), or a walk toward the fight. <paramref name="roll"/> (0-1) decides a slip.
        /// </summary>
        public static WarBotPlan Decide(in WarBotView v, IReadOnlyList<WarBotCombatant> all, WarBotSettings s, double roll)
        {
            if (!v.Alive) return default;
            CountAround(v, all, s.dangerMeters, out int attackersOnMe, out int enemiesNear, out int alliesNear, out int nearest);
            bool enemyInSight = nearest >= 0 && FlatDistance(v.Position, all[nearest].Position) <= s.sightMeters;

            if (v.Health < s.stimHealth && v.StimReady) return new WarBotPlan { Act = WarBotAct.Stim, Why = "low" };

            bool swarmed = attackersOnMe >= s.swarmedBy || enemiesNear - alliesNear >= s.loseMargin;
            if ((v.Retreating && v.Health < s.returnHealth) || (v.Health < RetreatHealth(s, v.Profile.Style) && swarmed))
                return new WarBotPlan { Act = WarBotAct.Retreat, MoveTo = FallBackPoint(v, nearest >= 0 ? all[nearest].Position : v.Position, s), Why = "swarmed" };

            if (v.HasLeader)
            {
                if (s.regroupWhenOutnumbered && Outnumbered(v.LeaderPosition, v.Side, all, s) && FlatDistance(v.Position, v.LeaderPosition) > s.followMeters)
                    return new WarBotPlan { Act = WarBotAct.Move, MoveTo = v.LeaderPosition, Why = "regroup" };
                if (v.Order == WarBotOrder.Follow && v.LeaderTargetGhostId == 0 && FlatDistance(v.Position, v.LeaderPosition) > s.followMeters)
                    return new WarBotPlan { Act = WarBotAct.Move, MoveTo = v.LeaderPosition, Why = "follow" };
            }

            if (v.BuffReady && (v.Profile.Style == WarBotStyle.Support || (s.buffBetweenFights && !enemyInSight)))
                return new WarBotPlan { Act = WarBotAct.Buff, Why = "buff" };

            if (v.Profile.Role == WarBotRole.Ranged && nearest >= 0)
            {
                WarBotCombatant close = all[nearest];
                if (close.TargetGhostId == v.GhostId && FlatDistance(v.Position, close.Position) < s.kiteWithinMeters)
                    return new WarBotPlan { Act = WarBotAct.Move, MoveTo = Away(v.Position, close.Position, s.kiteToMeters), TargetGhostId = close.GhostId, Why = "kite" };
            }

            if (v.TargetGhostId != 0 && v.TargetValid)
                return v.SkillReady ? new WarBotPlan { Act = WarBotAct.Skill, TargetGhostId = v.TargetGhostId, Why = "combo" } : default;

            int pick = 0;
            string why = null;
            if (v.HasLeader && v.Order != WarBotOrder.Hold && v.LeaderTargetGhostId != 0 && IsLiveEnemy(v, all, v.LeaderTargetGhostId))
            {
                pick = v.LeaderTargetGhostId;
                why = "leader";
            }
            if (pick == 0) pick = PickTarget(v, all, s, roll, out why);
            if (pick != 0) return new WarBotPlan { Act = WarBotAct.Attack, TargetGhostId = pick, Why = why };

            if (v.HasLeader && v.Order == WarBotOrder.Hold && FlatDistance(v.Position, v.HoldAt) > s.holdMeters * 0.5f)
                return new WarBotPlan { Act = WarBotAct.Move, MoveTo = v.HoldAt, Why = "hold" };
            if (v.Profile.Role == WarBotRole.Tank && TryAlliesCentre(v, all, out Vector3 centre) && FlatDistance(v.Position, centre) > s.tankLeashMeters)
                return new WarBotPlan { Act = WarBotAct.Move, MoveTo = centre, Why = "with side" };
            if (v.Walking) return default;
            // nothing in sight: toward the nearest enemy of the war (team deathmatch: the kills are out there)
            if (nearest >= 0 && !(v.HasLeader && v.Order == WarBotOrder.Hold))
                return new WarBotPlan { Act = WarBotAct.Move, MoveTo = all[nearest].Position, Why = "hunt" };
            return default;
        }

        /// <summary>
        /// The enemy to attack, or 0: alive, of another side, in sight (within the hold ring under a Hold order). The best score wins:
        /// distance, less a bonus for missing health, for being isolated and for attacking a teammate, plus a penalty for each teammate
        /// beyond two already on it; a fight it would clearly lose is left out unless the bot is aggressive. A slip (by skill) takes the
        /// second best.
        /// </summary>
        public static int PickTarget(in WarBotView v, IReadOnlyList<WarBotCombatant> all, WarBotSettings s, double roll, out string why)
        {
            why = null;
            if (all == null) return 0;
            int best = -1, second = -1;
            float bestScore = float.MaxValue, secondScore = float.MaxValue;
            string bestWhy = null, secondWhy = null;
            bool hold = v.HasLeader && v.Order == WarBotOrder.Hold;
            for (int i = 0; i < all.Count; i++)
            {
                WarBotCombatant c = all[i];
                if (!c.Alive || c.GhostId == v.GhostId || c.Side == v.Side || c.Side == FactionId.None) continue;
                float distance = FlatDistance(v.Position, c.Position);
                if (distance > s.sightMeters) continue;
                if (hold && FlatDistance(v.HoldAt, c.Position) > s.holdMeters) continue;
                if (v.Profile.Style != WarBotStyle.Aggressive && WouldClearlyLose(v, c, all, s)) continue;

                string reason = "nearest";
                float score = distance - s.lowHealthBonusMeters * (1f - Mathf.Clamp01(c.Health));
                if (c.Health < 0.5f) reason = "low";
                if (IsIsolated(c, all, s.isolatedMeters))
                {
                    score -= s.isolatedBonusMeters * (v.Profile.Style == WarBotStyle.LoneWolf ? 2f : 1f);
                    reason = "isolated";
                }
                if (c.TargetGhostId != 0 && IsTeammate(v.Side, c.TargetGhostId, all, v.GhostId))
                {
                    bool helper = v.Profile.Style == WarBotStyle.Support || v.Profile.Role == WarBotRole.Tank;
                    if (v.Profile.Style != WarBotStyle.LoneWolf) score -= s.helpTeammateBonusMeters * (helper ? 2f : 1f);
                    reason = "help";
                }
                if (c.TargetGhostId == v.GhostId) { score -= s.helpTeammateBonusMeters; reason = "self-defence"; }
                int crowd = AttackersFromSide(c.GhostId, v.Side, all, v.GhostId);
                if (crowd > 2) score += (crowd - 2) * s.crowdPenaltyMeters;

                if (score < bestScore || (score == bestScore && best >= 0 && c.GhostId < all[best].GhostId))
                {
                    second = best; secondScore = bestScore; secondWhy = bestWhy;
                    best = i; bestScore = score; bestWhy = reason;
                }
                else if (score < secondScore)
                {
                    second = i; secondScore = score; secondWhy = reason;
                }
            }
            if (best < 0) return 0;
            if (second >= 0 && roll < SlipChance(s, v.Profile.Skill))
            {
                why = secondWhy + " (slip)";
                return all[second].GhostId;
            }
            why = bestWhy;
            return all[best].GhostId;
        }

        /// <summary>The health share under which this personality runs from a swarm.</summary>
        public static float RetreatHealth(WarBotSettings s, WarBotStyle style) => style switch
        {
            WarBotStyle.Aggressive => s.retreatHealthAggressive,
            WarBotStyle.Careful => s.retreatHealthCareful,
            _ => (s.retreatHealthAggressive + s.retreatHealthCareful) * 0.5f,
        };

        /// <summary>A target in a crowd of its own side, far from the bot's: enemies round it outnumber the bot's side round the bot by Lose Margin.</summary>
        public static bool WouldClearlyLose(in WarBotView v, in WarBotCombatant target, IReadOnlyList<WarBotCombatant> all, WarBotSettings s)
        {
            int enemies = 0, allies = 1;   // the bot itself
            for (int i = 0; i < all.Count; i++)
            {
                WarBotCombatant c = all[i];
                if (!c.Alive || c.GhostId == v.GhostId) continue;
                if (c.Side == target.Side && FlatDistance(c.Position, target.Position) <= s.dangerMeters) enemies++;
                else if (c.Side == v.Side && FlatDistance(c.Position, v.Position) <= s.dangerMeters) allies++;
            }
            return enemies - allies >= s.loseMargin;
        }

        // ------------------------------------------------------------------ parties

        /// <summary>The leader's party-chat line read as an order (a whole word of the settings' lists, any case); None otherwise.</summary>
        public static WarBotOrder ParseOrder(string text, WarBotSettings s)
        {
            if (string.IsNullOrWhiteSpace(text)) return WarBotOrder.None;
            string t = " " + Normalise(text) + " ";
            if (HasWord(t, s.holdWords)) return WarBotOrder.Hold;
            if (HasWord(t, s.followWords)) return WarBotOrder.Follow;
            if (HasWord(t, s.attackWords)) return WarBotOrder.Attack;
            return WarBotOrder.None;
        }

        /// <summary>
        /// Whether the bot takes a party invite: only from its own side; a real player's when the settings say so (a bot in a bots-only
        /// party leaves it for the player); another bot's only when the bot is in no party.
        /// </summary>
        public static bool AcceptsInvite(WarBotSettings s, bool fromRealPlayer, bool sameSide, bool inParty, bool inPartyWithRealPlayer)
        {
            if (!sameSide) return false;
            if (fromRealPlayer) return s.acceptPlayerInvites && !inPartyWithRealPlayer;
            return !inParty && s.botPartySize > 1;
        }

        /// <summary>
        /// Which bots make way for real players (backfill), as indexes of up to <paramref name="count"/> bots: idle bots that are not in a
        /// real player's party first, then any idle bot, then fighting bots not in a real player's party, then anyone.
        /// </summary>
        public static List<int> PickReleases(IReadOnlyList<(bool inFight, bool inPartyWithRealPlayer)> bots, int count)
        {
            var order = new List<int>();
            if (bots == null || count <= 0) return order;
            for (int pass = 0; pass < 4 && order.Count < count; pass++)
                for (int i = 0; i < bots.Count && order.Count < count; i++)
                {
                    // pass 0: idle and not with a real player; 1: idle; 2: fighting, not with a real player; 3: anyone
                    bool fit = pass switch
                    {
                        0 => !bots[i].inFight && !bots[i].inPartyWithRealPlayer,
                        1 => !bots[i].inFight,
                        2 => !bots[i].inPartyWithRealPlayer,
                        _ => true,
                    };
                    if (fit && !order.Contains(i)) order.Add(i);
                }
            return order;
        }

        // ------------------------------------------------------------------ manners

        /// <summary>The bot may say a line now: its chat gap is over and the chance falls its way.</summary>
        public static bool MaySay(WarBotSettings s, double lastSaidAt, double now, double roll) => now - lastSaidAt >= s.chatGapSeconds && roll < s.chatChance;

        /// <summary>The bot may emote now: its emote gap is over and the chance falls its way.</summary>
        public static bool MayEmote(WarBotSettings s, double lastEmoteAt, double now, double roll) => now - lastEmoteAt >= s.emoteGapSeconds && roll < s.emoteChance;

        /// <summary>One line of a list (<paramref name="roll"/> 0-1), or null for an empty list.</summary>
        public static string Pick(string[] lines, double roll)
        {
            if (lines == null || lines.Length == 0) return null;
            int i = (int)(Clamp01(roll) * lines.Length);
            return lines[Math.Min(lines.Length - 1, i)];
        }

        // ------------------------------------------------------------------ geometry and counts

        /// <summary>
        /// After a respawn: the bot is put back on its Rally Point only when it got up far from it. In a war the server already revives
        /// it at its own side's Rally Point shrine (Crossfire's yard respawn), and it stays where it got up.
        /// </summary>
        public static bool BackToRallyAfterRespawn(Vector3 gotUpAt, Vector3 rally, WarBotSettings s) =>
            FlatDistance(gotUpAt, rally) > s.backToRallyAfterRespawnMeters;

        public static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>A point <paramref name="meters"/> from <paramref name="threat"/>, straight away from it past the bot.</summary>
        public static Vector3 Away(Vector3 me, Vector3 threat, float meters)
        {
            Vector3 d = me - threat;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0001f) d = Vector3.forward;
            return threat + d.normalized * meters + new Vector3(0f, me.y - threat.y, 0f);
        }

        /// <summary>Where a running bot goes: toward its Rally Point, swung away from the nearest enemy; the Rally Point itself when it is near.</summary>
        public static Vector3 FallBackPoint(in WarBotView v, Vector3 threat, WarBotSettings s)
        {
            Vector3 toRally = v.Rally - v.Position;
            toRally.y = 0f;
            if (toRally.magnitude <= s.kiteToMeters * 2f) return v.Rally;
            Vector3 away = v.Position - threat;
            away.y = 0f;
            Vector3 dir = (toRally.normalized * 2f + (away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.zero)).normalized;
            return v.Position + dir * Math.Min(toRally.magnitude, s.kiteToMeters * 3f);
        }

        static void CountAround(in WarBotView v, IReadOnlyList<WarBotCombatant> all, float radius, out int attackersOnMe, out int enemiesNear, out int alliesNear, out int nearestEnemy)
        {
            attackersOnMe = enemiesNear = alliesNear = 0;
            nearestEnemy = -1;
            float nearestDistance = float.MaxValue;
            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                WarBotCombatant c = all[i];
                if (!c.Alive || c.GhostId == v.GhostId || c.Side == FactionId.None) continue;
                float d = FlatDistance(v.Position, c.Position);
                if (c.Side == v.Side) { if (d <= radius) alliesNear++; continue; }
                if (d <= radius) enemiesNear++;
                if (c.TargetGhostId == v.GhostId && d <= radius) attackersOnMe++;
                if (d < nearestDistance) { nearestDistance = d; nearestEnemy = i; }
            }
        }

        static bool Outnumbered(Vector3 at, FactionId side, IReadOnlyList<WarBotCombatant> all, WarBotSettings s)
        {
            int enemies = 0, allies = 0;
            for (int i = 0; i < all.Count; i++)
            {
                WarBotCombatant c = all[i];
                if (!c.Alive || c.Side == FactionId.None || FlatDistance(at, c.Position) > s.dangerMeters) continue;
                if (c.Side == side) allies++; else enemies++;
            }
            return enemies - allies >= s.loseMargin;
        }

        static bool IsIsolated(in WarBotCombatant c, IReadOnlyList<WarBotCombatant> all, float meters)
        {
            for (int i = 0; i < all.Count; i++)
            {
                WarBotCombatant o = all[i];
                if (o.GhostId != c.GhostId && o.Alive && o.Side == c.Side && FlatDistance(o.Position, c.Position) <= meters) return false;
            }
            return true;
        }

        static bool IsTeammate(FactionId side, int ghostId, IReadOnlyList<WarBotCombatant> all, int self)
        {
            if (ghostId == self) return false;
            for (int i = 0; i < all.Count; i++)
                if (all[i].GhostId == ghostId) return all[i].Side == side;
            return false;
        }

        static bool IsLiveEnemy(in WarBotView v, IReadOnlyList<WarBotCombatant> all, int ghostId)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i].GhostId == ghostId) return all[i].Alive && all[i].Side != v.Side && all[i].Side != FactionId.None;
            return false;
        }

        static int AttackersFromSide(int targetGhostId, FactionId side, IReadOnlyList<WarBotCombatant> all, int self)
        {
            int n = 0;
            for (int i = 0; i < all.Count; i++)
                if (all[i].GhostId != self && all[i].Alive && all[i].Side == side && all[i].TargetGhostId == targetGhostId) n++;
            return n;
        }

        static bool TryAlliesCentre(in WarBotView v, IReadOnlyList<WarBotCombatant> all, out Vector3 centre)
        {
            centre = default;
            int n = 0;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < all.Count; i++)
            {
                WarBotCombatant c = all[i];
                if (!c.Alive || c.GhostId == v.GhostId || c.Side != v.Side) continue;
                sum += c.Position;
                n++;
            }
            if (n == 0) return false;
            centre = sum / n;
            return true;
        }

        static string Normalise(string text)
        {
            var chars = text.ToLowerInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = ' ';
            return string.Join(" ", new string(chars).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
        }

        static bool HasWord(string paddedText, string[] words)
        {
            if (words == null) return false;
            foreach (string w in words)
            {
                if (string.IsNullOrWhiteSpace(w)) continue;
                if (paddedText.Contains(" " + Normalise(w) + " ")) return true;
            }
            return false;
        }

        /// <summary>
        /// War Bots fight for real players only (Master-24, 16 Nov 2026): true once no real player has been in the war for
        /// <see cref="WarBotSettings.noRealPlayerSeconds"/>, so every bot leaves. <paramref name="noneSince"/> is when the last real
        /// player went (NaN while one is there); it is kept here, so a player back within the grace starts the count again.
        /// </summary>
        public static bool AllGoHome(int realPlayers, ref double noneSince, double now, WarBotSettings s)
        {
            if (realPlayers > 0) { noneSince = double.NaN; return false; }
            if (double.IsNaN(noneSince)) noneSince = now;
            return now - noneSince >= s.noRealPlayerSeconds;
        }

        static double Clamp01(double x) => x < 0d ? 0d : x > 1d ? 1d : x;
    }
}
