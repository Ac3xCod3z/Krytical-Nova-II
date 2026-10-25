using KryticalNova.Networking;
using System;
using KryticalNova.Core;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// Owns learned ranks and persistent selection. Skill selection is separate from paid destination commands.
    /// Ranks and selection are decided where the player is simulated (the server, or offline) and mirrored to every
    /// client; an owning client asks the server to learn a rank or change the selection.
    /// </summary>
    [DefaultExecutionOrder(InitializationOrder)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacterProgression), typeof(PlayerResources))]
    public sealed class PlayerMovementSkills : NetworkBehaviour, ISaveParticipant
    {
        private const int InitializationOrder = -100;
        [SerializeField] private MovementSettings movementSettings;
        [SerializeField] private MovementSkillData runSkill;
        [SerializeField] private MovementSkillData jumpSkill;
        private PlayerCharacterProgression progression;
        private PlayerResources resources;
        private PlayerGmState gmState;
        private CharacterCombatStats combatStats;
        private int runRank;
        private int jumpRank;
        private MovementSkillId selectedSkill;

        private readonly NetworkVariable<MovementSkillState> netState = new NetworkVariable<MovementSkillState>(
            writePerm: NetworkVariableWritePermission.Server);

        public event Action Changed;
        public MovementSettings MovementSettings => movementSettings;
        public MovementSkillId SelectedSkill => selectedSkill;
        /// <summary>The learned ranks of Run and Jump (0 = not learned); mirrored to every machine.</summary>
        public int RunRank => runRank;
        public int JumpRank => jumpRank;
        public bool IsRunning => selectedSkill == MovementSkillId.Run && runRank > 0;
        /// <summary>A rider does not jump (owner, 25 Oct 2026: get off first).</summary>
        public bool IsJumpSelected => selectedSkill == MovementSkillId.Jump && jumpRank > 0 && !IsRiding;
        /// <summary>On a mount: walking and running go at the mount's speeds (<see cref="SetRideSpeeds"/>).</summary>
        public bool IsRiding => rideWalkSpeed > 0f;
        public float CurrentSpeed => BaseSpeed * (IsRiding ? 1f : StatSpeedMultiplier) * (gmState != null ? Mathf.Max(0.1f, gmState.SpeedMultiplier) : 1f);
        /// <summary>What the Move Speed stat (Boots, the ranged Stance) multiplies walking and running by.</summary>
        public float StatSpeedMultiplier => movementSettings == null || combatStats == null ? 1f
            : movementSettings.StatSpeedMultiplier(combatStats.GetStatValue(StatType.MoveSpeedPercent));
        private float BaseSpeed => IsRiding ? (IsRunning ? rideRunSpeed : rideWalkSpeed)
            : movementSettings == null ? 0f : IsRunning && runSkill != null && runSkill.TryGetRank(runRank, out var rank)
            ? Mathf.Max(movementSettings.walkSpeed, rank.runSpeed) : movementSettings.walkSpeed;

        private float rideWalkSpeed, rideRunSpeed;

        /// <summary>
        /// The mount's walk and run, in metres a second, while its rider is on it; 0 = on foot again. Run is still
        /// the Run selection (the same button as on foot). The Move Speed stat does not add to it (owner, 25 Oct 2026: a
        /// mount's top speed is 34 m/s); a GM's speed still does.
        /// Set on every machine by the mount system.
        /// </summary>
        public void SetRideSpeeds(float walk, float run)
        {
            rideWalkSpeed = Mathf.Max(0f, walk);
            rideRunSpeed = Mathf.Max(rideWalkSpeed, run);
            Changed?.Invoke();
        }

        /// <summary>True where this player's ranks and selection are decided (the server, or offline).</summary>
        public bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            progression = GetComponent<PlayerCharacterProgression>();
            resources = GetComponent<PlayerResources>();
            gmState = GetComponent<PlayerGmState>();
            combatStats = GetComponent<CharacterCombatStats>();
            if (movementSettings == null || runSkill == null || jumpSkill == null
                || runSkill.skillId != MovementSkillId.Run || jumpSkill.skillId != MovementSkillId.Jump)
            {
                Debug.LogError("PlayerMovementSkills needs movement settings and valid Run/Jump skill assets.", this);
                enabled = false;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netState.OnValueChanged += OnNetStateChanged;
            if (IsServer) netState.Value = Capture();
            else ApplyReplicated(netState.Value);
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetStateChanged(MovementSkillState previous, MovementSkillState current)
        {
            if (!IsServer) ApplyReplicated(current);
        }

        private MovementSkillState Capture() => new MovementSkillState
        {
            runRank = (byte)Mathf.Clamp(runRank, 0, byte.MaxValue),
            jumpRank = (byte)Mathf.Clamp(jumpRank, 0, byte.MaxValue),
            selected = selectedSkill,
        };

        private void ApplyReplicated(MovementSkillState state)
        {
            runRank = state.runRank;
            jumpRank = state.jumpRank;
            selectedSkill = state.selected;
            Changed?.Invoke();
        }

        /// <summary>Publishes a change made here, where the skills are decided.</summary>
        private void Commit()
        {
            if (IsSpawned && IsServer) netState.Value = Capture();
            Changed?.Invoke();
        }

        public string SaveKey => "movement";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave() => JsonUtility.ToJson(Capture());

        /// <summary>Learned ranks come back; the character always re-enters the world walking.</summary>
        public void RestoreSave(string json)
        {
            if (!HasAuthority || string.IsNullOrEmpty(json) || runSkill == null || jumpSkill == null) return;
            var state = JsonUtility.FromJson<MovementSkillState>(json);
            runRank = Mathf.Clamp(state.runRank, 0, runSkill.MaxRank);
            jumpRank = Mathf.Clamp(state.jumpRank, 0, jumpSkill.MaxRank);
            selectedSkill = MovementSkillId.None;
            Commit();
        }

        /// <summary>Returns the shared definition for a known movement skill.</summary>
        public MovementSkillData GetDefinition(MovementSkillId skill)
        {
            return skill switch { MovementSkillId.Run => runSkill, MovementSkillId.Jump => jumpSkill, _ => null };
        }

        /// <summary>Returns the learned rank; zero means the skill has not been purchased.</summary>
        public int GetRank(MovementSkillId skill) => skill switch
        {
            MovementSkillId.Run => runRank, MovementSkillId.Jump => jumpRank, _ => 0
        };

        /// <summary>Reports whether a learned skill can be assigned or selected, independent of Chi affordability.</summary>
        public bool CanUse(MovementSkillId skill) => isActiveAndEnabled && GetDefinition(skill) != null && GetRank(skill) > 0;

        /// <summary>Gets the active rank's effect and resource cost.</summary>
        public bool TryGetLearnedRank(MovementSkillId skill, out MovementSkillRank rank)
        {
            rank = default;
            MovementSkillData definition = GetDefinition(skill);
            return CanUse(skill) && definition.TryGetRank(GetRank(skill), out rank);
        }

        /// <summary>Checks resource availability without charging. Invalid targets must be rejected before spending.</summary>
        public bool CanPayForCommand(MovementSkillId skill, out string reason)
        {
            reason = string.Empty;
            if (!TryGetLearnedRank(skill, out var rank)) { reason = "Learn this skill first."; return false; }
            if (resources == null || !resources.IsAlive) { reason = "You need HP to use skills."; return false; }
            if (!resources.CanSpendChi(rank.chiCost)) { reason = $"Not enough Flux. {GetDefinition(skill).displayName} needs {rank.chiCost:0.#} Flux per destination."; return false; }
            return true;
        }

        /// <summary>Charges the learned rank cost once for a validated command.</summary>
        public bool TryPayForCommand(MovementSkillId skill, out float paidCost, out string reason)
        {
            paidCost = 0f;
            if (!CanPayForCommand(skill, out reason) || !TryGetLearnedRank(skill, out var rank)) return false;
            if (!resources.TrySpendChi(rank.chiCost)) { reason = "Not enough Flux."; return false; }
            paidCost = rank.chiCost;
            return true;
        }

        /// <summary>Validates rank cap, character-level gate and skill-point cost without spending anything.</summary>
        public bool CanUpgrade(MovementSkillId skill, out string reason)
        {
            reason = string.Empty;
            if (!isActiveAndEnabled || progression == null) { reason = "Skills unavailable"; return false; }
            MovementSkillData definition = GetDefinition(skill);
            if (definition == null) { reason = "Unknown skill"; return false; }
            if (!definition.TryGetRank(GetRank(skill) + 1, out var next)) { reason = "Maximum rank reached"; return false; }
            if (progression.CurrentLevel < next.requiredCharacterLevel) { reason = $"Requires character level {next.requiredCharacterLevel}"; return false; }
            if (progression.SkillPoints < next.skillPointCost) { reason = $"Requires {next.skillPointCost} skill point(s)"; return false; }
            return true;
        }

        /// <summary>Purchases one Run or Jump rank after validating all requirements.</summary>
        public bool TryUpgrade(MovementSkillId skill)
        {
            return TryUpgradeMultiple(skill, 1) > 0;
        }

        /// <summary>
        /// Purchases up to maxCount Run or Jump ranks after validating all requirements. On an owning client this asks
        /// the server and returns how many ranks it is expected to grant; the new rank arrives through <see cref="Changed"/>.
        /// </summary>
        public int TryUpgradeMultiple(MovementSkillId skill, int maxCount)
        {
            if (maxCount <= 0 || !isActiveAndEnabled || progression == null) return 0;
            MovementSkillData definition = GetDefinition(skill);
            if (definition == null) return 0;

            if (!HasAuthority)
            {
                if (!IsOwner) return 0;
                int expected = CountAffordableRanks(definition, GetRank(skill), maxCount);
                if (expected > 0) UpgradeServerRpc(skill, expected);
                return expected;
            }

            int upgraded = 0;
            while (upgraded < maxCount)
            {
                int nextRank = GetRank(skill) + 1;
                if (!definition.TryGetRank(nextRank, out var next))
                    break;
                if (progression.CurrentLevel < next.requiredCharacterLevel)
                    break;
                if (!progression.TrySpendSkillPoints(next.skillPointCost))
                    break;

                if (skill == MovementSkillId.Run) runRank++;
                else if (skill == MovementSkillId.Jump) jumpRank++;
                upgraded++;
            }

            if (upgraded > 0)
            {
                Commit();
            }
            return upgraded;
        }

        /// <summary>How many ranks above <paramref name="rank"/> the mirrored level and skill points cover.</summary>
        private int CountAffordableRanks(MovementSkillData definition, int rank, int maxCount)
        {
            int points = progression.SkillPoints;
            int count = 0;
            while (count < maxCount && definition.TryGetRank(rank + count + 1, out var next)
                   && progression.CurrentLevel >= next.requiredCharacterLevel && points >= next.skillPointCost)
            {
                points -= next.skillPointCost;
                count++;
            }
            return count;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void UpgradeServerRpc(MovementSkillId skill, int count)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(UpgradeServerRpc))) return;
            TryUpgradeMultiple(skill, count);
        }

        /// <summary>GM: learns Run and Jump at their top ranks, ignoring level and skill points. Authority only.</summary>
        public void GmSetMaxRanks()
        {
            if (!HasAuthority || !isActiveAndEnabled) return;
            runRank = runSkill.MaxRank;
            jumpRank = jumpSkill.MaxRank;
            Commit();
        }

        /// <summary>Selects a learned skill persistently. Selecting it again returns to walking; no Chi is charged here.</summary>
        public bool ToggleSelection(MovementSkillId skill)
        {
            if (!CanUse(skill) || resources == null || !resources.IsAlive) return false;
            return Select(selectedSkill == skill ? MovementSkillId.None : skill);
        }

        /// <summary>Returns to ordinary walking while preserving any valid ground destination.</summary>
        public void SelectWalk()
        {
            if (selectedSkill == MovementSkillId.None) return;
            Select(MovementSkillId.None);
        }

        private bool Select(MovementSkillId skill)
        {
            if (!HasAuthority)
            {
                if (!IsOwner) return false;
                // Shown at once so the hotbar and the next click agree; the server's answer replaces it.
                SelectServerRpc(skill);
            }
            selectedSkill = skill;
            Commit();
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SelectServerRpc(MovementSkillId skill)
        {
            if (!RpcBudget.Allow(this, CallKind.Move, nameof(SelectServerRpc))) return;
            bool allowed = skill == MovementSkillId.None || (CanUse(skill) && resources != null && resources.IsAlive);
            if (allowed && selectedSkill != skill)
            {
                selectedSkill = skill;
                Commit();
                return;
            }
            // Nothing changed here, so nothing would be replicated: put the owner back on the real selection.
            if (!allowed) CorrectSelectionRpc(Capture());
        }

        [Rpc(SendTo.Owner)]
        private void CorrectSelectionRpc(MovementSkillState state)
        {
            if (!IsServer) ApplyReplicated(state);
        }
    }
}
