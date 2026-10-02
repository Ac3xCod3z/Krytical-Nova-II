using System;
using System.Collections.Generic;
using System.Text;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Progression;
using KryticalNova.World;
using KryticalNova.World.Portals;
using QFSW.QC;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console GM / development commands (prefix "gm."). Like the item and world commands they act on the
    /// authority's own player only (host or offline), because progression and max HP/Chi are computed on the server.
    /// Time and weather shortcuts wrap <see cref="WorldEnvironmentCommands"/>' API.
    /// </summary>
    public static class GmCommands
    {
        private const string NoPlayer = "No local player.";
        private const string NoAuthority = "Only the host/offline player can use GM commands.";
        private const float NavSampleRadius = 6f;

        private static readonly object GmStatSource = new object();
        private static readonly Dictionary<StatType, float> gmFlatStats = new Dictionary<StatType, float>();
        private static bool oneShot;
        private static float pausedTimeScale = 1f;

        /// <summary>Read by the editor GM Tools window to show the toggle state.</summary>
        public static bool OneShotEnabled => oneShot;

        /// <summary>Read-only teleport target listing for tools (the editor GM Tools window).</summary>
        public static List<(string Name, string Kind, Vector3 Position)> GetTeleportTargets()
        {
            var result = new List<(string, string, Vector3)>();
            foreach (var t in CollectTargets())
                if (t.Transform != null) result.Add((t.Name, t.Kind, TargetPosition(t.Transform)));
            return result;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            gmFlatStats.Clear();
            oneShot = false;
            pausedTimeScale = 1f;
        }

        #region Player lookup
        private static bool TryGetPlayer(out GameObject player, out string error)
        {
            player = null;
            error = null;
            var inv = PlayerInventory.Local;
            if (inv == null) { error = NoPlayer; return false; }
            if (inv.IsSpawned && !inv.IsServer) { error = NoAuthority; return false; }
            player = inv.gameObject;
            return true;
        }

        private static bool TryGet<T>(out T component, out string error) where T : Component
        {
            component = null;
            if (!TryGetPlayer(out var player, out error)) return false;
            component = player.GetComponent<T>();
            if (component == null) { error = $"Local player has no {typeof(T).Name}."; return false; }
            return true;
        }
        #endregion

        #region Help
        [Command("gm.help", "List GM commands.")]
        private static string Help()
        {
            return string.Join("\n", new[]
            {
                "PROGRESSION  gm.level <n> · gm.maxlevel · gm.xp <n> · gm.statpoints <n> · gm.skillpoints <n> · gm.attr <str|vit|agi|spi> <n> · gm.resetstats",
                "STATS        gm.stats · gm.setstat <StatType> <value> · gm.clearstats",
                "VITALS       gm.heal · gm.fillchi · gm.damage <n> · gm.spendchi <n> · gm.kill",
                "COMBAT       gm.dummy.hit [times] · gm.dummy.reset · gm.dropskills <true|false>",
                "GOD MODE     gm.god [on|off] · gm.infchi · gm.nocd · gm.oneshot · gm.speed <mult> · gm.reset",
                "TELEPORT     gm.poi · gm.tp <name> · gm.tpxyz <x> <y> <z> · gm.tpcursor",
                "TIME         time.set <h> · time.pause · time.resume · time.scale <x> · time.dawn · time.noon · time.dusk · time.midnight · time.daylength <s>",
                "WEATHER      weather.set <name> [secs] · weather.clear · weather.lock <bool> · weather.list",
                "SKY          sky.rainbow [0-1|off] · sky.moonphase <new|first|full|last|0-1|off> · sky.moon.next · sky.halo [sun|moon|both|off] · sky.shimmer <x|off> · sky.flare <x|off> · sky.lightning · sky.status · sky.reset",
                "ITEMS        item.give <id|name> [n] · item.list [slot] · item.clear",
            });
        }
        #endregion

        #region Progression
        [Command("gm.level", "Set character level (raising grants each skipped level's stat/skill points; lowering rebuilds the character).")]
        private static string SetLevel(int level)
        {
            if (!TryGet<PlayerCharacterProgression>(out var prog, out var error)) return error;
            prog.SetLevel(level);
            return $"Level {prog.CurrentLevel} · {prog.StatPoints} stat pts · {prog.SkillPoints} skill pts.";
        }

        [Command("gm.maxlevel", "Jump to the maximum level.")]
        private static string MaxLevel()
        {
            if (!TryGet<PlayerCharacterProgression>(out var prog, out var error)) return error;
            prog.SetLevel(prog.MaxLevel);
            return $"Max level {prog.CurrentLevel} · {prog.StatPoints} stat pts · {prog.SkillPoints} skill pts.";
        }

        [Command("gm.xp", "Grant XP (levels up normally).")]
        private static string GrantXp(int amount)
        {
            if (!TryGet<PlayerCharacterProgression>(out var prog, out var error)) return error;
            prog.GrantExperience(amount);
            return $"Level {prog.CurrentLevel} · {prog.Experience}/{prog.ExperienceRequired} XP.";
        }

        [Command("gm.statpoints", "Add (or remove with a negative value) unspent stat points.")]
        private static string StatPoints(int amount)
        {
            if (!TryGet<PlayerCharacterProgression>(out var prog, out var error)) return error;
            prog.AddStatPoints(amount);
            return $"{prog.StatPoints} stat points.";
        }

        [Command("gm.skillpoints", "Add (or remove with a negative value) unspent skill points.")]
        private static string SkillPoints(int amount)
        {
            if (!TryGet<PlayerCharacterProgression>(out var prog, out var error)) return error;
            prog.AddSkillPoints(amount);
            return $"{prog.SkillPoints} skill points.";
        }

        [Command("gm.attr", "Set an attribute directly: gm.attr vit 50 (str, vit, agi, spi).")]
        private static string SetAttribute(string attribute, int value)
        {
            if (!TryGet<PlayerCharacterProgression>(out var prog, out var error)) return error;
            if (!TryParseAttribute(attribute, out var attr)) return $"Unknown attribute '{attribute}'. Use str, vit, agi or spi.";
            prog.SetAttribute(attr, value);
            return $"{attr} = {prog.GetAttribute(attr)}.";
        }

        [Command("gm.resetstats", "Refund all allocated attribute points.")]
        private static string ResetStats()
        {
            if (!TryGet<PlayerCharacterProgression>(out var prog, out var error)) return error;
            prog.ResetAllocations();
            return $"Attributes reset · {prog.StatPoints} stat points.";
        }

        private static bool TryParseAttribute(string text, out CharacterAttribute attribute)
        {
            switch (text.Trim().ToLowerInvariant())
            {
                case "str": case "strength": attribute = CharacterAttribute.Strength; return true;
                case "vit": case "vitality": attribute = CharacterAttribute.Vitality; return true;
                case "agi": case "agility": attribute = CharacterAttribute.Agility; return true;
                case "spi": case "spirit": case "chi": attribute = CharacterAttribute.Spirit; return true;
                default: attribute = default; return false;
            }
        }
        #endregion

        #region Stats
        [Command("gm.stats", "Print every stat (base → final).")]
        private static string Stats()
        {
            if (!TryGet<CharacterCombatStats>(out var stats, out var error)) return error;
            var sb = new StringBuilder();
            foreach (StatType stat in Enum.GetValues(typeof(StatType)))
            {
                float baseValue = stats.GetBaseStatValue(stat);
                float final = stats.GetStatValue(stat);
                sb.Append(stat.ToString().PadRight(28)).Append(baseValue.ToString("0.##").PadLeft(9))
                  .Append("  →  ").AppendLine(final.ToString("0.##"));
            }
            if (TryGet<PlayerResources>(out var res, out _))
                sb.AppendLine($"HP {res.Health:0}/{res.MaxHealth:0}   Chi {res.Chi:0}/{res.MaxChi:0}");
            return sb.ToString();
        }

        [Command("gm.setstat", "Add a flat GM bonus to any stat, e.g. gm.setstat MaxHP 500 or gm.setstat Luck 25 (0 removes it).")]
        private static string SetStat(StatType stat, float value)
        {
            if (!TryGet<CharacterCombatStats>(out var stats, out var error)) return error;
            if (Mathf.Approximately(value, 0f)) gmFlatStats.Remove(stat);
            else gmFlatStats[stat] = value;
            ApplyGmModifiers(stats);
            return $"{stat} GM bonus {value:+0.##;-0.##;0} → {stats.GetStatValue(stat):0.##}.";
        }

        [Command("gm.clearstats", "Remove every GM stat bonus (incl. one-shot).")]
        private static string ClearStats()
        {
            if (!TryGet<CharacterCombatStats>(out var stats, out var error)) return error;
            gmFlatStats.Clear();
            oneShot = false;
            ApplyGmModifiers(stats);
            return "GM stat bonuses cleared.";
        }

        private static void ApplyGmModifiers(CharacterCombatStats stats)
        {
            var mods = new List<StatModifier>();
            foreach (var kv in gmFlatStats)
                mods.Add(new StatModifier(kv.Key, ModifierType.Flat, kv.Value, GmStatSource));
            if (oneShot)
                mods.Add(new StatModifier(StatType.AttackPower, ModifierType.PercentMult, 100000f, GmStatSource));
            stats.SetSourceModifiers(GmStatSource, mods);
        }
        #endregion

        #region Vitals
        [Command("gm.heal", "Fully restore HP and Chi.")]
        private static string Heal()
        {
            if (!TryGet<PlayerResources>(out var res, out var error)) return error;
            res.RestoreAll();
            return $"HP {res.Health:0}/{res.MaxHealth:0}   Chi {res.Chi:0}/{res.MaxChi:0}";
        }

        [Command("gm.fillchi", "Fully restore Chi.")]
        private static string FillChi()
        {
            if (!TryGet<PlayerResources>(out var res, out var error)) return error;
            res.RestoreChi(res.MaxChi);
            return $"Chi {res.Chi:0}/{res.MaxChi:0}";
        }

        [Command("gm.damage", "Deal damage to yourself (ignored while god mode is on).")]
        private static string Damage(float amount)
        {
            if (!TryGet<PlayerResources>(out var res, out var error)) return error;
            res.ApplyDamage(amount);
            return $"HP {res.Health:0}/{res.MaxHealth:0}{(GmState.GodMode ? " (god mode on)" : "")}";
        }

        [Command("gm.spendchi", "Spend Chi (tests the Chi bar / regen; ignored while infinite Chi is on).")]
        private static string SpendChi(float amount)
        {
            if (!TryGet<PlayerResources>(out var res, out var error)) return error;
            res.TrySpendChi(Mathf.Min(amount, res.Chi));
            return $"Chi {res.Chi:0}/{res.MaxChi:0}{(GmState.InfiniteChi ? " (infinite Chi on)" : "")}";
        }

        [Command("gm.kill", "Kill yourself (bypasses god mode).")]
        private static string Kill()
        {
            if (!TryGet<PlayerResources>(out var res, out var error)) return error;
            bool god = GmState.GodMode;
            GmState.GodMode = false;
            res.ApplyDamage(res.Health + 1f);
            GmState.GodMode = god;
            return "You died.";
        }
        #endregion

        #region God mode
        [Command("gm.god", "Toggle (or set) no-damage god mode.")]
        private static string God() => God(!GmState.GodMode);

        [Command("gm.god")]
        private static string God(bool on)
        {
            GmState.GodMode = on;
            return $"God mode {(on ? "ON" : "OFF")}.";
        }

        [Command("gm.infchi", "Toggle infinite Chi (skills never fail or drain Chi).")]
        private static string InfiniteChi()
        {
            GmState.InfiniteChi = !GmState.InfiniteChi;
            return $"Infinite Chi {(GmState.InfiniteChi ? "ON" : "OFF")}.";
        }

        [Command("gm.nocd", "Toggle no skill cooldowns.")]
        private static string NoCooldowns()
        {
            GmState.NoCooldowns = !GmState.NoCooldowns;
            return $"No cooldowns {(GmState.NoCooldowns ? "ON" : "OFF")}.";
        }

        [Command("gm.oneshot", "Toggle ×1000 Attack Power.")]
        private static string OneShot()
        {
            if (!TryGet<CharacterCombatStats>(out var stats, out var error)) return error;
            oneShot = !oneShot;
            ApplyGmModifiers(stats);
            return $"One-shot {(oneShot ? "ON" : "OFF")} · AP {stats.GetStatValue(StatType.AttackPower):0}.";
        }

        [Command("gm.speed", "Movement speed multiplier (1 = normal).")]
        private static string Speed(float multiplier)
        {
            GmState.SpeedMultiplier = Mathf.Clamp(multiplier, 0.1f, 20f);
            return $"Speed ×{GmState.SpeedMultiplier:0.##}.";
        }

        [Command("gm.reset", "Turn off every GM toggle and stat bonus.")]
        private static string ResetAll()
        {
            GmState.ResetAll();
            gmFlatStats.Clear();
            oneShot = false;
            if (TryGet<CharacterCombatStats>(out var stats, out _)) ApplyGmModifiers(stats);
            return "GM toggles reset.";
        }
        #endregion

        #region Combat testing
        private static CombatEntity NearestDummy(GameObject player)
        {
            CombatEntity best = null;
            float bestSqr = float.MaxValue;
            foreach (var entity in UnityEngine.Object.FindObjectsByType<CombatEntity>(FindObjectsInactive.Exclude))
            {
                if (entity.gameObject == player || entity.GetComponent<PlayerResources>() != null) continue;
                float sqr = (entity.transform.position - player.transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = entity; }
            }
            return best;
        }

        [Command("gm.dummy.hit", "Strike the nearest combat dummy/NPC target with a normal attack roll.")]
        private static string HitDummy(int times = 1)
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            var stats = player.GetComponent<CharacterCombatStats>();
            var target = NearestDummy(player);
            if (stats == null || target == null || target.CombatStats == null) return "No combat target found in the scene.";
            var sb = new StringBuilder();
            for (int i = 0; i < Mathf.Clamp(times, 1, 50); i++)
            {
                var result = CombatCalculator.CalculateAttack(stats, target.CombatStats);
                target.TakeDamage(result, stats);
                ClientPresentationRegistry.Feedback?.ShowCombatFeedback(target.Transform.position, result);
                sb.AppendLine(result.IsEvaded ? "EVADED"
                    : result.IsReflected ? $"REFLECTED ({result.ReflectedDamage:0})"
                    : $"{(result.IsCritical ? "CRIT " : "")}{result.DamageDealt:0} dmg");
            }
            sb.Append($"{target.EntityName} HP {target.CurrentHealth:0}/{target.MaxHealth:0}");
            return sb.ToString();
        }

        [Command("gm.dummy.reset", "Restore the nearest combat dummy/NPC target to full health.")]
        private static string ResetDummy()
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            var target = NearestDummy(player);
            if (target == null) return "No combat target found in the scene.";
            target.ResetHealth();
            return $"{target.EntityName} HP {target.CurrentHealth:0}/{target.MaxHealth:0}";
        }

        [Command("gm.dropskills", "Unlock (true) or lock (false) every rare drop-book combat skill.")]
        private static string DropSkills(bool unlock = true)
        {
            if (!TryGet<PlayerCombatSkills>(out var skills, out var error)) return error;
            if (unlock) skills.UnlockAllDropSkills(); else skills.LockAllDropSkills();
            return unlock ? "All drop-book skills unlocked." : "All drop-book skills locked.";
        }
        #endregion

        #region Teleport
        private struct TeleportTarget
        {
            public string Name;
            public string Kind;
            public Transform Transform;
        }

        // Named Zone01 landmarks (built by StarterTownBuilder) that don't carry a PointOfInterest component yet.
        // Most targets are PointOfInterest components laid down by the city builder; these are extra named objects.
        private static readonly string[] LandmarkNames = { "Slot_Elder", "Slot_GateMaster" };

        private static List<TeleportTarget> CollectTargets()
        {
            var list = new List<TeleportTarget>();
            foreach (var poi in PointOfInterest.Active)
                if (poi != null) list.Add(new TeleportTarget { Name = poi.Id, Kind = "poi", Transform = poi.transform });
            foreach (var portal in ZonePortal.Active)
                if (portal != null) list.Add(new TeleportTarget { Name = portal.PortalId, Kind = "portal", Transform = portal.transform });
            var spawns = PlayerSpawnPoint.Active;
            for (int i = 0; i < spawns.Count; i++)
                // GameObject names ("Spawn Point 1") are stable; the registration order isn't.
                if (spawns[i] != null) list.Add(new TeleportTarget { Name = spawns[i].name, Kind = "spawn", Transform = spawns[i].transform });
            foreach (var landmark in LandmarkNames)
            {
                var go = GameObject.Find(landmark);
                if (go != null && !list.Exists(t => t.Transform == go.transform))
                    list.Add(new TeleportTarget { Name = landmark, Kind = "landmark", Transform = go.transform });
            }
            return list;
        }

        [Command("gm.poi", "List teleport targets.")]
        private static string ListPoi()
        {
            var targets = CollectTargets();
            if (targets.Count == 0) return "No teleport targets in this scene.";
            var sb = new StringBuilder();
            foreach (var t in targets)
            {
                Vector3 p = TargetPosition(t.Transform);
                sb.Append(t.Name.PadRight(26)).Append(t.Kind.PadRight(10)).AppendLine($"({p.x:0}, {p.y:0}, {p.z:0})");
            }
            return sb.ToString();
        }

        [Command("gm.tp", "Teleport to a POI / portal / spawnN / landmark, or any scene object by name. Partial names work.")]
        private static string Teleport(string target)
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            var targets = CollectTargets();
            TeleportTarget? match = null;
            foreach (var t in targets)
                if (string.Equals(t.Name, target, StringComparison.OrdinalIgnoreCase)) { match = t; break; }
            if (match == null)
                foreach (var t in targets)
                    if (t.Name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0) { match = t; break; }

            Vector3 destination;
            string label;
            if (match != null)
            {
                destination = TargetPosition(match.Value.Transform);
                label = match.Value.Name;
            }
            else
            {
                var go = GameObject.Find(target);
                if (go == null) return $"No target '{target}'. Try gm.poi.";
                destination = TargetPosition(go.transform);
                label = go.name;
            }
            return Warp(player, destination) ? $"Teleported to {label}." : $"No walkable ground near {label}.";
        }

        [Command("gm.tpxyz", "Teleport to world coordinates.")]
        private static string TeleportXyz(float x, float y, float z)
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            return Warp(player, new Vector3(x, y, z)) ? $"Teleported to ({x:0}, {y:0}, {z:0})." : "No walkable ground near that point.";
        }

        [Command("gm.tpcursor", "Teleport to the ground under the mouse cursor.")]
        private static string TeleportCursor()
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            var cam = Camera.main;
            if (cam == null || Mouse.current == null) return "No camera or mouse.";
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out var hit, 2000f, ~0, QueryTriggerInteraction.Ignore)) return "Cursor isn't over the ground.";
            return Warp(player, hit.point) ? $"Teleported to ({hit.point.x:0}, {hit.point.y:0}, {hit.point.z:0})." : "No walkable ground there.";
        }

        /// <summary>
        /// Group objects (e.g. the "Plaza" folder built by StarterTownBuilder) sit at the origin, so a parent with
        /// children resolves to the centre of its renderers instead of its own pivot.
        /// </summary>
        private static Vector3 TargetPosition(Transform t)
        {
            if (t.childCount == 0) return t.position;
            var renderers = t.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return t.position;
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds.center;
        }

        private static bool Warp(GameObject player, Vector3 position)
        {
            if (!NavMesh.SamplePosition(position, out NavMeshHit hit, NavSampleRadius, NavMesh.AllAreas)) return false;
            position = hit.position;
            if (player.TryGetComponent<NavMeshAgent>(out var agent))
            {
                position += Vector3.up * agent.baseOffset;
                if (agent.isActiveAndEnabled && agent.isOnNavMesh)
                {
                    agent.ResetPath();
                    agent.Warp(position);
                }
            }
            player.transform.position = position;
            return true;
        }
        #endregion

        #region Time & weather shortcuts
        [Command("time.pause", "Freeze the world clock.")]
        [Command("gm.timepause")]
        private static string PauseTime()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return "No WorldEnvironmentNetwork in the scene.";
            float current = env.State.TimeScale;
            if (current > 0f) pausedTimeScale = current;
            return env.SetTimeScale(0f) ? "Time paused." : NoAuthority;
        }

        [Command("time.resume", "Resume the world clock at its previous speed.")]
        [Command("gm.timeresume")]
        private static string ResumeTime()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return "No WorldEnvironmentNetwork in the scene.";
            return env.SetTimeScale(pausedTimeScale) ? $"Time resumed (×{pausedTimeScale:0.##})." : NoAuthority;
        }

        [Command("time.dawn", "Jump to 06:00.")] private static string Dawn() => SetHour(6f);
        [Command("time.noon", "Jump to 12:00.")] private static string Noon() => SetHour(12f);
        [Command("time.dusk", "Jump to 18:30.")] private static string Dusk() => SetHour(18.5f);
        [Command("time.midnight", "Jump to 00:00.")] private static string Midnight() => SetHour(0f);

        private static string SetHour(float hours)
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return "No WorldEnvironmentNetwork in the scene.";
            return env.SetTimeOfDay(hours) ? $"Time set to {hours:0.00}h." : NoAuthority;
        }

        [Command("weather.clear", "Blend to clear skies.")]
        private static string ClearWeather(float transitionSeconds = 5f)
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null || env.Catalog == null) return "No WorldEnvironmentNetwork in the scene.";
            int index = env.Catalog.IndexOf("Clear");
            if (index < 0) return "No 'Clear' weather profile.";
            return env.ForceWeather(index, transitionSeconds) ? "Weather → Clear." : NoAuthority;
        }
        #endregion
    }
}
