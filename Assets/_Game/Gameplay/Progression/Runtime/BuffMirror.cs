using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>One buff running on a player as the server has it (M4.5i): what every view's copy is kept equal to.</summary>
    public struct RunningBuffEntry
    {
        public string SkillId;
        public int Rank;
        /// <summary>Seconds left.</summary>
        public float Remaining;
        public bool FromSquad;
        public string Giver;
    }

    /// <summary>
    /// The entity netcode's buff rule (M4.5i, owner pick A, 5 Nov 2026): the server's running buffs are one list on the player's ghost,
    /// and every view of that player keeps its <see cref="CharacterBuffManager"/> copy equal to it. It gives what NGO's messages gave:
    /// a buff the server dropped early (shield broken, dispelled, one-hit buff spent, channel interrupted, class swap, death) leaves
    /// every copy (NGO BuffRemovedClientRpc / ConsumeNextHitBuffsClientRpc), and a view that arrives later gets the running ones with
    /// their time left (NGO SyncBuffClientRpc). A copy only shows: its heals and shields decide nothing on a view (PlayerResources is a
    /// mirror there, and hits are resolved on the server).
    /// </summary>
    public static class BuffMirror
    {
        /// <summary>Most buffs the list carries.</summary>
        public const int Max = 16;
        /// <summary>A copy's clock is set again only when it is this far off the server's (no jitter on the bar).</summary>
        public const float DriftSeconds = 1f;

        /// <summary>Server: the running buffs of <paramref name="buffs"/>, at most <see cref="Max"/>.</summary>
        public static void Capture(CharacterBuffManager buffs, List<RunningBuffEntry> into)
        {
            into.Clear();
            if (buffs == null) return;
            foreach (ActiveBuff buff in buffs.ActiveBuffs)
            {
                if (into.Count >= Max) break;
                if (buff?.Skill == null || buff.RemainingDuration <= 0f) continue;
                into.Add(new RunningBuffEntry
                {
                    SkillId = buff.Skill.skillId, Rank = buff.Rank, Remaining = buff.RemainingDuration,
                    FromSquad = buff.FromSquadmate, Giver = buff.Giver ?? string.Empty,
                });
            }
        }

        /// <summary>
        /// A view: makes <paramref name="copy"/> hold exactly the server's <paramref name="running"/> buffs: drops what the server no
        /// longer has, adds what it lacks with its time left, applies a changed rank again and sets a clock that drifted. True when
        /// anything changed.
        /// </summary>
        public static bool Reconcile(CharacterBuffManager copy, IReadOnlyList<RunningBuffEntry> running, Func<string, SkillDefinitionSO> skillOf)
        {
            if (copy == null || running == null || skillOf == null) return false;
            bool changed = false;
            List<string> gone = null;
            foreach (ActiveBuff buff in copy.ActiveBuffs)
            {
                if (buff?.Skill == null) continue;
                bool kept = false;
                for (int i = 0; i < running.Count && !kept; i++) kept = running[i].SkillId == buff.Skill.skillId && running[i].Remaining > 0f;
                if (!kept) (gone ??= new List<string>()).Add(buff.Skill.skillId);
            }
            if (gone != null) foreach (string skillId in gone) changed |= copy.RemoveBuff(skillId);

            for (int i = 0; i < running.Count; i++)
            {
                RunningBuffEntry entry = running[i];
                if (entry.Remaining <= 0f) continue;
                SkillDefinitionSO skill = skillOf(entry.SkillId);
                if (skill == null) continue;
                ActiveBuff have = copy.GetBuff(entry.SkillId);
                if (have != null && have.Rank == entry.Rank && have.FromSquadmate == entry.FromSquad)
                {
                    if (Mathf.Abs(have.RemainingDuration - entry.Remaining) > DriftSeconds) { have.RemainingDuration = entry.Remaining; changed = true; }
                    continue;
                }
                // a shared Stance never takes a lower rank by being given again: the server's rank is set by dropping the copy first
                if (have != null) copy.RemoveBuff(entry.SkillId);
                changed |= copy.ApplyBuff(skill, entry.Rank, entry.Remaining, entry.FromSquad, entry.FromSquad ? entry.Giver : null);
            }
            return changed;
        }
    }
}
