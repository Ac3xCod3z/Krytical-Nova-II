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

            if (members.Count <= 1)
            {
                Pay(credited, hostile, stats, gaps, creditedGap, 0f);
            }
            else
            {
                // The gap of the Squad's highest member here is the worst one; a far lower member is paid by it.
                int highestGap = int.MaxValue;
                foreach (PlayerParty member in members)
                    if (member.gameObject.scene == hostile.gameObject.scene) highestGap = Mathf.Min(highestGap, LevelGapRules.Gap(hostile.Level, member.Level));
                foreach (PlayerParty member in members)
                {
                    if (member.gameObject.scene != hostile.gameObject.scene) continue; // only members in the map of the kill
                    var progression = member.GetComponent<PlayerCharacterProgression>();
                    if (progression == null) continue;
                    int gap = LevelGapRules.SquadGap(gaps, LevelGapRules.Gap(hostile.Level, member.Level), highestGap);
                    Pay(progression, hostile, stats, gaps, gap, member.ServerExperienceBoost());
                }
            }

            Rewarded?.Invoke(new HostileKill
            {
                SpeciesId = hostile.Definition != null ? hostile.Definition.id : (ushort)0,
                Level = hostile.Level,
                Tier = stats.Tier,
                Position = hostile.transform.position,
                Credited = credited,
                DropPercent = LevelGapRules.DropPercent(gaps, creditedGap),
                GearDrops = LevelGapRules.GearDrops(gaps, creditedGap),
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

        private static void Pay(PlayerCharacterProgression player, HostileNet hostile, in HostileStats stats, LevelGapSettings gaps, int gap, float squadBoost)
        {
            int amount = ExperienceFor(stats, gaps, gap, squadBoost);
            var chat = player.GetComponent<PlayerChat>();
            string name = hostile.Definition != null ? hostile.Definition.displayName : "Hostile";
            if (amount <= 0)
            {
                if (chat != null) chat.ServerCombatNotice($"{name} is too far below you to be worth XP.");
                return;
            }
            player.GrantExperience(amount);
            if (chat != null) chat.ServerCombatNotice($"+{amount} XP ({name})");
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
