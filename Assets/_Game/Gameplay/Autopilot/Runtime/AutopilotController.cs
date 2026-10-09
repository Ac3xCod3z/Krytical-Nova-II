using System.Collections.Generic;
using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.Hostiles;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Autopilot
{
    /// <summary>
    /// Autopilot: hunts by itself inside one Hostile Zone. It runs on the player's own game and does only what a
    /// player could: it uses a Stim from the Pack, presses a Hotbar slot, asks to walk somewhere, asks to attack
    /// a Hostile. Every one of those is checked by the server exactly as when a hand does it, so Autopilot can do
    /// nothing a player cannot, and it never acts faster than a person. Each turn it does the first thing that
    /// needs doing: a Stim, a buff that ran out, a drop worth walking to, then the fight.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerAutopilot))]
    public sealed class AutopilotController : MonoBehaviour
    {
        /// <summary>GM: prints what Autopilot does each turn and why it stopped.</summary>
        public static bool Logging { get; set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Logging = false;

        private PlayerAutopilot autopilot;
        private MagnetController magnet;
        private PointAndClickPlayer pointer;
        private PlayerMeleeCombat melee;
        private PlayerSkillHotbar hotbar;
        private PlayerCombatSkills skills;
        private PlayerInventory inventory;
        private PlayerResources resources;
        private CharacterBuffManager buffs;
        private PlayerLoot loot;
        private PlayerCharacterProgression progression;
        private NetworkObject networkObject;

        private const float AttackRepeatSeconds = 2.5f; // an attack order that did not take is given again after this

        private HostileNet target;
        private HostileNet ordered;
        private float orderedAt;
        private WorldDrop fetching;
        private float nextAction;
        private bool wasOn;

        private void Awake()
        {
            autopilot = GetComponent<PlayerAutopilot>();
            magnet = GetComponent<MagnetController>();
            pointer = GetComponent<PointAndClickPlayer>();
            melee = GetComponent<PlayerMeleeCombat>();
            hotbar = GetComponent<PlayerSkillHotbar>();
            skills = GetComponent<PlayerCombatSkills>();
            inventory = GetComponent<PlayerInventory>();
            resources = GetComponent<PlayerResources>();
            buffs = GetComponent<CharacterBuffManager>();
            loot = GetComponent<PlayerLoot>();
            progression = GetComponent<PlayerCharacterProgression>();
            networkObject = GetComponent<NetworkObject>();
        }

        private void OnEnable()
        {
            if (pointer != null) pointer.ManualCommand += OnManualCommand;
        }

        private void OnDisable()
        {
            if (pointer != null) pointer.ManualCommand -= OnManualCommand;
        }

        private bool IsLocal => inventory != null && PlayerInventory.Local == inventory;

        /// <summary>A click on the world is the player taking over.</summary>
        private void OnManualCommand()
        {
            if (IsLocal && autopilot.IsOn) Stop("you took over");
        }

        private void Update()
        {
            // The on / off key is the HUD's (AutopilotWindowController): it knows when the player is typing.
            if (!IsLocal || autopilot.Settings == null) return;

            bool on = autopilot.IsOn;
            if (on != wasOn)
            {
                wasOn = on;
                target = null;
                ordered = null;
                fetching = null;
                nextAction = Time.time + 0.5f;
                if (!on && melee != null) melee.CancelAttack();
            }
            if (!on || Time.time < nextAction) return;
            Vector2 delay = autopilot.Settings.actionDelaySeconds;
            nextAction = Time.time + Random.Range(Mathf.Min(delay.x, delay.y), Mathf.Max(delay.x, delay.y));
            Step();
        }

        private void Stop(string why)
        {
            Note("stopping: " + why);
            autopilot.RequestSetOn(false);
        }

        private static void Note(string what)
        {
            if (Logging) Debug.Log("[Autopilot] " + what);
        }

        private void Step()
        {
            if (resources != null && !resources.IsAlive) { Stop("defeated"); return; }
            AutopilotPreferences preferences = autopilot.Preferences;
            if (UseStim(preferences)) return;
            if (KeepBuff(preferences.buffSlotA) || KeepBuff(preferences.buffSlotB)) return;
            if (FetchDrop(preferences)) return;
            Fight(preferences);
        }

        // ------------------------------------------------------------------ 1. Stims

        private bool UseStim(AutopilotPreferences preferences)
        {
            if (resources == null || inventory == null) return false;
            bool lowHealth = resources.MaxHealth > 0f && resources.Health / resources.MaxHealth * 100f < preferences.stimHealthPercent;
            bool lowFlux = resources.MaxChi > 0f && resources.Chi / resources.MaxChi * 100f < preferences.stimFluxPercent;
            if (!lowHealth && !lowFlux) return false;

            ItemDefinition stim = FindStim(lowHealth, lowFlux);
            if (stim == null)
            {
                // Low on Flux alone is no reason to stop: the basic attack costs none.
                if (lowHealth && preferences.whenStimsOut == WhenStimsOut.Stop)
                {
                    ClientPresentationRegistry.Messages?.ShowMessage("Autopilot: out of HP Stims.");
                    Stop("out of HP Stims");
                    return true;
                }
                return false;
            }
            Note($"using {stim.displayName}");
            inventory.UseItem(stim.itemId);
            return true;
        }

        /// <summary>The Stim that does the most of what is needed, among those in the Pack.</summary>
        private ItemDefinition FindStim(bool health, bool flux)
        {
            ItemCatalog catalog = inventory.Catalog;
            if (catalog == null) return null;
            ItemDefinition best = null;
            float bestScore = 0f;
            for (int i = 0; i < PlayerInventory.Capacity; i++)
            {
                ItemStack cell = inventory.Get(i);
                if (cell.IsEmpty) continue;
                ItemDefinition item = catalog.Get(cell.itemId);
                if (item == null) continue;
                float score = (health ? item.restoreHealth : 0f) + (flux ? item.restoreChi : 0f);
                if (score > bestScore) { bestScore = score; best = item; }
            }
            return best;
        }

        // ------------------------------------------------------------------ 2. buffs

        private bool KeepBuff(int slot)
        {
            if (slot == AutopilotPreferences.NoSlot || hotbar == null || skills == null) return false;
            SkillDefinitionSO skill = hotbar.GetSlotEntry(slot).combatSkill;
            if (skill == null || (buffs != null && buffs.HasBuff(skill.skillId))) return false;
            if (!skills.CanCast(skill, out _)) return false;
            Note($"recasting {skill.skillId} (Hotbar {slot + 1})");
            return hotbar.ActivateSlot(slot);
        }

        // ------------------------------------------------------------------ 3. drops

        private bool FetchDrop(AutopilotPreferences preferences)
        {
            if (magnet == null || loot == null || !preferences.magnetOn) return false;
            if (magnet.PackFull && preferences.whenPackFull == WhenPackFull.Stop)
            {
                ClientPresentationRegistry.Messages?.ShowMessage("Autopilot: the Pack is full.");
                Stop("the Pack is full");
                return true;
            }
            if (!TryGetArea(preferences, out Vector3 centre, out float radius)) return false;

            WorldDrop drop = magnet.NearestWanted(radius, true);
            if (drop == null || !InArea(drop.transform.position, centre, radius)) { fetching = null; return false; }
            Vector3 offset = drop.transform.position - transform.position;
            offset.y = 0f;
            if (offset.sqrMagnitude <= loot.PickupKeyRange * loot.PickupKeyRange) return false; // Magnet takes it from here
            if (drop == fetching && pointer.IsTravelling) return true;                          // already on the way
            fetching = drop;
            ordered = null;
            if (melee != null) melee.CancelAttack();
            Note($"walking to {drop.DisplayName}");
            return pointer.TravelTo(drop.transform.position, drop.DisplayName);
        }

        // ------------------------------------------------------------------ 4. the fight

        private readonly List<HostileNet> near = new List<HostileNet>(16);

        private void Fight(AutopilotPreferences preferences)
        {
            if (!TryGetArea(preferences, out Vector3 centre, out float radius)) return;
            int level = progression != null ? progression.CurrentLevel : 1;

            if (target == null || !target.isActiveAndEnabled || !target.Entity.IsAlive || !Allowed(target, preferences, level, centre, radius))
                target = Pick(preferences, level, centre, radius);
            if (target == null)
            {
                // Nothing to fight: wait where the hunt is, not where the last chase ended.
                if (!InArea(transform.position, centre, radius * 0.8f) && !pointer.IsTravelling) pointer.TravelTo(centre, "the hunting ground");
                return;
            }

            // One order per target: the attack runs by itself from there, and ordering it again every turn
            // would start the swing over. It is only given again when it plainly did not take.
            bool attacking = melee != null && melee.CurrentTarget == (IDamageable)target.Entity;
            if (ordered != target || (!attacking && Time.time - orderedAt > AttackRepeatSeconds))
            {
                Note($"attacking {target.Entity.EntityName} Lv {target.Level}");
                ordered = target;
                orderedAt = Time.time;
                pointer.CommandAttack(target.Entity);
                return;
            }
            if (!attacking) return;

            // The area skill once enough of them are close; otherwise the single-target one. The basic attack
            // fills the gaps by itself.
            if (preferences.areaSlot != AutopilotPreferences.NoSlot && Gathered(autopilot.Settings.gatherRadius) >= preferences.gatherCount
                && Press(preferences.areaSlot)) return;
            if (preferences.attackSlot != AutopilotPreferences.NoSlot) Press(preferences.attackSlot);
        }

        private bool Press(int slot)
        {
            if (hotbar == null || skills == null) return false;
            SkillDefinitionSO skill = hotbar.GetSlotEntry(slot).combatSkill;
            if (skill == null || !skills.CanCast(skill, target != null ? target.transform : null, out _)) return false;
            Note($"using {skill.skillId} (Hotbar {slot + 1})");
            return hotbar.ActivateSlot(slot);
        }

        private int Gathered(float metres)
        {
            int count = 0;
            foreach (HostileNet hostile in HostileNet.Active)
                if (hostile.Entity.IsAlive && (hostile.transform.position - transform.position).sqrMagnitude <= metres * metres) count++;
            return count;
        }

        private HostileNet Pick(AutopilotPreferences preferences, int level, Vector3 centre, float radius)
        {
            HostileNet best = null;
            float bestSqr = float.MaxValue;
            foreach (HostileNet hostile in HostileNet.Active)
            {
                if (!hostile.Entity.IsAlive || !Allowed(hostile, preferences, level, centre, radius)) continue;
                float sqr = (hostile.transform.position - transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = hostile; }
            }
            return best;
        }

        private bool Allowed(HostileNet hostile, AutopilotPreferences preferences, int level, Vector3 centre, float radius)
        {
            if (!InArea(hostile.transform.position, centre, radius)) return false;
            HostileDefinition definition = hostile.Definition;
            if (definition != null && definition.tier == HostileTier.Bruiser && !preferences.includeBruisers) return false;
            if (preferences.skipTagged && hostile.IsTaggedByAnother(networkObject)) return false;
            LevelGapSettings gaps = hostile.LevelGap;
            if (gaps == null) return true;
            int gap = LevelGapRules.Gap(hostile.Level, level);
            if (preferences.skipGrey && LevelGapRules.ExperiencePercent(gaps, gap) <= 0f) return false;
            return !(preferences.skipRed && LevelGapRules.IsTopBand(gaps, gap));
        }

        // ------------------------------------------------------------------ where

        /// <summary>The ground Autopilot keeps to: the whole Hostile Zone, or a circle round where it was switched on.</summary>
        private bool TryGetArea(AutopilotPreferences preferences, out Vector3 centre, out float radius)
        {
            HostileZone zone = HostileZone.At(autopilot.Anchor);
            if (preferences.wholeZone && zone != null)
            {
                centre = zone.Centre;
                radius = zone.Radius;
                return true;
            }
            centre = autopilot.Anchor;
            radius = preferences.huntRadius;
            // A circle that reaches past the zone is cut to it by the server's own rule (leaving the zone stops Autopilot).
            return zone != null || autopilot.Settings.allowedOutsideHostileZones;
        }

        private static bool InArea(Vector3 point, Vector3 centre, float radius)
        {
            Vector3 offset = point - centre;
            offset.y = 0f;
            return offset.sqrMagnitude <= radius * radius;
        }
    }
}
