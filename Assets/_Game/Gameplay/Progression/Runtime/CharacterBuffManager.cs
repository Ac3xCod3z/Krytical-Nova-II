using System;
using System.Collections.Generic;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Progression
{
    [Serializable]
    public sealed class ActiveBuff
    {
        public SkillDefinitionSO Skill { get; }
        public int Rank { get; }
        public float RemainingDuration { get; set; }
        public float MaxDuration { get; }
        public float CurrentShieldCapacity { get; set; }
        public float MaxShieldCapacity { get; }
        public float NextChannelTickTime { get; set; }
        public float ReflectionChance { get; }
        public float ShieldShatterChance { get; }
        public float DispelBuffChance { get; }
        public List<StatModifier> AppliedModifiers { get; } = new List<StatModifier>();
        /// <summary>Given by a Squad member's cast rather than this character's own (a shared Stance).</summary>
        public bool FromSquadmate { get; set; }
        /// <summary>Who gave it, when <see cref="FromSquadmate"/>; for the buff bar.</summary>
        public string Giver { get; set; }

        public ActiveBuff(SkillDefinitionSO skill, int rank, float shieldCapacity, float duration, float reflectionChance, float shatterChance, float dispelChance)
        {
            Skill = skill;
            Rank = rank;
            MaxDuration = duration;
            RemainingDuration = duration;
            MaxShieldCapacity = shieldCapacity;
            CurrentShieldCapacity = shieldCapacity;
            ReflectionChance = reflectionChance;
            ShieldShatterChance = shatterChance;
            DispelBuffChance = dispelChance;
        }
    }

    /// <summary>Controls active timed buffs, absorption shields, channeled meditation, and master reactive procs.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterCombatStats))]
    public sealed class CharacterBuffManager : MonoBehaviour
    {
        private CharacterCombatStats combatStats;
        private PlayerResources resources;
        private readonly List<ActiveBuff> activeBuffs = new List<ActiveBuff>();

        public event Action BuffsChanged;
        public event Action<string> BuffMessage;
        /// <summary>Raised where the hit was resolved (server or offline) when one-hit buffs are spent.</summary>
        public event Action NextHitBuffsConsumed;
        /// <summary>A buff left this character for any reason (expired, spent, broken, dispelled, replaced), by skill id.</summary>
        public event Action<string> BuffRemoved;

        public IReadOnlyList<ActiveBuff> ActiveBuffs => activeBuffs;
        public bool HasActiveShield => GetActiveShieldCapacity() > 0f;
        public bool IsChanneling { get; private set; }

        private void Awake()
        {
            EnsureComponents();
        }

        private void EnsureComponents()
        {
            if (combatStats == null) combatStats = GetComponent<CharacterCombatStats>();
            if (resources == null) resources = GetComponent<PlayerResources>();
        }

        private void Update()
        {
            if (activeBuffs.Count == 0 && !IsChanneling) return;

            float dt = Time.deltaTime;
            bool changed = false;

            for (int i = activeBuffs.Count - 1; i >= 0; i--)
            {
                var buff = activeBuffs[i];

                // Channeled skill tick (e.g. Heavenly Serenity): a rank-scaled share of max HP and Chi each tick.
                if (buff.Skill.executionType == SkillExecutionType.Channeled)
                {
                    if (Time.time >= buff.NextChannelTickTime)
                    {
                        buff.NextChannelTickTime = Time.time + buff.Skill.channelTickInterval;
                        if (resources != null)
                        {
                            float tickHealth = resources.MaxHealth * (buff.Skill.GetChannelHealthRecovery(buff.Rank) / 100f);
                            float tickChi = resources.MaxChi * (buff.Skill.GetChannelChiRecovery(buff.Rank) / 100f);
                            // Every peer ticks its copy of the buff, but only the authority changes the pools;
                            // clients receive the result through the replicated values.
                            if (resources.HasAuthority)
                            {
                                resources.Heal(tickHealth);
                                resources.RestoreChi(tickChi);
                            }
                            string healed = tickHealth > 0f ? $"+{tickHealth:0.#} HP, " : string.Empty;
                            BuffMessage?.Invoke($"[{buff.Skill.displayName}] Restored {healed}+{tickChi:0.#} Flux");
                        }
                    }
                }

                buff.RemainingDuration -= dt;
                if (buff.RemainingDuration <= 0f)
                {
                    RemoveBuffAt(i);
                    changed = true;
                }
            }

            if (changed)
            {
                BuffsChanged?.Invoke();
            }
        }

        /// <param name="remaining">Seconds left when joining a buff already running elsewhere; negative = a fresh, full-length buff.</param>
        /// <param name="fromSquadmate">Given by a Squad member's cast (a shared Stance), named by <paramref name="giver"/>.</param>
        public bool ApplyBuff(SkillDefinitionSO skill, int rank = 1, float remaining = -1f, bool fromSquadmate = false, string giver = null)
        {
            EnsureComponents();
            if (skill == null || combatStats == null) return false;
            rank = Mathf.Clamp(rank, skill.minRank, skill.maxRank);
            // A buff shared in the Squad never stacks and is never made weaker by being given again: the timer
            // starts over at the higher of the two ranks (SquadBuffRules).
            if (skill.sharesWithSquad)
            {
                ActiveBuff running = GetBuff(skill.skillId);
                rank = SquadBuffRules.RankAfterRecast(running != null, running != null ? running.Rank : 0, rank);
            }

            float duration = skill.GetDuration(rank);
            float shieldCapacity = skill.isAbsorptionShield ? skill.GetShieldMultiplier(rank) * combatStats.GetStatValue(StatType.MaxHP) : 0f;
            float reflectChance = skill.GetReflectionChance(rank);
            float shatterChance = skill.GetDestructionChance(rank);
            float dispelChance = skill.dispelBuffChance;

            // Check if buff of this skillId already active: refresh it
            int existingIndex = activeBuffs.FindIndex(b => b.Skill.skillId == skill.skillId);
            if (existingIndex >= 0)
            {
                RemoveBuffAt(existingIndex);
            }

            var newBuff = new ActiveBuff(skill, rank, shieldCapacity, duration, reflectChance, shatterChance, dispelChance)
            {
                FromSquadmate = fromSquadmate,
                Giver = fromSquadmate ? giver : null,
            };
            if (remaining >= 0f) newBuff.RemainingDuration = Mathf.Min(remaining, duration);

            if (skill.executionType == SkillExecutionType.Channeled)
            {
                IsChanneling = true;
                newBuff.NextChannelTickTime = Time.time + skill.channelTickInterval;
            }

            // Apply rank-scaled stat modifiers to CharacterCombatStats
            var modifiers = skill.GetStatModifiers(rank);
            if (modifiers != null && modifiers.Length > 0)
            {
                foreach (var modPayload in modifiers)
                {
                    var mod = new StatModifier(modPayload.statType, modPayload.modifierType, modPayload.value, newBuff);
                    newBuff.AppliedModifiers.Add(mod);
                    combatStats.AddModifier(mod);
                }
            }

            activeBuffs.Add(newBuff);
            BuffMessage?.Invoke($"Applied {skill.displayName} (Rank {rank}, {duration:0}s)");
            BuffsChanged?.Invoke();
            ClientPresentationRegistry.Feel?.BuffStarted(transform, skill.PresentationId, newBuff.RemainingDuration);
            return true;
        }

        /// <summary>Drops a buff by skill id (a client mirroring a removal the server decided). Returns false if absent.</summary>
        public bool RemoveBuff(string skillId)
        {
            int index = activeBuffs.FindIndex(b => b.Skill.skillId == skillId);
            if (index < 0) return false;
            RemoveBuffAt(index);
            BuffsChanged?.Invoke();
            return true;
        }

        public bool HasBuff(string skillId)
        {
            return activeBuffs.Exists(b => b.Skill.skillId == skillId);
        }

        public ActiveBuff GetBuff(string skillId)
        {
            return activeBuffs.Find(b => b.Skill.skillId == skillId);
        }

        public float GetActiveShieldCapacity()
        {
            float total = 0f;
            for (int i = 0; i < activeBuffs.Count; i++)
            {
                if (activeBuffs[i].Skill.isAbsorptionShield)
                    total += activeBuffs[i].CurrentShieldCapacity;
            }
            return total;
        }

        /// <summary>Absorbs incoming damage up to the remaining capacity of active absorption shields. Returns unabsorbed damage.</summary>
        public float AbsorbDamage(float incomingDamage)
        {
            if (incomingDamage <= 0f) return 0f;

            float remainingDamage = incomingDamage;
            bool changed = false;

            for (int i = activeBuffs.Count - 1; i >= 0; i--)
            {
                var buff = activeBuffs[i];
                if (!buff.Skill.isAbsorptionShield || buff.CurrentShieldCapacity <= 0f) continue;

                if (buff.CurrentShieldCapacity >= remainingDamage)
                {
                    buff.CurrentShieldCapacity -= remainingDamage;
                    BuffMessage?.Invoke($"[{buff.Skill.displayName}] Absorbed {remainingDamage:0} dmg! (Shield HP: {buff.CurrentShieldCapacity:0}/{buff.MaxShieldCapacity:0})");
                    remainingDamage = 0f;
                    changed = true;
                    break;
                }
                else
                {
                    float absorbed = buff.CurrentShieldCapacity;
                    remainingDamage -= absorbed;
                    buff.CurrentShieldCapacity = 0f;
                    BuffMessage?.Invoke($"[{buff.Skill.displayName}] Absorbed {absorbed:0} dmg and depleted!");
                    RemoveBuffAt(i);
                    changed = true;
                }
            }

            if (changed) BuffsChanged?.Invoke();
            return remainingDamage;
        }

        /// <summary>Instantly shatters all active absorption shields (e.g. from Heavenly Destruction proc).</summary>
        public void ShatterShield()
        {
            bool shattered = false;
            for (int i = activeBuffs.Count - 1; i >= 0; i--)
            {
                if (activeBuffs[i].Skill.isAbsorptionShield)
                {
                    RemoveBuffAt(i);
                    shattered = true;
                }
            }
            if (shattered)
            {
                BuffMessage?.Invoke("[Shatterpoint] Shattered Aegis!");
                BuffsChanged?.Invoke();
            }
        }

        /// <summary>Checks if Heavenly Repulsion (defensive reflection) is active.</summary>
        public bool HasReflection(out float reflectionChance, out float reflectionMultiplier)
        {
            reflectionChance = 0f;
            reflectionMultiplier = 1.0f;
            for (int i = 0; i < activeBuffs.Count; i++)
            {
                if (activeBuffs[i].Skill.hasReflection && activeBuffs[i].ReflectionChance > 0f)
                {
                    reflectionChance = activeBuffs[i].ReflectionChance;
                    reflectionMultiplier = activeBuffs[i].Skill.reflectionMultiplier;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Checks if Heavenly Destruction (offensive shatter proc) is active.</summary>
        public bool HasShieldShatter(out float shatterChance, out float dispelChance)
        {
            shatterChance = 0f;
            dispelChance = 0f;
            for (int i = 0; i < activeBuffs.Count; i++)
            {
                if (activeBuffs[i].Skill.hasShieldShatter && activeBuffs[i].ShieldShatterChance > 0f)
                {
                    shatterChance = activeBuffs[i].ShieldShatterChance;
                    dispelChance = activeBuffs[i].DispelBuffChance;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Dispels active stance and general buffs (called on HD dispel proc).</summary>
        public void DispelStanceBuffs()
        {
            bool dispelled = false;
            for (int i = activeBuffs.Count - 1; i >= 0; i--)
            {
                var skill = activeBuffs[i].Skill;
                if (skill.category == SkillCategory.WeaponStance || skill.category == SkillCategory.GeneralUtility)
                {
                    RemoveBuffAt(i);
                    dispelled = true;
                }
            }
            if (dispelled)
            {
                BuffMessage?.Invoke("Stance wards dispelled by Shatterpoint!");
                BuffsChanged?.Invoke();
            }
        }

        /// <summary>True while a buff that empowers one hit (Redline) is charged and waiting for it.</summary>
        public bool HasNextHitBuff
        {
            get
            {
                for (int i = 0; i < activeBuffs.Count; i++)
                    if (activeBuffs[i].Skill.consumedOnNextHit) return true;
                return false;
            }
        }

        /// <summary>
        /// Spends buffs that only empower one hit (e.g. Spiritual Rage). Call after the hit's damage is resolved,
        /// so that hit benefits and the following ones do not.
        /// </summary>
        public bool ConsumeNextHitBuffs()
        {
            bool consumed = false;
            for (int i = activeBuffs.Count - 1; i >= 0; i--)
            {
                if (!activeBuffs[i].Skill.consumedOnNextHit) continue;
                BuffMessage?.Invoke($"{activeBuffs[i].Skill.displayName} spent on this hit.");
                RemoveBuffAt(i);
                consumed = true;
            }
            if (consumed)
            {
                BuffsChanged?.Invoke();
                NextHitBuffsConsumed?.Invoke();
            }
            return consumed;
        }

        /// <summary>Removes all active buffs, shields, and channels.</summary>
        public void ClearAllBuffs()
        {
            for (int i = activeBuffs.Count - 1; i >= 0; i--)
            {
                RemoveBuffAt(i);
            }
            IsChanneling = false;
            BuffsChanged?.Invoke();
        }

        /// <summary>Interrupts active channeled skill (e.g. Heavenly Serenity when damaged or moved).</summary>
        public void InterruptChanneled()
        {
            if (!IsChanneling) return;

            bool interrupted = false;
            for (int i = activeBuffs.Count - 1; i >= 0; i--)
            {
                if (activeBuffs[i].Skill.executionType == SkillExecutionType.Channeled)
                {
                    RemoveBuffAt(i);
                    interrupted = true;
                }
            }
            IsChanneling = false;
            if (interrupted)
            {
                BuffMessage?.Invoke("Second Wind interrupted!");
                BuffsChanged?.Invoke();
            }
        }

        private void RemoveBuffAt(int index)
        {
            var buff = activeBuffs[index];
            if (buff.Skill.executionType == SkillExecutionType.Channeled)
            {
                IsChanneling = false;
            }

            // Remove applied stat modifiers
            if (buff.AppliedModifiers.Count > 0 && combatStats != null)
            {
                foreach (var mod in buff.AppliedModifiers)
                {
                    combatStats.RemoveModifier(mod);
                }
            }

            activeBuffs.RemoveAt(index);
            ClientPresentationRegistry.Feel?.BuffEnded(transform, buff.Skill.PresentationId);
            BuffRemoved?.Invoke(buff.Skill.skillId);
        }
    }
}
