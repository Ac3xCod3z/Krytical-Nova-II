using System;
using KryticalNova.Items;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Autopilot
{
    /// <summary>
    /// The Autobuff's hand on the player's own game: one press of its Hotbar key casts every slotted buff that is
    /// down, one per action tick (at Autopilot's own pace), through the ordinary cast path, and stops when all are up
    /// or nothing more can be cast now (on cooldown, not learned, no Flux). Autopilot presses it the same way to
    /// keep the buffs up. Nothing happens without a running licence (<see cref="PlayerAutobuff"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerAutobuff))]
    public sealed class AutobuffCaster : MonoBehaviour, IHotbarAutobuff
    {
        private const int MostTicks = AutobuffRules.Slots * 3; // a run of presses never goes on for ever

        private PlayerAutobuff autobuff;
        private PlayerAutopilot autopilot;
        private PlayerCombatSkills skills;
        private CharacterBuffManager buffs;
        private PlayerResources resources;
        private PlayerInventory inventory;
        private bool running;
        private float nextAction;
        private int ticksLeft;

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
        public string Label => "AUTOBUFF";
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
            if (!autobuff.IsActive) { skills.Announce("Autobuff has expired. Use an Autobuff Pass."); return false; }
            if (SlottedCount == 0) { skills.Announce("Put buff skills into Autobuff in the Autopilot window."); return false; }
            if (running) return true; // a second press while it runs changes nothing
            running = true;
            nextAction = Time.time;
            ticksLeft = MostTicks;
            return true;
        }

        public string Describe(string key)
        {
            if (autobuff == null) return string.Empty;
            string line = autobuff.IsActive ? $"{autobuff.TimeLeftLine}\nClick or press {key}: every buff below that is down is cast in turn."
                : "Autobuff has expired. Use an Autobuff Pass.";
            string[] ids = autobuff.SkillIds;
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < ids.Length; i++)
            {
                if (string.IsNullOrEmpty(ids[i])) continue;
                SkillDefinitionSO skill = skills != null ? skills.GetSkill(ids[i]) : null;
                names.Append('\n').Append(skill != null ? skill.displayName : ids[i]);
                if (skill != null && buffs != null && buffs.HasBuff(skill.skillId)) names.Append("  ·  up");
            }
            if (names.Length == 0) names.Append("\nNo buffs slotted yet.");
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

        /// <summary>
        /// Casts the next slotted buff that is down and can be cast now, one. True when one was asked for. Used by a
        /// press of the key (tick by tick) and by Autopilot's turn.
        /// </summary>
        public bool CastNext()
        {
            if (!IsLocal || autobuff == null || skills == null || !autobuff.IsActive) return false;
            if (resources != null && !resources.IsAlive) return false;
            string[] ids = autobuff.SkillIds;
            int cell = AutobuffRules.NextToCast(ids, id => buffs != null && buffs.HasBuff(id), id =>
            {
                SkillDefinitionSO skill = skills.GetSkill(id);
                return skill != null && skills.IsLearned(skill) && skills.CanCast(skill, out _);
            });
            if (cell < 0) return false;
            SkillDefinitionSO next = skills.GetSkill(ids[cell]);
            return next != null && skills.TryCastSkill(next, out _, quiet: true);
        }

        private void Update()
        {
            if (!running) return;
            if (!IsLocal || autobuff == null || !autobuff.IsActive || (resources != null && !resources.IsAlive)) { running = false; return; }
            if (Time.time < nextAction) return;
            Vector2 delay = autopilot != null && autopilot.Settings != null ? autopilot.Settings.actionDelaySeconds : new Vector2(0.3f, 0.5f);
            nextAction = Time.time + UnityEngine.Random.Range(Mathf.Min(delay.x, delay.y), Mathf.Max(delay.x, delay.y));
            // The run is over once nothing is left to cast now, or it has gone on long enough.
            if (!CastNext() || --ticksLeft <= 0) running = AnyDown() && ticksLeft > 0 && CanCastAny();
        }

        private bool CanCastAny()
        {
            if (autobuff == null || skills == null) return false;
            return AutobuffRules.NextToCast(autobuff.SkillIds, id => buffs != null && buffs.HasBuff(id), id =>
            {
                SkillDefinitionSO skill = skills.GetSkill(id);
                return skill != null && skills.IsLearned(skill) && skills.CanCast(skill, out _);
            }) >= 0;
        }
    }
}
