using System;
using KryticalNova.Core;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// Owns learned combat skills, rare drop book unlocks, cooldowns, and skill casting execution. Ranks and book
    /// unlocks are decided where the player is simulated (the server, or offline) and mirrored to the owning client.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacterProgression), typeof(PlayerResources), typeof(CharacterBuffManager))]
    public sealed class PlayerCombatSkills : NetworkBehaviour, ISaveParticipant, KryticalNova.Items.IItemUseHandler
    {
        /// <summary>A remote owner waits this long for the server's answer before it may ask to cast again.</summary>
        private const float CastRequestTimeout = 0.5f;

        [SerializeField] private SkillDefinitionSO[] availableSkills = Array.Empty<SkillDefinitionSO>();

        private PlayerCharacterProgression progression;
        private PlayerResources resources;
        private CharacterBuffManager buffManager;
        private CharacterCombatStats combatStats;
        private ISkillAttackExecutor attackExecutor;
        private ISkillQueue queue;
        private PlayerGmState gmState;
        private IWeaponClassSource classSource;

        // Indexed like availableSkills: 0 = not learned. Only the owner needs them; casts carry their rank to everyone else.
        private NetworkList<byte> netRanks;
        private readonly NetworkVariable<ulong> netUnlockedBooks = new NetworkVariable<ulong>(
            0UL, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        private bool mirrorDirty;

        private readonly Dictionary<string, int> learnedRanks = new Dictionary<string, int>();
        private readonly HashSet<string> unlockedDropSkills = new HashSet<string>();
        private readonly Dictionary<string, float> cooldownEndTimes = new Dictionary<string, float>();
        private float castRequestPendingUntil;

        public event Action Changed;
        public event Action<SkillDefinitionSO> SkillCast;
        public event Action<string> SkillMessage;
        /// <summary>The character's weapon class changed (it equipped another class's weapon): (previous, current).</summary>
        public event Action<WeaponClassId, WeaponClassId> ActiveClassChanged;

        public IReadOnlyList<SkillDefinitionSO> AvailableSkills => availableSkills;

        /// <summary>The class of the weapon in hand. None when nothing tracks it (a test avatar): every skill is then usable.</summary>
        public WeaponClassId ActiveClass => classSource != null ? classSource.ActiveClass : WeaponClassId.None;

        /// <summary>Shared skills always; a class skill only while its class's weapon is in hand.</summary>
        public bool IsInActiveKit(SkillDefinitionSO skill)
        {
            if (skill == null) return false;
            if (!skill.IsClassSkill || classSource == null) return true;
            var active = classSource.ActiveClass;
            return active == WeaponClassId.None || skill.weaponClass == active;
        }

        /// <summary>The skill a class has in a slot, or null.</summary>
        public SkillDefinitionSO GetClassSkill(WeaponClassId weaponClass, ClassSkillSlot slot)
        {
            if (availableSkills == null) return null;
            foreach (var skill in availableSkills)
                if (skill != null && skill.weaponClass == weaponClass && skill.classSlot == slot) return skill;
            return null;
        }

        /// <summary>True where this player's skills are decided (the server, or offline). Clients only mirror them.</summary>
        public bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            EnsureComponents();
            netRanks = new NetworkList<byte>(readPerm: NetworkVariableReadPermission.Owner,
                writePerm: NetworkVariableWritePermission.Server);
        }

        private void EnsureComponents()
        {
            if (progression == null) progression = GetComponent<PlayerCharacterProgression>();
            if (resources == null) resources = GetComponent<PlayerResources>();
            if (buffManager == null) buffManager = GetComponent<CharacterBuffManager>();
            if (combatStats == null) combatStats = GetComponent<CharacterCombatStats>();
            if (attackExecutor == null) attackExecutor = GetComponent<ISkillAttackExecutor>();
            if (queue == null) queue = GetComponent<ISkillQueue>();
            if (gmState == null) gmState = GetComponent<PlayerGmState>();
            if (classSource == null) classSource = GetComponent<IWeaponClassSource>();
        }

        private void OnEnable()
        {
            EnsureComponents();
            if (classSource != null) classSource.ActiveClassChanged += OnActiveClassChanged;
        }

        private void OnDisable()
        {
            if (classSource != null) classSource.ActiveClassChanged -= OnActiveClassChanged;
        }

        /// <summary>
        /// A stance or guard belongs to the class that raised it: swapping to another class's weapon drops it. Decided
        /// where buffs are decided; the removal reaches clients like any other cut-short buff.
        /// </summary>
        private void OnActiveClassChanged(WeaponClassId previous, WeaponClassId current)
        {
            if (HasAuthority && buffManager != null && current != WeaponClassId.None)
            {
                List<string> stale = null;
                foreach (var buff in buffManager.ActiveBuffs)
                {
                    if (buff.Skill == null || !buff.Skill.IsClassSkill || buff.Skill.weaponClass == current) continue;
                    (stale ??= new List<string>()).Add(buff.Skill.skillId);
                }
                if (stale != null) foreach (string skillId in stale) buffManager.RemoveBuff(skillId);
            }
            ActiveClassChanged?.Invoke(previous, current);
            Changed?.Invoke();
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
            if (IsServer)
            {
                WriteNetState();
            }
            else if (IsOwner)
            {
                netRanks.OnListChanged += OnNetRanksChanged;
                netUnlockedBooks.OnValueChanged += OnNetBooksChanged;
                ReadNetState();
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
            if (netRanks != null) netRanks.OnListChanged -= OnNetRanksChanged;
            netUnlockedBooks.OnValueChanged -= OnNetBooksChanged;
            mirrorDirty = false;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            netRanks?.Dispose(); // guarded; covers the never-spawned offline avatar
        }

        private void OnNetRanksChanged(NetworkListEvent<byte> change) => mirrorDirty = true;
        private void OnNetBooksChanged(ulong previous, ulong current) => mirrorDirty = true;

        // A burst of rank changes (e.g. every skill maxed at once) refreshes the mirror once.
        private void LateUpdate()
        {
            if (!mirrorDirty) return;
            mirrorDirty = false;
            ReadNetState();
        }

        /// <summary>Server: publishes ranks and book unlocks to the owner.</summary>
        private void WriteNetState()
        {
            if (!IsSpawned || !IsServer || availableSkills == null) return;
            ulong books = 0UL;
            for (int i = 0; i < availableSkills.Length; i++)
            {
                var skill = availableSkills[i];
                byte rank = skill != null ? (byte)Mathf.Clamp(GetRank(skill), 0, byte.MaxValue) : (byte)0;
                if (i >= netRanks.Count) netRanks.Add(rank);
                else if (netRanks[i] != rank) netRanks[i] = rank;
                if (skill != null && i < 64 && Unlocked(skill)) books |= 1UL << i;
            }
            // The list can shrink when the skill set changes (a character's faction becomes known).
            while (netRanks.Count > availableSkills.Length) netRanks.RemoveAt(netRanks.Count - 1);
            netUnlockedBooks.Value = books;
        }

        /// <summary>Owning client: rebuilds the local view from what the server published.</summary>
        private void ReadNetState()
        {
            if (availableSkills == null) return;
            learnedRanks.Clear();
            unlockedDropSkills.Clear();
            ulong books = netUnlockedBooks.Value;
            for (int i = 0; i < availableSkills.Length; i++)
            {
                var skill = availableSkills[i];
                if (skill == null) continue;
                if (i < netRanks.Count && netRanks[i] > 0) learnedRanks[skill.RankKey] = netRanks[i];
                if (i < 64 && (books & (1UL << i)) != 0) unlockedDropSkills.Add(skill.LockKey);
            }
            Changed?.Invoke();
        }

        /// <summary>Publishes a change made here, where the skills are decided.</summary>
        private void Commit()
        {
            WriteNetState();
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ saving

        [Serializable]
        private struct SavedRank
        {
            public string key;
            public int rank;
        }

        [Serializable]
        private sealed class SkillsSave
        {
            public List<SavedRank> ranks = new List<SavedRank>();
            public List<string> books = new List<string>();
        }

        public string SaveKey => "skills";
        public SaveScope SaveScope => SaveScope.Character;

        /// <summary>Ranks and Skill Chip unlocks by rank key, so they survive a change to the list of skills.</summary>
        public string CaptureSave()
        {
            var save = new SkillsSave();
            foreach (var pair in learnedRanks)
                if (pair.Value > 0) save.ranks.Add(new SavedRank { key = pair.Key, rank = pair.Value });
            save.ranks.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            save.books.AddRange(unlockedDropSkills);
            save.books.Sort(string.CompareOrdinal);
            return JsonUtility.ToJson(save);
        }

        public void RestoreSave(string json)
        {
            if (!HasAuthority || string.IsNullOrEmpty(json)) return;
            var save = JsonUtility.FromJson<SkillsSave>(json);
            if (save == null) return;
            learnedRanks.Clear();
            unlockedDropSkills.Clear();
            if (save.ranks != null)
                foreach (var saved in save.ranks)
                    if (!string.IsNullOrEmpty(saved.key) && saved.rank > 0) learnedRanks[saved.key] = saved.rank;
            if (save.books != null)
                foreach (string key in save.books)
                    if (!string.IsNullOrEmpty(key)) unlockedDropSkills.Add(key);
            Commit();
        }

        /// <summary>
        /// Replaces the skills this character can have (shared skills plus the kits of its faction). Every peer must
        /// be given the same list in the same order: ranks are mirrored to the owner by position in it.
        /// </summary>
        public void SetAvailableSkills(SkillDefinitionSO[] skills)
        {
            availableSkills = skills ?? Array.Empty<SkillDefinitionSO>();
            if (IsSpawned && IsServer) WriteNetState();
            else if (IsSpawned && IsOwner) { ReadNetState(); return; } // raises Changed itself
            Changed?.Invoke();
        }

        /// <summary>The key a skill's rank, unlock and cooldown are stored under, from its ID.</summary>
        private string RankKeyOf(string skillId)
        {
            var skill = GetSkill(skillId);
            return skill != null ? skill.RankKey : skillId;
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
            return learnedRanks.TryGetValue(skill.RankKey, out int rank) ? Mathf.Max(0, rank) : 0;
        }

        public bool IsLearned(SkillDefinitionSO skill)
        {
            if (skill == null) return false;
            return GetRank(skill) > 0;
        }

        /// <summary>Book skills (level-60 drops, Zenith techniques) stay locked until their book is acquired, whatever the level.</summary>
        public bool IsLocked(SkillDefinitionSO skill) => skill != null && skill.requiresSkillBook && !Unlocked(skill);

        // Unlocked by its own item, or by its whole slot (a GM unlock, or a save from before a Zenith technique had
        // a Dossier of its own).
        private bool Unlocked(SkillDefinitionSO skill) =>
            unlockedDropSkills.Contains(skill.LockKey) || unlockedDropSkills.Contains(skill.RankKey);

        public bool IsDropSkillUnlocked(string skillId)
        {
            var skill = GetSkill(skillId);
            return skill != null ? Unlocked(skill) : unlockedDropSkills.Contains(skillId);
        }

        /// <summary>The key reading a skill's own item is stored under, from its ID.</summary>
        private string LockKeyOf(string skillId)
        {
            var skill = GetSkill(skillId);
            return skill != null ? skill.LockKey : skillId;
        }

        /// <summary>Authority only: book unlocks come from the server (drops, GM).</summary>
        public void UnlockDropSkill(string skillId)
        {
            if (!HasAuthority) return;
            string key = LockKeyOf(skillId);
            if (!unlockedDropSkills.Contains(key))
            {
                unlockedDropSkills.Add(key);
                SkillMessage?.Invoke($"Acquired Skill Chip: {skillId} unlocked!");
                Commit();
            }
        }

        // ---------------- a Dossier is used (IItemUseHandler, server) ----------------

        public KryticalNova.Items.ItemUseEffect Effect => KryticalNova.Items.ItemUseEffect.LearnSkill;

        /// <summary>The skill an item unlocks when it is used, or null.</summary>
        public SkillDefinitionSO SkillTaughtBy(ushort itemId)
        {
            if (itemId == 0 || availableSkills == null) return null;
            foreach (SkillDefinitionSO skill in availableSkills)
                if (skill != null && skill.unlockItemId == itemId) return skill;
            return null;
        }

        public string UseBlockedReason(KryticalNova.Items.ItemDefinition item)
        {
            SkillDefinitionSO skill = item != null ? SkillTaughtBy(item.itemId) : null;
            if (skill == null) return "There is nothing in it your character can learn.";
            if (!IsLocked(skill)) return $"You have already unlocked {skill.displayName}.";
            EnsureComponents();
            if (progression != null && progression.CurrentLevel < skill.requiredCharacterLevel)
                return $"Requires level {skill.requiredCharacterLevel}.";
            return null;
        }

        public void ServerUse(KryticalNova.Items.ItemDefinition item)
        {
            if (!HasAuthority || item == null) return;
            SkillDefinitionSO skill = SkillTaughtBy(item.itemId);
            if (skill == null || !unlockedDropSkills.Add(skill.LockKey)) return;
            SkillMessage?.Invoke($"{item.displayName} read: {skill.displayName} is unlocked. Learn it with skill points.");
            Commit();
        }

        public void LockDropSkill(string skillId)
        {
            if (!HasAuthority) return;
            // Its own lock and its slot's: a skill unlocked either way is locked again.
            bool own = unlockedDropSkills.Remove(LockKeyOf(skillId));
            bool slot = unlockedDropSkills.Remove(RankKeyOf(skillId));
            if (own || slot)
            {
                Commit();
            }
        }

        public void UnlockAllDropSkills()
        {
            if (!HasAuthority || availableSkills == null) return;
            foreach (var skill in availableSkills)
            {
                if (skill != null && skill.requiresSkillBook)
                    unlockedDropSkills.Add(skill.LockKey);
            }
            SkillMessage?.Invoke("All Skill Chip skills unlocked for testing!");
            Commit();
        }

        public void LockAllDropSkills()
        {
            if (!HasAuthority) return;
            unlockedDropSkills.Clear();
            SkillMessage?.Invoke("All Skill Chip skills locked.");
            Commit();
        }

        /// <summary>GM: unlocks every book skill and puts every skill at its top rank, ignoring level and skill points.</summary>
        public void GmSetAllRanksToMax()
        {
            if (!HasAuthority || availableSkills == null) return;
            foreach (var skill in availableSkills)
            {
                if (skill == null) continue;
                if (skill.requiresSkillBook) unlockedDropSkills.Add(skill.LockKey);
                learnedRanks[skill.RankKey] = skill.maxRank;
            }
            SkillMessage?.Invoke("GM: every skill unlocked at max rank.");
            Commit();
        }

        public bool CanUpgrade(SkillDefinitionSO skill, out string reason)
        {
            EnsureComponents();
            reason = string.Empty;
            if (skill == null) { reason = "Unknown skill"; return false; }

            if (progression == null) { reason = "Progression missing"; return false; }
            if (IsLocked(skill))
            {
                reason = $"Requires Skill Chip ({skill.acquisitionNote})";
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

        /// <summary>
        /// Spends skill points on up to <paramref name="maxCount"/> ranks. On an owning client this asks the server and
        /// returns how many ranks it is expected to grant; the new rank arrives through <see cref="Changed"/>.
        /// </summary>
        public int TryUpgradeMultiple(SkillDefinitionSO skill, int maxCount)
        {
            if (skill == null || maxCount <= 0 || progression == null) return 0;

            if (!HasAuthority)
            {
                if (!IsOwner) return 0;
                int expected = CountAffordableRanks(skill, maxCount);
                if (expected > 0) UpgradeServerRpc(skill.skillId, expected);
                return expected;
            }

            int upgraded = 0;
            while (upgraded < maxCount)
            {
                if (!CanUpgrade(skill, out _)) break;
                int curRank = GetRank(skill);
                int cost = skill.GetSkillPointCostForRank(curRank);
                if (!progression.TrySpendSkillPoints(cost)) break;

                learnedRanks[skill.RankKey] = curRank + 1;
                upgraded++;
            }

            if (upgraded > 0)
            {
                SkillMessage?.Invoke($"Upgraded {skill.displayName} to Rank {learnedRanks[skill.RankKey]}");
                Commit();
            }
            return upgraded;
        }

        /// <summary>How many more ranks of <paramref name="skill"/> the mirrored level and skill points cover.</summary>
        private int CountAffordableRanks(SkillDefinitionSO skill, int maxCount)
        {
            if (IsLocked(skill) || progression.CurrentLevel < skill.requiredCharacterLevel) return 0;
            int rank = GetRank(skill);
            int points = progression.SkillPoints;
            int count = 0;
            while (count < maxCount && rank + count < skill.maxRank)
            {
                int cost = skill.GetSkillPointCostForRank(rank + count);
                if (points < cost) break;
                points -= cost;
                count++;
            }
            return count;
        }

        [ServerRpc]
        private void UpgradeServerRpc(string skillId, int count)
        {
            var skill = GetSkill(skillId);
            if (skill != null) TryUpgradeMultiple(skill, count);
        }

        public bool IsOnCooldown(SkillDefinitionSO skill)
        {
            if (skill == null) return false;
            if (gmState != null && gmState.NoCooldowns) return false;
            return cooldownEndTimes.TryGetValue(skill.RankKey, out float endTime) && Time.time < endTime;
        }

        public float GetRemainingCooldown(SkillDefinitionSO skill)
        {
            if (skill == null) return 0f;
            if (cooldownEndTimes.TryGetValue(skill.RankKey, out float endTime))
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
        public bool CanCast(SkillDefinitionSO skill, Transform target, out string reason) =>
            CheckCast(skill, target, out reason) == CastCheck.Ready;

        /// <summary>The attack skill waiting to be cast on this machine's own player (it is walking into reach, or still in a swing), or null.</summary>
        public SkillDefinitionSO QueuedSkill => queue != null ? queue.Queued : null;

        /// <summary>How far this skill reaches at the rank it is learned to.</summary>
        public float RangeOf(SkillDefinitionSO skill) => skill != null ? skill.GetRange(GetRank(skill)) : 0f;

        /// <summary>
        /// What stands between a skill and its cast right now. With <see cref="CastCheck.Busy"/> the reason is empty:
        /// "not yet" is no fault of the player's.
        /// </summary>
        public CastCheck CheckCast(SkillDefinitionSO skill, Transform target, out string reason)
        {
            EnsureComponents();
            reason = string.Empty;
            if (skill == null) { reason = "No skill selected"; return CastCheck.Refused; }

            if (!IsLearned(skill))
            {
                reason = IsLocked(skill)
                    ? $"{skill.displayName} requires a Skill Chip ({skill.acquisitionNote})."
                    : $"Learn {skill.displayName} with a skill point first.";
                return CastCheck.Refused;
            }
            if (!IsInActiveKit(skill))
            {
                reason = $"{skill.displayName} needs a {WeaponClasses.DisplayName(skill.weaponClass)} in hand.";
                return CastCheck.Refused;
            }
            if (resources == null || !resources.IsAlive)
            {
                reason = "Cannot cast while defeated.";
                return CastCheck.Refused;
            }
            if (Time.time < castRequestPendingUntil) return CastCheck.Busy;
            if (attackExecutor != null && attackExecutor.IsCasting && !attackExecutor.CanInterruptCast(skill)) return CastCheck.Busy;
            if (IsOnCooldown(skill))
            {
                float rem = GetRemainingCooldown(skill);
                reason = $"{skill.displayName} on cooldown ({rem:0.0}s remaining).";
                return CastCheck.Refused;
            }

            int rank = GetRank(skill);
            float chiCost = skill.GetChiCost(rank);
            if (!resources.CanSpendChi(chiCost))
            {
                reason = $"Not enough Flux ({chiCost:0} Flux required, current: {resources.Chi:0}).";
                return CastCheck.Refused;
            }
            if (skill.IsAttack)
            {
                if (attackExecutor == null) { reason = "This character cannot use attack skills."; return CastCheck.Refused; }
                if (!attackExecutor.CanExecute(skill, rank, target, out reason, out bool outOfReach))
                    return outOfReach ? CastCheck.OutOfReach : CastCheck.Refused;
            }
            return CastCheck.Ready;
        }

        /// <summary>Shows a skill-related line in the player's feedback area.</summary>
        public void Announce(string message)
        {
            if (!string.IsNullOrEmpty(message)) SkillMessage?.Invoke(message);
        }

        /// <summary>
        /// The player asks for a skill. One that can be cast is cast. An attack that cannot be cast yet, because its
        /// target is out of reach or a swing is still playing, is kept by the queue (<see cref="ISkillQueue"/>),
        /// which walks in and casts it; asking for another skill lets go of the one that was waiting.
        /// </summary>
        /// <param name="quiet">Suppresses the failure message and the queue (used while a hotbar key is held to repeat).</param>
        public bool TryCastSkill(SkillDefinitionSO skill, out string reason, bool quiet = false)
        {
            EnsureComponents();
            Transform target = ResolveTarget(skill);
            if (!quiet && queue != null && queue.Queued != null && queue.Queued != skill) queue.Clear();
            CastCheck check = CheckCast(skill, target, out reason);
            if (check != CastCheck.Ready)
            {
                bool waits = check == CastCheck.Busy || check == CastCheck.OutOfReach;
                if (!quiet && waits && queue != null && skill != null && queue.Offer(skill, target, check == CastCheck.OutOfReach)) return false;
                if (!quiet && !string.IsNullOrEmpty(reason)) SkillMessage?.Invoke(reason);
                return false;
            }
            if (queue != null && queue.Queued != null) queue.Clear(); // this cast is the answer to whatever was waiting
            KryticalNova.Diagnostics.PerformanceLog.Mark("skill.cast", skill != null ? skill.skillId : null);

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

        /// <summary>
        /// Where combat is decided (the server, or the offline game): casts the skill now if it can be cast. For the
        /// queue, which has already walked the player into reach. False with the reason when it cannot.
        /// </summary>
        public bool ServerTryCast(SkillDefinitionSO skill, Transform target, out string reason)
        {
            reason = string.Empty;
            if (IsSpawned && !IsServer) return false;
            return CanCast(skill, target, out reason) && ServerCast(skill, target, out reason);
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
                cooldownEndTimes[skill.RankKey] = Time.time + skill.cooldown;
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
                    reason = $"Not enough Flux ({chiCost:0} Flux required, current: {resources.Chi:0}).";
                    SkillMessage?.Invoke(reason);
                    return false;
                }
            }

            cooldownEndTimes[skill.RankKey] = Time.time + skill.cooldown;

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
