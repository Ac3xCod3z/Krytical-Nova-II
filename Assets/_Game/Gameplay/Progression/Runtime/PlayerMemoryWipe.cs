using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// A Memory Wipe is used (wave 3b, owner 12 Nov 2026): every combat rank and Run and Jump go back to 0 (a new character's ranks)
    /// and exactly the skill points they cost come back, summed with the spend's own costs (<see cref="ProgressionResetRules"/>),
    /// capped at what the level earned. Skill Chip unlocks stay. Decided where the player is (the server body, or offline); the
    /// Pack takes the item only when <see cref="UseBlockedReason"/> says yes, in the same change (PlayerInventory.ServerUse).
    /// Its own component because <see cref="PlayerCombatSkills"/> is already the handler of Dossiers.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerMemoryWipe : MonoBehaviour, IItemUseHandler
    {
        private PlayerCharacterProgression progression;
        private PlayerCombatSkills combat;
        private PlayerMovementSkills movement;

        public ItemUseEffect Effect => ItemUseEffect.MemoryWipe;

        private void Awake() => Ensure();

        private void Ensure()
        {
            if (progression == null) progression = GetComponent<PlayerCharacterProgression>();
            if (combat == null) combat = GetComponent<PlayerCombatSkills>();
            if (movement == null) movement = GetComponent<PlayerMovementSkills>();
        }

        /// <summary>Skill points the learned ranks cost (combat and Run / Jump).</summary>
        public long SpentOnRanks
        {
            get
            {
                Ensure();
                return (combat != null ? combat.SpentOnRanks() : 0) + (movement != null ? movement.SpentOnRanks() : 0);
            }
        }

        /// <summary>The skill points a Memory Wipe hands back now: what the ranks cost, capped at what the level earned.</summary>
        public int HandBack
        {
            get
            {
                Ensure();
                return progression == null ? 0 : ProgressionResetRules.Capped(SpentOnRanks, progression.SkillPoints, progression.SkillPointsEarnedByLevel);
            }
        }

        public string UseBlockedReason(UsedItem item)
        {
            Ensure();
            if (progression == null || combat == null) return ProgressionResetRules.ReasonNoSkillsLearned;
            bool learned = combat.HasLearnedRanks || (movement != null && movement.HasLearnedRanks);
            if (!learned || SpentOnRanks <= 0) return ProgressionResetRules.ReasonNoSkillsLearned;
            if (Busy) return ProgressionResetRules.ReasonBusy;
            return HandBack <= 0 ? ProgressionResetRules.ReasonOnlyGiven : null;
        }

        // A cast under way or asked for, a jump in the air or a walk-in plan of the server body (the wipe takes Run and Jump).
        private bool Busy
        {
            get
            {
                if (combat != null && combat.CastBusy) return true;
                IBodyMover mover = TryGetComponent(out BodyRoleTag tag) ? tag.Mover : null;
                return mover != null && (mover.Airborne || mover.Busy);
            }
        }

        public void ServerUse(UsedItem item)
        {
            Ensure();
            if (progression == null || combat == null || !progression.HasAuthority) return;
            long spent = SpentOnRanks;
            int handBack = HandBack;
            if (handBack <= 0)
            {
                Debug.LogError($"[Memory Wipe] {name}: the item was used with nothing to hand back (check and use disagree).", this);
                return;
            }
            string trimmed = ProgressionResetRules.TrimLine("Memory Wipe", name, handBack, spent);
            if (trimmed != null) Debug.Log(trimmed, this);
            combat.ClearRanks();
            if (movement != null) movement.ClearRanks();
            progression.AddSkillPoints(handBack);
        }
    }
}
