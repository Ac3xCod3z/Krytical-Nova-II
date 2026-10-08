#if !UNITY_SERVER
using KryticalNova.Combat;
using KryticalNova.GameFeel;
using KryticalNova.Progression;
using QFSW.QC;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for looking at effects: what a hit of each kind looks like with the weapon in hand,
    /// and the three looks a critical hit on another player can have. They only show things on this screen; nothing
    /// is hit, and nothing is sent. Like the other GM commands they are the host's.
    /// </summary>
    public static class FxCommands
    {
        private const string NoPresenter = "The game feel is not in this scene.";

        private static bool TryGetFighter(out PlayerMeleeCombat melee, out string error)
        {
            melee = null;
            if (!GmTarget.TryGetPlayer(out var player, out error)) return false;
            melee = player.GetComponent<PlayerMeleeCombat>();
            if (melee == null) error = "That player cannot fight (run Krytical Nova/Setup/Player Prefab).";
            return melee != null;
        }

        /// <summary>The nearest thing that can be hit: another player when <paramref name="playersOnly"/>, otherwise anyone.</summary>
        private static Transform Nearest(Transform from, bool playersOnly)
        {
            Transform best = null;
            float bestSqr = float.MaxValue;
            foreach (var entity in Object.FindObjectsByType<CombatEntity>(FindObjectsInactive.Exclude))
            {
                if (entity.transform == from) continue;
                if (playersOnly && entity.GetComponent<PlayerResources>() == null) continue;
                float sqr = (entity.transform.position - from.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; best = entity.transform; }
            }
            return best;
        }

        [Command("gm.fx.hit", "Show what a hit of yours looks like on the nearest target, with the weapon you hold: " +
                              "gm.fx.hit [normal|elemental|crit|shatter|backlash] [light|heavy]. Nothing is hit.")]
        private static string Hit(string kind = "normal", string weight = "heavy")
        {
            if (!TryGetFighter(out var melee, out string error)) return error;
            Transform target = Nearest(melee.transform, false);
            if (target == null) return "Nothing to show it on: stand near a Combat Dummy or another player.";
            bool critical = false, elemental = false, shattered = false, reflected = false;
            switch (kind.ToLowerInvariant())
            {
                case "normal": break;
                case "elemental": elemental = true; break;
                case "crit": case "critical": critical = true; break;
                case "shatter": case "shattered": shattered = true; break;
                case "backlash": case "reflected": reflected = true; break;
                default: return "Use: gm.fx.hit [normal|elemental|crit|shatter|backlash] [light|heavy]";
            }
            bool heavy = weight.ToLowerInvariant() != "light";
            melee.PreviewImpact(target, heavy ? 3 : 1, critical, reflected, shattered, elemental);
            return $"A {(heavy ? "heavy" : "light")} {kind} hit on {target.name}.";
        }

        [Command("gm.fx.pvpcrit", "The look of a critical hit on another player, on this screen until the game is closed: " +
                                  "gm.fx.pvpcrit [splash|frame|faction|default]. It is shown on the nearest other player, or on the " +
                                  "nearest target when no other player is near. Without a word it shows the one in use.")]
        private static string PvpCrit(string look = "")
        {
            GameFeelPresenter feel = GameFeelPresenter.Instance;
            if (feel == null || feel.Looks == null) return NoPresenter;
            if (!TryGetFighter(out var melee, out string error)) return error;
            switch (look.ToLowerInvariant())
            {
                case "": break;
                case "splash": feel.PvpCritOverride = PvpCritStyle.SplashPanel; break;
                case "frame": feel.PvpCritOverride = PvpCritStyle.ImpactFrame; break;
                case "faction": feel.PvpCritOverride = PvpCritStyle.FactionStrike; break;
                case "default": feel.PvpCritOverride = null; break;
                default: return "Use: gm.fx.pvpcrit [splash|frame|faction|default]";
            }
            PvpCritStyle inUse = feel.PvpCritOverride ?? feel.Looks.pvpCritStyle;
            Transform target = Nearest(melee.transform, true);
            if (target == null) target = Nearest(melee.transform, false);
            if (target == null) return $"In use: {inUse}. Nothing near to show it on.";
            feel.PreviewPvpCrit(melee.transform, target);
            return $"In use: {inUse}{(feel.PvpCritOverride.HasValue ? " (this screen only, until the game is closed)" : " (the settings' own)")}. Shown on {target.name}.";
        }
    }
}
#endif
