using System;
using System.Collections.Generic;
using System.Text;
using KryticalNova.Hostiles;
using KryticalNova.Player;
using KryticalNova.Progression;
using QFSW.QC;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console Hostile commands. Like the GM commands they only run where the game is decided (host,
    /// server or offline) and act for the player picked by <see cref="GmTarget"/>. "The target" is the Hostile
    /// that player has under the target ring, or failing that the nearest one.
    /// </summary>
    public static class HostileCommands
    {
        private const int MaxSpawnCount = 50;
        private const float SpawnSpacing = 1.6f;
        private static bool frozen;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => frozen = false;

        private static bool TryGetPlayer(out GameObject player, out string error) => GmTarget.TryGetPlayer(out player, out error);

        private static bool TryParseSwitch(string text, bool current, out bool value)
        {
            text = (text ?? string.Empty).Trim().ToLowerInvariant();
            value = !current;
            if (text.Length == 0) return true;
            if (text == "on" || text == "true" || text == "1") { value = true; return true; }
            if (text == "off" || text == "false" || text == "0") { value = false; return true; }
            return false;
        }

        private static HostileNet TargetOf(GameObject player)
        {
            if (player.TryGetComponent(out PointAndClickPlayer pointer) && pointer.SelectedTarget != null)
            {
                var selected = pointer.SelectedTarget.GetComponentInParent<HostileNet>();
                if (selected != null) return selected;
            }
            // The nearest living one; a corpse only when nothing is alive.
            HostileNet nearest = null, nearestDead = null;
            float best = float.MaxValue, bestDead = float.MaxValue;
            foreach (HostileNet hostile in HostileNet.Active)
            {
                float sqr = (hostile.transform.position - player.transform.position).sqrMagnitude;
                if (hostile.Entity.IsAlive) { if (sqr < best) { best = sqr; nearest = hostile; } }
                else if (sqr < bestDead) { bestDead = sqr; nearestDead = hostile; }
            }
            return nearest != null ? nearest : nearestDead;
        }

        private static List<HostileNet> Within(GameObject player, float radius, bool aliveOnly)
        {
            var found = new List<HostileNet>();
            foreach (HostileNet hostile in HostileNet.Active)
            {
                if (aliveOnly && !hostile.Entity.IsAlive) continue;
                if ((hostile.transform.position - player.transform.position).sqrMagnitude <= radius * radius) found.Add(hostile);
            }
            return found;
        }

        private static string Line(HostileNet hostile) =>
            $"{hostile.Definition.displayName} Lv {hostile.Level} · HP {hostile.Entity.CurrentHealth:0}/{hostile.Entity.MaxHealth:0}";

        [Command("gm.hostile.list", "List every Hostile species: id, name, level, tier.")]
        private static string List()
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            List<HostileNet> species = HostileSpawner.Species();
            if (species.Count == 0) return "No Hostile species. Build them with Krytical Nova/Hostiles/Build Hostiles.";
            var sb = new StringBuilder();
            foreach (HostileNet s in species)
            {
                HostileDefinition d = s.Definition;
                sb.AppendLine($"{d.id,4}  {d.displayName,-22} Lv {d.level,3}  {d.tier}  {(d.behaviour == HostileBehaviour.AttacksOnSight ? "attacks on sight" : "fights back")}");
            }
            return sb.ToString().TrimEnd();
        }

        [Command("gm.hostile.spawn", "Spawn Hostiles under the mouse cursor (or beside you): gm.hostile.spawn <id|name> [count] [level]. Outside any Hostile Zone.")]
        private static string Spawn(string idOrName, int count = 1, int level = 0)
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            HostileNet species = HostileSpawner.Find(idOrName);
            if (species == null) return $"No Hostile species '{idOrName}'. gm.hostile.list shows them.";
            HostileStatCurve curve = species.Curve;
            if (curve != null && level > curve.MaxLevel) return $"The highest Hostile level is {curve.MaxLevel}.";

            Vector3 centre = player.transform.position + player.transform.forward * 4f;
            Camera cam = Camera.main;
            if (cam != null && Mouse.current != null &&
                Physics.Raycast(cam.ScreenPointToRay(Mouse.current.position.ReadValue()), out RaycastHit hit, 2000f, ~0, QueryTriggerInteraction.Ignore))
                centre = hit.point;

            count = Mathf.Clamp(count, 1, MaxSpawnCount);
            int side = Mathf.CeilToInt(Mathf.Sqrt(count)), made = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = new Vector3((i % side - (side - 1) * 0.5f) * SpawnSpacing, 0f, (i / side - (side - 1) * 0.5f) * SpawnSpacing);
                Vector3 at = centre + offset;
                Vector3 toPlayer = player.transform.position - at;
                float facing = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg;
                if (HostileSpawner.Spawn(species, at, facing, level, true) != null) made++;
            }
            if (made == 0) return "No walkable ground there.";
            int shownLevel = level > 0 ? level : species.Definition.level;
            return $"Spawned {made} {species.Definition.displayName} at level {shownLevel}.";
        }

        [Command("gm.hostile.clear", "Remove GM-spawned Hostiles: all of them, or those within [radius] metres.")]
        private static string Clear(float radius = 0f)
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            var doomed = new List<HostileNet>();
            foreach (HostileNet hostile in HostileNet.Active)
            {
                if (!hostile.GmSpawned) continue;
                if (radius > 0f && (hostile.transform.position - player.transform.position).sqrMagnitude > radius * radius) continue;
                doomed.Add(hostile);
            }
            foreach (HostileNet hostile in doomed) HostileSpawner.Remove(hostile);
            return $"Removed {doomed.Count} GM-spawned Hostile(s).";
        }

        [Command("gm.hostile.kill", "Kill the target, or every Hostile within [radius] metres, as if you had struck the blow.")]
        private static string Kill(float radius = 0f)
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            var victims = new List<HostileNet>();
            if (radius > 0f) victims = Within(player, radius, true);
            else
            {
                HostileNet target = TargetOf(player);
                if (target != null && target.Entity.IsAlive) victims.Add(target);
            }
            if (victims.Count == 0) return "No living Hostile there.";
            var attacker = player.GetComponent<CharacterCombatStats>();
            foreach (HostileNet victim in victims)
                victim.Entity.Kill(new KillingBlow { Attacker = attacker, Direction = KillingBlow.DirectionFrom(player.transform, victim.transform) });
            return $"Killed {victims.Count} Hostile(s).";
        }

        [Command("gm.hostile.info", "The target's species, level, numbers, what its brain is doing and who it is fighting.")]
        private static string Info()
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            HostileNet target = TargetOf(player);
            if (target == null) return "No Hostile in the world. gm.hostile.spawn <id> makes one.";
            HostileStats s = target.Stats;
            float distance = Vector3.Distance(player.transform.position, target.transform.position);
            return string.Join("\n", new[]
            {
                $"{Line(target)} · {target.Definition.tier} · species {target.Definition.id}{(target.GmSpawned ? " · GM-spawned" : "")}",
                $"Attack {s.Attack:0} · Defense {s.Defense:0} · Hit {s.HitRate:0} · Evasion {s.Evasion:0} · Crit {s.CriticalRate:0}%",
                $"XP {s.Experience} · Creds {s.Creds} · aggro {s.AggroRadius:0.#} m · walk {s.WalkSpeed:0.##} / run {s.RunSpeed:0.##} m/s · respawn {s.RespawnSeconds:0.#} s",
                $"Brain: {(target.Brain != null ? target.Brain.Describe() : "none")} · {distance:0.#} m from you",
            });
        }

        [Command("gm.hostile.hp", "Set the target's health: a number, or a percentage (gm.hostile.hp 25%).")]
        private static string Health(string value)
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            HostileNet target = TargetOf(player);
            if (target == null || !target.Entity.IsAlive) return "No living Hostile there.";
            value = (value ?? string.Empty).Trim();
            bool percent = value.EndsWith("%");
            if (!float.TryParse(percent ? value.TrimEnd('%') : value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float amount) || amount <= 0f)
                return "Give a positive number or a percentage: gm.hostile.hp 120 or gm.hostile.hp 25%.";
            target.Entity.SetHealth(percent ? target.Entity.MaxHealth * amount / 100f : amount);
            return Line(target);
        }

        [Command("gm.hostile.freeze", "Stop every Hostile's brain (they stand still and take hits): gm.hostile.freeze [on|off]. Hostiles spawned later are not frozen.")]
        private static string Freeze(string state = "")
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            if (!TryParseSwitch(state, frozen, out bool value)) return "Use gm.hostile.freeze on or off.";
            frozen = value;
            int count = 0;
            foreach (HostileNet hostile in HostileNet.Active)
            {
                if (hostile.Brain == null) continue;
                hostile.Brain.Frozen = frozen;
                count++;
            }
            return frozen ? $"{count} Hostile(s) frozen." : $"{count} Hostile(s) released.";
        }

        [Command("gm.hostile.ignore", "Hostiles never notice the targeted player by themselves: gm.hostile.ignore [on|off]. Hitting one still starts a fight.")]
        private static string Ignore(string state = "")
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            if (!TryParseSwitch(state, HostileIgnore.IsIgnored(player.transform), out bool value)) return "Use gm.hostile.ignore on or off.";
            HostileIgnore.Set(player.transform, value);
            if (value)
                foreach (HostileNet hostile in HostileNet.Active)
                    if (hostile.Brain != null && hostile.Brain.Target == player.transform) hostile.Brain.Calm();
            return value ? "Hostiles no longer notice that player." : "Hostiles notice that player again.";
        }

        [Command("gm.hostile.ttk", "Hits to kill and hits to die against a species with the targeted player's stats: gm.hostile.ttk <id|name> [level].")]
        private static string TimeToKill(string idOrName, int level = 0)
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            HostileNet species = HostileSpawner.Find(idOrName);
            if (species == null) return $"No Hostile species '{idOrName}'. gm.hostile.list shows them.";
            if (species.Curve == null) return "That species has no stat curve.";
            var sheet = player.GetComponent<CharacterCombatStats>();
            var entity = player.GetComponent<CombatEntity>();
            if (sheet == null || entity == null) return "That player has no combat stats.";

            HostileStats h = species.Definition.StatsFrom(species.Curve, level);
            CombatStatTuningData tuning = sheet.TuningData;
            float minHit = tuning != null ? tuning.minimumHitChance : 10f, maxHit = tuning != null ? tuning.maximumHitChance : 95f;
            float critMultiplier = (tuning != null ? tuning.baseCriticalMultiplier : 1.5f) + sheet.GetStatValue(StatType.CriticalDamagePercent) / 100f;

            // The damage formula of CombatCalculator, on averages instead of rolls.
            float myHit = Mathf.Max(1f, sheet.GetStatValue(StatType.AttackPower) - h.Defense)
                          + Mathf.Max(0f, sheet.GetStatValue(StatType.ElementalAttack));
            float myChance = Chance(sheet.GetStatValue(StatType.HitRate), h.Evasion, minHit, maxHit);
            float myCrit = Mathf.Clamp(sheet.GetStatValue(StatType.CriticalRate), 0f, 100f) / 100f;
            float myAverage = myHit * (1f + myCrit * (critMultiplier - 1f));
            float itsHit = Mathf.Max(1f, h.Attack - sheet.GetStatValue(StatType.DefensePower));
            float itsChance = Chance(h.HitRate, sheet.GetStatValue(StatType.EvasionRate), minHit, maxHit);

            return string.Join("\n", new[]
            {
                $"{species.Definition.displayName} Lv {h.Level}: HP {h.MaxHealth:0} · Attack {h.Attack:0} · Defense {h.Defense:0} · Hit {h.HitRate:0} · Evasion {h.Evasion:0}",
                $"You: {myHit:0} a plain hit, landing {myChance:0}% of the time, critical {myCrit * 100f:0}% -> {h.MaxHealth / myHit:0.#} hits to kill ({h.MaxHealth / (myAverage * myChance / 100f):0.#} swings on average)",
                $"It: {itsHit:0} a hit, landing {itsChance:0}% of the time -> {entity.MaxHealth / itsHit:0.#} hits to kill you ({entity.MaxHealth / (itsHit * itsChance / 100f):0.#} swings on average)",
            });
        }

        // ------------------------------------------------------------------ level gap and rates

        private static LevelGapSettings GapSettings()
        {
            foreach (HostileNet species in HostileSpawner.Species())
                if (species.LevelGap != null) return species.LevelGap;
            return null;
        }

        private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        private static string GapLine(LevelGapSettings gaps, int gap)
        {
            LevelGapSettings.Band band = LevelGapRules.BandOf(gaps, gap);
            float hit = band.hitChanceModifier;
            return $"gap {gap,4}: {band.name,-7} {Hex(band.nameColor)} · XP {LevelGapRules.ExperiencePercent(gaps, gap),3:0}% · drops {band.dropPercent,3:0}%{(band.gearDrops ? "" : ", no gear")} · your hit chance {(hit == 0f ? "unchanged" : $"{hit:+0;-0} points")}";
        }

        [Command("gm.gap", "What a Hostile of that level is to the targeted player: name colour, XP, drops, hit chance. gm.gap <hostile level>.")]
        private static string Gap(int hostileLevel)
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            LevelGapSettings gaps = GapSettings();
            if (gaps == null) return "No level-gap settings. Build the Hostiles first.";
            var progression = player.GetComponent<PlayerCharacterProgression>();
            int level = progression != null ? progression.CurrentLevel : 1;
            return $"Level {level} against a level {hostileLevel} Hostile -> " + GapLine(gaps, LevelGapRules.Gap(hostileLevel, level));
        }

        [Command("gm.gap.table", "The whole level-gap table as the game has it loaded, gaps -12 to +12.")]
        private static string GapTable()
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            LevelGapSettings gaps = GapSettings();
            if (gaps == null) return "No level-gap settings. Build the Hostiles first.";
            var sb = new StringBuilder();
            for (int gap = 12; gap >= -12; gap--) sb.AppendLine(GapLine(gaps, gap));
            sb.Append($"Death to a Hostile: {gaps.deathExperienceLossPercent:0.#}% of the level's XP ({(HostileRates.DeathPenalty ? "on" : "off")}) · kill held for {gaps.tagTimeoutSeconds:0} s · XP rate x{HostileRates.Experience:0.##} · drop rate x{HostileRates.Drops:0.##}");
            return sb.ToString();
        }

        [Command("gm.xprate", "Multiply the XP of every Hostile kill: gm.xprate <x> (1 = normal).")]
        private static string ExperienceRate(float multiplier)
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            HostileRates.SetExperience(multiplier);
            return $"Hostile kills pay x{HostileRates.Experience:0.##} XP.";
        }

        [Command("gm.droprate", "Multiply the drop chances of every Hostile kill: gm.droprate <x> (1 = normal).")]
        private static string DropRate(float multiplier)
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            HostileRates.SetDrops(multiplier);
            return $"Hostile kills drop at x{HostileRates.Drops:0.##}.";
        }

        [Command("gm.deathpenalty", "Turn the XP loss for dying to a Hostile on or off: gm.deathpenalty [on|off].")]
        private static string DeathPenalty(string state = "")
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            if (!TryParseSwitch(state, HostileRates.DeathPenalty, out bool value)) return "Use gm.deathpenalty on or off.";
            HostileRates.DeathPenalty = value;
            return value ? "Dying to a Hostile costs XP." : "Dying to a Hostile costs nothing.";
        }

        private const int AssumedKillsPerHour = 600; // one kill every six seconds, for the income comparison

        [Command("gm.hostile.loot", "Simulate kills of a species at the targeted player's level and print XP, Creds and drops per kill and per hour: gm.hostile.loot <id|name> [kills].")]
        private static string Loot(string idOrName, int kills = 1000)
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            HostileNet species = HostileSpawner.Find(idOrName);
            if (species == null) return $"No Hostile species '{idOrName}'. gm.hostile.list shows them.";
            if (species.Curve == null || species.Settings == null || species.LevelGap == null) return "That species is missing its settings. Build the Hostiles again.";
            kills = Mathf.Clamp(kills, 1, 1_000_000);

            var progression = player.GetComponent<PlayerCharacterProgression>();
            int level = progression != null ? progression.CurrentLevel : 1;
            HostileStats stats = species.Definition.StatsFrom(species.Curve);
            int gap = LevelGapRules.Gap(stats.Level, level);
            float dropPercent = LevelGapRules.DropPercent(species.LevelGap, gap);
            bool gearAllowed = LevelGapRules.GearDrops(species.LevelGap, gap);
            int experience = HostileKillRewards.ExperienceFor(stats, species.LevelGap, gap, 0f);

            var random = new System.Random(12345);
            long creds = 0;
            int stims = 0, gear = 0;
            for (int i = 0; i < kills; i++)
            {
                HostileLootRoll roll = HostileLoot.Roll(stats, species.Curve, species.Settings, dropPercent, gearAllowed, HostileRates.Drops,
                    (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble());
                creds += roll.Creds;
                if (roll.Stim != null) stims++;
                if (roll.Gear != null) gear++;
            }
            double credsPerKill = creds / (double)kills;
            double expected = stats.Creds * species.Curve.credsDropChance * dropPercent / 100f * HostileRates.Drops;
            var wallet = player.GetComponent<KryticalNova.Economy.PlayerWallet>();
            long target = wallet != null && wallet.Settings != null ? wallet.Settings.CredsPerHour(level) : 0;
            return string.Join("\n", new[]
            {
                $"{species.Definition.displayName} Lv {stats.Level} for a level {level} player (gap {gap}), {kills} kills:",
                $"XP {experience} a kill · Creds {credsPerKill:0.##} a kill (expected {expected:0.##}) · Stims {stims * 100.0 / kills:0.##}% · gear {gear * 100.0 / kills:0.##}%",
                $"At {AssumedKillsPerHour} kills an hour: {experience * (long)AssumedKillsPerHour} XP, {credsPerKill * AssumedKillsPerHour:0} Creds" +
                (target > 0 ? $" (the income target at level {level} is {target} an hour)" : ""),
            });
        }

        // ------------------------------------------------------------------ Hostile Zones

        private static HostileZone FindZone(string id)
        {
            id = (id ?? string.Empty).Trim().ToLowerInvariant();
            foreach (HostileZone zone in HostileZone.All)
                if (zone.Definition != null && zone.Definition.id.ToLowerInvariant() == id) return zone;
            foreach (HostileZone zone in HostileZone.All)
                if (zone.Definition != null && zone.Definition.id.ToLowerInvariant().Contains(id) && id.Length > 0) return zone;
            return null;
        }

        private const string NoZone = "No Hostile Zone with that id. gm.zone.list shows them.";

        [Command("gm.zone.list", "Every Hostile Zone: id, level, species, alive / max, awake or asleep, seconds to the next respawn.")]
        private static string ZoneList()
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            if (HostileZone.All.Count == 0) return "This map has no Hostile Zones.";
            var sb = new StringBuilder();
            foreach (HostileZone zone in HostileZone.All)
            {
                HostileZoneDefinition d = zone.Definition;
                if (d == null) continue;
                string species = d.hostile != null ? $"Lv {d.hostile.level,3} {d.hostile.displayName,-16}" : "no species";
                float next = zone.SecondsToNextRespawn;
                string bruiser = !zone.HasBruiser ? "" : zone.BruiserAlive ? " · Bruiser alive" : zone.SecondsToBruiser >= 0f ? $" · Bruiser in {zone.SecondsToBruiser:0} s" : "";
                sb.AppendLine($"{d.id,-16} {species} {zone.Alive,2}/{zone.Capacity,-2} {(zone.IsAwake ? "awake " : "asleep")} {(next >= 0f ? $"next in {next:0.#} s" : "full")}{bruiser}");
            }
            return sb.ToString().TrimEnd();
        }

        [Command("gm.zone.fill", "Bring back every dead Hostile of a zone now: gm.zone.fill <id>.")]
        private static string ZoneFill(string id)
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            HostileZone zone = FindZone(id);
            if (zone == null) return NoZone;
            return $"{zone.Definition.displayName}: {zone.FillNow()} brought back, {zone.Alive}/{zone.Capacity} alive.";
        }

        [Command("gm.zone.clear", "Empty a zone; its Hostiles come back after [seconds] (default 30): gm.zone.clear <id> [seconds].")]
        private static string ZoneClear(string id, float seconds = 30f)
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            HostileZone zone = FindZone(id);
            if (zone == null) return NoZone;
            zone.Empty(Mathf.Max(0f, seconds));
            return $"{zone.Definition.displayName} emptied for {Mathf.Max(0f, seconds):0} s.";
        }

        [Command("gm.zone.respawn", "Set the respawn time of every zone, for fast testing: gm.zone.respawn <seconds|default>.")]
        private static string ZoneRespawn(string seconds)
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            float value = -1f;
            bool isDefault = string.Equals((seconds ?? string.Empty).Trim(), "default", StringComparison.OrdinalIgnoreCase);
            if (!isDefault && (!float.TryParse(seconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) || value < 0f))
                return "Use gm.zone.respawn <seconds> or gm.zone.respawn default.";
            foreach (HostileZone zone in HostileZone.All) zone.OverrideRespawn(isDefault ? -1f : value);
            return isDefault ? "Every zone uses its species respawn time again." : $"Every zone now respawns after {value:0.#} s (kills from now on).";
        }

        [Command("gm.zone.sleep", "Force a zone asleep or awake, or let players decide again: gm.zone.sleep <id> [on|off|auto].")]
        private static string ZoneSleep(string id, string state = "on")
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            HostileZone zone = FindZone(id);
            if (zone == null) return NoZone;
            state = (state ?? string.Empty).Trim().ToLowerInvariant();
            if (state == "auto") { zone.ForceAwake(null); return $"{zone.Definition.displayName} wakes and sleeps by itself again."; }
            if (!TryParseSwitch(state, false, out bool asleep)) return "Use gm.zone.sleep <id> on, off or auto.";
            zone.ForceAwake(!asleep);
            return $"{zone.Definition.displayName} is held {(asleep ? "asleep" : "awake")}.";
        }

        [Command("gm.zone.bruiser", "Bring a zone's Bruiser now, or print its timer: gm.zone.bruiser <id>.")]
        private static string ZoneBruiser(string id)
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            HostileZone zone = FindZone(id);
            if (zone == null) return NoZone;
            if (!zone.HasBruiser) return $"{zone.Definition.displayName} has no Bruiser.";
            if (zone.BruiserAlive) return "Its Bruiser is alive.";
            return zone.SpawnBruiserNow() ? "Its Bruiser is here." : "The Bruiser could not be placed (zone not started, or no free ground).";
        }

        [Command("gm.hostile.stats", "Live counts: Hostiles in the world, asleep, waiting in the pools and made since Play started; the same for drops.")]
        private static string Stats()
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            int alive = 0, asleep = 0;
            foreach (HostileNet hostile in HostileNet.Active)
            {
                if (hostile.Entity.IsAlive) alive++;
                if (hostile.Asleep) asleep++;
            }
            return $"In the world {HostileNet.Active.Count} ({alive} alive, {asleep} asleep) · waiting in pools {HostilePool.Waiting} · made since Play started {HostilePool.Created}\n" +
                   $"Drops on the ground {KryticalNova.Items.WorldDrop.Active.Count} · waiting in the pool {KryticalNova.Items.WorldDropPool.Waiting} · made since Play started {KryticalNova.Items.WorldDropPool.Created}";
        }

        private static float Chance(float hitRate, float evasion, float min, float max) =>
            Mathf.Clamp(hitRate / Mathf.Max(1f, hitRate + evasion) * 100f, min, max);
    }
}
