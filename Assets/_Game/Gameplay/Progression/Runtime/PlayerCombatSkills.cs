using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>Owns learned combat skills, rare drop book unlocks, cooldowns, and skill casting execution.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacterProgression), typeof(PlayerResources), typeof(CharacterBuffManager))]
    public sealed class PlayerCombatSkills : NetworkBehaviour
    {
        /// <summary>A remote owner waits this long for the server's answer before it may ask to cast again.</summary>
        private const float CastRequestTimeout = 0.5f;

        [SerializeField] private SkillDefinitionSO[] availableSkills = Array.Empty<SkillDefinitionSO>();

        private PlayerCharacterProgression progression;
        private PlayerResources resources;
        private CharacterBuffManager buffManager;
        private CharacterCombatStats combatStats;
        private ISkillAttackExecutor attackExecutor;

        private readonly Dictionary<string, int> learnedRanks = new Dictionary<string, int>();
        private readonly HashSet<string> unlockedDropSkills = new HashSet<string>();
        private readonly Dictionary<string, float> cooldownEndTimes = new Dictionary<string, float>();
        private float castRequestPendingUntil;

        public event Action Changed;
        public event Action<SkillDefinitionSO> SkillCast;
        public event Action<string> SkillMessage;

        public IReadOnlyList<SkillDefinitionSO> AvailableSkills => availableSkills;

        private void Awake()
        {
            EnsureComponents();
        }

        private void EnsureComponents()
        {
            if (progression == null) progression = GetComponent<PlayerCharacterProgression>();
            if (resources == null) resources = GetComponent<PlayerResources>();
            if (buffManager == null) buffManager = GetComponent<CharacterBuffManager>();
            if (combatStats == null) combatStats = GetComponent<CharacterCombatStats>();
            if (attackExecutor == null) attackExecutor = GetComponent<ISkillAttackExecutor>();
        }

        public override void OnNetworkSpawn()
        {
            EnsureComponents();
            if (IsServer && buffManager != null)
            {
                buffManager.NextHitBuffsConsumed += OnNextHitBuffsConsumed;
                buffManager.BuffRemoved += OnServerBuffRemoved;
                NetworkManager.OnClientConnectedCallback += OnClientConnectedToServer;
            }
            if (IsOwner && !IsServer)
            {
                foreach (var pair in learnedRanks) SubmitRankServerRpc(pair.Key, pair.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (buffManager != null)
            {
                buffManager.NextHitBuffsConsumed -= OnNextHitBuffsConsumed;
                buffManager.BuffRemoved -= OnServerBuffRemoved;
            }
            if (NetworkManager != null) NetworkManager.OnClientConnectedCallback -= OnClientConnectedToServer;
        }

        public void SetAvailableSkills(SkillDefinitionSO[] skills)
        {
            availableSkills = skills;
            Changed?.Invoke();
        }

        public SkillDefinitionSO GetSkill(string skillId)
        {
            if (availableSkills == null) return null;
            for (int i = 0; i < availableSkills.Length; i++)
            {
                if (availableSkills[i] != null && availableSkills[i].skillId == skillId)
                    return availableSkills[i];
            }
            return null;
        }

        /// <summary>0 until a skill point has been spent on the skill; reaching its level alone teaches nothing.</summary>
        public int GetRank(SkillDefinitionSO skill)
        {
            if (skill == null) return 0;
            return learnedRanks.TryGetValue(skill.skillId, out int rank) ? Mathf.Max(0, rank) : 0;
        }

        public bool IsLearned(SkillDefinitionSO skill)
        {
            if (skill == null) return false;
            return GetRank(skill) > 0;
        }

        /// <summary>Book skills (level-60 drops, Wargod) stay locked until their book is acquired, whatever the level.</summary>
        public bool IsLocked(SkillDefinitionSO skill) =>
            skill != null && skill.requiresSkillBook && !unlockedDropSkills.Contains(skill.skillId);

        public bool IsDropSkillUnlocked(string skillId) => unlockedDropSkills.Contains(skillId);

        public void UnlockDropSkill(string skillId)
        {
            if (!unlockedDropSkills.Contains(skillId))
            {
                unlockedDropSkills.Add(skillId);
                SkillMessage?.Invoke($"Acquired secret martial arts book: {skillId} unlocked!");
                Changed?.Invoke();
            }
        }

        public void LockDropSkill(string skillId)
        {
            if (unlockedDropSkills.Remove(skillId))
            {
                Changed?.Invoke();
            }
        }

        public void UnlockAllDropSkills()
        {
            if (availableSkills == null) return;
            foreach (var skill in availableSkills)
            {
                if (skill != null && skill.requiresSkillBook)
                    unlockedDropSkills.Add(skill.skillId);
            }
            SkillMessage?.Invoke("All Rare Drop Book Skills unlocked for testing!");
            Changed?.Invoke();
        }

        public void LockAllDropSkills()
        {
            unlockedDropSkills.Clear();
            SkillMessage?.Invoke("All Rare Drop Book Skills locked.");
            Changed?.Invoke();
        }

        /// <summary>GM: unlocks every book skill and puts every skill at its top rank, ignoring level and skill points.</summary>
        public void GmSetAllRanksToMax()
        {
            if (availableSkills == null) return;
            foreach (var skill in availableSkills)
            {
                if (skill == null) continue;
                if (skill.requiresSkillBook) unlockedDropSkills.Add(skill.skillId);
                learnedRanks[skill.skillId] = skill.maxRank;
            }
            SkillMessage?.Invoke("GM: every skill unlocked at max rank.");
            Changed?.Invoke();
        }

        public bool CanUpgrade(SkillDefinitionSO skill, out string reason)
        {
            EnsureComponents();
            reason = string.Empty;
            if (skill == null) { reason = "Unknown skill"; return false; }

            if (progression == null) { reason = "Progression missing"; return false; }
            if (IsLocked(skill))
            {
                reason = $"Requires skill book ({skill.acquisitionNote})";
                return false;
            }
            if (progression.CurrentLevel < skill.requiredCharacterLevel)
            {
                reason = $"Requires character level {skill.requiredCharacterLevel}";
                return false;
            }

            int curRank = GetRank(skill);
            if (curRank >= skill.maxRank)
            {
                reason = "Maximum rank reached";
                return false;
            }

            int cost = skill.GetSkillPointCostForRank(curRank);
            if (progression.SkillPoints < cost)
            {
                reason = $"Requires {cost} skill point(s)";
                return false;
            }

            return true;
        }

        public int TryUpgradeMultiple(SkillDefinitionSO skill, int maxCount)
        {
            if (skill == null || maxCount <= 0 || progression == null) return 0;

            int upgraded = 0;
            while (upgraded < maxCount)
            {
                if (!CanUpgrade(skill, out _)) break;
                int curRank = GetRank(skill);
                int cost = skill.GetSkillPointCostForRank(curRank);
                if (!progression.TrySpendSkillPoints(cost)) break;

                learnedRanks[skill.skillId] = curRank + 1;
                upgraded++;
            }

            if (upgraded > 0)
            {
                if (IsSpawned && !IsServer) SubmitRankServerRpc(skill.skillId, learnedRanks[skill.skillId]);
                SkillMessage?.Invoke($"Upgraded {skill.displayName} to Rank {learnedRanks[skill.skillId]}");
                Changed?.Invoke();
            }
            return upgraded;
        }

        /// <summary>
        /// Tells the server which rank this player has, so its damage and costs use the real rank.
        /// TODO(progression): the server trusts this until skill points are server-owned.
        /// </summary>
        [ServerRpc]
        private void SubmitRankServerRpc(string skillId, int rank)
        {
            var skill = GetSkill(skillId);
            if (skill == null) return;
            learnedRanks[skillId] = Mathf.Clamp(rank, 0, skill.maxRank);
        }

        public bool IsOnCooldown(SkillDefinitionSO skill)
        {
            if (skill == null) return false;
            if (KryticalNova.Core.GmState.NoCooldowns && (!IsSpawned || IsOwner)) return false;
            return cooldownEndTimes.TryGetValue(skill.skillId, out float endTime) && Time.time < endTime;
        }

        public float GetRemainingCooldown(SkillDefinitionSO skill)
        {
            if (skill == null) return 0f;
            if (cooldownEndTimes.TryGetValue(skill.skillId, out float endTime))
                return Mathf.Max(0f, endTime - Time.time);
            return 0f;
        }

        /// <summary>Checks a cast against the local player's current target.</summary>
        public bool CanCast(SkillDefinitionSO skill, out string reason)
        {
            EnsureComponents();
            return CanCast(skill, ResolveTarget(skill), out reason);
        }

        /// <summary>An empty <paramref name="reason"/> on failure means "not yet" (still casting), not a user error.</summary>
        public bool CanCast(SkillDefinitionSO skill, Transform target, out string reason)
        {
            EnsureComponents();
            reason = string.Empty;
            if (skill == null) { reason = "No skill selected"; return false; }

            if (!IsLearned(skill))
            {
                reason = IsLocked(skill)
                    ? $"{skill.displayName} requires a skill book ({skill.acquisitionNote})."
                    : $"Learn {skill.displayName} with a skill point first.";
                return false;
            }
            if (resources == null || !resources.IsAlive)
            {
                reason = "Cannot cast while defeated.";
                return false;
            }
            if (Time.time < castRequestPendingUntil) return false;
            if (attackExecutor != null && attackExecutor.IsCasting && !attackExecutor.CanInterruptCast(skill)) return false;
            if (IsOnCooldown(skill))
            {
                float rem = GetRemainingCooldown(skill);
                reason = $"{skill.displayName} on cooldown ({rem:0.0}s remaining).";
                return false;
            }

            int rank = GetRank(skill);
            float chiCost = skill.GetChiCost(rank);
            if (!resources.CanSpendChi(chiCost))
            {
                reason = $"Not enough Chi ({chiCost:0} Chi required, current: {resources.Chi:0}).";
                return false;
            }
            if (skill.IsAttack)
            {
                if (attackExecutor == null) { reason = "This character cannot use attack skills."; return false; }
                if (!attackExecutor.CanExecute(skill, rank, target, out reason)) return false;
            }
            return true;
        }

        /// <summary>Shows a skill-related line in the player's feedback area.</summary>
        public void Announce(string message)
        {
            if (!string.IsNullOrEmpty(message)) SkillMessage?.Invoke(message);
        }

        /// <param name="quiet">Suppresses the failure message (used while a hotbar key is held to repeat).</param>
        public bool TryCastSkill(SkillDefinitionSO skill, out string reason, bool quiet = false)
        {
            EnsureComponents();
            Transform target = ResolveTarget(skill);
            if (!CanCast(skill, target, out reason))
            {
                if (!quiet && !string.IsNullOrEmpty(reason)) SkillMessage?.Invoke(reason);
                return false;
            }

            if (IsSpawned && !IsServer)
            {
                castRequestPendingUntil = Time.time + CastRequestTimeout;
                RequestCastSkillServerRpc(skill.skillId, ToReference(target));
                return true;
            }

            return ServerCast(skill, target, out reason);
        }

        private Transform ResolveTarget(SkillDefinitionSO skill) =>
            skill != null && skill.IsAttack && attackExecutor != null ? attackExecutor.ResolveTarget(skill) : null;

        private static NetworkObjectReference ToReference(Transform target)
        {
            var netObj = target != null ? target.GetComponentInParent<NetworkObject>() : null;
            return netObj != null && netObj.IsSpawned ? new NetworkObjectReference(netObj) : default;
        }

        private static Transform FromReference(NetworkObjectReference reference) =>
            reference.TryGet(out NetworkObject netObj) ? netObj.transform : null;

        [ServerRpc]
        private void RequestCastSkillServerRpc(string skillId, NetworkObjectReference targetReference)
        {
            var skill = GetSkill(skillId);
            if (skill == null) return;

            Transform target = FromReference(targetReference);
            string reason;
            if (CanCast(skill, target, out reason) && ServerCast(skill, target, out reason)) return;
            NotifyCastFailedClientRpc(reason ?? string.Empty);
        }

        [ClientRpc]
        private void NotifyCastFailedClientRpc(string reason)
        {
            if (!IsOwner) return;
            castRequestPendingUntil = 0f;
            if (!string.IsNullOrEmpty(reason)) SkillMessage?.Invoke(reason);
        }

        /// <summary>Runs the cast where combat is authoritative, then shows it on every peer.</summary>
        private bool ServerCast(SkillDefinitionSO skill, Transform target, out string reason)
        {
            if (!ExecuteCastInternal(skill, target, out reason)) return false;
            int rank = GetRank(skill);
            if (IsSpawned) TriggerSkillCastClientRpc(skill.skillId, rank, ToReference(target));
            else PresentCast(skill, rank, target);
            return true;
        }

        [ClientRpc]
        private void TriggerSkillCastClientRpc(string skillId, int rank, NetworkObjectReference targetReference)
        {
            var skill = GetSkill(skillId);
            if (skill == null) return;

            if (!IsServer)
            {
                // Mirror what the server already applied to its own copy.
                castRequestPendingUntil = 0f;
                cooldownEndTimes[skill.skillId] = Time.time + skill.cooldown;
                if (!skill.IsAttack && buffManager != null)
                {
                    buffManager.ApplyBuff(skill, rank);
                }

                SkillCast?.Invoke(skill);
                SkillMessage?.Invoke($"Activated {skill.displayName} (Rank {rank})!");
                Changed?.Invoke();
            }

            PresentCast(skill, rank, FromReference(targetReference));
        }

        private void PresentCast(SkillDefinitionSO skill, int rank, Transform target)
        {
            KryticalNova.Core.ClientPresentationRegistry.Feedback?.ShowFloatingText(transform.position, $"[{skill.displayName}]");
            attackExecutor?.Present(skill, rank, target);
        }

        private bool ExecuteCastInternal(SkillDefinitionSO skill, Transform target, out string reason)
        {
            reason = string.Empty;
            if (skill == null) return false;

            int rank = GetRank(skill);
            float chiCost = skill.GetChiCost(rank);
            if (chiCost > 0f)
            {
                if (resources != null && !resources.TrySpendChi(chiCost))
                {
                    reason = $"Not enough Chi ({chiCost:0} Chi required, current: {resources.Chi:0}).";
                    SkillMessage?.Invoke(reason);
                    return false;
                }
            }

            cooldownEndTimes[skill.skillId] = Time.time + skill.cooldown;

            if (skill.IsAttack)
            {
                attackExecutor?.Execute(skill, rank, target);
            }
            else if (buffManager != null)
            {
                buffManager.ApplyBuff(skill, rank);
            }

            SkillCast?.Invoke(skill);
            SkillMessage?.Invoke($"Activated {skill.displayName} (Rank {rank})!");
            Changed?.Invoke();
            return true;
        }

        // One-hit buffs are spent where the hit resolves (the server); clients drop their mirrored copy.
        private void OnNextHitBuffsConsumed() => ConsumeNextHitBuffsClientRpc();

        [ClientRpc]
        private void ConsumeNextHitBuffsClientRpc()
        {
            if (IsServer || buffManager == null) return;
            buffManager.ConsumeNextHitBuffs();
        }

        // Buffs tick on every peer by themselves, but only the server knows when one is cut short (shield broken,
        // dispelled, channel interrupted). Clients drop their mirrored copy so stats, the buff bar and the aura agree.
        private void OnServerBuffRemoved(string skillId)
        {
            if (IsSpawned) BuffRemovedClientRpc(skillId);
        }

        [ClientRpc]
        private void BuffRemovedClientRpc(string skillId)
        {
            if (IsServer || buffManager == null) return;
            buffManager.RemoveBuff(skillId);
        }

        /// <summary>A late joiner never saw the casts, so it is sent this player's running buffs with their time left.</summary>
        private void OnClientConnectedToServer(ulong clientId)
        {
            if (!IsSpawned || buffManager == null || clientId == NetworkManager.ServerClientId) return;
            var target = new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } } };
            foreach (var buff in buffManager.ActiveBuffs)
                SyncBuffClientRpc(buff.Skill.skillId, buff.Rank, buff.RemainingDuration, target);
        }

        [ClientRpc]
        private void SyncBuffClientRpc(string skillId, int rank, float remaining, ClientRpcParams target = default)
        {
            if (IsServer || buffManager == null || buffManager.HasBuff(skillId)) return;
            var skill = GetSkill(skillId);
            if (skill != null) buffManager.ApplyBuff(skill, rank, remaining);
        }
    }
}
