using System;
using System.Collections.Generic;
using System.Text;
using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using KryticalNova.World;
using KryticalNova.World.Portals;
using QFSW.QC;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console GM / development commands (prefix "gm."). Like the item and world commands they only run where
    /// the game is decided (host, server or offline), because progression and max HP/Chi are computed there. They
    /// act on the local player, or on the connected player picked with gm.target (see <see cref="GmTarget"/>).
    /// Time and weather shortcuts wrap <see cref="WorldEnvironmentCommands"/>' API.
    /// </summary>
    public static class GmCommands
    {
        private const string NoAuthority = "Only the host/offline player can use GM commands.";
        private const float NavSampleRadius = 6f;

        private static float pausedTimeScale = 1f;
        // Physics step at normal speed, remembered the first time the game speed is changed (0 = never changed).
        private static float normalFixedDeltaTime;

        /// <summary>The GM toggles of the player the commands act on (read by the editor GM Tools window); null if none.</summary>
        public static PlayerGmState TargetGmState =>
            GmTarget.TryGetPlayer(out var player, out _) ? player.GetComponent<PlayerGmState>() : null;

        /// <summary>Read by the editor GM Tools window to show the toggle state.</summary>
        public static bool OneShotEnabled
        {
            get
            {
                var gm = TargetGmState;
                return gm != null && gm.OneShot;
            }
        }

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
            pausedTimeScale = 1f;
            RestoreGameSpeed();
        }

        #region Player lookup
        private static bool TryGetPlayer(out GameObject player, out string error) => GmTarget.TryGetPlayer(out player, out error);

        private static bool TryGet<T>(out T component, out string error) where T : Component
        {
            component = null;
            if (!TryGetPlayer(out var player, out error)) return false;
            component = player.GetComponent<T>();
            if (component == null) { error = $"That player has no {typeof(T).Name}."; return false; }
            return true;
        }

        [Command("gm.players", "List connected players and their client ids (host / server).")]
        private static string Players()
        {
            var players = GmTarget.ConnectedPlayers();
            if (players.Count == 0) return "No network session hosted here: GM commands act on your own player.";
            ulong? target = GmTarget.ClientId;
            var local = PlayerInventory.Local;
            var sb = new StringBuilder();
            foreach (var (clientId, player) in players)
            {
                bool isLocal = local != null && local.gameObject == player;
                bool isTarget = target.HasValue ? target.Value == clientId : isLocal;
                int level = player.TryGetComponent<PlayerCharacterProgression>(out var prog) ? prog.CurrentLevel : 0;
                sb.Append(isTarget ? "> " : "  ").Append("client ").Append(clientId)
                  .Append(isLocal ? " (you)" : string.Empty).Append("  level ").Append(level)
                  .Append("  at ").AppendLine(player.transform.position.ToString("0"));
            }
            sb.Append("Pick one with gm.target <clientId>; gm.target self goes back to your own player.");
            return sb.ToString();
        }

        [Command("gm.target", "Choose which connected player GM and item commands act on: gm.target <clientId> or gm.target self.")]
        private static string Target(string who = "")
        {
            who = (who ?? string.Empty).Trim();
            if (who.Length == 0) return $"GM commands act on {GmTarget.Describe()}.";
            if (who.Equals("self", StringComparison.OrdinalIgnoreCase) || who.Equals("me", StringComparison.OrdinalIgnoreCase))
            {
                GmTarget.TargetSelf();
                return "GM commands act on your own player.";
            }
            if (!ulong.TryParse(who, out ulong clientId)) return "Use gm.target <clientId> or gm.target self. gm.players lists the ids.";
            return GmTarget.TrySetTarget(clientId, out var error) ? $"GM commands now act on {GmTarget.Describe()}." : error;
        }
        #endregion

        #region Help
        [Command("gm.help", "List GM commands.")]
        private static string Help()
        {
            return string.Join("\n", new[]
            {
                "TARGET       gm.players · gm.target <clientId|self>   (host: aim every command below at a connected player)",
                "PROGRESSION  gm.level <n> · gm.maxlevel · gm.xp <n> · gm.statpoints <n> · gm.skillpoints <n> · gm.attr <str|vit|agi|spi> <n> · gm.resetstats",
                "STATS        gm.stats · gm.setstat <StatType> <value> · gm.clearstats",
                "VITALS       gm.heal · gm.fillchi · gm.damage <n> · gm.hitme [n] [normal|heavy|crit] [front|back|left|right] · gm.spendchi <n> · gm.kill",
                "SKILLS       gm.skillpoints <n> · gm.skills.max · gm.skills · gm.dropskills <true|false> · gm.unlockskill <id> · gm.lockskill <id>",
                "BUFFS        gm.buffs · gm.buff.remove <id|name> · gm.buff.clear   (take down active skills)",
                "COMBAT       gm.dummy.hit [times] · gm.dummy.reset · gm.dummy.hp <n> · gm.dummy.maxhp <n> (0 = base) · gm.dummy.inf [on|off]",
                "IN A FIGHT   gm.combat   (is the player in a fight, and why) · gm.combat hold [on|off]   (kept in one, as a war will) · gm.combat watch [on|off]",
                "FIGHT MUSIC  gm.music   (the tracks, what is on) · gm.music.track <n|name|round> · gm.music.click [on|off]   (a click on every beat) · gm.music.nudge <ms>",
                "CADENCE      gm.cadence   (its numbers, and how the last skill key pressed in a fight fell against the beat) · gm.cadence window [on|off]   (a running readout: beats coming, hits on and off the beat) · gm.cadence reset · gm.cadence log [on|off]",
                "PVP          gm.pvp [on|off]   (factions fight each other, for everyone) · gm.duel <clientIdA> <clientIdB>   (a Callout between two players, no request) · gm.duel.end",
                "GOD MODE     gm.god [on|off] · gm.infchi · gm.nocd · gm.oneshot · gm.speed <mult> · gm.reset",
                "GAME SPEED   gm.slowmo [x]   (0.25 = quarter speed, 1 = normal; no value toggles quarter speed)",
                "CREDS        gm.creds [n] · gm.creds.set <n> · gm.creds.report   (the only way Creds enter the game for now)",
                "STORAGE      gm.vault [deep|off]   (opens it here; storage then works with no NPC near) · gm.drop <item> [n]",
                "TRADE        gm.trade.reset   (daily Creds limit) · gm.kiosk · gm.kiosk.expire",
                "SOCIAL       gm.guild [name|none] · gm.guild.list   (a Crew takes its own faction only, GM or not)",
                "SPONSOR      gm.sponsor [clientIdA clientIdB]   (links two players, the higher sponsors; no ids prints the links) · gm.sponsor.end · gm.sponsor.points [n] · gm.sponsor.window",
                "APPEARANCE   gm.weave [off]   (opens the Holoweaver here; Weaves and dyes then work with no NPC near) · gm.dye · gm.weave.unlock <all|item> · gm.weave.clear · gm.weave.list · gm.restyle <stylist|reshaper|coach|off> · gm.passes [n]   (Chroma Cells, Prism Cells, Restyle and Training Passes)",
                "CRAFTING     gm.craft [off]   (opens the Fabricator here; crafting then works with no NPC near) · gm.craftkit [times]   (the materials of every recipe) · gm.dossier   (the three Dossiers) · gm.zenith [all]   (the Zenith Dossiers of the player's faction, or all nine)",
                "XP BOOSTERS  gm.boosters [n]   (n of each: Field, Companion, Tandem) · gm.boost   (what is running) · gm.boost.clear",
                "PETS         gm.pet · gm.pet.growth <0-100> · gm.pet.hunger <0-100> · gm.pet.feed · gm.pet.xp <n> · gm.pet.event <win|loss> · gm.pet.say <died|drop|level|bruiser|crit|lowhp|idle>",
                "EFFECTS      gm.fx.hit [normal|elemental|crit|shatter|backlash] [light|heavy] · gm.fx.pvpcrit [splash|frame|faction|default]   (shown on this screen; nothing is hit)",
                "SAVING       gm.save · gm.save.where · gm.save.wipe confirm",
                "HOSTILES     gm.hostile.list · gm.hostile.spawn <id|name> [count] [level] [health|inf] · gm.hostile.clear [radius] · gm.hostile.kill [radius] · gm.hostile.info · gm.hostile.hp <n|n%> · gm.hostile.freeze [on|off] · gm.hostile.ignore [on|off] · gm.hostile.ttk <id|name> [level] · gm.hostile.stats · gm.hostile.loot <id|name> [kills]",
                "LEVEL GAP    gm.gap <hostile level> · gm.gap.table · gm.xprate <x> · gm.droprate <x> · gm.deathpenalty [on|off]",
                "CREW HALLS   gm.home   (to the Hall sandbox: a Lot with a Hall, far from town) · gm.home.inside · gm.home.floor up|ground|basement · gm.home.outside · gm.home.leave · gm.furnish.kit [n] [all]   (n of every furnishing in the player's faction's style, or of every style, into the Furnishing Crate, with Facades and Finishes) · gm.furnish.clear · gm.home.reset   (everything back into the Crate) · gm.home.fill [n]   (a load test)",
                "AUTOPILOT    gm.autopilot [on|off] · gm.autopilot.time <minutes> · gm.autopilot.log [on|off] · gm.magnet.test · item.give \"Autopilot Chip\" [n]",
                "ZONES        gm.zone.list · gm.zone.fill <id> · gm.zone.clear <id> [seconds] · gm.zone.respawn <seconds|default> · gm.zone.sleep <id> [on|off|auto] · gm.zone.bruiser <id>   (Hostile Zones; gm.tp <id> goes to one)",
                "TELEPORT     gm.poi · gm.tp <name> · gm.tpxyz <x> <y> <z> · gm.tpcursor",
                "TIME         time.set <h> · time.pause · time.resume · time.scale <x> · time.dawn · time.noon · time.dusk · time.midnight · time.daylength <s>",
                "WEATHER      weather.set <name> [secs] · weather.clear · weather.lock <bool> · weather.list",
                "SKY          sky.rainbow [0-1|off] · sky.moonphase <new|first|full|last|0-1|off> · sky.moon.next · sky.halo [sun|moon|both|off] · sky.shimmer <x|off> · sky.flare <x|off> · sky.lightning · sky.status · sky.reset",
                "ITEMS        item.give <id|name> [n] · item.list [slot] · item.clear   (stims: Pill_Vitality, Pill_Chi, Pill_DualHarmony)",
                "GEAR         gm.gear <slot> <level> <rarity> [class] · gm.loadout [class]   (e.g. gm.gear weapon 45 rare carbine) · gm.gearset [level] [rarity] [temper] [wear]   (one whole set, e.g. gm.gearset 112 epic 60 wear)",
                "KITS         gm.kit gear [level] [rarity] · gm.kit temper [n] · gm.kit pets · gm.kit all   (gear: every piece that fits, 112 Epic unless told)",
                "FORGE        gm.forge [off]   (opens it here; the forge then works with no Armorer near) · gm.forgekit [n] · gm.temper <%> [slot] · gm.temper.all <%> [worn|pack|all] · gm.fuse <0-12> [slot] · gm.tune <0-25> [slot]",
                "RENOWN       gm.renown [n] · gm.renown.set <n>   (the only way Renown enters the game for now) · gm.ascension [0-96] · gm.standing <vanguard|bulwark|ironblood|mindwell|all> <0-12> · gm.standing.wear <track|none> · gm.herald [off]",
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
                sb.AppendLine($"HP {res.Health:0}/{res.MaxHealth:0}   Flux {res.Chi:0}/{res.MaxChi:0}");
            return sb.ToString();
        }

        [Command("gm.setstat", "Add a flat GM bonus to any stat, e.g. gm.setstat MaxHP 500 or gm.setstat Luck 25 (0 removes it).")]
        private static string SetStat(StatType stat, float value)
        {
            if (!TryGet<CharacterCombatStats>(out var stats, out var error)) return error;
            if (!TryGet<PlayerGmState>(out var gm, out error)) return error;
            gm.SetFlatStat(stat, value);
            return $"{stat} GM bonus {value:+0.##;-0.##;0} → {stats.GetStatValue(stat):0.##}.";
        }

        [Command("gm.clearstats", "Remove every GM stat bonus (incl. one-shot).")]
        private static string ClearStats()
        {
            if (!TryGet<PlayerGmState>(out var gm, out var error)) return error;
            gm.ClearStatBonuses();
            return "GM stat bonuses cleared.";
        }

        private static bool GodModeOn(Component player) => player.TryGetComponent<PlayerGmState>(out var gm) && gm.GodMode;
        private static bool InfiniteChiOn(Component player) => player.TryGetComponent<PlayerGmState>(out var gm) && gm.InfiniteChi;
        #endregion

        #region Vitals
        [Command("gm.heal", "Fully restore HP and Flux.")]
        private static string Heal()
        {
            if (!TryGet<PlayerResources>(out var res, out var error)) return error;
            res.RestoreAll();
            return $"HP {res.Health:0}/{res.MaxHealth:0}   Flux {res.Chi:0}/{res.MaxChi:0}";
        }

        [Command("gm.fillchi", "Fully restore Flux.")]
        private static string FillChi()
        {
            if (!TryGet<PlayerResources>(out var res, out var error)) return error;
            res.RestoreChi(res.MaxChi);
            return $"Flux {res.Chi:0}/{res.MaxChi:0}";
        }

        [Command("gm.damage", "Deal damage to yourself (ignored while god mode is on).")]
        private static string Damage(float amount)
        {
            if (!TryGet<PlayerResources>(out var res, out var error)) return error;
            res.ApplyDamage(amount);
            return $"HP {res.Health:0}/{res.MaxHealth:0}{(GodModeOn(res) ? " (god mode on)" : "")}";
        }

        [Command("gm.hitme", "Take a hit yourself to see the impact effect and hit animation: gm.hitme [amount] [normal|heavy|crit] " +
                             "[front|back|left|right]. Amount 0 = 10% of max HP.")]
        private static string HitMe(float amount = 0f, string style = "normal", string from = "front")
        {
            if (!TryGetPlayer(out var player, out var error)) return error;
            var res = player.GetComponent<PlayerResources>();
            var melee = player.GetComponent<KryticalNova.Combat.PlayerMeleeCombat>();
            if (res == null || melee == null) return "Local player has no PlayerResources / PlayerMeleeCombat.";
            if (!TryParseHitStyle(style, out int comboStep, out bool critical))
                return $"Unknown style '{style}'. Use normal, heavy or crit.";
            if (!TryParseHitSide(from, player.transform, out Vector3 towardAttacker))
                return $"Unknown side '{from}'. Use front, back, left or right.";
            if (amount <= 0f) amount = res.MaxHealth * 0.1f;

            float before = res.Health;
            res.ApplyDamage(amount);
            float lost = before - res.Health;
            melee.PreviewImpact(player.transform, comboStep, critical);
            // This bypasses TakeDamage, so the hit animation is asked for here, the way a real blow asks for it.
            player.GetComponent<KryticalNova.Player.PlayerAnimancerMotor>()?.ShowHitReaction(towardAttacker, comboStep >= 3);
            ClientPresentationRegistry.Feedback?.ShowCombatFeedback(player.transform.position,
                new DamageResult { DamageDealt = amount, IsCritical = critical });
            // The networked path raises this from the replicated health; offline nothing else does.
            if (!res.IsSpawned && lost > 0f)
                ClientPresentationRegistry.Feel?.LocalPlayerDamaged(lost / Mathf.Max(1f, res.MaxHealth));
            return $"Hit for {amount:0} ({style}, from the {from}) · HP {res.Health:0}/{res.MaxHealth:0}{(GodModeOn(res) ? " (god mode on: no HP lost)" : "")}";
        }

        private static bool TryParseHitSide(string text, Transform victim, out Vector3 towardAttacker)
        {
            towardAttacker = victim.forward;
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "": case "front": case "f": return true;
                case "back": case "behind": case "b": towardAttacker = -victim.forward; return true;
                case "left": case "l": towardAttacker = -victim.right; return true;
                case "right": case "r": towardAttacker = victim.right; return true;
                default: return false;
            }
        }

        private static bool TryParseHitStyle(string text, out int comboStep, out bool critical)
        {
            comboStep = 1;
            critical = false;
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "": case "normal": case "light": return true;
                case "heavy": case "finisher": comboStep = 3; return true;
                case "crit": case "critical": comboStep = 3; critical = true; return true;
                default: return false;
            }
        }

        [Command("gm.spendchi", "Spend Flux (tests the Flux bar / regen; ignored while infinite Flux is on).")]
        private static string SpendChi(float amount)
        {
            if (!TryGet<PlayerResources>(out var res, out var error)) return error;
            res.TrySpendChi(Mathf.Min(amount, res.Chi));
            return $"Flux {res.Chi:0}/{res.MaxChi:0}{(InfiniteChiOn(res) ? " (infinite Flux on)" : "")}";
        }

        [Command("gm.kill", "Kill the targeted player (bypasses god mode): gm.kill [normal|crit] [front|back|left|right]. " +
                            "crit plays the thrown death; the side is where the blow comes from (default front).")]
        private static string Kill(string kind = "normal", string side = "front")
        {
            if (!TryGet<CombatEntity>(out var entity, out var error)) return error;
            if (!entity.IsAlive) return $"{entity.EntityName} is already dead. gm.respawn brings them back.";

            bool crit = string.Equals(kind, "crit", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "critical", StringComparison.OrdinalIgnoreCase);
            if (!crit && !string.Equals(kind, "normal", StringComparison.OrdinalIgnoreCase)) { side = kind; }

            Transform body = entity.transform;
            Vector3 from; // where the blow comes from, relative to the body
            switch (side.ToLowerInvariant())
            {
                case "front": from = body.forward; break;
                case "back": from = -body.forward; break;
                case "left": from = -body.right; break;
                case "right": from = body.right; break;
                default: return "Use: gm.kill [normal|crit] [front|back|left|right]";
            }
            from.y = 0f;
            entity.Kill(new KillingBlow { Direction = -from.normalized, IsCritical = crit });
            return $"Killed {entity.EntityName} ({(crit ? "critical" : "normal")}, blow from the {side.ToLowerInvariant()}).";
        }

        [Command("gm.respawn", "Respawn the targeted player now if they are dead, without waiting for the timer.")]
        private static string Respawn()
        {
            if (!TryGet<PlayerDeathMotor>(out var death, out var error)) return error;
            if (!death.IsDead) return "That player is not dead.";
            death.Respawn();
            return "Respawned.";
        }
        #endregion

        #region PvP
        [Command("gm.pvp", "Toggle (or set) PvP between factions for everyone: gm.pvp [true|false].")]
        private static string Pvp() => Pvp(!FactionRules.CrossFactionPvp);

        [Command("gm.pvp")]
        private static string Pvp(bool on)
        {
            // Any player will do: this only checks that we are the host, the server or offline.
            if (!TryGet<PlayerIdentity>(out _, out var error)) return error;
            PlayerIdentity.SetCrossFactionPvp(on);
            return on ? "Cross-faction PvP ON: players of different factions can attack each other."
                      : "Cross-faction PvP OFF: every player is friendly, except the two players of a callout.";
        }

        [Command("gm.duel", "Start a callout between two connected players at once, without a request: gm.duel <clientIdA> <clientIdB> (see gm.players).")]
        private static string Duel(ulong clientA, ulong clientB)
        {
            if (!TryDuelist(clientA, out var a, out var error) || !TryDuelist(clientB, out var b, out error)) return error;
            return a.ServerForceStart(b)
                ? $"Callout started: {a.DuelistName} against {b.DuelistName}."
                : "Could not start that callout (the same player twice, one of them is dead, or already in a callout).";
        }

        [Command("gm.duel.end", "Stop the targeted player's callout with no winner.")]
        private static string DuelEnd()
        {
            if (!TryGet<PlayerDuel>(out var duel, out var error)) return error;
            return duel.ServerCancel("The callout was ended by the GM.") ? "Callout ended." : "That player is not in a callout.";
        }

        private static bool TryDuelist(ulong clientId, out PlayerDuel duel, out string error)
        {
            duel = null;
            error = null;
            foreach (var (id, player) in GmTarget.ConnectedPlayers())
            {
                if (id != clientId) continue;
                duel = player.GetComponent<PlayerDuel>();
                break;
            }
            if (duel == null) error = $"No connected player with client id {clientId} (host / server only). Try gm.players.";
            return duel != null;
        }
        #endregion

        #region Chat
        [Command("gm.say", "Announce on the GM channel: every player of every faction reads it, wherever they are. Wrap the text in quotes.")]
        private static string Say(string text)
        {
            // Any player will do: this only checks that we are the host, the server or offline.
            if (!TryGetPlayer(out _, out var error)) return error;
            int reached = PlayerChat.ServerBroadcastGm(text, "GM");
            return reached > 0 ? $"Announced to {reached} player(s)." : "Nothing sent (empty text, or nobody is connected).";
        }

        [Command("gm.guild", "Put the targeted player in the Crew with that name, founding it when there is none (no fee, level or " +
                             "Vaultkeeper; the Crew must be of their faction): gm.guild <name>. gm.guild none takes them out. " +
                             "No value prints their Crew.")]
        private static string Guild(string guild = "")
        {
            if (!TryGet<KryticalNova.Social.PlayerCrew>(out var crew, out var error)) return error;
            if (string.IsNullOrWhiteSpace(guild))
            {
                var mine = KryticalNova.Social.CrewService.Of(crew.CharacterId);
                return mine == null ? $"{crew.MemberName} is in no Crew."
                    : $"{crew.MemberName} is {KryticalNova.Social.CrewRules.RankName(KryticalNova.Social.CrewRules.Find(mine, crew.CharacterId).Rank)} of the Crew '{mine.name}' ({mine.members.Count} member(s)).";
            }
            return string.Equals(guild, "none", StringComparison.OrdinalIgnoreCase)
                ? KryticalNova.Social.CrewService.GmLeave(crew)
                : KryticalNova.Social.CrewService.GmJoin(crew, guild);
        }

        [Command("gm.guild.list", "List every Crew the server knows with its members and their ranks.")]
        private static string Guilds()
        {
            var all = KryticalNova.Social.CrewService.All;
            if (all.Count == 0) return "No Crews.";
            var lines = new System.Text.StringBuilder();
            foreach (var crew in all)
            {
                lines.Append($"{crew.name} ({crew.members.Count}): ");
                for (int i = 0; i < crew.members.Count; i++)
                    lines.Append(i > 0 ? ", " : string.Empty).Append(crew.members[i].name).Append(" [").Append(KryticalNova.Social.CrewRules.RankName(crew.members[i].Rank)).Append(']');
                lines.AppendLine();
            }
            return lines.ToString().TrimEnd();
        }
        #endregion

        #region God mode
        [Command("gm.god", "Toggle (or set) no-damage god mode.")]
        private static string God()
        {
            if (!TryGet<PlayerGmState>(out var gm, out var error)) return error;
            return God(!gm.GodMode);
        }

        [Command("gm.god")]
        private static string God(bool on)
        {
            if (!TryGet<PlayerGmState>(out var gm, out var error)) return error;
            gm.SetGodMode(on);
            return $"God mode {(on ? "ON" : "OFF")}.";
        }

        [Command("gm.infchi", "Toggle infinite Flux (skills never fail or drain Flux).")]
        private static string InfiniteChi()
        {
            if (!TryGet<PlayerGmState>(out var gm, out var error)) return error;
            gm.SetInfiniteChi(!gm.InfiniteChi);
            return $"Infinite Flux {(gm.InfiniteChi ? "ON" : "OFF")}.";
        }

        [Command("gm.nocd", "Toggle no skill cooldowns.")]
        private static string NoCooldowns()
        {
            if (!TryGet<PlayerGmState>(out var gm, out var error)) return error;
            gm.SetNoCooldowns(!gm.NoCooldowns);
            return $"No cooldowns {(gm.NoCooldowns ? "ON" : "OFF")}.";
        }

        [Command("gm.oneshot", "Toggle ×1000 Attack Power.")]
        private static string OneShot()
        {
            if (!TryGet<CharacterCombatStats>(out var stats, out var error)) return error;
            if (!TryGet<PlayerGmState>(out var gm, out error)) return error;
            gm.SetOneShot(!gm.OneShot);
            return $"One-shot {(gm.OneShot ? "ON" : "OFF")} · AP {stats.GetStatValue(StatType.AttackPower):0}.";
        }

        [Command("gm.speed", "Movement speed multiplier (1 = normal).")]
        private static string Speed(float multiplier)
        {
            if (!TryGet<PlayerGmState>(out var gm, out var error)) return error;
            gm.SetSpeedMultiplier(multiplier);
            return $"Speed ×{gm.SpeedMultiplier:0.##}.";
        }

        [Command("gm.slowmo", "Slow motion for the whole game: gm.slowmo 0.25 is quarter speed, gm.slowmo 1 is normal. With no value it toggles quarter speed.")]
        private static string SlowMo() => SlowMo(Mathf.Approximately(Time.timeScale, 1f) ? 0.25f : 1f);

        [Command("gm.slowmo")]
        private static string SlowMo(float speed)
        {
            if (!TryGetPlayer(out _, out var error)) return error;
            speed = Mathf.Clamp(speed, 0.05f, 2f);
            if (normalFixedDeltaTime <= 0f) normalFixedDeltaTime = Time.fixedDeltaTime / Mathf.Max(0.01f, Time.timeScale);
            Time.timeScale = speed;
            Time.fixedDeltaTime = normalFixedDeltaTime * speed; // physics keeps stepping every frame instead of stuttering
            return Mathf.Approximately(speed, 1f) ? "Game speed normal." : $"Game speed ×{speed:0.##}.";
        }

        private static void RestoreGameSpeed()
        {
            if (normalFixedDeltaTime <= 0f) return;
            Time.timeScale = 1f;
            Time.fixedDeltaTime = normalFixedDeltaTime;
            normalFixedDeltaTime = 0f;
        }

        [Command("gm.reset", "Turn off every GM toggle and stat bonus.")]
        private static string ResetAll()
        {
            RestoreGameSpeed();
            if (TryGet<PlayerGmState>(out var gm, out _)) gm.ResetAll();
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
            return DummyHealth(target);
        }

        private static string DummyHealth(CombatEntity target) =>
            $"{target.EntityName} HP {target.CurrentHealth:0}/{target.MaxHealth:0}{(target.InfiniteHealth ? " (infinite)" : "")}";

        private static bool TryGetDummy(out CombatEntity target, out string error)
        {
            target = null;
            if (!TryGetPlayer(out var player, out error)) return false;
            target = NearestDummy(player);
            if (target == null) error = "No combat target found in the scene.";
            return target != null;
        }

        /// <summary>Read by the editor GM Tools window to show the toggle state.</summary>
        public static bool DummyInfiniteHealthEnabled
        {
            get
            {
                var target = GmTarget.TryGetPlayer(out var player, out _) ? NearestDummy(player) : null;
                return target != null && target.InfiniteHealth;
            }
        }

        [Command("gm.dummy.hp", "Set the nearest combat dummy/NPC target's current health.")]
        private static string SetDummyHealth(float value)
        {
            if (!TryGetDummy(out var target, out var error)) return error;
            return target.SetHealth(value) ? DummyHealth(target) : NoAuthority;
        }

        [Command("gm.dummy.maxhp", "Set the nearest combat dummy/NPC target's max health and refill it (0 = back to its base value).")]
        private static string SetDummyMaxHealth(float value)
        {
            if (!TryGetDummy(out var target, out var error)) return error;
            return target.SetMaxHealthOverride(value) ? DummyHealth(target) : NoAuthority;
        }

        [Command("gm.dummy.inf", "Toggle (or set) infinite health on the nearest combat dummy/NPC target: it refills instead of dying.")]
        private static string InfiniteDummyHealth()
        {
            if (!TryGetDummy(out var target, out var error)) return error;
            return InfiniteDummyHealth(!target.InfiniteHealth);
        }

        [Command("gm.dummy.inf")]
        private static string InfiniteDummyHealth(bool on)
        {
            if (!TryGetDummy(out var target, out var error)) return error;
            return target.SetInfiniteHealth(on) ? $"Infinite health {(on ? "ON" : "OFF")} · {DummyHealth(target)}" : NoAuthority;
        }

        [Command("gm.dropskills", "Unlock (true) or lock (false) every rare drop-book combat skill.")]
        private static string DropSkills(bool unlock = true)
        {
            if (!TryGet<PlayerCombatSkills>(out var skills, out var error)) return error;
            if (unlock) skills.UnlockAllDropSkills(); else skills.LockAllDropSkills();
            return unlock ? "All drop-book skills unlocked." : "All drop-book skills locked.";
        }

        [Command("gm.unlockskill", "Unlock one drop-book skill (level-60 / Zenith) by skill id or name. It still costs skill points to learn.")]
        private static string UnlockSkill(string skill)
        {
            if (!TryGet<PlayerCombatSkills>(out var skills, out var error)) return error;
            var def = FindSkill(skills, skill);
            if (def == null) return $"No skill '{skill}'. See gm.skills.";
            if (!def.requiresSkillBook) return $"{def.displayName} does not need a Skill Chip.";
            skills.UnlockDropSkill(def.skillId);
            return $"{def.displayName} unlocked.";
        }

        [Command("gm.lockskill", "Lock one drop-book skill again by skill id or name.")]
        private static string LockSkill(string skill)
        {
            if (!TryGet<PlayerCombatSkills>(out var skills, out var error)) return error;
            var def = FindSkill(skills, skill);
            if (def == null) return $"No skill '{skill}'. See gm.skills.";
            if (!def.requiresSkillBook) return $"{def.displayName} does not need a Skill Chip.";
            skills.LockDropSkill(def.skillId);
            return $"{def.displayName} locked.";
        }

        [Command("gm.skills.max", "Unlock every skill (books included) and set every combat and movement skill to its max rank, free.")]
        private static string MaxAllSkills()
        {
            if (!TryGet<PlayerCombatSkills>(out var skills, out var error)) return error;
            skills.GmSetAllRanksToMax();
            if (skills.TryGetComponent<PlayerMovementSkills>(out var movement)) movement.GmSetMaxRanks();
            return "Every skill unlocked at max rank.";
        }

        [Command("gm.buffs", "List the skills (buffs) active on you, with the time each has left.")]
        private static string ListBuffs()
        {
            if (!TryGet<CharacterBuffManager>(out var buffs, out var error)) return error;
            if (buffs.ActiveBuffs.Count == 0) return "No active skills.";
            var sb = new StringBuilder();
            foreach (var buff in buffs.ActiveBuffs)
                if (buff?.Skill != null) sb.AppendLine($"{buff.Skill.skillId}  ({buff.Skill.displayName})  {buff.RemainingDuration:0.0}s left");
            return sb.ToString().TrimEnd();
        }

        [Command("gm.buff.remove", "Take down one active skill (buff) by skill id or name: its stats, aura and buff-bar entry go.")]
        private static string RemoveBuff(string skill)
        {
            if (!TryGet<CharacterBuffManager>(out var buffs, out var error)) return error;
            string query = (skill ?? string.Empty).Replace(" ", string.Empty);
            foreach (var buff in buffs.ActiveBuffs)
            {
                if (buff?.Skill == null) continue;
                // By id, or by the start of the name without spaces ("killerinst" finds "Killer Instinct").
                bool match = string.Equals(buff.Skill.skillId, query, StringComparison.OrdinalIgnoreCase)
                    || (query.Length > 0 && (buff.Skill.displayName ?? string.Empty).Replace(" ", string.Empty)
                        .StartsWith(query, StringComparison.OrdinalIgnoreCase));
                if (!match) continue;
                string name = buff.Skill.displayName;
                return buffs.RemoveBuff(buff.Skill.skillId) ? $"{name} removed." : $"{name} could not be removed.";
            }
            return $"'{skill}' is not active. Use gm.buffs to list active skills.";
        }

        [Command("gm.buff.clear", "Take down every active skill (buff) on you.")]
        private static string ClearBuffs()
        {
            if (!TryGet<CharacterBuffManager>(out var buffs, out var error)) return error;
            int count = buffs.ActiveBuffs.Count;
            buffs.ClearAllBuffs();
            return count == 0 ? "No active skills." : $"{count} active skill(s) removed.";
        }

        [Command("gm.skills", "List combat skills with their id, rank and book lock state.")]
        private static string ListSkills()
        {
            if (!TryGet<PlayerCombatSkills>(out var skills, out var error)) return error;
            var lines = new System.Text.StringBuilder();
            foreach (var def in skills.AvailableSkills)
            {
                if (def == null) continue;
                string book = !def.requiresSkillBook ? "trainer" : skills.IsLocked(def) ? "BOOK LOCKED" : "book unlocked";
                lines.AppendLine($"{def.skillId,-20} lvl {def.requiredCharacterLevel,-3} rank {skills.GetRank(def)}/{def.maxRank}  {book}");
            }
            return lines.ToString().TrimEnd();
        }

        private static SkillDefinitionSO FindSkill(PlayerCombatSkills skills, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;
            string wanted = query.Replace(" ", string.Empty);
            foreach (var def in skills.AvailableSkills)
            {
                if (def == null) continue;
                if (string.Equals(def.skillId, wanted, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(def.displayName.Replace(" ", string.Empty), wanted, StringComparison.OrdinalIgnoreCase))
                    return def;
            }
            return null;
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
                if (agent.isActiveAndEnabled && agent.isOnNavMesh) agent.ResetPath();
            }
            // Observers snap with the player instead of watching it slide across the map.
            NetworkPlacement.Place(player.transform, position, player.transform.rotation);
            return true;
        }
        #endregion

        #region Time & weather shortcuts
        [Command("time.pause", "Freeze the world clock.")]
        private static string PauseTime()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return "No WorldEnvironmentNetwork in the scene.";
            float current = env.State.TimeScale;
            if (current > 0f) pausedTimeScale = current;
            return env.SetTimeScale(0f) ? "Time paused." : NoAuthority;
        }

        [Command("time.resume", "Resume the world clock at its previous speed.")]
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
