using System;
using System.Collections.Generic;
using KryticalNova.Progression;
using KryticalNova.Social;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>What one Hostile kill was, for whatever counts or rewards kills (Contracts later).</summary>
    public struct HostileKill
    {
        public HostileNet Hostile;
        public ushort SpeciesId;
        public int Level;
        public HostileTier Tier;
        public Vector3 Position;
        /// <summary>The player the kill is credited to: the holder of the first hit.</summary>
        public PlayerCharacterProgression Credited;
        /// <summary>Drop chance at the credited player's level gap, percent of normal, before the server multiplier.</summary>
        public float DropPercent;
        /// <summary>Whether gear may drop at the credited player's level gap.</summary>
        public bool GearDrops;
    }

    /// <summary>
    /// Pays out a Hostile kill where the game is decided: XP to the player who holds the kill and to the Squad
    /// members in the same map, each by their own level gap and with the Squad boost; and the XP a player loses
    /// for dying to a Hostile. The player who holds a kill is the one who landed the first hit
    /// (<see cref="HostileNet.TagHolder"/>), never whoever dealt the last blow or the most damage.
    /// </summary>
    public static class HostileKillRewards
    {
        /// <summary>Raised after a kill has been paid out. Drops, pet XP and Contracts hang here.</summary>
        public static event Action<HostileKill> Rewarded;

        private static readonly List<PlayerParty> members = new List<PlayerParty>(PartyRules.MaxSize);
        private static readonly List<PlayerParty> escorts = new List<PlayerParty>(PartyRules.MaxSize);
        private static readonly List<PayoutMember> records = new List<PayoutMember>(PartyRules.MaxSize);
        private static readonly List<Payout> payouts = new List<Payout>(PartyRules.MaxSize);

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Rewarded = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Listen()
        {
            HostileNet.AnyKilled -= OnHostileKilled;
            HostileNet.AnyKilled += OnHostileKilled;
            CombatEntity.AnyKilled -= OnAnyKilled;
            CombatEntity.AnyKilled += OnAnyKilled;
        }

        private static void OnHostileKilled(HostileNet hostile, KillingBlow blow)
        {
            LevelGapSettings gaps = hostile.LevelGap;
            // A kill nobody tagged (a GM command) is credited to whoever struck it, if a player did.
            PlayerCharacterProgression credited = hostile.TagHolder;
            if (credited == null && blow.Attacker != null) credited = blow.Attacker.GetComponent<PlayerCharacterProgression>();
            if (credited == null || gaps == null) return;

            HostileStats stats = hostile.Stats;
            int creditedGap = LevelGapRules.Gap(hostile.Level, credited.CurrentLevel);

            members.Clear();
            var party = credited.GetComponent<PlayerParty>();
            if (party != null) party.ServerGetMembers(members);

            // A Sponsor in a Squad with one of its Recruits is there to help, not to hunt.
            escorts.Clear();
            if (members.Count > 1)
                foreach (PlayerParty member in members)
                    if (PlayerParty.SponsorsOneOf(member, members)) escorts.Add(member);
            float dropPercent = LevelGapRules.DropPercent(gaps, creditedGap);
            bool gearDrops = LevelGapRules.GearDrops(gaps, creditedGap);

            // Who is paid, by which gap, is KillPayoutPlan (tested without a scene). "Same map" is the same scene: it holds with one
            // map server; map servers will need a map id (port list).
            var holderSponsor = credited.GetComponent<PlayerSponsor>();
            float sponsorRange = holderSponsor != null && holderSponsor.Settings != null ? holderSponsor.Settings.rangeMetres : 0f;
            records.Clear();
            foreach (PlayerParty member in members)
            {
                var memberSponsor = member.GetComponent<PlayerSponsor>();
                bool recruit = holderSponsor != null && holderSponsor.ServerSponsors(memberSponsor);
                records.Add(new PayoutMember
                {
                    Level = member.Level,
                    SameMap = member.gameObject.scene == hostile.gameObject.scene,
                    IsEscort = escorts.Contains(member),
                    IsRecruitOfHolder = recruit,
                    WithinSponsorRange = recruit && holderSponsor.IsNear(memberSponsor, sponsorRange),
                });
            }
            PayoutOutcome outcome = KillPayoutPlan.Plan(hostile.Level, gaps, records, party != null ? members.IndexOf(party) : -1, payouts);

            if (outcome == PayoutOutcome.EscortPaysRecruits || outcome == PayoutOutcome.EscortNoRecruitNear)
            {
                // What the Sponsor holds pays it nothing (owner pick A, 12 Nov 2026): its Recruits near it are paid as for their own
                // kill, the rest of the Squad by the Sponsor's gap at best; far enough below it, it drops nothing either (as before).
                if (holderSponsor != null && holderSponsor.Settings != null
                    && SponsorRules.SponsorDropsNothing(hostile.Level, credited.CurrentLevel, holderSponsor.Settings.sponsorNoDropLevels))
                {
                    dropPercent = 0f;
                    gearDrops = false;
                }
                var chat = credited.GetComponent<PlayerChat>();
                if (chat != null)
                    chat.ServerCombatNotice(outcome == PayoutOutcome.EscortPaysRecruits
                        ? "Your Recruit earned this kill's XP."
                        : $"Your Recruits must be within {sponsorRange:0} m to earn your kill's XP.");
                PayAll(members, payouts, hostile, stats, gaps);
            }
            else if (outcome == PayoutOutcome.Solo)
            {
                Pay(credited, hostile, stats, gaps, creditedGap, 0f);
            }
            else
            {
                PayAll(members, payouts, hostile, stats, gaps);
            }

            Rewarded?.Invoke(new HostileKill
            {
                Hostile = hostile,
                SpeciesId = hostile.Definition != null ? hostile.Definition.id : (ushort)0,
                Level = hostile.Level,
                Tier = stats.Tier,
                Position = hostile.transform.position,
                Credited = credited,
                DropPercent = dropPercent,
                GearDrops = gearDrops,
            });
        }

        /// <summary>XP for one player: the Hostile's own, by the level gap, with the Squad boost and the server rate.</summary>
        public static int ExperienceFor(in HostileStats stats, LevelGapSettings gaps, int gap, float squadBoost)
        {
            float percent = LevelGapRules.ExperiencePercent(gaps, gap);
            if (percent <= 0f || HostileRates.Experience <= 0f) return 0;
            float amount = stats.Experience * percent / 100f * (1f + Mathf.Max(0f, squadBoost)) * HostileRates.Experience;
            return Mathf.Max(1, Mathf.RoundToInt(amount)); // a kill that pays at all pays at least 1
        }

        private static void PayAll(List<PlayerParty> members, List<Payout> plan, HostileNet hostile, in HostileStats stats, LevelGapSettings gaps)
        {
            foreach (Payout payout in plan)
            {
                PlayerParty member = members[payout.Index];
                var progression = member.GetComponent<PlayerCharacterProgression>();
                if (progression == null) continue;
                // An escort is paid by its own gap and with no boost: the Squad's is for those it helps.
                Pay(progression, hostile, stats, gaps, payout.Gap, payout.SquadBoost ? member.ServerExperienceBoost() : 0f, payout.SponsorBonus);
            }
        }

        private static void Pay(PlayerCharacterProgression player, HostileNet hostile, in HostileStats stats, LevelGapSettings gaps, int gap, float squadBoost,
            bool sponsorBonusAllowed = true)
        {
            // A Recruit hunting near its Sponsor is paid the Sponsor's bonus: on top of the Squad's in the Sponsor's
            // own Squad, instead of it anywhere else, when it is the larger. Never on a kill the Sponsor itself holds.
            bool inSponsorSquad = false;
            PlayerSponsor sponsor = null;
            float sponsorBonus = sponsorBonusAllowed && player.TryGetComponent(out sponsor) ? sponsor.ServerExperienceBonus(out inSponsorSquad) : 0f;
            bool adds = inSponsorSquad && sponsor.Settings != null && sponsor.Settings.addsToSquadBoost;
            float boost = SponsorRules.ExperienceBoost(squadBoost, sponsorBonus, adds);
            // XP boosters add to whatever the Squad and the Sponsor give.
            float boosters = player.TryGetComponent(out PlayerExperienceBoosts boosts) ? boosts.ServerCharacterBonus : 0f;
            int amount = ExperienceFor(stats, gaps, gap, boost + boosters);
            var chat = player.GetComponent<PlayerChat>();
            string name = hostile.Definition != null ? hostile.Definition.displayName : "Hostile";
            if (amount <= 0)
            {
                if (chat != null) chat.ServerCombatNotice($"{name} is too far below you to be worth XP.");
                return;
            }
            player.GrantExperience(amount);
            bool sponsored = sponsorBonus > 0f && (adds || sponsorBonus >= squadBoost);
            if (chat != null)
                chat.ServerCombatNotice((sponsored ? $"+{amount} XP ({name}) · Sponsor +{Mathf.RoundToInt(sponsorBonus * 100f)}%" : $"+{amount} XP ({name})")
                    + (boosters > 0f ? $" · Booster +{Mathf.RoundToInt(boosters * 100f)}%" : string.Empty));
        }

        // ------------------------------------------------------------------ dying to a Hostile

        private static void OnAnyKilled(CombatEntity victim, KillingBlow blow)
        {
            if (!HostileRates.DeathPenalty || victim == null || blow.Attacker == null) return;
            var player = victim.GetComponent<PlayerCharacterProgression>();
            if (player == null) return; // not a player
            // Only a death to a Hostile costs XP: not a Callout, not another faction, not a GM command.
            var killer = blow.Attacker.GetComponent<HostileNet>();
            if (killer == null || killer.LevelGap == null) return;

            int loss = LevelGapRules.DeathLoss(killer.LevelGap, player.ExperienceRequired, player.Experience);
            if (loss <= 0) return;
            player.SetExperience(player.Experience - loss);
            var chat = player.GetComponent<PlayerChat>();
            if (chat != null) chat.ServerSystem($"You lost {loss} XP for falling to a Hostile.");
        }
    }
}
