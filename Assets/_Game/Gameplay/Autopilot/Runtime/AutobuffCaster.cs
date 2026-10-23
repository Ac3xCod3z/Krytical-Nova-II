using System;
using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Autopilot
{
    /// <summary>
    /// The Autobuff's hand on the player's own game: one press of its Hotbar key casts every slotted ward that is
    /// down at once (owner, 23 Oct 2026: "use all buffs in the loadout at once, not need to be hit multiple times"),
    /// each paying its own Flux and cooldown (<see cref="PlayerCombatSkills.TryCastWardsAtOnce"/>); one on cooldown, not
    /// learned or short of Flux is left. Autopilot presses it the same way to keep the wards up. Nothing happens
    /// without a running licence (<see cref="PlayerAutobuff"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerAutobuff))]
    public sealed class AutobuffCaster : MonoBehaviour, IHotbarAutobuff
    {
        private PlayerAutobuff autobuff;
        private PlayerAutopilot autopilot;
        private PlayerCombatSkills skills;
        private CharacterBuffManager buffs;
        private PlayerResources resources;
        private PlayerInventory inventory;
        private readonly bool running = false; // the wards go at once: nothing runs on

        private void Awake()
        {
            autobuff = GetComponent<PlayerAutobuff>();
            autopilot = GetComponent<PlayerAutopilot>();
            skills = GetComponent<PlayerCombatSkills>();
            buffs = GetComponent<CharacterBuffManager>();
            resources = GetComponent<PlayerResources>();
            inventory = GetComponent<PlayerInventory>();
        }

        private bool IsLocal => inventory != null && PlayerInventory.Local == inventory;

        // ------------------------------------------------------------------ IHotbarAutobuff

        public bool IsAvailable => autobuff != null && autobuff.IsActive;
        public string Label => "AUTOWARD";
        public int SlottedCount => autobuff != null ? AutobuffRules.Count(autobuff.SkillIds) : 0;
        public bool IsRunning => running;

        public Sprite Icon
        {
            get
            {
                AutopilotSettings settings = autobuff != null ? autobuff.Settings : null;
                if (settings == null || settings.autobuffTiers == null || settings.autobuffTiers.Length == 0) return null;
                ItemCatalog catalog = inventory != null ? inventory.Catalog : null;
                ItemDefinition pass = catalog != null ? catalog.Get(settings.autobuffTiers[0].passItemId) : null;
                return pass != null ? pass.icon : null;
            }
        }

        public bool Press()
        {
            if (!IsLocal || autobuff == null || skills == null) return false;
            if (!autobuff.IsActive) { skills.Announce("Autoward has expired. Use an Autoward Pass."); return false; }
            if (SlottedCount == 0) { skills.Announce("Put ward skills into Autoward in the Autopilot window."); return false; }
            if (!AnyDown()) { skills.Announce("Every ward in Autoward is up."); return true; }
            if (CastAllDown(out string reason) == 0 && !string.IsNullOrEmpty(reason)) skills.Announce(reason);
            return true;
        }

        public string Describe(string key)
        {
            if (autobuff == null) return string.Empty;
            string line = autobuff.IsActive ? $"{autobuff.TimeLeftLine}\nClick or press {key}: every ward below that is down is cast at once."
                : "Autoward has expired. Use an Autoward Pass.";
            string[] ids = autobuff.SkillIds;
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < ids.Length; i++)
            {
                if (string.IsNullOrEmpty(ids[i])) continue;
                SkillDefinitionSO skill = skills != null ? skills.GetSkill(ids[i]) : null;
                names.Append('\n').Append(skill != null ? skill.displayName : ids[i]);
                if (skill != null && buffs != null && buffs.HasBuff(skill.skillId)) names.Append("  ·  up");
            }
            if (names.Length == 0) names.Append("\nNo wards slotted yet.");
            return line + names + "\nRight-click to clear";
        }

        // ------------------------------------------------------------------ casting

        /// <summary>True while any slotted buff is not up on the character.</summary>
        public bool AnyDown()
        {
            if (autobuff == null) return false;
            foreach (string id in autobuff.SkillIds)
                if (!string.IsNullOrEmpty(id) && (buffs == null || !buffs.HasBuff(id))) return true;
            return false;
        }

        /// <summary>Casts every slotted ward that is down and can be cast now, at once. True when any was asked for. Autopilot's turn.</summary>
        public bool CastNext() => CastAllDown(out _) > 0;

        private readonly List<SkillDefinitionSO> slotted = new List<SkillDefinitionSO>();

        private int CastAllDown(out string reason)
        {
            reason = string.Empty;
            if (!IsLocal || autobuff == null || skills == null || !autobuff.IsActive) return 0;
            if (resources != null && !resources.IsAlive) return 0;
            slotted.Clear();
            foreach (string id in autobuff.SkillIds)
            {
                SkillDefinitionSO skill = string.IsNullOrEmpty(id) ? null : skills.GetSkill(id);
                if (skill != null) slotted.Add(skill);
            }
            return skills.TryCastWardsAtOnce(slotted, out reason);
        }
    }
}
