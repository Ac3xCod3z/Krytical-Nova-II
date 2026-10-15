using System;
using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Persistence
{
    /// <summary>
    /// Remembers which skill and item sits on which Hotbar key, per character. The Hotbar is the player's own
    /// arrangement (the server never sees it), so like the HUD layout it is kept on this machine, in PlayerPrefs.
    /// Only the local player's Hotbar is remembered.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerSkillHotbar))]
    public sealed class PlayerHotbarMemory : MonoBehaviour
    {
        private const string KeyPrefix = "kn.hotbar.v1.";

        [Serializable]
        private struct SavedSlot
        {
            public int index;
            public int type;
            public int movement;
            public string skill;
            public int item;
        }

        [Serializable]
        private sealed class SavedHotbar
        {
            public List<SavedSlot> slots = new List<SavedSlot>();
        }

        private PlayerSkillHotbar hotbar;
        private PlayerIdentity identity;
        private PlayerCombatSkills combatSkills;
        private PlayerInventory inventory;

        private string loadedFor;
        private bool restoring;
        // Skills that were not in the character's skill list yet when the layout was read.
        private readonly List<SavedSlot> waiting = new List<SavedSlot>(PlayerSkillHotbar.SlotCount);

        private void Awake()
        {
            hotbar = GetComponent<PlayerSkillHotbar>();
            identity = GetComponent<PlayerIdentity>();
            combatSkills = GetComponent<PlayerCombatSkills>();
            inventory = GetComponent<PlayerInventory>();
        }

        private void OnEnable()
        {
            hotbar.Changed += OnHotbarChanged;
            if (identity != null) identity.Changed += Load;
            if (combatSkills != null) combatSkills.Changed += PlaceWaiting;
            PlayerInventory.LocalChanged += Load;
            Load();
        }

        private void OnDisable()
        {
            hotbar.Changed -= OnHotbarChanged;
            if (identity != null) identity.Changed -= Load;
            if (combatSkills != null) combatSkills.Changed -= PlaceWaiting;
            PlayerInventory.LocalChanged -= Load;
        }

        private bool IsLocalPlayer => inventory != null && PlayerInventory.Local == inventory;

        private void Load()
        {
            if (!IsLocalPlayer || identity == null || !identity.HasIdentity) return;
            string characterId = identity.CharacterId;
            if (string.IsNullOrEmpty(characterId) || loadedFor == characterId) return;
            loadedFor = characterId;

            waiting.Clear();
            string json = PlayerPrefs.GetString(KeyPrefix + characterId, string.Empty);
            if (string.IsNullOrEmpty(json)) return; // first time on this machine: keep whatever is on the Hotbar
            SavedHotbar saved;
            try
            {
                saved = JsonUtility.FromJson<SavedHotbar>(json);
            }
            catch (Exception)
            {
                return;
            }
            if (saved?.slots == null) return;

            restoring = true;
            for (int i = 0; i < PlayerSkillHotbar.SlotCount; i++) hotbar.RestoreSlot(i, default, notify: false);
            foreach (SavedSlot slot in saved.slots)
                if (!Place(slot)) waiting.Add(slot);
            hotbar.NotifyRestored();
            restoring = false;
        }

        private void PlaceWaiting()
        {
            if (waiting.Count == 0) return;
            restoring = true;
            bool placed = false;
            for (int i = waiting.Count - 1; i >= 0; i--)
            {
                if (!Place(waiting[i])) continue;
                waiting.RemoveAt(i);
                placed = true;
            }
            if (placed) hotbar.NotifyRestored();
            restoring = false;
        }

        /// <summary>False when the slot holds a combat skill this character's list does not have (yet).</summary>
        private bool Place(SavedSlot slot)
        {
            switch ((HotbarItemType)slot.type)
            {
                case HotbarItemType.Movement:
                    hotbar.RestoreSlot(slot.index, new HotbarSlotEntry { itemType = HotbarItemType.Movement, movementSkill = (MovementSkillId)slot.movement }, notify: false);
                    return true;
                case HotbarItemType.Item:
                    hotbar.RestoreSlot(slot.index, new HotbarSlotEntry { itemType = HotbarItemType.Item, itemId = (ushort)slot.item }, notify: false);
                    return true;
                case HotbarItemType.CombatSkill:
                    SkillDefinitionSO skill = combatSkills != null ? combatSkills.GetSkill(slot.skill) : null;
                    if (skill == null) return false;
                    hotbar.RestoreSlot(slot.index, new HotbarSlotEntry { itemType = HotbarItemType.CombatSkill, combatSkill = skill }, notify: false);
                    return true;
                case HotbarItemType.Autobuff:
                    hotbar.RestoreSlot(slot.index, new HotbarSlotEntry { itemType = HotbarItemType.Autobuff }, notify: false);
                    return true;
                default:
                    return true;
            }
        }

        private void OnHotbarChanged()
        {
            if (restoring || string.IsNullOrEmpty(loadedFor) || !IsLocalPlayer) return;
            var saved = new SavedHotbar();
            for (int i = 0; i < PlayerSkillHotbar.SlotCount; i++)
            {
                HotbarSlotEntry entry = hotbar.GetSlotEntry(i);
                if (entry.IsEmpty) continue;
                saved.slots.Add(new SavedSlot
                {
                    index = i,
                    type = (int)entry.itemType,
                    movement = (int)entry.movementSkill,
                    skill = entry.combatSkill != null ? entry.combatSkill.skillId : string.Empty,
                    item = entry.itemId,
                });
            }
            // Skills still waiting for their definition keep their keys.
            foreach (SavedSlot slot in waiting)
                if (hotbar.GetSlotEntry(slot.index).IsEmpty) saved.slots.Add(slot);
            PlayerPrefs.SetString(KeyPrefix + loadedFor, JsonUtility.ToJson(saved));
            PlayerPrefs.Save();
        }
    }
}
